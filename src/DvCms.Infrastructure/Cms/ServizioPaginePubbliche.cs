using DvCms.Application.Cms;
using DvCms.Application.Tenancy;
using DvCms.Domain.Cms;
using DvCms.Infrastructure.Persistenza;
using Microsoft.EntityFrameworkCore;

namespace DvCms.Infrastructure.Cms;

public sealed class ServizioPaginePubbliche : IServizioPaginePubbliche
{
    private readonly ContestoDatabase _contesto;
    private readonly IContestoTenant _tenant;

    public ServizioPaginePubbliche(ContestoDatabase contesto, IContestoTenant tenant)
    {
        _contesto = contesto;
        _tenant = tenant;
    }

    public async Task<IReadOnlyList<LinguaPubblica>> LingueAsync(CancellationToken annullamento) =>
        [.. (await LingueDelSito.ElencaAsync(_contesto, _tenant.IdSito, annullamento)).Select(lingua => new LinguaPubblica(lingua.Codice, lingua.Nome, lingua.Predefinita))];

    public async Task<PaginaPubblica?> TrovaAsync(string? slug, string? codiceLingua, CancellationToken annullamento)
    {
        var cercaIniziale = string.IsNullOrWhiteSpace(slug);
        var slugNormalizzato = string.Empty;
        if (!cercaIniziale && !NormalizzatoreSlug.TentaNormalizza(slug, out slugNormalizzato))
        {
            return null;
        }

        var idSito = _tenant.IdSito;
        var lingueSito = await LingueDelSito.ElencaAsync(_contesto, idSito, annullamento);
        var lingua = string.IsNullOrWhiteSpace(codiceLingua)
            ? lingueSito.FirstOrDefault(abilitata => abilitata.Predefinita)
            : lingueSito.FirstOrDefault(abilitata => string.Equals(abilitata.Codice, codiceLingua, StringComparison.OrdinalIgnoreCase));
        if (lingua is null)
        {
            return null;
        }

        // Solo la revisione pubblicata di una pagina pubblicata, nella lingua richiesta, del sito dell'host (il filtro di tenant è nel contesto).
        var trovata = await (
            from pagina in _contesto.Pagine.AsNoTracking()
            join revisione in _contesto.RevisioniPagina.AsNoTracking() on pagina.IdRevisionePubblicata equals revisione.Id
            where pagina.IdSito == idSito
                && pagina.IdLingua == lingua.IdLingua
                && pagina.Stato == StatoPagina.Pubblicata
                && (cercaIniziale ? pagina.PaginaIniziale : pagina.Slug == slugNormalizzato)
            select new { pagina, revisione })
            .SingleOrDefaultAsync(annullamento);
        if (trovata is null)
        {
            return null;
        }

        // Le versioni collegate già pubblicate, nelle lingue ancora abilitate (per hreflang e selettore).
        var gruppo = await (
            from altra in _contesto.Pagine.AsNoTracking()
            where altra.IdSito == idSito && altra.IdGruppoTraduzione == trovata.pagina.IdGruppoTraduzione && altra.Stato == StatoPagina.Pubblicata
            select new { altra.IdLingua, altra.Slug, altra.PaginaIniziale })
            .ToListAsync(annullamento);
        var traduzioni = lingueSito
            .Select(abilitata => (Lingua: abilitata, Pagina: gruppo.FirstOrDefault(p => p.IdLingua == abilitata.IdLingua)))
            .Where(coppia => coppia.Pagina is not null)
            .Select(coppia => new TraduzionePubblica(
                coppia.Lingua.Codice,
                coppia.Lingua.Nome,
                LingueDelSito.Percorso(coppia.Lingua.Codice, coppia.Lingua.Predefinita, coppia.Pagina!.Slug, coppia.Pagina.PaginaIniziale),
                coppia.Lingua.Predefinita))
            .ToList();

        return new PaginaPubblica(
            trovata.revisione.Titolo,
            trovata.revisione.Contenuto,
            lingua.Codice,
            trovata.pagina.Slug,
            trovata.pagina.PubblicatoIlUtc ?? trovata.revisione.CreatoIlUtc,
            trovata.revisione.IdModello,
            trovata.revisione.VersioneModello,
            trovata.revisione.DatiModello,
            LingueDelSito.Percorso(lingua.Codice, lingua.Predefinita, trovata.pagina.Slug, trovata.pagina.PaginaIniziale),
            traduzioni);
    }
}
