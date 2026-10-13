using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using DvCms.Contracts.Negozio;
using DvCms.DTO.Negozio;
using DvCms.Models.Negozio;

namespace DvCms.Services.Negozio.Pagamenti;

/// <summary>Esito della creazione di un pagamento sulla pagina ospitata di Nexi: dove mandare il cliente e il token delle notifiche.</summary>
public sealed record PagamentoNexiCreato(Uri PaginaPagamento, string TokenSicurezza)
{
    public override string ToString() => "PagamentoNexiCreato(…)";
}

/// <summary>
/// Client di Nexi XPay per la pagina di pagamento ospitata (HPP; ADR-0022): <c>POST /orders/hpp</c> per creare il pagamento e
/// <c>GET /orders/{orderId}</c> per leggerne l'esito. Importi e valuta arrivano dal chiamante. L'incasso è automatico
/// (<c>captureType</c> IMPLICIT), nessun dato di carta passa dal nostro server e nessun errore riporta il corpo della risposta: solo il
/// codice HTTP e i codici di errore del gateway.
/// </summary>
public sealed class ClienteNexi
{
    public const string NomeClientHttp = "nexi";

    /// <summary>Lunghezza massima dell'identificativo ordine accettato da Nexi.</summary>
    public const int LunghezzaMassimaIdOrdine = 27;

    private static readonly Uri _baseSandbox = new("https://xpaysandbox.nexigroup.com/api/phoenix-0.0/psp/api/v1/");
    private static readonly Uri _baseLive = new("https://xpay.nexigroup.com/api/phoenix-0.0/psp/api/v1/");
    private static readonly TimeSpan _attesaMassima = TimeSpan.FromSeconds(30);

    private readonly IHttpClientFactory _fabbrica;

    public ClienteNexi(IHttpClientFactory fabbrica)
    {
        _fabbrica = fabbrica;
    }

    /// <summary>
    /// Identificativo dell'ordine presso Nexi: unico nel terminale, al più 27 caratteri e solo quelli ammessi (lettere, cifre e alcuni
    /// simboli). Numero dell'ordine e una parte casuale del tentativo: ogni tentativo di pagamento ha il suo.
    /// </summary>
    public static string NuovoIdOrdine(int numeroOrdine, Guid idTentativo) =>
        string.Create(CultureInfo.InvariantCulture, $"NX{numeroOrdine}-{idTentativo.ToString("N", CultureInfo.InvariantCulture)[^10..]}");

    public async Task<PagamentoNexiCreato> CreaPagamentoAsync(CredenzialiNexi credenziali, string idOrdineNexi, RichiestaOrdineProvider richiesta, CancellationToken annullamento)
    {
        ArgumentNullException.ThrowIfNull(credenziali);
        ArgumentNullException.ThrowIfNull(richiesta);
        if (richiesta.Importo <= 0 || !string.Equals(richiesta.Valuta, VarianteProdotto.ValutaPredefinita, StringComparison.Ordinal))
        {
            throw new ArgumentException("Importo o valuta non validi.", nameof(richiesta));
        }

        ControllaIdOrdine(idOrdineNexi);
        var importo = ImportoInCentesimi(richiesta.Importo);
        var ordine = new JsonObject
        {
            ["orderId"] = idOrdineNexi,
            ["amount"] = importo,
            ["currency"] = richiesta.Valuta,
            ["description"] = Tronca(richiesta.Descrizione, 100),
        };
        if (!string.IsNullOrWhiteSpace(richiesta.EmailCliente) || !string.IsNullOrWhiteSpace(richiesta.NomeCliente))
        {
            var cliente = new JsonObject();
            if (!string.IsNullOrWhiteSpace(richiesta.NomeCliente))
            {
                cliente["cardHolderName"] = Tronca(richiesta.NomeCliente, 50);
            }

            if (!string.IsNullOrWhiteSpace(richiesta.EmailCliente))
            {
                cliente["cardHolderEmail"] = Tronca(richiesta.EmailCliente, 254);
            }

            ordine["customerInfo"] = cliente;
        }

        var sessione = new JsonObject
        {
            ["actionType"] = "PAY",
            ["amount"] = importo,
            ["captureType"] = "IMPLICIT",
            ["language"] = CodiceLingua(richiesta.Lingua),
            ["resultUrl"] = richiesta.UrlRitorno.AbsoluteUri,
            ["cancelUrl"] = richiesta.UrlAnnullo.AbsoluteUri,
        };
        if (richiesta.UrlNotifica is not null)
        {
            sessione["notificationUrl"] = richiesta.UrlNotifica.AbsoluteUri;
        }

        var corpo = new JsonObject { ["order"] = ordine, ["paymentSession"] = sessione };
        using var client = _fabbrica.CreateClient(NomeClientHttp);
        using var messaggio = Costruisci(HttpMethod.Post, credenziali, "orders/hpp", richiesta.IdRichiesta, corpo.ToJsonString());
        using var risposta = await InviaAsync(client, messaggio, annullamento);
        var json = await LeggiGiornaleAsync(risposta, annullamento);
        if (!risposta.IsSuccessStatusCode)
        {
            throw Errore(risposta, json);
        }

        var pagina = Testo(json, "hostedPage");
        var token = Testo(json, "securityToken");
        if (!Uri.TryCreate(pagina, UriKind.Absolute, out var url) || !PaginaFidata(url) || string.IsNullOrWhiteSpace(token) || token.Length > 200)
        {
            throw new ProviderPagamentoException("Nexi ha risposto in un formato inatteso alla creazione del pagamento.");
        }

        return new PagamentoNexiCreato(url, token);
    }

    public async Task<EsitoCatturaProvider> LeggiOrdineAsync(CredenzialiNexi credenziali, string idOrdineNexi, CancellationToken annullamento) =>
        Interpreta(await LeggiGiornaleOrdineAsync(credenziali, idOrdineNexi, annullamento));

    /// <summary>I rimborsi eseguiti per l'ordine (anche dal pannello di Nexi): operazioni <c>REFUND</c> con risultato <c>REFUNDED</c>.</summary>
    public async Task<IReadOnlyList<RimborsoDelProvider>> LeggiRimborsiAsync(CredenzialiNexi credenziali, string idOrdineNexi, CancellationToken annullamento) =>
        Rimborsi(await LeggiGiornaleOrdineAsync(credenziali, idOrdineNexi, annullamento));

    private async Task<JsonElement> LeggiGiornaleOrdineAsync(CredenzialiNexi credenziali, string idOrdineNexi, CancellationToken annullamento)
    {
        ArgumentNullException.ThrowIfNull(credenziali);
        ControllaIdOrdine(idOrdineNexi);

        using var client = _fabbrica.CreateClient(NomeClientHttp);
        using var messaggio = Costruisci(HttpMethod.Get, credenziali, $"orders/{Uri.EscapeDataString(idOrdineNexi)}", Guid.NewGuid(), null);
        using var risposta = await InviaAsync(client, messaggio, annullamento);
        var json = await LeggiGiornaleAsync(risposta, annullamento);
        if (risposta.StatusCode == HttpStatusCode.NotFound)
        {
            // Il cliente non è mai arrivato alla pagina di pagamento: per Nexi l'ordine non esiste ancora (nessuna operazione).
            using var vuoto = JsonDocument.Parse("{}");
            return vuoto.RootElement.Clone();
        }

        return risposta.IsSuccessStatusCode ? json : throw Errore(risposta, json);
    }

    public static IReadOnlyList<RimborsoDelProvider> Rimborsi(JsonElement json) =>
        [.. Operazioni(json)
            .Where(o => Testo(o, "operationType") == "REFUND" && Testo(o, "operationResult") == "REFUNDED")
            .Select(o => (Id: Testo(o, "operationId"), Importo: LeggiImporto(Testo(o, "operationAmount")), Valuta: Testo(o, "operationCurrency")))
            .Where(r => r.Id is { Length: > 0 and <= 64 } && r.Importo is > 0 && r.Valuta is { Length: 3 })
            .Select(r => new RimborsoDelProvider(r.Id!, r.Importo!.Value, r.Valuta!))];

    /// <summary>
    /// Da operazioni e importi dell'ordine all'esito. Pagato solo con un'operazione <c>CAPTURE</c> eseguita (incasso confermato); in
    /// corso se c'è un'operazione in sospeso o un'autorizzazione non ancora incassata; rifiutato se il gateway ha respinto senza che ci sia
    /// poi un incasso; altrimenti nulla è stato approvato (cliente uscito, 3DS non completato).
    /// </summary>
    public static EsitoCatturaProvider Interpreta(JsonElement json)
    {
        var operazioni = Operazioni(json);
        foreach (var operazione in operazioni.Where(o => Testo(o, "operationType") == "CAPTURE" && Testo(o, "operationResult") == "EXECUTED"))
        {
            var importo = LeggiImporto(Testo(operazione, "operationAmount"));
            return new EsitoCatturaProvider(StatoCatturaProvider.Completata, Testo(operazione, "operationId"), importo, Testo(operazione, "operationCurrency"));
        }

        var risultati = operazioni.Select(o => Testo(o, "operationResult")).ToList();
        if (risultati.Contains("PENDING") || operazioni.Any(o => Testo(o, "operationType") == "AUTHORIZATION" && Testo(o, "operationResult") == "AUTHORIZED"))
        {
            return new EsitoCatturaProvider(StatoCatturaProvider.InSospeso, null, null, null);
        }

        if (risultati.Any(r => r is "DECLINED" or "DENIED_BY_RISK" or "THREEDS_FAILED" or "FAILED"))
        {
            return new EsitoCatturaProvider(StatoCatturaProvider.Rifiutata, null, null, null);
        }

        return new EsitoCatturaProvider(StatoCatturaProvider.NonApprovata, null, null, null);
    }

    public static string ImportoInCentesimi(decimal importo) =>
        decimal.Round(importo * 100m, 0, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture);

    private static decimal? LeggiImporto(string? centesimi) =>
        long.TryParse(centesimi, NumberStyles.None, CultureInfo.InvariantCulture, out var valore) ? valore / 100m : null;

    private static List<JsonElement> Operazioni(JsonElement json)
    {
        if (json.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        var elenco = json.TryGetProperty("operations", out var diretto) && diretto.ValueKind == JsonValueKind.Array
            ? diretto
            : json.TryGetProperty("orderStatus", out var stato) && stato.ValueKind == JsonValueKind.Object
                && stato.TryGetProperty("operations", out var annidato) && annidato.ValueKind == JsonValueKind.Array
                ? annidato
                : default;
        return elenco.ValueKind == JsonValueKind.Array ? [.. elenco.EnumerateArray().Where(o => o.ValueKind == JsonValueKind.Object)] : [];
    }

    private static string CodiceLingua(string? lingua) => (lingua ?? string.Empty).Trim().ToLowerInvariant().Split('-')[0] switch
    {
        "it" => "ita",
        "fr" => "fra",
        "de" => "deu",
        "es" => "spa",
        "pt" => "por",
        _ => "eng",
    };

    private static void ControllaIdOrdine(string idOrdine)
    {
        if (string.IsNullOrEmpty(idOrdine) || idOrdine.Length > LunghezzaMassimaIdOrdine || !idOrdine.All(c => c is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-' or '_' or '.'))
        {
            throw new ArgumentException("Identificativo dell'ordine presso Nexi non valido.", nameof(idOrdine));
        }
    }

    /// <summary>La pagina di pagamento deve essere HTTPS su un dominio di Nexi (<c>xpay.nexigroup.com</c>, <c>xpaysandbox.nexigroup.com</c>), senza porta insolita né credenziali nell'indirizzo.</summary>
    private static bool PaginaFidata(Uri uri) =>
        uri.Scheme == Uri.UriSchemeHttps
        && string.IsNullOrEmpty(uri.UserInfo)
        && uri.IsDefaultPort
        && uri.HostNameType == UriHostNameType.Dns
        && uri.Host.EndsWith(".nexigroup.com", StringComparison.OrdinalIgnoreCase);

    private static HttpRequestMessage Costruisci(HttpMethod metodo, CredenzialiNexi credenziali, string percorso, Guid idCorrelazione, string? corpo)
    {
        var baseUri = credenziali.Ambiente == AmbientePagamento.Live ? _baseLive : _baseSandbox;
        var messaggio = new HttpRequestMessage(metodo, new Uri(baseUri, percorso));
        messaggio.Headers.TryAddWithoutValidation("X-API-KEY", credenziali.ChiaveApi);
        messaggio.Headers.TryAddWithoutValidation("Correlation-Id", idCorrelazione.ToString("D"));
        if (corpo is not null)
        {
            messaggio.Content = new StringContent(corpo, System.Text.Encoding.UTF8, "application/json");
        }

        return messaggio;
    }

    private static async Task<HttpResponseMessage> InviaAsync(HttpClient client, HttpRequestMessage messaggio, CancellationToken annullamento)
    {
        using var limite = CancellationTokenSource.CreateLinkedTokenSource(annullamento);
        limite.CancelAfter(_attesaMassima);
        try
        {
            return await client.SendAsync(messaggio, limite.Token);
        }
        catch (HttpRequestException eccezione)
        {
            throw new ProviderPagamentoException("Nexi non è raggiungibile.", eccezione);
        }
        catch (OperationCanceledException eccezione) when (!annullamento.IsCancellationRequested)
        {
            throw new ProviderPagamentoException("Nexi non ha risposto in tempo.", eccezione);
        }
    }

    private static async Task<JsonElement> LeggiGiornaleAsync(HttpResponseMessage risposta, CancellationToken annullamento)
    {
        try
        {
            var testo = await risposta.Content.ReadAsStringAsync(annullamento);
            using var documento = JsonDocument.Parse(string.IsNullOrWhiteSpace(testo) ? "{}" : testo);
            return documento.RootElement.Clone();
        }
        catch (JsonException)
        {
            using var vuoto = JsonDocument.Parse("{}");
            return vuoto.RootElement.Clone();
        }
    }

    /// <summary>Solo il codice HTTP e i codici di errore del gateway (<c>GW0001</c>…), mai la descrizione né il corpo.</summary>
    private static ProviderPagamentoException Errore(HttpResponseMessage risposta, JsonElement json)
    {
        var codici = new List<string>();
        if (json.ValueKind == JsonValueKind.Object && json.TryGetProperty("errors", out var errori) && errori.ValueKind == JsonValueKind.Array)
        {
            codici.AddRange(errori.EnumerateArray().Select(e => Testo(e, "code")).Where(c => c is { Length: > 0 and <= 20 } && c.All(char.IsLetterOrDigit))!);
        }

        var dettaglio = codici.Count > 0 ? $" (codici {string.Join(", ", codici.Take(3))})" : string.Empty;
        return new ProviderPagamentoException($"Nexi ha risposto con HTTP {(int)risposta.StatusCode}{dettaglio}.");
    }

    private static string? Testo(JsonElement oggetto, string nome) =>
        oggetto.ValueKind == JsonValueKind.Object && oggetto.TryGetProperty(nome, out var valore) && valore.ValueKind == JsonValueKind.String ? valore.GetString() : null;

    private static string Tronca(string testo, int massimo) => testo.Length <= massimo ? testo : testo[..massimo];
}
