using DvCms.DTO.Negozio;
using DvCms.DTO.Piattaforma;

namespace DvCms.Contracts.Negozio;

/// <summary>
/// Giacenza e prenotazioni (F-005, ADR-0020). La quantità fisica la imposta l'amministrazione; la prenotazione di 15 minuti si fa
/// alla creazione dell'ordine con un aggiornamento condizionale unico (mai lettura e poi scrittura) e il database impone
/// <c>Riservata ≤ Disponibile</c>. Scaduta, fallita o annullata la prenotazione si rilascia; con il pagamento si conferma.
/// </summary>
public interface IServizioGiacenze
{
    /// <summary>Tutte le varianti del sito con la loro giacenza. Prima fa scadere le prenotazioni vecchie, così le cifre sono vere.</summary>
    Task<IReadOnlyList<GiacenzaVista>> ElencaAsync(Guid idAttore, CancellationToken annullamento);

    /// <summary>
    /// Imposta la quantità fisica (anche per iniziare a gestirla); <c>null</c> smette di gestirla. Esiti: <c>DatiNonValidi</c>
    /// (quantità negativa, oltre il massimo o sotto quanto è riservato), <c>NonTrovato</c>, <c>NonConsentito</c> (negozio non attivo,
    /// o prenotazioni in corso quando si smette di gestirla), <c>Conflitto</c> (modifica concorrente).
    /// </summary>
    Task<EsitoOperazione> ImpostaAsync(Guid idAttore, Guid idVariante, int? disponibile, CancellationToken annullamento);

    /// <summary>
    /// Rilascia le prenotazioni scadute di tutti i tenant e annulla i loro ordini ancora non pagati. Idempotente; restituisce quanti
    /// ordini ha annullato. Lo chiamano la manutenzione periodica e, con un ambito minore, chi sta per prenotare.
    /// </summary>
    Task<int> RilasciaScaduteAsync(CancellationToken annullamento);

    /// <summary>
    /// Conferma le prenotazioni di un ordine del tenant corrente (pagamento incassato): la quantità esce dal magazzino. Se una
    /// prenotazione era già stata rilasciata (scaduta prima del pagamento) restituisce <c>Conflitto</c> e non cambia nulla.
    /// </summary>
    Task<EsitoOperazione> ConfermaOrdineAsync(Guid idOrdine, CancellationToken annullamento);

    /// <summary>Rilascia le prenotazioni attive di un ordine del tenant corrente. Idempotente.</summary>
    /// <summary>
    /// Rimette in giacenza gli articoli di un ordine **interamente rimborsato** (stato «Rimborsato»), una volta sola: per ogni riga con
    /// giacenza gestita la disponibilità aumenta della quantità ordinata e il movimento «Reso» resta nello storico. Decide l'amministratore,
    /// perché la merce rientra davvero solo se è stata restituita. Esiti: <c>NonConsentito</c> (ordine non interamente rimborsato),
    /// <c>Conflitto</c> (già reinserita), <c>NonTrovato</c>, <c>NonAutorizzato</c>. Per i rimborsi parziali si usa <c>ImpostaAsync</c>.
    /// </summary>
    Task<EsitoOperazione> ReinserisciOrdineAsync(Guid idAttore, Guid idOrdine, CancellationToken annullamento);

    Task<EsitoOperazione> RilasciaOrdineAsync(Guid idOrdine, CancellationToken annullamento);
}
