using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DvCms.IntegrationTests.Supporto;

/// <summary>Richiesta registrata dal Nexi finto.</summary>
public sealed record RichiestaNexi(HttpMethod Metodo, Uri Indirizzo, string? ChiaveApi, string? IdCorrelazione, string Corpo);

/// <summary>
/// Nexi XPay finto per i test (ADR-0022): riproduce, con stato, <c>POST /orders/hpp</c> (pagina ospitata e token di sicurezza) e
/// <c>GET /orders/{orderId}</c> (operazioni dell'ordine). Il «cliente» paga o rifiuta con i metodi del finto. Nessuna richiesta esce
/// dal processo e nessuna chiave è vera.
/// </summary>
public sealed class NexiFinto : HttpMessageHandler
{
    public const string ChiaveValida = "chiave-nexi-di-prova";

    private readonly object _blocco = new();
    private readonly Dictionary<string, OrdineNexi> _ordini = [];
    private int _contatore;

    public ConcurrentQueue<RichiestaNexi> Richieste { get; } = new();

    /// <summary>Se impostata, ogni richiesta fallisce con questa eccezione (servizio non raggiungibile).</summary>
    public Exception? Eccezione { get; set; }

    public IReadOnlyList<string> IdOrdini
    {
        get
        {
            lock (_blocco)
            {
                return [.. _ordini.Keys];
            }
        }
    }

    public string Token(string idOrdine)
    {
        lock (_blocco)
        {
            return _ordini[idOrdine].Token;
        }
    }

    public string Importo(string idOrdine)
    {
        lock (_blocco)
        {
            return _ordini[idOrdine].Importo;
        }
    }

    public JsonObject CorpoCreazione(string idOrdine)
    {
        lock (_blocco)
        {
            return (JsonObject)JsonNode.Parse(_ordini[idOrdine].CorpoRichiesta)!;
        }
    }

    /// <summary>Il cliente paga con la carta: autorizzazione e incasso eseguiti (importo diverso solo se indicato).</summary>
    public void Paga(string idOrdine, string? importo = null) => Aggiungi(idOrdine, ("AUTHORIZATION", "AUTHORIZED", importo), ("CAPTURE", "EXECUTED", importo));

    public void Rifiuta(string idOrdine) => Aggiungi(idOrdine, ("AUTHORIZATION", "DECLINED", null));

    public void Annulla(string idOrdine) => Aggiungi(idOrdine, ("AUTHORIZATION", "CANCELED", null));

    public void MettiInSospeso(string idOrdine) => Aggiungi(idOrdine, ("AUTHORIZATION", "PENDING", null));

    /// <summary>Il corpo di una notifica come la invierebbe il gateway.</summary>
    public string Notifica(string idOrdine, string? idEvento = null, string? token = null, string tipo = "CAPTURE", string risultato = "EXECUTED") => new JsonObject
    {
        ["eventId"] = idEvento ?? Guid.NewGuid().ToString("D"),
        ["eventTime"] = "2026-10-13T10:00:00.000Z",
        ["securityToken"] = token ?? Token(idOrdine),
        ["operation"] = new JsonObject
        {
            ["orderId"] = idOrdine,
            ["operationId"] = "OP-1",
            ["operationType"] = tipo,
            ["operationResult"] = risultato,
            ["operationAmount"] = Importo(idOrdine),
            ["operationCurrency"] = "EUR",
        },
    }.ToJsonString();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var corpo = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        var chiave = request.Headers.TryGetValues("X-API-KEY", out var k) ? k.FirstOrDefault() : null;
        var correlazione = request.Headers.TryGetValues("Correlation-Id", out var c) ? c.FirstOrDefault() : null;
        Richieste.Enqueue(new RichiestaNexi(request.Method, request.RequestUri!, chiave, correlazione, corpo));
        if (Eccezione is not null)
        {
            throw Eccezione;
        }

        if (chiave != ChiaveValida || !Guid.TryParse(correlazione, out _))
        {
            return Errore(HttpStatusCode.Unauthorized, "GW0401");
        }

        var percorso = request.RequestUri!.AbsolutePath;
        if (request.Method == HttpMethod.Post && percorso.EndsWith("/orders/hpp", StringComparison.Ordinal))
        {
            return Crea(corpo);
        }

        if (request.Method == HttpMethod.Get && percorso.Contains("/orders/", StringComparison.Ordinal))
        {
            return Leggi(Uri.UnescapeDataString(percorso[(percorso.LastIndexOf("/orders/", StringComparison.Ordinal) + "/orders/".Length)..]));
        }

        return Errore(HttpStatusCode.NotFound, "GW0404");
    }

    private HttpResponseMessage Crea(string corpo)
    {
        JsonNode? radice;
        try
        {
            radice = JsonNode.Parse(corpo);
        }
        catch (JsonException)
        {
            return Errore(HttpStatusCode.BadRequest, "GW0001");
        }

        var ordine = radice?["order"];
        var sessione = radice?["paymentSession"];
        var idOrdine = ordine?["orderId"]?.GetValue<string>();
        var importo = ordine?["amount"]?.GetValue<string>();
        if (string.IsNullOrEmpty(idOrdine) || idOrdine.Length > 27 || string.IsNullOrEmpty(importo) || ordine?["currency"]?.GetValue<string>() != "EUR"
            || sessione?["amount"]?.GetValue<string>() != importo || string.IsNullOrEmpty(sessione?["language"]?.GetValue<string>())
            || string.IsNullOrEmpty(sessione?["resultUrl"]?.GetValue<string>()))
        {
            return Errore(HttpStatusCode.BadRequest, "GW0001");
        }

        lock (_blocco)
        {
            if (_ordini.ContainsKey(idOrdine))
            {
                return Errore(HttpStatusCode.BadRequest, "GW0002");
            }

            var numero = Interlocked.Increment(ref _contatore);
            _ordini[idOrdine] = new OrdineNexi(importo, $"token-nexi-{numero}-{Guid.NewGuid():N}", corpo);
            return Json(HttpStatusCode.OK, new JsonObject
            {
                ["hostedPage"] = $"https://pay.nexi-finto.example/hpp/{idOrdine}",
                ["securityToken"] = _ordini[idOrdine].Token,
            });
        }
    }

    private HttpResponseMessage Leggi(string idOrdine)
    {
        lock (_blocco)
        {
            if (!_ordini.TryGetValue(idOrdine, out var ordine))
            {
                return Errore(HttpStatusCode.NotFound, "GW0404");
            }

            var operazioni = new JsonArray();
            var progressivo = 0;
            foreach (var (tipo, risultato, importo) in ordine.Operazioni)
            {
                operazioni.Add(new JsonObject
                {
                    ["orderId"] = idOrdine,
                    ["operationId"] = $"OP-{++progressivo}-{idOrdine}",
                    ["operationType"] = tipo,
                    ["operationResult"] = risultato,
                    ["operationAmount"] = importo ?? ordine.Importo,
                    ["operationCurrency"] = "EUR",
                    ["operationTime"] = "2026-10-13T10:00:00.000Z",
                });
            }

            return Json(HttpStatusCode.OK, new JsonObject
            {
                ["orderStatus"] = new JsonObject { ["order"] = new JsonObject { ["orderId"] = idOrdine, ["amount"] = ordine.Importo, ["currency"] = "EUR" } },
                ["operations"] = operazioni,
            });
        }
    }

    private void Aggiungi(string idOrdine, params (string Tipo, string Risultato, string? Importo)[] operazioni)
    {
        lock (_blocco)
        {
            _ordini[idOrdine].Operazioni.AddRange(operazioni);
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode stato, JsonNode corpo) =>
        new(stato) { Content = new StringContent(corpo.ToJsonString(), Encoding.UTF8, "application/json") };

    private static HttpResponseMessage Errore(HttpStatusCode stato, string codice) =>
        Json(stato, new JsonObject { ["errors"] = new JsonArray { new JsonObject { ["code"] = codice, ["description"] = "DESCRIZIONE-RISERVATA" } } });

    private sealed class OrdineNexi
    {
        public OrdineNexi(string importo, string token, string corpoRichiesta)
        {
            Importo = importo;
            Token = token;
            CorpoRichiesta = corpoRichiesta;
        }

        public string Importo { get; }

        public string Token { get; }

        public string CorpoRichiesta { get; }

        public List<(string Tipo, string Risultato, string? Importo)> Operazioni { get; } = [];
    }
}
