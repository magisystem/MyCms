
namespace DvCms.DTO.Negozio;

/// <summary>
/// Una riga del carrello come la vede l'acquirente, con i valori calcolati dal server sul catalogo attuale. <c>Massimo</c> è la
/// quantità disponibile adesso (al più 99; con giacenza gestita è la parte non prenotata né venduta): se è inferiore alla quantità
/// del carrello l'ordine non potrà essere creato finché non si riduce. È un avviso: la garanzia è la prenotazione all'ordine.
/// </summary>
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
    decimal Iva,
    int Massimo);

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
