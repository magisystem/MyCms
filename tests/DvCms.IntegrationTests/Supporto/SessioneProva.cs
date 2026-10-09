using System.Net;
using System.Text.RegularExpressions;
using Xunit;

namespace DvCms.IntegrationTests.Supporto;

/// <summary>
/// Browser minimale per i test web: un host fisso, cookie gestiti a mano (il contenitore di .NET li lega all'URL e non
/// all'intestazione Host) e invio di moduli con il token antiforgery.
/// </summary>
public sealed partial class SessioneProva
{
    private readonly HttpClient _client;
    private readonly Dictionary<string, string> _cookie = [];

    public SessioneProva(HttpClient client, string host)
    {
        _client = client;
        client.DefaultRequestHeaders.Host = host;
    }

    public IReadOnlyDictionary<string, string> Cookie => _cookie;

    public string? CookieAdmin => _cookie.GetValueOrDefault(".DvCms.Admin");

    public async Task<HttpResponseMessage> GetAsync(string percorso)
    {
        using var richiesta = new HttpRequestMessage(HttpMethod.Get, percorso);
        return await InviaAsync(richiesta);
    }

    /// <summary>Apre il modulo, ne legge il token antiforgery e lo invia con i campi indicati.</summary>
    public async Task<HttpResponseMessage> InviaModuloAsync(
        string percorsoModulo,
        IEnumerable<KeyValuePair<string, string>> campi,
        string? percorsoInvio = null,
        bool conToken = true)
    {
        string? token = null;
        if (conToken)
        {
            using var pagina = await GetAsync(percorsoModulo);
            var html = await pagina.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            token = TokenAntiforgery().Match(html) is { Success: true } trovato ? trovato.Groups[1].Value : null;
        }

        var contenuto = new List<KeyValuePair<string, string>>(campi);
        if (token is not null)
        {
            contenuto.Add(new KeyValuePair<string, string>("__RequestVerificationToken", token));
        }

        using var invio = new HttpRequestMessage(HttpMethod.Post, percorsoInvio ?? percorsoModulo)
        {
            Content = new FormUrlEncodedContent(contenuto),
        };
        return await InviaAsync(invio);
    }

    public async Task<HttpResponseMessage> AccediAsync(string email, string password, string percorsoAccesso = "/admin/accesso") =>
        await InviaModuloAsync(
            percorsoAccesso,
            [new("Email", email), new("Password", password)]);

    public async Task<HttpResponseMessage> InviaAsync(HttpRequestMessage richiesta)
    {
        if (_cookie.Count > 0)
        {
            richiesta.Headers.TryAddWithoutValidation("Cookie", string.Join("; ", _cookie.Select(coppia => $"{coppia.Key}={coppia.Value}")));
        }

        var risposta = await _client.SendAsync(richiesta, TestContext.Current.CancellationToken);
        AggiornaCookie(risposta);
        return risposta;
    }

    public static IEnumerable<string> IntestazioniSetCookie(HttpResponseMessage risposta) =>
        risposta.Headers.TryGetValues("Set-Cookie", out var valori) ? valori : [];

    private void AggiornaCookie(HttpResponseMessage risposta)
    {
        foreach (var intestazione in IntestazioniSetCookie(risposta))
        {
            var parti = intestazione.Split(';', StringSplitOptions.TrimEntries);
            var coppia = parti[0].Split('=', 2);
            if (coppia.Length != 2)
            {
                continue;
            }

            var scaduto = parti.Any(parte => parte.StartsWith("expires=", StringComparison.OrdinalIgnoreCase)
                && DateTimeOffset.TryParse(parte["expires=".Length..], out var scadenza)
                && scadenza < DateTimeOffset.UtcNow);
            if (scaduto || string.IsNullOrEmpty(coppia[1]))
            {
                _cookie.Remove(coppia[0]);
            }
            else
            {
                _cookie[coppia[0]] = coppia[1];
            }
        }
    }

    public static string? Destinazione(HttpResponseMessage risposta)
    {
        var destinazione = risposta.Headers.Location;
        return destinazione is { IsAbsoluteUri: true } ? destinazione.PathAndQuery : destinazione?.OriginalString;
    }

    public static bool EReindirizzamento(HttpResponseMessage risposta) =>
        risposta.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.Found or HttpStatusCode.SeeOther;

    [GeneratedRegex("name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"", RegexOptions.CultureInvariant)]
    private static partial Regex TokenAntiforgery();
}
