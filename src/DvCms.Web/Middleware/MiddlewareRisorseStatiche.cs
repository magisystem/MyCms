namespace DvCms.Web.Middleware;

/// <summary>
/// Sotto <c>/lib/</c> (librerie di terze parti copiate per intero, ADR-0013) si servono solo asset statici
/// dell'elenco consentito: CSS, JavaScript, font, immagini e mappe. Tutto il resto (pagine HTML demo, documentazione,
/// sorgenti, PDF e altri formati del kit) risponde 404, anche se il file esiste su disco.
/// </summary>
public sealed class MiddlewareRisorseStatiche
{
    public const string PrefissoLibrerie = "/lib/";

    private static readonly HashSet<string> _estensioniConsentite = new(StringComparer.OrdinalIgnoreCase)
    {
        ".css", ".js", ".mjs", ".map",
        ".woff", ".woff2", ".ttf", ".otf", ".eot",
        ".svg", ".png", ".jpg", ".jpeg", ".gif", ".webp", ".avif", ".ico",
        ".json",
    };

    private readonly RequestDelegate _successivo;

    public MiddlewareRisorseStatiche(RequestDelegate successivo)
    {
        _successivo = successivo;
    }

    public Task InvokeAsync(HttpContext contesto)
    {
        ArgumentNullException.ThrowIfNull(contesto);

        var percorso = contesto.Request.Path;
        if (percorso.StartsWithSegments("/lib", StringComparison.OrdinalIgnoreCase)
            && !_estensioniConsentite.Contains(Path.GetExtension(percorso.Value ?? string.Empty)))
        {
            contesto.Response.StatusCode = StatusCodes.Status404NotFound;
            return Task.CompletedTask;
        }

        return _successivo(contesto);
    }
}
