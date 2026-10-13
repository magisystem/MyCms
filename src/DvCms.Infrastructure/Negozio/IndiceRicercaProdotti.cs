using DvCms.Infrastructure.Persistenza;
using DvCms.Models.Negozio;
using Microsoft.EntityFrameworkCore;

namespace DvCms.Infrastructure.Negozio;

/// <summary>Il testo di un prodotto in una lingua, come entra nell'indice di ricerca.</summary>
internal sealed record TestoProdottoIndicizzabile(Guid IdLingua, string CodiceLingua, string Nome, string Descrizione);

/// <summary>
/// Indice di ricerca dei prodotti (M7.24): una riga per termine normalizzato (vedi <see cref="AnalizzatoreRicerca"/>), con il campo da cui
/// viene. Si aggiorna nello stesso salvataggio del prodotto; per i prodotti nati prima dell'indice si ricostruisce alla prima ricerca.
/// </summary>
internal static class IndiceRicercaProdotti
{
    /// <summary>Prodotti ricostruiti al massimo in una sola richiesta: il resto alle ricerche successive.</summary>
    public const int MassimoRicostruzioniPerRichiesta = 200;

    public static HashSet<(Guid IdLingua, CampoRicerca Campo, string Termine)> Costruisci(IEnumerable<TestoProdottoIndicizzabile> testi, IEnumerable<string> codici)
    {
        var righe = new HashSet<(Guid, CampoRicerca, string)>();
        foreach (var testo in testi)
        {
            foreach (var termine in AnalizzatoreRicerca.Termini(testo.Nome, testo.CodiceLingua))
            {
                righe.Add((testo.IdLingua, CampoRicerca.Nome, termine));
            }

            foreach (var termine in AnalizzatoreRicerca.Termini(testo.Descrizione, testo.CodiceLingua, AnalizzatoreRicerca.MassimoTerminiDescrizione))
            {
                righe.Add((testo.IdLingua, CampoRicerca.Descrizione, termine));
            }
        }

        foreach (var codice in codici)
        {
            foreach (var termine in AnalizzatoreRicerca.TerminiCodice(codice))
            {
                righe.Add((TermineRicercaProdotto.LinguaNeutra, CampoRicerca.Codice, termine));
            }
        }

        return righe;
    }

    /// <summary>Allinea le righe dell'indice del prodotto a quelle volute (aggiunge le mancanti, toglie le superflue). Non salva.</summary>
    public static async Task SincronizzaAsync(ContestoDatabase contesto, Prodotto prodotto, bool nuovo, IEnumerable<TestoProdottoIndicizzabile> testi, IEnumerable<string> codici, CancellationToken annullamento)
    {
        var volute = Costruisci(testi, codici);
        var esistenti = nuovo
            ? []
            : await contesto.TerminiRicercaProdotto.Where(t => t.IdProdotto == prodotto.Id).ToListAsync(annullamento);

        foreach (var superflua in esistenti.Where(t => !volute.Contains((t.IdLingua, t.Campo, t.Termine))))
        {
            contesto.TerminiRicercaProdotto.Remove(superflua);
        }

        var presenti = esistenti.Select(t => (t.IdLingua, t.Campo, t.Termine)).ToHashSet();
        foreach (var (idLingua, campo, termine) in volute.Where(v => !presenti.Contains(v)))
        {
            contesto.TerminiRicercaProdotto.Add(new TermineRicercaProdotto
            {
                IdTenant = prodotto.IdTenant,
                IdSito = prodotto.IdSito,
                IdProdotto = prodotto.Id,
                IdLingua = idLingua,
                Campo = campo,
                Termine = termine,
            });
        }
    }

    /// <summary>Indicizza i prodotti indicati leggendone testi e codici dal database (prodotti precedenti all'indice).</summary>
    public static async Task RicostruisciAsync(ContestoDatabase contesto, Guid idSito, IReadOnlyCollection<Guid> idProdotti, CancellationToken annullamento)
    {
        var prodotti = await contesto.Prodotti.Where(p => p.IdSito == idSito && idProdotti.Contains(p.Id)).ToListAsync(annullamento);
        var traduzioni = await contesto.TraduzioniProdotto.AsNoTracking().Where(t => t.IdSito == idSito && idProdotti.Contains(t.IdProdotto)).ToListAsync(annullamento);
        var codiciLingua = await contesto.Lingue.AsNoTracking().ToDictionaryAsync(l => l.Id, l => l.Codice, annullamento);
        var varianti = await contesto.VariantiProdotto.AsNoTracking().Where(v => v.IdSito == idSito && v.Attiva && idProdotti.Contains(v.IdProdotto)).Select(v => new { v.IdProdotto, v.Sku }).ToListAsync(annullamento);

        foreach (var prodotto in prodotti)
        {
            var testi = traduzioni
                .Where(t => t.IdProdotto == prodotto.Id)
                .Select(t => new TestoProdottoIndicizzabile(t.IdLingua, codiciLingua.GetValueOrDefault(t.IdLingua) ?? string.Empty, t.Nome, t.Descrizione));
            await SincronizzaAsync(contesto, prodotto, nuovo: true, testi, varianti.Where(v => v.IdProdotto == prodotto.Id).Select(v => v.Sku), annullamento);
        }
    }
}
