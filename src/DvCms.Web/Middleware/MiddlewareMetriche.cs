using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace DvCms.Web.Middleware;

/// <summary>
/// Metriche delle richieste HTTP (M6), raccoglibili con OpenTelemetry o <c>dotnet-counters</c> sul meter <c>DvCms.Web</c>:
/// <c>dvcms.http.richieste</c> e <c>dvcms.http.durata</c> (ms), con tag a bassa cardinalità: <c>area</c> (admin, media, seo, statico, salute, pubblico), <c>classe</c> dello stato (2xx, 3xx, 4xx, 5xx) e <c>metodo</c> (solo i metodi noti). Mai percorsi, host, tenant o utenti nei tag.
/// </summary>
public sealed class MiddlewareMetriche
{
    public const string NomeMeter = "DvCms.Web";

    private static readonly Meter _meter = new(NomeMeter);
    private static readonly Counter<long> _richieste = _meter.CreateCounter<long>("dvcms.http.richieste", description: "Richieste HTTP completate");
    private static readonly Histogram<double> _durata = _meter.CreateHistogram<double>("dvcms.http.durata", "ms", "Durata delle richieste HTTP");
    private static readonly HashSet<string> _metodiNoti = new(StringComparer.OrdinalIgnoreCase) { "GET", "HEAD", "POST", "PUT", "DELETE", "OPTIONS", "PATCH" };

    private readonly RequestDelegate _successivo;

    public MiddlewareMetriche(RequestDelegate successivo) => _successivo = successivo;

    public async Task InvokeAsync(HttpContext contesto)
    {
        ArgumentNullException.ThrowIfNull(contesto);

        var inizio = Stopwatch.GetTimestamp();
        try
        {
            await _successivo(contesto);
        }
        finally
        {
            var stato = contesto.Response.StatusCode;
            // Un'eccezione non gestita finirà in 500: se la risposta non è ancora stata impostata si conta comunque come errore.
            var classe = stato >= 100 && stato < 600 ? $"{stato / 100}xx" : "altro";
            var tag = new TagList
            {
                { "area", Area(contesto.Request.Path) },
                { "classe", classe },
                { "metodo", _metodiNoti.Contains(contesto.Request.Method) ? contesto.Request.Method.ToUpperInvariant() : "altro" },
            };
            _richieste.Add(1, tag);
            _durata.Record(Stopwatch.GetElapsedTime(inizio).TotalMilliseconds, tag);
        }
    }

    public static string Area(PathString percorso)
    {
        if (percorso.StartsWithSegments("/health"))
        {
            return "salute";
        }

        if (percorso.StartsWithSegments("/admin"))
        {
            return "admin";
        }

        if (percorso.StartsWithSegments("/media"))
        {
            return "media";
        }

        if (percorso.StartsWithSegments("/sitemap.xml") || percorso.StartsWithSegments("/robots.txt"))
        {
            return "seo";
        }

        if (percorso.StartsWithSegments("/lib") || percorso.StartsWithSegments("/css") || percorso.StartsWithSegments("/js") || percorso.StartsWithSegments("/img"))
        {
            return "statico";
        }

        return "pubblico";
    }
}
