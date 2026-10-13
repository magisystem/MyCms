using DvCms.Models.Negozio;

namespace DvCms.DTO.Negozio;

/// <summary>
/// Scelte del cliente alla cassa. Non c'è alcun importo da fornire: totali e prezzi si ricalcolano sul server. <c>TotaleAtteso</c>
/// è il totale che il cliente ha visto: se nel frattempo è cambiato (prezzo, disponibilità, tariffa) l'ordine non si crea
/// (<c>Conflitto</c>) e il cliente rivede la cassa.
/// </summary>
public sealed record DatiOrdine(
    Guid IdMetodoSpedizione,
    string? Destinatario,
    string? IndirizzoRiga1,
    string? IndirizzoRiga2,
    string? Cap,
    string? Citta,
    string? Paese,
    decimal TotaleAtteso,
    ModalitaPagamento ModalitaPagamento = ModalitaPagamento.Online);

public sealed record RigaOrdineVista(
    int Posizione,
    string Sku,
    string NomeProdotto,
    string? EtichettaVariante,
    int Quantita,
    decimal PrezzoUnitarioLordo,
    decimal AliquotaIva,
    decimal Lordo,
    decimal Imponibile,
    decimal Iva);

public sealed record RiepilogoOrdine(
    Guid Id,
    int Numero,
    StatoOrdine Stato,
    decimal TotaleLordo,
    string Valuta,
    DateTime CreatoIlUtc,
    string EmailAcquirente,
    ModalitaPagamento ModalitaPagamento = ModalitaPagamento.Online);

public sealed record OrdineVista(
    Guid Id,
    int Numero,
    StatoOrdine Stato,
    string Valuta,
    decimal TotaleLordo,
    decimal TotaleImponibile,
    decimal TotaleIva,
    decimal TotaleRimborsato,
    TipoMetodoSpedizione TipoSpedizione,
    AreaSpedizione? AreaSpedizione,
    string NomeSpedizione,
    decimal SpedizioneLorda,
    decimal SpedizioneImponibile,
    decimal SpedizioneIva,
    string EmailAcquirente,
    string NomeAcquirente,
    string? Destinatario,
    string? IndirizzoRiga1,
    string? IndirizzoRiga2,
    string? Cap,
    string? Citta,
    string? Paese,
    DateTime CreatoIlUtc,
    DateTime AggiornatoIlUtc,
    DateTime? PrenotazioneScadeIlUtc,
    IReadOnlyList<RigaOrdineVista> Righe,
    DateTime? GiacenzaReinseritaIlUtc = null,
    ModalitaPagamento ModalitaPagamento = ModalitaPagamento.Online,
    decimal CommissioneLorda = 0m,
    decimal CommissioneImponibile = 0m,
    decimal CommissioneIva = 0m);
