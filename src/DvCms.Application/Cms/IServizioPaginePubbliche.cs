namespace DvCms.Application.Cms;

/// <summary>Contenuto pubblicato di una pagina, pronto per il rendering pubblico.</summary>
public sealed record PaginaPubblica(string Titolo, string Contenuto, string CodiceLingua, string Slug, DateTime PubblicatoIlUtc);

/// <summary>
/// Lettura delle pagine per il sito pubblico (C-003). Lavora sul sito risolto dall'host; restituisce solo la
/// revisione pubblicata: una bozza, una pagina ritirata o inesistente non si distinguono (nessun risultato).
/// </summary>
public interface IServizioPaginePubbliche
{
    /// <summary>
    /// Trova la pagina per slug (o la pagina iniziale se lo slug è nullo o vuoto). Se esistono più lingue con lo stesso
    /// slug sceglie la lingua corrente, altrimenti quella predefinita.
    /// </summary>
    Task<PaginaPubblica?> TrovaAsync(string? slug, CancellationToken annullamento);
}
