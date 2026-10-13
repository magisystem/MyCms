using DvCms.DTO.Cms;

namespace DvCms.Contracts.Cms;

/// <summary>
/// Lettura delle pagine per il sito pubblico (C-003). Lavora sul sito risolto dall'host; restituisce solo la
/// revisione pubblicata: una bozza, una pagina ritirata o inesistente non si distinguono (nessun risultato).
/// </summary>
public interface IServizioPaginePubbliche
{
    /// <summary>Le lingue abilitate per il sito, quella predefinita per prima. La predefinita non ha prefisso nell'URL, le altre sì.</summary>
    Task<IReadOnlyList<LinguaPubblica>> LingueAsync(CancellationToken annullamento);

    /// <summary>
    /// Trova la pagina per slug (o la pagina iniziale se lo slug è nullo o vuoto) nella lingua indicata, o in quella
    /// predefinita del sito se nulla. Nessun ripiego su un'altra lingua: se la pagina non esiste in quella lingua, nessun risultato.
    /// </summary>
    Task<PaginaPubblica?> TrovaAsync(string? slug, string? codiceLingua, CancellationToken annullamento);

    /// <summary>
    /// Come <see cref="TrovaAsync"/>, ma tiene conto dei reindirizzamenti: un vecchio slug di una pagina oggi pubblicata, o la
    /// pagina iniziale richiesta con il suo slug, danno il percorso canonico (un solo salto, mai verso lo stesso percorso).
    /// </summary>
    Task<RisoluzionePagina> RisolviAsync(string? slug, string? codiceLingua, CancellationToken annullamento);
}
