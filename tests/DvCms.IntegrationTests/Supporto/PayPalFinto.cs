using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DvCms.IntegrationTests.Supporto;

/// <summary>Richiesta registrata dal PayPal finto.</summary>
public sealed record RichiestaPayPal(HttpMethod Metodo, string Percorso, string? Autorizzazione, string? IdRichiesta, string Corpo);

/// <summary>
/// PayPal finto per i test (ADR-0020): riproduce, con stato, le risposte che il negozio si aspetta da Orders v2 — token, creazione
/// con idempotenza su <c>PayPal-Request-Id</c>, approvazione del cliente, cattura, lettura e verifica della firma del webhook.
/// Nessuna richiesta esce dal processo e nessuna credenziale è vera.
/// </summary>
public sealed class PayPalFinto : HttpMessageHandler
{
    public const string FirmaValida = "firma-valida";

    private readonly object _blocco = new();
    private readonly Dictionary<string, OrdineFinto> _ordini = [];
    private readonly Dictionary<string, string> _perRichiesta = [];
    private readonly Dictionary<string, RimborsoFinto> _rimborsi = [];
    private readonly Dictionary<string, string> _rimborsiPerRichiesta = [];
    private int _contatore;
    private int _contatoreRimborsi;

    public ConcurrentQueue<RichiestaPayPal> Richieste { get; } = new();

    public string IdClient { get; init; } = "client-di-prova";

    public string SegretoClient { get; init; } = "segreto-di-prova";

    /// <summary>Come finisce la prossima cattura: <c>COMPLETED</c> (predefinito), <c>DECLINED</c>, <c>PENDING</c>.</summary>
    public string EsitoCattura { get; set; } = "COMPLETED";

    /// <summary>Come finisce il prossimo rimborso: <c>COMPLETED</c> (predefinito), <c>PENDING</c>, <c>FAILED</c>.</summary>
    public string EsitoRimborso { get; set; } = "COMPLETED";

    /// <summary>Un rimborso in sospeso si conclude (PayPal finisce la sua elaborazione).</summary>
    public void ConcludiRimborso(string id)
    {
        lock (_blocco)
        {
            _rimborsi[id].Stato = "COMPLETED";
        }
    }

    /// <summary>Se valorizzato, la cattura risponde con questo importo invece di quello dell'ordine (prova di manomissione).</summary>
    public string? ImportoCatturato { get; set; }

    /// <summary>Se impostata, ogni richiesta fallisce con questa eccezione (servizio non raggiungibile).</summary>
    public Exception? Eccezione { get; set; }

    public int Conta(HttpMethod metodo, string percorsoInizia) =>
        Richieste.Count(r => r.Metodo == metodo && r.Percorso.StartsWith(percorsoInizia, StringComparison.Ordinal));

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

    public string ImportoOrdine(string id)
    {
        lock (_blocco)
        {
            return _ordini[id].Importo;
        }
    }

    /// <summary>Un incasso in sospeso si conclude (PayPal finisce i suoi controlli).</summary>
    public void ConcludiSospeso(string id)
    {
        lock (_blocco)
        {
            _ordini[id].StatoCattura = "COMPLETED";
        }
    }

    /// <summary>Il cliente ha pagato e PayPal ha incassato, ma l'applicazione non l'ha saputo (ritorno perso, webhook non arrivato).</summary>
    public void IncassaSenzaAvvisare(string id)
    {
        lock (_blocco)
        {
            var ordine = _ordini[id];
            ordine.Approvato = true;
            ordine.Catturato = true;
            ordine.StatoCattura = "COMPLETED";
            ordine.ImportoCatturato = ordine.Importo;
        }
    }

    /// <summary>Il cliente approva il pagamento sulla pagina di PayPal.</summary>
    public void Approva(string id)
    {
        lock (_blocco)
        {
            _ordini[id].Approvato = true;
        }
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var corpo = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        var idRichiesta = request.Headers.TryGetValues("PayPal-Request-Id", out var valori) ? valori.FirstOrDefault() : null;
        Richieste.Enqueue(new RichiestaPayPal(request.Method, request.RequestUri!.AbsolutePath, request.Headers.Authorization?.ToString(), idRichiesta, corpo));
        if (Eccezione is not null)
        {
            throw Eccezione;
        }

        var percorso = request.RequestUri.AbsolutePath;
        if (percorso == "/v1/oauth2/token")
        {
            return Token(request);
        }

        if (request.Headers.Authorization?.ToString() != "Bearer TOKEN-FINTO")
        {
            return Risposta(HttpStatusCode.Unauthorized, new JsonObject { ["name"] = "AUTHENTICATION_FAILURE" });
        }

        if (percorso == "/v2/checkout/orders" && request.Method == HttpMethod.Post)
        {
            return Crea(corpo, idRichiesta);
        }

        if (percorso == "/v1/notifications/verify-webhook-signature")
        {
            var richiesta = JsonNode.Parse(corpo)!;
            var valida = richiesta["transmission_sig"]?.GetValue<string>() == FirmaValida && !string.IsNullOrEmpty(richiesta["webhook_id"]?.GetValue<string>());
            return Risposta(HttpStatusCode.OK, new JsonObject { ["verification_status"] = valida ? "SUCCESS" : "FAILURE" });
        }

        const string prefissoCatture = "/v2/payments/captures/";
        if (percorso.StartsWith(prefissoCatture, StringComparison.Ordinal) && percorso.EndsWith("/refund", StringComparison.Ordinal) && request.Method == HttpMethod.Post)
        {
            return Rimborsa(percorso[prefissoCatture.Length..^"/refund".Length], corpo, idRichiesta);
        }

        const string prefissoRimborsi = "/v2/payments/refunds/";
        if (percorso.StartsWith(prefissoRimborsi, StringComparison.Ordinal) && request.Method == HttpMethod.Get)
        {
            lock (_blocco)
            {
                return _rimborsi.TryGetValue(percorso[prefissoRimborsi.Length..], out var rimborso)
                    ? Risposta(HttpStatusCode.OK, new JsonObject
                    {
                        ["id"] = percorso[prefissoRimborsi.Length..],
                        ["status"] = rimborso.Stato,
                        ["amount"] = new JsonObject { ["value"] = rimborso.Importo.ToString("0.00", CultureInfo.InvariantCulture), ["currency_code"] = "EUR" },
                        ["links"] = new JsonArray
                        {
                            new JsonObject { ["rel"] = "self", ["href"] = $"https://api-m.sandbox.paypal.com/v2/payments/refunds/{percorso[prefissoRimborsi.Length..]}" },
                            new JsonObject { ["rel"] = "up", ["href"] = $"https://api-m.sandbox.paypal.com/v2/payments/captures/{rimborso.IdCattura}" },
                        },
                    })
                    : Risposta(HttpStatusCode.NotFound, new JsonObject { ["name"] = "RESOURCE_NOT_FOUND" });
            }
        }

        const string prefisso = "/v2/checkout/orders/";
        if (percorso.StartsWith(prefisso, StringComparison.Ordinal))
        {
            var resto = percorso[prefisso.Length..];
            if (resto.EndsWith("/capture", StringComparison.Ordinal) && request.Method == HttpMethod.Post)
            {
                return Cattura(resto[..^"/capture".Length], idRichiesta);
            }

            if (request.Method == HttpMethod.Get)
            {
                lock (_blocco)
                {
                    return _ordini.TryGetValue(resto, out var ordine)
                        ? Risposta(HttpStatusCode.OK, Descrivi(resto, ordine))
                        : Risposta(HttpStatusCode.NotFound, new JsonObject { ["name"] = "RESOURCE_NOT_FOUND" });
                }
            }
        }

        return Risposta(HttpStatusCode.NotFound, new JsonObject { ["name"] = "RESOURCE_NOT_FOUND" });
    }

    private HttpResponseMessage Token(HttpRequestMessage richiesta)
    {
        var atteso = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{IdClient}:{SegretoClient}"));
        return richiesta.Headers.Authorization?.ToString() == $"Basic {atteso}"
            ? Risposta(HttpStatusCode.OK, new JsonObject { ["access_token"] = "TOKEN-FINTO", ["expires_in"] = 3600 })
            : Risposta(HttpStatusCode.Unauthorized, new JsonObject { ["error"] = "invalid_client" });
    }

    private HttpResponseMessage Crea(string corpo, string? idRichiesta)
    {
        lock (_blocco)
        {
            if (idRichiesta is not null && _perRichiesta.TryGetValue(idRichiesta, out var esistente))
            {
                return Risposta(HttpStatusCode.OK, Creato(esistente));
            }

            var unita = JsonNode.Parse(corpo)!["purchase_units"]![0]!;
            var id = $"ORD-{Interlocked.Increment(ref _contatore):D4}";
            _ordini[id] = new OrdineFinto(
                unita["amount"]!["value"]!.GetValue<string>(),
                unita["amount"]!["currency_code"]!.GetValue<string>(),
                unita["custom_id"]?.GetValue<string>());
            if (idRichiesta is not null)
            {
                _perRichiesta[idRichiesta] = id;
            }

            return Risposta(HttpStatusCode.Created, Creato(id));
        }
    }

    private static JsonObject Creato(string id) => new()
    {
        ["id"] = id,
        ["status"] = "PAYER_ACTION_REQUIRED",
        ["links"] = new JsonArray
        {
            new JsonObject { ["href"] = $"https://api-m.sandbox.paypal.com/v2/checkout/orders/{id}", ["rel"] = "self", ["method"] = "GET" },
            new JsonObject { ["href"] = $"https://www.sandbox.paypal.com/checkoutnow?token={id}", ["rel"] = "payer-action", ["method"] = "GET" },
        },
    };

    private HttpResponseMessage Cattura(string id, string? idRichiesta)
    {
        lock (_blocco)
        {
            if (!_ordini.TryGetValue(id, out var ordine))
            {
                return Risposta(HttpStatusCode.NotFound, new JsonObject { ["name"] = "RESOURCE_NOT_FOUND" });
            }

            if (ordine.Catturato)
            {
                // Stessa richiesta ripetuta: stessa risposta. Una richiesta diversa su un ordine già incassato è un errore.
                return idRichiesta is not null && idRichiesta == ordine.IdRichiestaCattura
                    ? Risposta(HttpStatusCode.Created, Descrivi(id, ordine))
                    : Errore422("ORDER_ALREADY_CAPTURED");
            }

            if (!ordine.Approvato)
            {
                return Errore422("ORDER_NOT_APPROVED");
            }

            ordine.Catturato = true;
            ordine.StatoCattura = EsitoCattura;
            ordine.ImportoCatturato = ImportoCatturato ?? ordine.Importo;
            ordine.IdRichiestaCattura = idRichiesta;
            return Risposta(HttpStatusCode.Created, Descrivi(id, ordine));
        }
    }

    /// <summary>Un rimborso fatto direttamente dal pannello PayPal del commerciante (non richiesto dal negozio): restituisce il suo identificativo.</summary>
    public string RimborsaEsternamente(string idOrdineProvider, decimal importo)
    {
        lock (_blocco)
        {
            var ordine = _ordini[idOrdineProvider];
            var id = $"REF-{Interlocked.Increment(ref _contatoreRimborsi):D4}";
            ordine.Rimborsato += importo;
            _rimborsi[id] = new RimborsoFinto { Stato = "COMPLETED", Importo = importo, IdCattura = $"CAP-{idOrdineProvider}" };
            return id;
        }
    }

    private HttpResponseMessage Rimborsa(string idCattura, string corpo, string? idRichiesta)
    {
        lock (_blocco)
        {
            if (idRichiesta is not null && _rimborsiPerRichiesta.TryGetValue(idRichiesta, out var esistente))
            {
                return Risposta(HttpStatusCode.Created, new JsonObject { ["id"] = esistente, ["status"] = _rimborsi[esistente].Stato });
            }

            var ordine = _ordini.Values.FirstOrDefault(o => o.Catturato && $"CAP-{_ordini.First(x => x.Value == o).Key}" == idCattura);
            if (ordine is null)
            {
                return Risposta(HttpStatusCode.NotFound, new JsonObject { ["name"] = "RESOURCE_NOT_FOUND" });
            }

            var importo = decimal.Parse(JsonNode.Parse(corpo)!["amount"]!["value"]!.GetValue<string>(), CultureInfo.InvariantCulture);
            var incassato = decimal.Parse(ordine.ImportoCatturato, CultureInfo.InvariantCulture);
            if (EsitoRimborso == "FAILED" || importo > incassato - ordine.Rimborsato)
            {
                return Errore422(importo > incassato - ordine.Rimborsato ? "REFUND_AMOUNT_EXCEEDED" : "REFUND_FAILED");
            }

            var id = $"REF-{Interlocked.Increment(ref _contatoreRimborsi):D4}";
            ordine.Rimborsato += importo;
            _rimborsi[id] = new RimborsoFinto { Stato = EsitoRimborso, Importo = importo, IdCattura = idCattura };
            if (idRichiesta is not null)
            {
                _rimborsiPerRichiesta[idRichiesta] = id;
            }

            return Risposta(HttpStatusCode.Created, new JsonObject { ["id"] = id, ["status"] = EsitoRimborso });
        }
    }

    private static HttpResponseMessage Errore422(string problema) => Risposta(
        HttpStatusCode.UnprocessableEntity,
        new JsonObject { ["name"] = "UNPROCESSABLE_ENTITY", ["details"] = new JsonArray { new JsonObject { ["issue"] = problema } } });

    private static JsonObject Descrivi(string id, OrdineFinto ordine)
    {
        var json = new JsonObject
        {
            ["id"] = id,
            ["status"] = ordine.Catturato ? (ordine.StatoCattura == "COMPLETED" ? "COMPLETED" : "APPROVED") : (ordine.Approvato ? "APPROVED" : "PAYER_ACTION_REQUIRED"),
        };
        if (ordine.Catturato)
        {
            json["purchase_units"] = new JsonArray
            {
                new JsonObject
                {
                    ["payments"] = new JsonObject
                    {
                        ["captures"] = new JsonArray
                        {
                            new JsonObject
                            {
                                ["id"] = $"CAP-{id}",
                                ["status"] = ordine.StatoCattura,
                                ["amount"] = new JsonObject { ["currency_code"] = ordine.Valuta, ["value"] = ordine.ImportoCatturato },
                            },
                        },
                    },
                },
            };
        }

        return json;
    }

    private static HttpResponseMessage Risposta(HttpStatusCode codice, JsonObject corpo) => new(codice)
    {
        Content = new StringContent(corpo.ToJsonString(), Encoding.UTF8, "application/json"),
    };

    private sealed class OrdineFinto
    {
        public OrdineFinto(string importo, string valuta, string? riferimento)
        {
            Importo = importo;
            Valuta = valuta;
            Riferimento = riferimento;
        }

        public string Importo { get; }

        public string Valuta { get; }

        public string? Riferimento { get; }

        public bool Approvato { get; set; }

        public bool Catturato { get; set; }

        public string StatoCattura { get; set; } = "COMPLETED";

        public string ImportoCatturato { get; set; } = string.Empty;

        public string? IdRichiestaCattura { get; set; }

        public decimal Rimborsato { get; set; }
    }

    private sealed class RimborsoFinto
    {
        public string Stato { get; set; } = "COMPLETED";

        public decimal Importo { get; set; }

        public string? IdCattura { get; set; }
    }
}

/// <summary>Fabbrica di client HTTP che serve sempre lo stesso gestore finto.</summary>
public sealed class FabbricaHttpPayPalFinta : IHttpClientFactory
{
    private readonly HttpMessageHandler _gestore;

    public FabbricaHttpPayPalFinta(HttpMessageHandler gestore)
    {
        _gestore = gestore;
    }

    public HttpClient CreateClient(string name) => new(_gestore, disposeHandler: false);
}
