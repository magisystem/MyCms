using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using DvCms.Contracts.Negozio;
using DvCms.DTO.Negozio;
using DvCms.Models.Negozio;

namespace DvCms.Services.Negozio.Pagamenti;

/// <summary>
/// Client di PayPal Orders v2 (ADR-0020). Importi e valuta arrivano dal chiamante; qui si costruiscono richieste e si leggono
/// risposte. Nessuna richiesta porta dati di carta o di conto del cliente e nessun messaggio d'errore riporta il corpo della risposta
/// (potrebbe riprendere dati inviati): solo il codice HTTP e l'identificativo di diagnosi del provider.
/// </summary>
public sealed partial class ClientePayPal : IProviderPagamento
{
    public const string NomeClientHttp = "paypal";

    private static readonly Uri _baseSandbox = new("https://api-m.sandbox.paypal.com");
    private static readonly Uri _baseLive = new("https://api-m.paypal.com");
    private static readonly TimeSpan _attesaMassima = TimeSpan.FromSeconds(30);

    private readonly IHttpClientFactory _fabbrica;

    public ClientePayPal(IHttpClientFactory fabbrica)
    {
        _fabbrica = fabbrica;
    }

    public async Task<OrdineProvider> CreaOrdineAsync(CredenzialiPayPal credenziali, RichiestaOrdineProvider richiesta, CancellationToken annullamento)
    {
        ArgumentNullException.ThrowIfNull(credenziali);
        ArgumentNullException.ThrowIfNull(richiesta);
        if (richiesta.Importo <= 0 || !string.Equals(richiesta.Valuta, VarianteProdotto.ValutaPredefinita, StringComparison.Ordinal))
        {
            throw new ArgumentException("Importo o valuta non validi.", nameof(richiesta));
        }

        var corpo = new JsonObject
        {
            ["intent"] = "CAPTURE",
            ["purchase_units"] = new JsonArray
            {
                new JsonObject
                {
                    ["reference_id"] = Tronca(richiesta.Riferimento, 256),
                    ["custom_id"] = Tronca(richiesta.Riferimento, 127),
                    ["description"] = Tronca(richiesta.Descrizione, 127),
                    ["amount"] = new JsonObject
                    {
                        ["currency_code"] = richiesta.Valuta,
                        ["value"] = richiesta.Importo.ToString("0.00", CultureInfo.InvariantCulture),
                    },
                },
            },
            ["payment_source"] = new JsonObject
            {
                ["paypal"] = new JsonObject
                {
                    ["experience_context"] = new JsonObject
                    {
                        ["payment_method_preference"] = "IMMEDIATE_PAYMENT_REQUIRED",
                        ["shipping_preference"] = "NO_SHIPPING",
                        ["user_action"] = "PAY_NOW",
                        ["return_url"] = richiesta.UrlRitorno.AbsoluteUri,
                        ["cancel_url"] = richiesta.UrlAnnullo.AbsoluteUri,
                    },
                },
            },
        };

        using var client = _fabbrica.CreateClient(NomeClientHttp);
        var token = await OttieniTokenAsync(client, credenziali, annullamento);
        using var messaggio = Costruisci(HttpMethod.Post, credenziali, "/v2/checkout/orders", token, corpo.ToJsonString(), richiesta.IdRichiesta);
        using var risposta = await InviaAsync(client, messaggio, annullamento);
        var json = await LeggiGiornaleAsync(risposta, annullamento);
        if (!risposta.IsSuccessStatusCode)
        {
            throw Errore(risposta);
        }

        var id = json.GetProperty("id").GetString();
        var url = json.TryGetProperty("links", out var collegamenti)
            ? collegamenti.EnumerateArray()
                .Where(l => l.TryGetProperty("rel", out var rel) && rel.GetString() is "payer-action" or "approve")
                .Select(l => l.GetProperty("href").GetString())
                .FirstOrDefault(h => h is not null)
            : null;
        if (!IdProviderValido(id) || !Uri.TryCreate(url, UriKind.Absolute, out var approvazione) || !OspiteFidato(approvazione))
        {
            throw new ProviderPagamentoException("PayPal ha risposto in un formato inatteso alla creazione dell'ordine.");
        }

        return new OrdineProvider(id!, approvazione);
    }

    public async Task<EsitoCatturaProvider> CatturaAsync(CredenzialiPayPal credenziali, string idProvider, Guid idRichiesta, CancellationToken annullamento)
    {
        ArgumentNullException.ThrowIfNull(credenziali);
        ControllaId(idProvider);

        using var client = _fabbrica.CreateClient(NomeClientHttp);
        var token = await OttieniTokenAsync(client, credenziali, annullamento);
        using var messaggio = Costruisci(HttpMethod.Post, credenziali, $"/v2/checkout/orders/{idProvider}/capture", token, "{}", idRichiesta);
        using var risposta = await InviaAsync(client, messaggio, annullamento);
        var json = await LeggiGiornaleAsync(risposta, annullamento);
        if (risposta.IsSuccessStatusCode)
        {
            return Interpreta(json);
        }

        if (risposta.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            var problemi = Problemi(json);
            if (problemi.Contains("ORDER_NOT_APPROVED"))
            {
                return new EsitoCatturaProvider(StatoCatturaProvider.NonApprovata, null, null, null);
            }

            if (problemi.Contains("ORDER_ALREADY_CAPTURED"))
            {
                return await LeggiOrdineAsync(credenziali, idProvider, annullamento);
            }

            if (problemi.Overlaps(["INSTRUMENT_DECLINED", "PAYER_ACCOUNT_RESTRICTED", "TRANSACTION_REFUSED", "PAYER_CANNOT_PAY"]))
            {
                return new EsitoCatturaProvider(StatoCatturaProvider.Rifiutata, null, null, null);
            }
        }

        throw Errore(risposta);
    }

    public async Task<EsitoCatturaProvider> LeggiOrdineAsync(CredenzialiPayPal credenziali, string idProvider, CancellationToken annullamento)
    {
        ArgumentNullException.ThrowIfNull(credenziali);
        ControllaId(idProvider);

        using var client = _fabbrica.CreateClient(NomeClientHttp);
        var token = await OttieniTokenAsync(client, credenziali, annullamento);
        using var messaggio = Costruisci(HttpMethod.Get, credenziali, $"/v2/checkout/orders/{idProvider}", token, null, null);
        using var risposta = await InviaAsync(client, messaggio, annullamento);
        var json = await LeggiGiornaleAsync(risposta, annullamento);
        return risposta.IsSuccessStatusCode ? Interpreta(json) : throw Errore(risposta);
    }

    public async Task<EsitoRimborsoProvider> RimborsaAsync(CredenzialiPayPal credenziali, string idCattura, decimal importo, string valuta, Guid idRichiesta, string? nota, CancellationToken annullamento)
    {
        ArgumentNullException.ThrowIfNull(credenziali);
        ControllaId(idCattura);
        if (importo <= 0 || !string.Equals(valuta, VarianteProdotto.ValutaPredefinita, StringComparison.Ordinal))
        {
            throw new ArgumentException("Importo o valuta del rimborso non validi.", nameof(importo));
        }

        var corpo = new JsonObject
        {
            ["amount"] = new JsonObject
            {
                ["currency_code"] = valuta,
                ["value"] = importo.ToString("0.00", CultureInfo.InvariantCulture),
            },
        };
        if (!string.IsNullOrWhiteSpace(nota))
        {
            corpo["note_to_payer"] = Tronca(nota.Trim(), 255);
        }

        using var client = _fabbrica.CreateClient(NomeClientHttp);
        var token = await OttieniTokenAsync(client, credenziali, annullamento);
        using var messaggio = Costruisci(HttpMethod.Post, credenziali, $"/v2/payments/captures/{idCattura}/refund", token, corpo.ToJsonString(), idRichiesta);
        using var risposta = await InviaAsync(client, messaggio, annullamento);
        var json = await LeggiGiornaleAsync(risposta, annullamento);
        if (risposta.IsSuccessStatusCode)
        {
            return InterpretaRimborso(json);
        }

        // Un rifiuto motivato (importo oltre il rimborsabile, incasso già rimborsato, conto non abilitato) è un esito, non un guasto.
        return risposta.StatusCode == HttpStatusCode.UnprocessableEntity || risposta.StatusCode == HttpStatusCode.Forbidden
            ? new EsitoRimborsoProvider(StatoRimborsoProvider.Fallito, null)
            : throw Errore(risposta);
    }

    public async Task<EsitoRimborsoProvider> LeggiRimborsoAsync(CredenzialiPayPal credenziali, string idRimborso, CancellationToken annullamento)
    {
        ArgumentNullException.ThrowIfNull(credenziali);
        ControllaId(idRimborso);

        using var client = _fabbrica.CreateClient(NomeClientHttp);
        var token = await OttieniTokenAsync(client, credenziali, annullamento);
        using var messaggio = Costruisci(HttpMethod.Get, credenziali, $"/v2/payments/refunds/{idRimborso}", token, null, null);
        using var risposta = await InviaAsync(client, messaggio, annullamento);
        var json = await LeggiGiornaleAsync(risposta, annullamento);
        return risposta.IsSuccessStatusCode ? InterpretaRimborso(json) : throw Errore(risposta);
    }

    public async Task<bool> VerificaWebhookAsync(CredenzialiPayPal credenziali, IntestazioniWebhook intestazioni, string corpo, CancellationToken annullamento)
    {
        ArgumentNullException.ThrowIfNull(credenziali);
        ArgumentNullException.ThrowIfNull(intestazioni);
        ArgumentNullException.ThrowIfNull(corpo);

        JsonNode? evento;
        try
        {
            evento = JsonNode.Parse(corpo);
        }
        catch (JsonException)
        {
            return false;
        }

        if (evento is not JsonObject || string.IsNullOrWhiteSpace(credenziali.IdWebhook))
        {
            return false;
        }

        var richiesta = new JsonObject
        {
            ["auth_algo"] = intestazioni.AuthAlgo,
            ["cert_url"] = intestazioni.CertUrl,
            ["transmission_id"] = intestazioni.TransmissionId,
            ["transmission_sig"] = intestazioni.TransmissionSig,
            ["transmission_time"] = intestazioni.TransmissionTime,
            ["webhook_id"] = credenziali.IdWebhook,
            ["webhook_event"] = evento,
        };

        using var client = _fabbrica.CreateClient(NomeClientHttp);
        var token = await OttieniTokenAsync(client, credenziali, annullamento);
        using var messaggio = Costruisci(HttpMethod.Post, credenziali, "/v1/notifications/verify-webhook-signature", token, richiesta.ToJsonString(), null);
        using var risposta = await InviaAsync(client, messaggio, annullamento);
        var json = await LeggiGiornaleAsync(risposta, annullamento);
        if (!risposta.IsSuccessStatusCode)
        {
            throw Errore(risposta);
        }

        return json.TryGetProperty("verification_status", out var stato) && string.Equals(stato.GetString(), "SUCCESS", StringComparison.Ordinal);
    }

    private static Uri Base(CredenzialiPayPal credenziali) => credenziali.Ambiente == AmbientePagamento.Live ? _baseLive : _baseSandbox;

    private static HttpRequestMessage Costruisci(HttpMethod metodo, CredenzialiPayPal credenziali, string percorso, string token, string? corpo, Guid? idRichiesta)
    {
        var messaggio = new HttpRequestMessage(metodo, new Uri(Base(credenziali), percorso));
        messaggio.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        messaggio.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (idRichiesta is { } id)
        {
            // Idempotenza lato provider: ripetere la richiesta con lo stesso identificativo non crea né incassa due volte.
            messaggio.Headers.Add("PayPal-Request-Id", id.ToString("D"));
        }

        if (corpo is not null)
        {
            messaggio.Content = new StringContent(corpo, Encoding.UTF8, "application/json");
        }

        return messaggio;
    }

    private static async Task<string> OttieniTokenAsync(HttpClient client, CredenzialiPayPal credenziali, CancellationToken annullamento)
    {
        using var messaggio = new HttpRequestMessage(HttpMethod.Post, new Uri(Base(credenziali), "/v1/oauth2/token"));
        var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{credenziali.IdClient}:{credenziali.SegretoClient}"));
        messaggio.Headers.Authorization = new AuthenticationHeaderValue("Basic", base64);
        messaggio.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        messaggio.Content = new FormUrlEncodedContent([new KeyValuePair<string, string>("grant_type", "client_credentials")]);
        using var risposta = await InviaAsync(client, messaggio, annullamento);
        var json = await LeggiGiornaleAsync(risposta, annullamento);
        if (!risposta.IsSuccessStatusCode)
        {
            throw new ProviderPagamentoException($"PayPal ha rifiutato le credenziali del conto (HTTP {(int)risposta.StatusCode}).");
        }

        var token = json.TryGetProperty("access_token", out var valore) ? valore.GetString() : null;
        return string.IsNullOrEmpty(token) ? throw new ProviderPagamentoException("PayPal non ha restituito un token di accesso.") : token;
    }

    private static async Task<HttpResponseMessage> InviaAsync(HttpClient client, HttpRequestMessage messaggio, CancellationToken annullamento)
    {
        using var scadenza = CancellationTokenSource.CreateLinkedTokenSource(annullamento);
        scadenza.CancelAfter(_attesaMassima);
        try
        {
            return await client.SendAsync(messaggio, HttpCompletionOption.ResponseContentRead, scadenza.Token);
        }
        catch (HttpRequestException eccezione)
        {
            throw new ProviderPagamentoException("PayPal non è raggiungibile.", eccezione);
        }
        catch (OperationCanceledException eccezione) when (!annullamento.IsCancellationRequested)
        {
            throw new ProviderPagamentoException("PayPal non ha risposto in tempo.", eccezione);
        }
    }

    /// <summary>Legge il corpo come JSON; se non lo è restituisce un oggetto vuoto (l'esito lo decide il codice HTTP).</summary>
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

    private static ProviderPagamentoException Errore(HttpResponseMessage risposta)
    {
        var diagnosi = risposta.Headers.TryGetValues("PayPal-Debug-Id", out var valori) ? valori.FirstOrDefault() : null;
        var sicura = diagnosi is not null && Diagnosi().IsMatch(diagnosi) ? $", diagnosi {diagnosi}" : string.Empty;
        return new ProviderPagamentoException($"PayPal ha risposto con HTTP {(int)risposta.StatusCode}{sicura}.", (int)risposta.StatusCode);
    }

    private static HashSet<string> Problemi(JsonElement json)
    {
        var problemi = new HashSet<string>(StringComparer.Ordinal);
        if (json.TryGetProperty("details", out var dettagli) && dettagli.ValueKind == JsonValueKind.Array)
        {
            foreach (var dettaglio in dettagli.EnumerateArray())
            {
                if (dettaglio.TryGetProperty("issue", out var problema) && problema.GetString() is { } testo)
                {
                    problemi.Add(testo);
                }
            }
        }

        return problemi;
    }

    /// <summary>Traduce la risposta di un ordine (dopo cattura o in lettura) nello stato che interessa al negozio.</summary>
    private static EsitoCatturaProvider Interpreta(JsonElement json)
    {
        var stato = json.TryGetProperty("status", out var s) ? s.GetString() : null;
        JsonElement? cattura = null;
        if (json.TryGetProperty("purchase_units", out var unita) && unita.ValueKind == JsonValueKind.Array)
        {
            foreach (var unitaOrdine in unita.EnumerateArray())
            {
                if (unitaOrdine.TryGetProperty("payments", out var pagamenti)
                    && pagamenti.TryGetProperty("captures", out var catture)
                    && catture.ValueKind == JsonValueKind.Array
                    && catture.GetArrayLength() > 0)
                {
                    cattura = catture[0];
                    break;
                }
            }
        }

        if (cattura is not { } c)
        {
            return new EsitoCatturaProvider(StatoCatturaProvider.NonApprovata, null, null, null);
        }

        var statoCattura = c.TryGetProperty("status", out var sc) ? sc.GetString() : null;
        var id = c.TryGetProperty("id", out var ic) ? ic.GetString() : null;
        decimal? importo = null;
        string? valuta = null;
        if (c.TryGetProperty("amount", out var somma))
        {
            valuta = somma.TryGetProperty("currency_code", out var v) ? v.GetString() : null;
            if (somma.TryGetProperty("value", out var valore)
                && decimal.TryParse(valore.GetString(), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var letto))
            {
                importo = letto;
            }
        }

        var risultato = (stato, statoCattura) switch
        {
            ("COMPLETED", "COMPLETED") => StatoCatturaProvider.Completata,
            (_, "PENDING") => StatoCatturaProvider.InSospeso,
            (_, "DECLINED" or "FAILED" or "VOIDED") => StatoCatturaProvider.Rifiutata,
            (_, "COMPLETED") => StatoCatturaProvider.Completata,
            _ => StatoCatturaProvider.InSospeso,
        };
        return new EsitoCatturaProvider(risultato, id, importo, valuta);
    }

    private static EsitoRimborsoProvider InterpretaRimborso(JsonElement json)
    {
        var id = json.TryGetProperty("id", out var i) ? i.GetString() : null;
        var stato = json.TryGetProperty("status", out var s) ? s.GetString() : null;
        var risultato = stato switch
        {
            "COMPLETED" => StatoRimborsoProvider.Completato,
            "FAILED" or "CANCELLED" => StatoRimborsoProvider.Fallito,
            _ => StatoRimborsoProvider.InSospeso,
        };

        decimal? importo = null;
        string? valuta = null;
        if (json.TryGetProperty("amount", out var quantita) && quantita.ValueKind == JsonValueKind.Object)
        {
            importo = quantita.TryGetProperty("value", out var valore) && decimal.TryParse(valore.GetString(), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var letto) ? letto : null;
            valuta = quantita.TryGetProperty("currency_code", out var codice) ? codice.GetString() : null;
        }

        // L'incasso a cui si riferisce è nel collegamento «up» (.../v2/payments/captures/{id}).
        string? idCattura = null;
        if (json.TryGetProperty("links", out var collegamenti) && collegamenti.ValueKind == JsonValueKind.Array)
        {
            var su = collegamenti.EnumerateArray()
                .Where(l => l.ValueKind == JsonValueKind.Object && l.TryGetProperty("rel", out var rel) && rel.GetString() == "up" && l.TryGetProperty("href", out _))
                .Select(l => l.GetProperty("href").GetString())
                .FirstOrDefault(h => h is not null);
            var corrispondenza = su is null ? null : CollegamentoCattura().Match(su);
            idCattura = corrispondenza is { Success: true } ? corrispondenza.Groups[1].Value : null;
        }

        return new EsitoRimborsoProvider(risultato, IdProviderValido(id) ? id : null, importo, valuta, idCattura);
    }

    private static bool OspiteFidato(Uri uri) =>
        uri.Scheme == Uri.UriSchemeHttps
        && (string.Equals(uri.Host, "paypal.com", StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith(".paypal.com", StringComparison.OrdinalIgnoreCase));

    private static bool IdProviderValido(string? id) => id is not null && IdProvider().IsMatch(id);

    private static void ControllaId(string idProvider)
    {
        if (!IdProviderValido(idProvider))
        {
            throw new ArgumentException("Identificativo dell'ordine presso il provider non valido.", nameof(idProvider));
        }
    }

    private static string Tronca(string testo, int massimo) => testo.Length <= massimo ? testo : testo[..massimo];

    [GeneratedRegex("/captures/([A-Za-z0-9-]{1,64})(?:$|[/?#])", RegexOptions.CultureInvariant)]
    private static partial Regex CollegamentoCattura();

    [GeneratedRegex("^[A-Za-z0-9-]{1,64}$", RegexOptions.CultureInvariant)]
    private static partial Regex IdProvider();

    [GeneratedRegex("^[A-Za-z0-9]{1,40}$", RegexOptions.CultureInvariant)]
    private static partial Regex Diagnosi();
}
