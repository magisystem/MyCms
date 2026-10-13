using DvCms.Data;
using Microsoft.EntityFrameworkCore;

namespace DvCms.Data.Cms;

/// <summary>Una lingua abilitata per un sito.</summary>
public sealed record LinguaAbilitata(Guid IdLingua, string Codice, string Nome, bool Predefinita);

public static class LingueDelSito
{
    /// <summary>
    /// Le lingue attive abilitate per il sito, quella predefinita per prima. Un sito senza righe in <c>LingueSito</c>
    /// (non dovrebbe esistere) usa la lingua predefinita della piattaforma, così nulla si rompe.
    /// </summary>
    public static async Task<IReadOnlyList<LinguaAbilitata>> ElencaAsync(ContestoDatabase contesto, Guid idSito, CancellationToken annullamento)
    {
        var abilitate = await (
            from ls in contesto.LingueSito.AsNoTracking()
            join lingua in contesto.Lingue.AsNoTracking() on ls.IdLingua equals lingua.Id
            where ls.IdSito == idSito && lingua.Attiva
            orderby ls.Predefinita descending, lingua.Codice
            select new LinguaAbilitata(lingua.Id, lingua.Codice, lingua.Nome, ls.Predefinita))
            .ToListAsync(annullamento);
        if (abilitate.Count > 0)
        {
            return abilitate;
        }

        return await contesto.Lingue.AsNoTracking()
            .Where(lingua => lingua.Attiva && lingua.Predefinita)
            .Select(lingua => new LinguaAbilitata(lingua.Id, lingua.Codice, lingua.Nome, true))
            .ToListAsync(annullamento);
    }

    /// <summary>Percorso canonico della pagina: la lingua predefinita non ha prefisso, la pagina iniziale sta alla radice della lingua.</summary>
    public static string Percorso(string codiceLingua, bool predefinita, string slug, bool paginaIniziale)
    {
        var prefisso = predefinita ? string.Empty : "/" + codiceLingua;
        var coda = paginaIniziale ? string.Empty : "/" + slug;
        var percorso = prefisso + coda;
        return percorso.Length == 0 ? "/" : percorso;
    }
}
