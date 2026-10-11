using DvCms.Application.Negozio;
using DvCms.Application.Tenancy;
using DvCms.Domain.Negozio;
using DvCms.Infrastructure.Cms;
using DvCms.Infrastructure.Persistenza;
using Microsoft.EntityFrameworkCore;

namespace DvCms.Infrastructure.Negozio;

/// <summary>Vetrina pubblica (vedi <see cref="IServizioVetrina"/>). Lavora solo sul tenant e sul sito dell'host.</summary>
public sealed class ServizioVetrina : IServizioVetrina
{
    private readonly ContestoDatabase _contesto;
    private readonly IContestoTenant _tenant;

    public ServizioVetrina(ContestoDatabase contesto, IContestoTenant tenant)
    {
        _contesto = contesto;
        _tenant = tenant;
    }

    public async Task<IReadOnlyList<ProdottoInVetrina>> ElencaAsync(string codiceLingua, CancellationToken annullamento)
    {
        var idSito = _tenant.IdSito;
        if (!await NegozioAttivoAsync(idSito, annullamento))
        {
            return [];
        }

        var (idRichiesta, idPredefinita) = await LinguaAsync(idSito, codiceLingua, annullamento);
        var prodotti = await _contesto.Prodotti.AsNoTracking().Where(p => p.IdSito == idSito && p.Stato == StatoProdotto.Pubblicato).ToListAsync(annullamento);
        var idProdotti = prodotti.Select(p => p.Id).ToList();
        var varianti = await _contesto.VariantiProdotto.AsNoTracking().Where(v => v.IdSito == idSito && v.Attiva && idProdotti.Contains(v.IdProdotto)).ToListAsync(annullamento);
        var nomi = await _contesto.TraduzioniProdotto.AsNoTracking().Where(t => t.IdSito == idSito && idProdotti.Contains(t.IdProdotto)).ToListAsync(annullamento);
        var massimi = await MassimiAsync(idSito, varianti.Select(v => v.Id).ToList(), annullamento);

        return [.. prodotti
            .Select(p => (Prodotto: p, Varianti: varianti.Where(v => v.IdProdotto == p.Id).ToList()))
            .Where(x => x.Varianti.Count > 0)
            .Select(x => new ProdottoInVetrina(
                x.Prodotto.Id,
                Scegli(nomi.Where(t => t.IdProdotto == x.Prodotto.Id), t => t.IdLingua, t => t.Nome, idRichiesta, idPredefinita) ?? string.Empty,
                x.Varianti.Min(v => v.PrezzoLordo),
                VarianteProdotto.ValutaPredefinita,
                x.Varianti.Any(v => massimi[v.Id] > 0)))
            .OrderBy(p => p.Nome, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(p => p.Id)];
    }

    public async Task<ProdottoPubblico?> LeggiAsync(Guid idProdotto, string codiceLingua, CancellationToken annullamento)
    {
        var idSito = _tenant.IdSito;
        if (!await NegozioAttivoAsync(idSito, annullamento))
        {
            return null;
        }

        var prodotto = await _contesto.Prodotti.AsNoTracking().SingleOrDefaultAsync(p => p.Id == idProdotto && p.IdSito == idSito && p.Stato == StatoProdotto.Pubblicato, annullamento);
        if (prodotto is null)
        {
            return null;
        }

        var varianti = await _contesto.VariantiProdotto.AsNoTracking().Where(v => v.IdProdotto == idProdotto && v.Attiva).OrderBy(v => v.Posizione).ToListAsync(annullamento);
        if (varianti.Count == 0)
        {
            return null;
        }

        var (idRichiesta, idPredefinita) = await LinguaAsync(idSito, codiceLingua, annullamento);
        var traduzioni = await _contesto.TraduzioniProdotto.AsNoTracking().Where(t => t.IdProdotto == idProdotto).ToListAsync(annullamento);
        var idVarianti = varianti.Select(v => v.Id).ToList();
        var etichette = await _contesto.TraduzioniVarianteProdotto.AsNoTracking().Where(t => idVarianti.Contains(t.IdVariante)).ToListAsync(annullamento);
        var massimi = await MassimiAsync(idSito, idVarianti, annullamento);

        return new ProdottoPubblico(
            prodotto.Id,
            Scegli(traduzioni, t => t.IdLingua, t => t.Nome, idRichiesta, idPredefinita) ?? string.Empty,
            Scegli(traduzioni, t => t.IdLingua, t => t.Descrizione, idRichiesta, idPredefinita) ?? string.Empty,
            [.. varianti.Select(v => new VarianteInVetrina(
                v.Id,
                v.Sku,
                Scegli(etichette.Where(e => e.IdVariante == v.Id), e => e.IdLingua, e => e.Etichetta, idRichiesta, idPredefinita),
                v.PrezzoLordo,
                v.Valuta,
                massimi[v.Id]))]);
    }

    private Task<bool> NegozioAttivoAsync(Guid idSito, CancellationToken annullamento) =>
        _contesto.Siti.AsNoTracking().AnyAsync(s => s.Id == idSito && s.CommercioAttivo, annullamento);

    private async Task<(Guid? Richiesta, Guid? Predefinita)> LinguaAsync(Guid idSito, string codiceLingua, CancellationToken annullamento)
    {
        var lingue = await LingueDelSito.ElencaAsync(_contesto, idSito, annullamento);
        var predefinita = lingue.FirstOrDefault(l => l.Predefinita)?.IdLingua;
        var richiesta = lingue.FirstOrDefault(l => string.Equals(l.Codice, codiceLingua, StringComparison.OrdinalIgnoreCase))?.IdLingua ?? predefinita;
        return (richiesta, predefinita);
    }

    /// <summary>Quantità richiedibile adesso per ogni variante: giacenza gestita = libera (disponibile meno riservata), altrimenti il massimo per riga.</summary>
    private async Task<Dictionary<Guid, int>> MassimiAsync(Guid idSito, List<Guid> idVarianti, CancellationToken annullamento)
    {
        var giacenze = await _contesto.GiacenzeVarianti.AsNoTracking().Where(g => g.IdSito == idSito && idVarianti.Contains(g.IdVariante)).ToDictionaryAsync(g => g.IdVariante, annullamento);
        return idVarianti.ToDictionary(
            id => id,
            id => giacenze.TryGetValue(id, out var g) ? Math.Clamp(g.Disponibile - g.Riservata, 0, RigaCarrello.QuantitaMassima) : RigaCarrello.QuantitaMassima);
    }

    private static string? Scegli<T>(IEnumerable<T> traduzioni, Func<T, Guid> lingua, Func<T, string> testo, Guid? idRichiesta, Guid? idPredefinita)
    {
        var elenco = traduzioni.ToList();
        var scelta = elenco.FirstOrDefault(t => idRichiesta is { } r && lingua(t) == r)
            ?? elenco.FirstOrDefault(t => idPredefinita is { } p && lingua(t) == p);
        return scelta is null ? null : testo(scelta);
    }
}
