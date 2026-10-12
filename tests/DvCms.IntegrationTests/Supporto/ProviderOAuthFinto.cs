using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Text;

namespace DvCms.IntegrationTests.Supporto;

/// <summary>Richiesta registrata dal provider OAuth finto (il corpo contiene i parametri inviati, mai usato per i segreti veri).</summary>
public sealed record RichiestaOAuth(HttpMethod Metodo, Uri Indirizzo, string? Autorizzazione, IReadOnlyDictionary<string, string> Modulo, string Corpo);

/// <summary>
/// Microsoft 365 e Google finti (ADR-0021): endpoint dei token (scambio del codice e rinnovo), informazioni sulla casella, revoca e
/// invio. Registra le richieste, ruota il refresh token come Microsoft e può simulare il consenso revocato (<c>invalid_grant</c>).
/// Nessuna richiesta esce dal processo e nessuna credenziale è vera.
/// </summary>
public sealed class ProviderOAuthFinto : HttpMessageHandler
{
    public const string CodiceValido = "codice-di-prova";

    private int _contatoreAccesso;
    private int _contatoreRinnovo;

    public ConcurrentQueue<RichiestaOAuth> Richieste { get; } = new();

    public string Casella { get; set; } = "mittente@esempio.it";

    /// <summary>Ambiti restituiti allo scambio del codice. Se manca quello di invio, il collegamento è inutile.</summary>
    public string? AmbitiConcessi { get; set; }

    /// <summary>Se falso lo scambio del codice non restituisce un refresh token (Google senza consenso esplicito).</summary>
    public bool EmetteRefreshToken { get; set; } = true;

    /// <summary>Se vero il rinnovo risponde <c>invalid_grant</c>: il consenso è stato revocato.</summary>
    public bool ConsensoRevocato { get; set; }

    public int DurataAccessoSecondi { get; set; } = 3600;

    public Exception? Eccezione { get; set; }

    public string? UltimoVerificatore { get; private set; }

    public string? UltimoRefreshTokenUsato { get; private set; }

    public IEnumerable<RichiestaOAuth> A(string host, string percorsoInizia) =>
        Richieste.Where(r => r.Indirizzo.Host == host && r.Indirizzo.AbsolutePath.StartsWith(percorsoInizia, StringComparison.Ordinal));

    public int ContaRinnovi() => Richieste.Count(r => r.Modulo.GetValueOrDefault("grant_type") == "refresh_token");

    public int ContaInvii() => Richieste.Count(r => r.Indirizzo.AbsolutePath.EndsWith("/sendMail", StringComparison.Ordinal) || r.Indirizzo.AbsolutePath.EndsWith("/messages/send", StringComparison.Ordinal));

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var corpo = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        var modulo = request.Content?.Headers.ContentType?.MediaType == "application/x-www-form-urlencoded" ? Modulo(corpo) : new Dictionary<string, string>();
        Richieste.Enqueue(new RichiestaOAuth(request.Method, request.RequestUri!, request.Headers.Authorization?.ToString(), modulo, corpo));
        if (Eccezione is not null)
        {
            throw Eccezione;
        }

        var host = request.RequestUri!.Host;
        var percorso = request.RequestUri.AbsolutePath;
        if (percorso.EndsWith("/token", StringComparison.Ordinal))
        {
            return Token(host, modulo);
        }

        if (host == "oauth2.googleapis.com" && percorso == "/revoke")
        {
            return new HttpResponseMessage(HttpStatusCode.OK);
        }

        if (percorso.EndsWith("/me", StringComparison.Ordinal))
        {
            return Json($"{{\"mail\":\"{Casella}\",\"userPrincipalName\":\"upn-{Casella}\"}}");
        }

        if (percorso.EndsWith("/userinfo", StringComparison.Ordinal))
        {
            return Json($"{{\"email\":\"{Casella}\",\"email_verified\":true}}");
        }

        // Invio (Graph sendMail o Gmail messages/send).
        return new HttpResponseMessage(percorso.EndsWith("/sendMail", StringComparison.Ordinal) ? HttpStatusCode.Accepted : HttpStatusCode.OK) { Content = new StringContent("{}") };
    }

    private HttpResponseMessage Token(string host, Dictionary<string, string> modulo)
    {
        var google = host == "oauth2.googleapis.com";
        var tipo = modulo.GetValueOrDefault("grant_type");
        if (tipo == "authorization_code")
        {
            if (modulo.GetValueOrDefault("code") != CodiceValido || string.IsNullOrEmpty(modulo.GetValueOrDefault("code_verifier")) || string.IsNullOrEmpty(modulo.GetValueOrDefault("client_secret")))
            {
                return Errore("invalid_grant");
            }

            UltimoVerificatore = modulo["code_verifier"];
            var ambiti = AmbitiConcessi ?? (google ? "openid https://www.googleapis.com/auth/userinfo.email https://www.googleapis.com/auth/gmail.send" : "Mail.Send User.Read profile openid email");
            return Json(Risposta(google, ambiti, EmetteRefreshToken ? "RINNOVO-0" : null));
        }

        if (tipo == "refresh_token")
        {
            UltimoRefreshTokenUsato = modulo.GetValueOrDefault("refresh_token");
            if (ConsensoRevocato)
            {
                return Errore("invalid_grant");
            }

            // Microsoft ruota il refresh token a ogni rinnovo, Google no.
            return Json(Risposta(google, google ? "https://www.googleapis.com/auth/gmail.send" : "Mail.Send", google ? null : $"RINNOVO-{Interlocked.Increment(ref _contatoreRinnovo)}"));
        }

        return Errore("unsupported_grant_type");
    }

    private string Risposta(bool google, string ambiti, string? refresh)
    {
        var accesso = $"ACCESSO-{Interlocked.Increment(ref _contatoreAccesso)}";
        var testo = new StringBuilder("{");
        testo.Append(CultureInfo.InvariantCulture, $"\"access_token\":\"{accesso}\",\"expires_in\":{DurataAccessoSecondi},\"token_type\":\"Bearer\",\"scope\":\"{ambiti}\"");
        if (refresh is not null)
        {
            testo.Append(CultureInfo.InvariantCulture, $",\"refresh_token\":\"{refresh}\"");
        }

        testo.Append('}');
        _ = google;
        return testo.ToString();
    }

    private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage Errore(string codice) =>
        new(HttpStatusCode.BadRequest) { Content = new StringContent($"{{\"error\":\"{codice}\",\"error_description\":\"DESCRIZIONE-RISERVATA\"}}", Encoding.UTF8, "application/json") };

    private static Dictionary<string, string> Modulo(string corpo) =>
        corpo.Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(parte => parte.Split('=', 2))
            .ToDictionary(p => Uri.UnescapeDataString(p[0].Replace('+', ' ')), p => p.Length > 1 ? Uri.UnescapeDataString(p[1].Replace('+', ' ')) : string.Empty);
}
