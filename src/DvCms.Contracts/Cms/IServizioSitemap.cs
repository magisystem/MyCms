using DvCms.DTO.Cms;

namespace DvCms.Contracts.Cms;

/// <summary>
/// Sitemap del sito dell'host (G-003). Contiene solo pagine pubblicate, nella revisione pubblicata, in lingue ancora abilitate,
/// con il loro percorso canonico e senza <c>noindex</c>; mai pagine di un altro tenant o sito. Si genera a ogni richiesta, quindi
/// è sempre coerente con pubblicazioni, ritiri e cambi di slug.
/// </summary>
public interface IServizioSitemap
{
    /// <summary>Numero massimo di indirizzi in una sitemap (limite del protocollo sitemaps.org).</summary>
    const int VociMassime = 50_000;

    Task<SitemapSito> GeneraAsync(CancellationToken annullamento);
}
