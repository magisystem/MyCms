using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DvCms.Extender;
using DvCms.Models.Accessi;
using DvCms.Models.Email;

namespace DvCms.Emails;

/// <summary>Token ottenuti dal provider. <see cref="Aggiornamento"/> è nullo se il provider non ne ha emesso uno nuovo.</summary>
public sealed record TokenOAuth(string Accesso, string? Aggiornamento, DateTime ScadenzaUtc, string Scope)
{
    public override string ToString() => "TokenOAuth(…)";
}

/// <summary>Il provider ha rifiutato il rinnovo con <c>invalid_grant</c>: il consenso non vale più e serve ricollegare la casella.</summary>
public sealed class ConsensoRevocatoException : Exception
{
    public ConsensoRevocatoException()
    {
    }

    public ConsensoRevocatoException(string message)
        : base(message)
    {
    }

    public ConsensoRevocatoException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Le richieste OAuth 2.0 verso Microsoft e Google (ADR-0021): URL di autorizzazione con PKCE, scambio del codice, rinnovo del
/// token, lettura dell'indirizzo della casella, revoca. Endpoint fissi nel codice (nessun indirizzo preso dalle impostazioni:
/// niente SSRF); gli errori riportano solo canale, codice HTTP e codice OAuth standard, mai il corpo della risposta.
/// </summary>
public sealed class ClienteOAuthEmail
{
    private const string AutorizzazioneGoogle = "https://accounts.google.com/o/oauth2/v2/auth";
    private const string TokenGoogle = "https://oauth2.googleapis.com/token";
    private const string RevocaGoogle = "https://oauth2.googleapis.com/revoke";
    private const string InformazioniGoogle = "https://openidconnect.googleapis.com/v1/userinfo";
    private const string InformazioniMicrosoft = "https://graph.microsoft.com/v1.0/me?$select=mail,userPrincipalName";

    private readonly IHttpClientFactory _fabbrica;
    private readonly TimeProvider _orologio;

    public ClienteOAuthEmail(IHttpClientFactory fabbrica, TimeProvider orologio)
    {
        _fabbrica = fabbrica;
        _orologio = orologio;
    }

    /// <summary>Gli ambiti minimi: invio e, per riconoscere la casella, il profilo di base.</summary>
    public static string Ambiti(ProviderEmail provider) => provider switch
    {
        ProviderEmail.MicrosoftOAuth => "offline_access Mail.Send User.Read",
        ProviderEmail.GoogleOAuth => "openid email https://www.googleapis.com/auth/gmail.send",
        _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, "Il canale non usa OAuth."),
    };

    /// <summary>L'ambito che dà il diritto di inviare: se manca dai permessi concessi il collegamento è inutile.</summary>
    public static bool ConcedeInvio(ProviderEmail provider, string scopeConcessi) =>
        scopeConcessi.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries).Any(scope =>
            provider == ProviderEmail.MicrosoftOAuth
                ? scope.Equals("Mail.Send", StringComparison.OrdinalIgnoreCase)
                    || scope.EndsWith("/Mail.Send", StringComparison.OrdinalIgnoreCase)
                : scope.Equals("https://www.googleapis.com/auth/gmail.send", StringComparison.Ordinal));

    public static string NuovoVerificatore() => Base64Url(RandomNumberGenerator.GetBytes(48));

    public static string NuovoStato() => Base64Url(RandomNumberGenerator.GetBytes(32));

    public static string Sfida(string verificatore) => Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verificatore)));

    public static string CostruisciUrlAutorizzazione(ProviderEmail provider, IReadOnlyDictionary<string, string> app, string urlRitorno, string stato, string sfida)
    {
        var parametri = new List<KeyValuePair<string, string>>
        {
            new("client_id", app["IdClient"]),
            new("response_type", "code"),
            new("redirect_uri", urlRitorno),
            new("scope", Ambiti(provider)),
            new("state", stato),
            new("code_challenge", sfida),
            new("code_challenge_method", "S256"),
        };

        string indirizzo;
        if (provider == ProviderEmail.MicrosoftOAuth)
        {
            indirizzo = $"https://login.microsoftonline.com/{Uri.EscapeDataString(app["TenantMicrosoft"])}/oauth2/v2.0/authorize";
            parametri.Add(new("response_mode", "query"));
            parametri.Add(new("prompt", "select_account"));
        }
        else
        {
            indirizzo = AutorizzazioneGoogle;
            parametri.Add(new("access_type", "offline"));
            parametri.Add(new("prompt", "consent"));
            parametri.Add(new("include_granted_scopes", "false"));
        }

        return indirizzo + "?" + string.Join('&', parametri.Select(p => $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value)}"));
    }

    public Task<TokenOAuth> ScambiaCodiceAsync(
        ProviderEmail provider,
        IReadOnlyDictionary<string, string> app,
        string urlRitorno,
        string codice,
        string verificatore,
        CancellationToken annullamento) =>
        RichiediTokenAsync(
            provider,
            app,
            new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = codice,
                ["redirect_uri"] = urlRitorno,
                ["code_verifier"] = verificatore,
            },
            annullamento);

    /// <exception cref="ConsensoRevocatoException">Il provider ha risposto <c>invalid_grant</c>.</exception>
    public Task<TokenOAuth> RinnovaAsync(ProviderEmail provider, IReadOnlyDictionary<string, string> app, string tokenAggiornamento, CancellationToken annullamento)
    {
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = tokenAggiornamento,
        };
        if (provider == ProviderEmail.MicrosoftOAuth)
        {
            form["scope"] = Ambiti(provider);
        }

        return RichiediTokenAsync(provider, app, form, annullamento);
    }

    /// <summary>L'indirizzo della casella a cui si è dato il consenso: sarà il mittente delle email.</summary>
    public async Task<string> LeggiCasellaAsync(ProviderEmail provider, string tokenAccesso, CancellationToken annullamento)
    {
        var canale = NomeCanale(provider);
        var client = _fabbrica.CreateClient(ComunicazioneEmail.NomeClientHttp);
        using var richiesta = new HttpRequestMessage(HttpMethod.Get, provider == ProviderEmail.MicrosoftOAuth ? InformazioniMicrosoft : InformazioniGoogle);
        richiesta.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", tokenAccesso);
        using var risposta = await ComunicazioneEmail.InviaAsync(client, canale, richiesta, annullamento);
        try
        {
            using var documento = await JsonDocument.ParseAsync(await risposta.Content.ReadAsStreamAsync(annullamento), cancellationToken: annullamento);
            var radice = documento.RootElement;
            var candidati = provider == ProviderEmail.MicrosoftOAuth ? new[] { "mail", "userPrincipalName" } : ["email"];
            foreach (var nome in candidati)
            {
                if (radice.TryGetProperty(nome, out var valore) && valore.ValueKind == JsonValueKind.String
                    && NormalizzatoreEmail.TentaNormalizza(valore.GetString(), out var normalizzato))
                {
                    return normalizzato;
                }
            }
        }
        catch (JsonException)
        {
            // Risposta non JSON: trattata come indirizzo mancante.
        }

        throw new InvioEmailFallitoException($"{canale}: il servizio non ha indicato l'indirizzo della casella.");
    }

    /// <summary>Revoca il consenso presso il provider dove esiste un endpoint (Google). Gli errori si ignorano: lo scollegamento locale avviene comunque.</summary>
    public async Task RevocaAsync(ProviderEmail provider, string token, CancellationToken annullamento)
    {
        if (provider != ProviderEmail.GoogleOAuth)
        {
            return;
        }

        try
        {
            var client = _fabbrica.CreateClient(ComunicazioneEmail.NomeClientHttp);
            using var richiesta = new HttpRequestMessage(HttpMethod.Post, RevocaGoogle)
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["token"] = token }),
            };
            using var risposta = await ComunicazioneEmail.InviaGrezzoAsync(client, "Google", richiesta, annullamento);
        }
        catch (InvioEmailFallitoException)
        {
            // Best effort.
        }
    }

    public static string NomeCanale(ProviderEmail provider) => provider == ProviderEmail.MicrosoftOAuth ? "Microsoft 365" : "Google";

    private async Task<TokenOAuth> RichiediTokenAsync(ProviderEmail provider, IReadOnlyDictionary<string, string> app, Dictionary<string, string> form, CancellationToken annullamento)
    {
        var canale = NomeCanale(provider);
        form["client_id"] = app["IdClient"];
        form["client_secret"] = app["SegretoClient"];
        var indirizzo = provider == ProviderEmail.MicrosoftOAuth
            ? $"https://login.microsoftonline.com/{Uri.EscapeDataString(app["TenantMicrosoft"])}/oauth2/v2.0/token"
            : TokenGoogle;

        var client = _fabbrica.CreateClient(ComunicazioneEmail.NomeClientHttp);
        using var richiesta = new HttpRequestMessage(HttpMethod.Post, indirizzo) { Content = new FormUrlEncodedContent(form) };
        using var risposta = await ComunicazioneEmail.InviaGrezzoAsync(client, canale, richiesta, annullamento);
        JsonDocument? documento = null;
        try
        {
            try
            {
                documento = await JsonDocument.ParseAsync(await risposta.Content.ReadAsStreamAsync(annullamento), cancellationToken: annullamento);
            }
            catch (JsonException)
            {
                // Corpo non JSON: si gestisce sotto come risposta non valida.
            }

            if (!risposta.IsSuccessStatusCode)
            {
                var errore = CodiceErrore(documento);
                if (errore == "invalid_grant" && form["grant_type"] == "refresh_token")
                {
                    throw new ConsensoRevocatoException($"{canale}: il consenso è stato revocato o è scaduto.");
                }

                throw new InvioEmailFallitoException($"{canale}: il servizio ha risposto con HTTP {(int)risposta.StatusCode} ({errore}).");
            }

            var radice = documento?.RootElement;
            if (radice is { ValueKind: JsonValueKind.Object } oggetto
                && oggetto.TryGetProperty("access_token", out var accesso) && accesso.ValueKind == JsonValueKind.String && accesso.GetString() is { Length: > 0 } token)
            {
                var durata = oggetto.TryGetProperty("expires_in", out var scadenza) && scadenza.ValueKind == JsonValueKind.Number && scadenza.TryGetInt32(out var secondi) && secondi > 0
                    ? secondi
                    : 3600;
                var aggiornamento = oggetto.TryGetProperty("refresh_token", out var rinnovo) && rinnovo.ValueKind == JsonValueKind.String && rinnovo.GetString() is { Length: > 0 } valore
                    ? valore
                    : null;
                var ambiti = oggetto.TryGetProperty("scope", out var scope) && scope.ValueKind == JsonValueKind.String ? scope.GetString() ?? string.Empty : string.Empty;
                return new TokenOAuth(token, aggiornamento, _orologio.GetUtcNow().UtcDateTime.AddSeconds(durata), ambiti);
            }

            throw new InvioEmailFallitoException($"{canale}: risposta di autenticazione non valida.");
        }
        finally
        {
            documento?.Dispose();
        }
    }

    /// <summary>Il solo codice OAuth standard (lettere minuscole e <c>_</c>): il resto del corpo non si riporta.</summary>
    private static string CodiceErrore(JsonDocument? documento)
    {
        if (documento is { RootElement: { ValueKind: JsonValueKind.Object } radice }
            && radice.TryGetProperty("error", out var errore) && errore.ValueKind == JsonValueKind.String
            && errore.GetString() is { Length: > 0 and <= 40 } codice && codice.All(c => c is (>= 'a' and <= 'z') or '_'))
        {
            return codice;
        }

        return "errore";
    }

    private static string Base64Url(byte[] dati) => Convert.ToBase64String(dati).Replace('+', '-').Replace('/', '_').TrimEnd('=');

    internal static string Impronta(ProviderEmail provider, IReadOnlyDictionary<string, string> app)
    {
        var testo = string.Create(CultureInfo.InvariantCulture, $"{provider}|{app.GetValueOrDefault("TenantMicrosoft")}|{app.GetValueOrDefault("IdClient")}");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(testo)));
    }
}
