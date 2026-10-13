using DvCms.Application.Piattaforma;
using DvCms.Domain.Negozio;

namespace DvCms.Application.Negozio;

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

/// <summary>
/// Ordini (F-003, ADR-0011, ADR-0020). Possono ordinare gli account autenticati e attivi (<c>Utente</c> o <c>TenantAdmin</c> del
/// tenant dell'host, <c>PlatformAdmin</c> ovunque), solo con il negozio attivo; ognuno vede e annulla soltanto i propri ordini.
/// L'amministrazione (<c>TenantAdmin</c> del tenant, <c>PlatformAdmin</c>) legge tutti gli ordini del sito. Il ruolo si riverifica
/// sul database a ogni operazione.
/// </summary>
public interface IServizioOrdini
{
    /// <summary>
    /// Crea l'ordine dal carrello dell'account e lo svuota, nella stessa transazione. Esiti: <c>NonConsentito</c> (negozio non attivo),
    /// <c>DatiNonValidi</c> (con i campi: carrello vuoto, metodo, indirizzo non coerente con l'area), <c>Conflitto</c> (articoli non
    /// più disponibili, giacenza insufficiente — campi <c>Giacenza:CODICE</c> — o totale diverso da quello atteso), <c>NonTrovato</c> (metodo di spedizione inesistente o disattivato).
    /// </summary>
    Task<EsitoOperazione> CreaDaCarrelloAsync(Guid idAccount, string codiceLingua, DatiOrdine dati, CancellationToken annullamento);

    Task<IReadOnlyList<RiepilogoOrdine>> ElencaMieiAsync(Guid idAccount, CancellationToken annullamento);

    /// <summary>L'ordine è restituito solo al suo proprietario: per chiunque altro è <c>null</c>.</summary>
    Task<OrdineVista?> LeggiMioAsync(Guid idAccount, Guid idOrdine, CancellationToken annullamento);

    /// <summary>Annulla un ordine non ancora pagato. <c>NonTrovato</c> se non è dell'account, <c>NonConsentito</c> se lo stato non lo ammette.</summary>
    Task<EsitoOperazione> AnnullaMioAsync(Guid idAccount, Guid idOrdine, CancellationToken annullamento);

    /// <summary>Amministrazione: i 200 ordini più recenti del sito.</summary>
    Task<IReadOnlyList<RiepilogoOrdine>> ElencaAsync(Guid idAttore, CancellationToken annullamento);

    Task<OrdineVista?> LeggiAsync(Guid idAttore, Guid idOrdine, CancellationToken annullamento);

    /// <summary>
    /// Amministrazione: segna un ordine pagato come spedito (consegna) o ritirato (ritiro in sede); un ordine in contrassegno da spedire
    /// diventa spedito e la merce prenotata esce dal magazzino. <c>NonTrovato</c>, <c>NonConsentito</c> se lo stato non lo ammette; il
    /// passaggio è condizionale, quindi non si ripete.
    /// </summary>
    Task<EsitoOperazione> SegnaEvasoAsync(Guid idAttore, Guid idOrdine, CancellationToken annullamento);

    /// <summary>
    /// Amministrazione: il bonifico è arrivato. Solo per un ordine con pagamento a bonifico ancora in attesa: diventa pagato, la merce
    /// prenotata esce dal magazzino e il cliente riceve la conferma. <c>NonConsentito</c> per altri ordini o stati.
    /// </summary>
    Task<EsitoOperazione> SegnaPagatoAsync(Guid idAttore, Guid idOrdine, CancellationToken annullamento);

    /// <summary>
    /// Amministrazione: annulla un ordine con pagamento manuale non ancora incassato (bonifico in attesa o contrassegno da spedire) e
    /// libera la merce prenotata. Gli ordini con pagamento online si annullano da soli o dal cliente.
    /// </summary>
    Task<EsitoOperazione> AnnullaAsync(Guid idAttore, Guid idOrdine, CancellationToken annullamento);

    /// <summary>
    /// Amministrazione: la consegna in contrassegno non è riuscita (rifiutata, mai ritirata). L'ordine spedito diventa annullato; la merce
    /// già uscita dal magazzino va rimessa in giacenza dalla pagina delle giacenze quando rientra.
    /// </summary>
    Task<EsitoOperazione> SegnaConsegnaNonRiuscitaAsync(Guid idAttore, Guid idOrdine, CancellationToken annullamento);
}
