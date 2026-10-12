using DvCms.Application.Negozio;
using DvCms.Application.Tenancy;
using DvCms.Domain.Media;
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
        var immagini = await ImmaginiAsync(idSito, idProdotti, idRichiesta, idPredefinita, annullamento);

        return [.. prodotti
            .Select(p => (Prodotto: p, Varianti: varianti.Where(v => v.IdProdotto == p.Id).ToList()))
            .Where(x => x.Varianti.Count > 0)
            .Select(x => new ProdottoInVetrina(
                x.Prodotto.Id,
                Scegli(nomi.Where(t => t.IdProdotto == x.Prodotto.Id), t => t.IdLingua, t => t.Nome, idRichiesta, idPredefinita) ?? string.Empty,
                x.Varianti.Min(v => v.PrezzoLordo),
                VarianteProdotto.ValutaPredefinita,
                x.Varianti.Any(v => massimi[v.Id] > 0),
                immagini.GetValueOrDefault(x.Prodotto.Id)?.FirstOrDefault()))
            .OrderBy(p => p.Nome, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(p => p.Id)];
    }

    public async Task<IReadOnlyList<ProdottoInVetrina>> ElencaPerCategoriaAsync(string codiceLingua, Guid idCategoria, CancellationToken annullamento)
    {
        var idSito = _tenant.IdSito;
        var visibili = await ElencaAsync(codiceLingua, annullamento);
        if (visibili.Count == 0)
        {
            return [];
        }

        var inCategoria = (await _contesto.ProdottiCategoria.AsNoTracking()
            .Where(p => p.IdSito == idSito && p.IdCategoria == idCategoria)
            .Select(p => p.IdProdotto)
            .ToListAsync(annullamento)).ToHashSet();
        return [.. visibili.Where(p => inCategoria.Contains(p.Id))];
    }

    public async Task<IReadOnlyList<ProdottoInVetrina>> CercaAsync(string codiceLingua, string? testo, Guid? idCategoria, CancellationToken annullamento)
    {
        var visibili = idCategoria is { } categoria
            ? await ElencaPerCategoriaAsync(codiceLingua, categoria, annullamento)
            : await ElencaAsync(codiceLingua, annullamento);
        if (visibili.Count == 0)
        {
            return visibili;
        }

        var idSito = _tenant.IdSito;
        var (idRichiesta, idPredefinita) = await LinguaAsync(idSito, codiceLingua, annullamento);
        var termini = AnalizzatoreRicerca.TerminiRicerca(testo, codiceLingua);
        if (termini.Count == 0)
        {
            return visibili;
        }

        var ids = visibili.Select(p => p.Id).ToList();
        await AssicuraIndiceAsync(idSito, ids, annullamento);

        // Una query per termine (al più cinque): ogni prodotto deve avere tutti i termini (E logico), con il punteggio migliore per termine.
        var lingue = new List<Guid> { TermineRicercaProdotto.LinguaNeutra };
        if (idRichiesta is { } richiesta)
        {
            lingue.Add(richiesta);
        }

        if (idPredefinita is { } predefinita && predefinita != idRichiesta)
        {
            lingue.Add(predefinita);
        }

        Dictionary<Guid, int>? punteggi = null;
        foreach (var termine in termini)
        {
            var prefisso = termine.Length >= 3;
            var trovati = await _contesto.TerminiRicercaProdotto.AsNoTracking()
                .Where(r => r.IdSito == idSito && lingue.Contains(r.IdLingua) && (prefisso ? EF.Functions.Like(r.Termine, termine + "%") : r.Termine == termine))
                .GroupBy(r => r.IdProdotto)
                .Select(g => new
                {
                    IdProdotto = g.Key,
                    Punti = g.Max(r => r.Campo == CampoRicerca.Nome ? (r.Termine == termine ? 10 : 6)
                        : r.Campo == CampoRicerca.Codice ? (r.Termine == termine ? 8 : 5)
                        : (r.Termine == termine ? 3 : 1)),
                })
                .ToListAsync(annullamento);

            punteggi = punteggi is null
                ? trovati.ToDictionary(t => t.IdProdotto, t => t.Punti)
                : trovati.Where(t => punteggi.ContainsKey(t.IdProdotto)).ToDictionary(t => t.IdProdotto, t => punteggi[t.IdProdotto] + t.Punti);
            if (punteggi.Count == 0)
            {
                return [];
            }
        }

        // Dal più pertinente; a parità l'ordine alfabetico della vetrina.
        return [.. visibili.Where(p => punteggi!.ContainsKey(p.Id)).OrderByDescending(p => punteggi![p.Id])];
    }

    /// <summary>
    /// I prodotti nati prima dell'indice di ricerca non hanno termini: alla prima ricerca si indicizzano (al più
    /// <see cref="IndiceRicercaProdotti.MassimoRicostruzioniPerRichiesta"/> per volta). Una richiesta concorrente che li ha già
    /// indicizzati non è un errore.
    /// </summary>
    private async Task AssicuraIndiceAsync(Guid idSito, List<Guid> idProdotti, CancellationToken annullamento)
    {
        var indicizzati = await _contesto.TerminiRicercaProdotto.AsNoTracking()
            .Where(r => r.IdSito == idSito && idProdotti.Contains(r.IdProdotto))
            .Select(r => r.IdProdotto)
            .Distinct()
            .ToListAsync(annullamento);
        var mancanti = idProdotti.Except(indicizzati).Take(IndiceRicercaProdotti.MassimoRicostruzioniPerRichiesta).ToList();
        if (mancanti.Count == 0)
        {
            return;
        }

        try
        {
            await IndiceRicercaProdotti.RicostruisciAsync(_contesto, idSito, mancanti, annullamento);
            await _contesto.SaveChangesAsync(annullamento);
        }
        catch (DbUpdateException)
        {
            _contesto.ChangeTracker.Clear();
        }
    }

    public async Task<IReadOnlyList<CategoriaInVetrina>> CategorieAsync(string codiceLingua, CancellationToken annullamento)
    {
        var idSito = _tenant.IdSito;
        var visibili = (await ElencaAsync(codiceLingua, annullamento)).Select(p => p.Id).ToHashSet();
        if (visibili.Count == 0)
        {
            return [];
        }

        var (idRichiesta, idPredefinita) = await LinguaAsync(idSito, codiceLingua, annullamento);
        var conteggi = (await _contesto.ProdottiCategoria.AsNoTracking().Where(p => p.IdSito == idSito).ToListAsync(annullamento))
            .Where(p => visibili.Contains(p.IdProdotto))
            .GroupBy(p => p.IdCategoria)
            .ToDictionary(g => g.Key, g => g.Count());
        if (conteggi.Count == 0)
        {
            return [];
        }

        var idCategorie = conteggi.Keys.ToList();
        var categorie = await _contesto.Categorie.AsNoTracking().Where(c => c.IdSito == idSito && idCategorie.Contains(c.Id)).ToListAsync(annullamento);
        var nomi = await _contesto.TraduzioniCategoria.AsNoTracking().Where(t => t.IdSito == idSito && idCategorie.Contains(t.IdCategoria)).ToListAsync(annullamento);
        return [.. categorie
            .Select(c => (Categoria: c, Nome: Scegli(nomi.Where(t => t.IdCategoria == c.Id), t => t.IdLingua, t => t.Nome, idRichiesta, idPredefinita) ?? string.Empty))
            .OrderBy(x => x.Categoria.Posizione)
            .ThenBy(x => x.Nome, StringComparer.CurrentCultureIgnoreCase)
            .Select(x => new CategoriaInVetrina(x.Categoria.Id, x.Nome, conteggi[x.Categoria.Id]))];
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
        var immagini = await ImmaginiAsync(idSito, [idProdotto], idRichiesta, idPredefinita, annullamento);

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
                massimi[v.Id]))],
            immagini.GetValueOrDefault(idProdotto) ?? []);
    }

    /// <summary>Le immagini dei prodotti in ordine, limitate alle risorse ancora pubbliche, pulite e davvero immagini del sito: una risorsa tolta o resa privata sparisce senza rompere il prodotto.</summary>
    private async Task<Dictionary<Guid, List<ImmagineVetrina>>> ImmaginiAsync(Guid idSito, List<Guid> idProdotti, Guid? idRichiesta, Guid? idPredefinita, CancellationToken annullamento)
    {
        var collegamenti = await _contesto.ImmaginiProdotto.AsNoTracking().Where(i => i.IdSito == idSito && idProdotti.Contains(i.IdProdotto)).OrderBy(i => i.Posizione).ToListAsync(annullamento);
        if (collegamenti.Count == 0)
        {
            return [];
        }

        var idRisorse = collegamenti.Select(i => i.IdRisorsa).Distinct().ToList();
        var risorse = await _contesto.RisorseMedia.AsNoTracking()
            .Where(r => r.IdSito == idSito && idRisorse.Contains(r.Id) && r.Visibilita == VisibilitaMedia.Pubblica
                && r.StatoScansione != StatoScansioneMedia.InQuarantena && r.TipoContenuto.StartsWith("image/"))
            .ToDictionaryAsync(r => r.Id, annullamento);
        var testi = await _contesto.TraduzioniRisorseMedia.AsNoTracking().Where(t => idRisorse.Contains(t.IdRisorsa)).ToListAsync(annullamento);
        return collegamenti
            .Where(i => risorse.ContainsKey(i.IdRisorsa))
            .GroupBy(i => i.IdProdotto)
            .ToDictionary(g => g.Key, g => g.Select(i => new ImmagineVetrina(i.IdRisorsa, Scegli(testi.Where(t => t.IdRisorsa == i.IdRisorsa), t => t.IdLingua, t => t.TestoAlternativo, idRichiesta, idPredefinita) is { Length: > 0 } alt ? alt : null)).ToList());
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
