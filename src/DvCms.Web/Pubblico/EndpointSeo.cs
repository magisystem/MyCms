using System.Globalization;
using System.Text;
using System.Xml;
using DvCms.Application.Cms;

namespace DvCms.Web.Pubblico;

/// <summary>
/// <c>/sitemap.xml</c> e <c>/robots.txt</c> del sito dell'host (G-003). Il tenant arriva dall'host come per ogni richiesta: ogni
/// dominio ha la sua sitemap con soli indirizzi sul proprio dominio, senza pagine di altri tenant o siti.
/// </summary>
public static class EndpointSeo
{
    private const string SpazioNomiSitemap = "http://www.sitemaps.org/schemas/sitemap/0.9";
    private const string SpazioNomiXhtml = "http://www.w3.org/1999/xhtml";

    public static IEndpointRouteBuilder MapSeo(this IEndpointRouteBuilder rotte)
    {
        ArgumentNullException.ThrowIfNull(rotte);

        string[] metodi = [HttpMethods.Get, HttpMethods.Head];
        rotte.MapMethods("/sitemap.xml", metodi, async (HttpContext contesto, IServizioSitemap sitemap) =>
        {
            var risultato = await sitemap.GeneraAsync(contesto.RequestAborted);
            contesto.Response.ContentType = "application/xml; charset=utf-8";
            contesto.Response.Headers.CacheControl = "public, max-age=300";
            if (HttpMethods.IsHead(contesto.Request.Method))
            {
                return;
            }

            await ScriviSitemapAsync(contesto.Response.Body, Origine(contesto.Request), risultato, contesto.RequestAborted);
        });

        rotte.MapMethods("/robots.txt", metodi, async (HttpContext contesto, IWebHostEnvironment ambiente) =>
        {
            contesto.Response.ContentType = "text/plain; charset=utf-8";
            contesto.Response.Headers.CacheControl = "public, max-age=300";
            if (!HttpMethods.IsHead(contesto.Request.Method))
            {
                await contesto.Response.WriteAsync(Robots(Origine(contesto.Request), ambiente.IsProduction()), Encoding.UTF8, contesto.RequestAborted);
            }
        });

        return rotte;
    }

    /// <summary>
    /// In produzione si indicizza tutto tranne l'area di amministrazione e si indica la sitemap; fuori dalla produzione
    /// (sviluppo, staging) si chiede ai motori di non indicizzare nulla, così un ambiente di prova non finisce nei risultati.
    /// </summary>
    internal static string Robots(string origine, bool produzione) => produzione
        ? $"User-agent: *\nDisallow: /admin/\n\nSitemap: {origine}/sitemap.xml\n"
        : "User-agent: *\nDisallow: /\n";

    internal static async Task ScriviSitemapAsync(Stream flusso, string origine, SitemapSito sitemap, CancellationToken annullamento)
    {
        var impostazioni = new XmlWriterSettings { Async = true, Encoding = new UTF8Encoding(false), Indent = true };
        await using var scrittore = XmlWriter.Create(flusso, impostazioni);
        await scrittore.WriteStartDocumentAsync();
        await scrittore.WriteStartElementAsync(null, "urlset", SpazioNomiSitemap);
        await scrittore.WriteAttributeStringAsync("xmlns", "xhtml", null, SpazioNomiXhtml);

        foreach (var voce in sitemap.Voci)
        {
            annullamento.ThrowIfCancellationRequested();
            await scrittore.WriteStartElementAsync(null, "url", null);
            await scrittore.WriteElementStringAsync(null, "loc", null, origine + voce.Percorso);
            await scrittore.WriteElementStringAsync(null, "lastmod", null, voce.ModificatoIlUtc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture));
            foreach (var alternativa in voce.Alternative)
            {
                await ScriviAlternativaAsync(scrittore, alternativa.CodiceLingua, origine + alternativa.Percorso);
            }

            if (voce.PercorsoPredefinito is { } predefinito)
            {
                await ScriviAlternativaAsync(scrittore, "x-default", origine + predefinito);
            }

            await scrittore.WriteEndElementAsync();
        }

        await scrittore.WriteEndElementAsync();
        await scrittore.WriteEndDocumentAsync();
        await scrittore.FlushAsync();
    }

    private static async Task ScriviAlternativaAsync(XmlWriter scrittore, string lingua, string indirizzo)
    {
        await scrittore.WriteStartElementAsync("xhtml", "link", SpazioNomiXhtml);
        await scrittore.WriteAttributeStringAsync(null, "rel", null, "alternate");
        await scrittore.WriteAttributeStringAsync(null, "hreflang", null, lingua);
        await scrittore.WriteAttributeStringAsync(null, "href", null, indirizzo);
        await scrittore.WriteEndElementAsync();
    }

    private static string Origine(HttpRequest richiesta) => $"{richiesta.Scheme}://{richiesta.Host}";
}
