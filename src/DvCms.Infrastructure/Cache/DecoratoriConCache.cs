using DvCms.Application.Cms;
using DvCms.Application.Temi;
using DvCms.Application.Tenancy;
using DvCms.Extender;
using DvCms.Models.Cms;

namespace DvCms.Infrastructure.Cache;

/// <summary>
/// Lettura delle pagine pubbliche con cache (G-002). La chiave comprende lingua e slug richiesti e, tramite il sito, il tenant:
/// nessuna voce è condivisa fra siti. Si memorizzano solo risultati con contenuto o con reindirizzamento; le assenze (404)
/// no, così chi cerca indirizzi a caso non riempie la memoria.
/// </summary>
public sealed class ServizioPaginePubblicheConCache : IServizioPaginePubbliche
{
    private readonly IServizioPaginePubbliche _interno;
    private readonly ICachePubblica _cache;
    private readonly IContestoTenant _tenant;

    public ServizioPaginePubblicheConCache(IServizioPaginePubbliche interno, ICachePubblica cache, IContestoTenant tenant)
    {
        _interno = interno;
        _cache = cache;
        _tenant = tenant;
    }

    public async Task<IReadOnlyList<LinguaPubblica>> LingueAsync(CancellationToken annullamento) =>
        (await _cache.OttieniAsync<IReadOnlyList<LinguaPubblica>>(_tenant.IdSito, "lingue", async () => await _interno.LingueAsync(annullamento)))!;

    public Task<PaginaPubblica?> TrovaAsync(string? slug, string? codiceLingua, CancellationToken annullamento) =>
        _interno.TrovaAsync(slug, codiceLingua, annullamento);

    public async Task<RisoluzionePagina> RisolviAsync(string? slug, string? codiceLingua, CancellationToken annullamento)
    {
        // La chiave usa lo slug normalizzato (spazi tolti, minuscolo): varianti equivalenti dello stesso indirizzo ("/%20about",
        // "/About") condividono la voce e non possono riempire la cache. Uno slug che non si normalizza non è mai in cache.
        var slugVuoto = string.IsNullOrWhiteSpace(slug);
        var slugNormalizzato = string.Empty;
        if (!slugVuoto && !NormalizzatoreSlug.TentaNormalizza(slug, out slugNormalizzato))
        {
            return await _interno.RisolviAsync(slug, codiceLingua, annullamento);
        }

        var chiave = $"pagina|{codiceLingua?.Trim().ToLowerInvariant()}|{slugNormalizzato}";
        var risoluzione = await _cache.OttieniAsync(
            _tenant.IdSito,
            chiave,
            async () =>
            {
                var risultato = await _interno.RisolviAsync(slug, codiceLingua, annullamento);
                return risultato is { Pagina: null, PercorsoReindirizzamento: null } ? null : risultato;
            });
        return risoluzione ?? new RisoluzionePagina(null, null);
    }
}

/// <summary>Tema del sito con cache (G-002): si rilegge quando il sito viene invalidato (cambio di tema, di nome del sito).</summary>
public sealed class RisolutoreTemaConCache : IRisolutoreTema
{
    private readonly IRisolutoreTema _interno;
    private readonly ICachePubblica _cache;
    private readonly IContestoTenant _tenant;

    public RisolutoreTemaConCache(IRisolutoreTema interno, ICachePubblica cache, IContestoTenant tenant)
    {
        _interno = interno;
        _cache = cache;
        _tenant = tenant;
    }

    public async Task<TemaRisolto> RisolviAsync(CancellationToken annullamento) =>
        (await _cache.OttieniAsync(_tenant.IdSito, "tema", async () => await _interno.RisolviAsync(annullamento)))!;
}

/// <summary>Sitemap con cache (G-002): è la richiesta più costosa e quella che i motori di ricerca ripetono di più.</summary>
public sealed class ServizioSitemapConCache : IServizioSitemap
{
    private readonly IServizioSitemap _interno;
    private readonly ICachePubblica _cache;
    private readonly IContestoTenant _tenant;

    public ServizioSitemapConCache(IServizioSitemap interno, ICachePubblica cache, IContestoTenant tenant)
    {
        _interno = interno;
        _cache = cache;
        _tenant = tenant;
    }

    public async Task<SitemapSito> GeneraAsync(CancellationToken annullamento) =>
        (await _cache.OttieniAsync(_tenant.IdSito, "sitemap", async () => await _interno.GeneraAsync(annullamento)))!;
}
