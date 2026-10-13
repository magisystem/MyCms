using DvCms.DTO.Negozio;
using DvCms.DTO.Piattaforma;

namespace DvCms.Contracts.Negozio;

/// <summary>
/// Dati personali degli acquirenti (A7, ADR-0020): esportazione, anonimizzazione dell'account su richiesta e anonimizzazione delle
/// copie negli ordini a fine conservazione. L'anonimizzazione dell'account cancella email, nome, credenziali e carrello e
/// annulla gli ordini non ancora pagati (liberando la giacenza); gli ordini pagati restano com'erano, perché vanno conservati.
/// </summary>
public interface IServizioDatiPersonali
{
    /// <summary>I dati dell'account dell'host e dei suoi ordini. Nulla se l'account non esiste o non è di questo sito.</summary>
    Task<EsportazioneDatiPersonali?> EsportaMieiAsync(Guid idAccount, CancellationToken annullamento);

    /// <summary>
    /// Anonimizza l'account di chi chiede, previa riconferma della password. Solo gli acquirenti: un amministratore che compra
    /// con la sessione cliente (ADR-0011) non si cancella da qui. <see cref="CodiceEsito.DatiNonValidi"/> se la password è errata.
    /// </summary>
    Task<EsitoOperazione> AnonimizzaMioAccountAsync(Guid idAccount, string? password, CancellationToken annullamento);

    /// <summary>
    /// Come sopra, su richiesta dell'acquirente a un amministratore del tenant (o alla piattaforma): l'acquirente è quello che ha
    /// fatto l'ordine indicato; senza password, con audit. <see cref="CodiceEsito.NonTrovato"/> se l'ordine non esiste o
    /// l'account non è più (o non è) un acquirente.
    /// </summary>
    Task<EsitoOperazione> AnonimizzaAcquirenteDiOrdineAsync(Guid idAttore, Guid idOrdine, CancellationToken annullamento);

    /// <summary>Per la manutenzione, su tutti i tenant: anonimizza la copia personale negli ordini fermi da più di <see cref="OpzioniDatiPersonali.ConservazioneOrdiniAnni"/> anni. Restituisce quanti ne ha trattati.</summary>
    Task<int> AnonimizzaOrdiniScadutiAsync(CancellationToken annullamento);
}
