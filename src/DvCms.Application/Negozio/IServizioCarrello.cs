using DvCms.Application.Piattaforma;

namespace DvCms.Application.Negozio;

/// <summary>Una riga del carrello come la vede l'acquirente, con i valori calcolati dal server sul catalogo attuale.</summary>
public sealed record RigaCarrelloVista(
    Guid IdVariante,
    string Sku,
    string NomeProdotto,
    string? EtichettaVariante,
    int Quantita,
    decimal PrezzoUnitarioLordo,
    decimal Aliquota,
    decimal Lordo,
    decimal Imponibile,
    decimal Iva);

/// <summary>
/// Il carrello. Le righe <see cref="NonDisponibili"/> (prodotto non più pubblicato, variante disattivata o eliminata) restano
/// visibili ma non entrano nei totali e non si possono ordinare finché non si tolgono.
/// </summary>
public sealed record CarrelloVista(
    IReadOnlyList<RigaCarrelloVista> Righe,
    IReadOnlyList<Guid> NonDisponibili,
    decimal TotaleLordo,
    decimal TotaleImponibile,
    decimal TotaleIva,
    string Valuta);

/// <summary>
/// Carrello dell'acquirente autenticato (F-002, ADR-0011). Riceve solo identificativi e quantità: il cliente non può proporre un
/// prezzo, un'aliquota o un totale. Ogni operazione riverifica sul database che l'account sia attivo e del tenant dell'host
/// (<c>Utente</c> o <c>TenantAdmin</c> di quel tenant, <c>PlatformAdmin</c> ovunque) e che il negozio sia attivo.
/// </summary>
public interface IServizioCarrello
{
    /// <param name="codiceLingua">Lingua preferita per nomi ed etichette; se manca la traduzione si ripiega sulla lingua predefinita del sito.</param>
    Task<CarrelloVista> LeggiAsync(Guid idAccount, string codiceLingua, CancellationToken annullamento);

    /// <summary>Aggiunge unità di una variante (si sommano a quelle già presenti, fino al massimo per riga). Esiti: <c>NonTrovato</c> se la variante non è acquistabile in questo sito, <c>DatiNonValidi</c> per quantità o numero di righe fuori limite.</summary>
    Task<EsitoOperazione> AggiungiAsync(Guid idAccount, Guid idVariante, int quantita, CancellationToken annullamento);

    /// <summary>Imposta la quantità di una riga già presente; zero la toglie.</summary>
    Task<EsitoOperazione> ImpostaQuantitaAsync(Guid idAccount, Guid idVariante, int quantita, CancellationToken annullamento);

    Task<EsitoOperazione> SvuotaAsync(Guid idAccount, CancellationToken annullamento);
}
