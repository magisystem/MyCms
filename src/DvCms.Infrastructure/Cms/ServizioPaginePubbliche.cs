using DvCms.Application.Cms;
using DvCms.Application.Localizzazione;
using DvCms.Application.Tenancy;
using DvCms.Domain.Cms;
using DvCms.Infrastructure.Persistenza;
using Microsoft.EntityFrameworkCore;

namespace DvCms.Infrastructure.Cms;

public sealed class ServizioPaginePubbliche : IServizioPaginePubbliche
{
    private readonly ContestoDatabase _contesto;
    private readonly IContestoTenant _tenant;
    private readonly IContestoLingua _lingua;

    public ServizioPaginePubbliche(ContestoDatabase contesto, IContestoTenant tenant, IContestoLingua lingua)
    {
        _contesto = contesto;
        _tenant = tenant;
        _lingua = lingua;
    }

    public async Task<PaginaPubblica?> TrovaAsync(string? slug, CancellationToken annullamento)
    {
        var cercaIniziale = string.IsNullOrWhiteSpace(slug);
        var slugNormalizzato = string.Empty;
        if (!cercaIniziale && !NormalizzatoreSlug.TentaNormalizza(slug, out slugNormalizzato))
        {
            return null;
        }

        // Solo pagine con una revisione pubblicata, del sito dell'host (il filtro di tenant è già nel contesto).
        var idSito = _tenant.IdSito;
        var query =
            from pagina in _contesto.Pagine.AsNoTracking()
            join revisione in _contesto.RevisioniPagina.AsNoTracking() on pagina.IdRevisionePubblicata equals revisione.Id
            join lingua in _contesto.Lingue.AsNoTracking() on pagina.IdLingua equals lingua.Id
            where pagina.IdSito == idSito
                && pagina.Stato == StatoPagina.Pubblicata
                && lingua.Attiva
                && (cercaIniziale ? pagina.PaginaIniziale : pagina.Slug == slugNormalizzato)
            select new { pagina, revisione, lingua.Codice, lingua.Predefinita };

        var candidate = await query.ToListAsync(annullamento);
        var scelta = candidate.FirstOrDefault(riga => string.Equals(riga.Codice, _lingua.CodiceLingua, StringComparison.OrdinalIgnoreCase))
            ?? candidate.FirstOrDefault(riga => riga.Predefinita)
            ?? candidate.OrderBy(riga => riga.Codice, StringComparer.Ordinal).FirstOrDefault();

        return scelta is null
            ? null
            : new PaginaPubblica(
                scelta.revisione.Titolo,
                scelta.revisione.Contenuto,
                scelta.Codice,
                scelta.pagina.Slug,
                scelta.pagina.PubblicatoIlUtc ?? scelta.revisione.CreatoIlUtc);
    }
}
