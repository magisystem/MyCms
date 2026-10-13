using DvCms.Contracts.Cms;
using DvCms.Contracts.Tenancy;
using DvCms.Data;
using DvCms.Data.Cms;
using DvCms.DTO.Cms;
using DvCms.Models.Cms;
using Microsoft.EntityFrameworkCore;

namespace DvCms.Infrastructure.Cms;

public sealed class ServizioSitemap : IServizioSitemap
{
    private readonly ContestoDatabase _contesto;
    private readonly IContestoTenant _tenant;

    public ServizioSitemap(ContestoDatabase contesto, IContestoTenant tenant)
    {
        _contesto = contesto;
        _tenant = tenant;
    }

    public async Task<SitemapSito> GeneraAsync(CancellationToken annullamento)
    {
        var idSito = _tenant.IdSito;
        var lingue = await LingueDelSito.ElencaAsync(_contesto, idSito, annullamento);
        var idLingue = lingue.Select(lingua => lingua.IdLingua).ToList();

        // Solo pagine pubblicate, con la revisione pubblicata che non chiede noindex (il filtro di tenant è nel contesto).
        var pagine = await (
            from pagina in _contesto.Pagine.AsNoTracking()
            join revisione in _contesto.RevisioniPagina.AsNoTracking() on pagina.IdRevisionePubblicata equals revisione.Id
            where pagina.IdSito == idSito
                && pagina.Stato == StatoPagina.Pubblicata
                && !revisione.NonIndicizzare
                && idLingue.Contains(pagina.IdLingua)
            select new { pagina.IdGruppoTraduzione, pagina.IdLingua, pagina.Slug, pagina.PaginaIniziale, Modificata = pagina.PubblicatoIlUtc ?? revisione.CreatoIlUtc })
            .ToListAsync(annullamento);

        string PercorsoDi(LinguaAbilitata lingua, string slug, bool iniziale) => LingueDelSito.Percorso(lingua.Codice, lingua.Predefinita, slug, iniziale);

        var voci = new List<VoceSitemap>();
        foreach (var gruppo in pagine.GroupBy(pagina => pagina.IdGruppoTraduzione))
        {
            var versioni = gruppo
                .Select(pagina => (Pagina: pagina, Lingua: lingue.Single(lingua => lingua.IdLingua == pagina.IdLingua)))
                .Select(coppia => (coppia.Pagina, coppia.Lingua, Percorso: PercorsoDi(coppia.Lingua, coppia.Pagina.Slug, coppia.Pagina.PaginaIniziale)))
                .OrderBy(versione => versione.Lingua.Codice, StringComparer.Ordinal)
                .ToList();
            var alternative = versioni.Count > 1
                ? versioni.Select(versione => new AlternativaSitemap(versione.Lingua.Codice, versione.Percorso)).ToList()
                : [];
            var predefinita = versioni.Count > 1 ? versioni.FirstOrDefault(versione => versione.Lingua.Predefinita).Percorso : null;

            foreach (var versione in versioni)
            {
                voci.Add(new VoceSitemap(versione.Percorso, DateTime.SpecifyKind(versione.Pagina.Modificata, DateTimeKind.Utc), alternative, predefinita));
            }
        }

        voci.Sort((a, b) => string.CompareOrdinal(a.Percorso, b.Percorso));
        return voci.Count > IServizioSitemap.VociMassime
            ? new SitemapSito(voci.GetRange(0, IServizioSitemap.VociMassime), Troncata: true)
            : new SitemapSito(voci, Troncata: false);
    }
}
