using DvCms.Application.Piattaforma;

namespace DvCms.Application.Negozio;

public sealed record NomeCategoriaDati(string CodiceLingua, string Nome);

public sealed record DatiCategoria(Guid? Id, int Posizione, IReadOnlyList<NomeCategoriaDati> Nomi);

public sealed record CategoriaVista(Guid Id, int Posizione, IReadOnlyList<NomeCategoriaDati> Nomi, int Prodotti);

/// <summary>
/// Categorie (collezioni) del negozio del sito dell'host: raggruppamenti piatti con nome per lingua, a cui si assegnano i prodotti dal
/// modulo del prodotto. Riservato agli amministratori del tenant (ruolo riverificato a ogni operazione) e al negozio attivo. Eliminare
/// una categoria toglie solo l'appartenenza: i prodotti restano.
/// </summary>
public interface IServizioCategorie
{
    Task<IReadOnlyList<CategoriaVista>> ElencaAsync(Guid idAttore, CancellationToken annullamento);

    /// <summary>
    /// Crea o modifica una categoria. Il nome serve nella lingua predefinita del sito (le altre sono facoltative). Esiti: <c>DatiNonValidi</c>
    /// (con i campi), <c>NonTrovato</c>, <c>NonConsentito</c> (negozio non attivo o troppe categorie), <c>NonAutorizzato</c>.
    /// </summary>
    Task<EsitoOperazione> SalvaAsync(Guid idAttore, DatiCategoria dati, CancellationToken annullamento);

    Task<EsitoOperazione> EliminaAsync(Guid idAttore, Guid idCategoria, CancellationToken annullamento);
}
