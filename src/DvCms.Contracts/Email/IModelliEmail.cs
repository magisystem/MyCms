using DvCms.DTO.Email;
using DvCms.DTO.Piattaforma;

namespace DvCms.Contracts.Email;

/// <summary>
/// Compone un'email da un modello del catalogo (ADR-0025). Cerca il modello nell'ordine: tenant e lingua, tenant e sua lingua
/// predefinita, fabbrica e lingua, fabbrica in italiano. Un modello del tenant che non si lascia più rendere non blocca l'invio:
/// si ripiega su quello di fabbrica.
/// </summary>
public interface IComponitoreEmail
{
    /// <param name="idTenant">Tenant che invia: i suoi modelli hanno la precedenza.</param>
    /// <param name="codice">Un valore di <see cref="CodiciModelloEmail"/>.</param>
    /// <param name="lingua">Lingua del destinatario.</param>
    /// <param name="variabili">Valori per il modello: testi, numeri, valori sì/no, elenchi e dizionari di questi. Niente oggetti del dominio.</param>
    Task<MessaggioEmail> ComponiAsync(
        Guid idTenant,
        string codice,
        string lingua,
        string destinatario,
        string? nomeDestinatario,
        IReadOnlyDictionary<string, object?> variabili,
        CancellationToken annullamento);
}

/// <summary>
/// Gestione dei modelli del tenant da parte del suo <c>TenantAdmin</c>. L'attore è riverificato sul database a ogni operazione, nel
/// tenant dell'host: il <c>PlatformAdmin</c> non vi accede.
/// </summary>
public interface IServizioModelliEmail
{
    /// <exception cref="UnauthorizedAccessException">L'attore non è un <c>TenantAdmin</c> attivo del tenant.</exception>
    Task<IReadOnlyList<VoceModelloEmail>> ElencaAsync(Guid idAttore, CancellationToken annullamento);

    /// <summary>Il modello in uso per la lingua (del tenant o di fabbrica, con il ripiego), oppure nulla se codice o lingua non esistono.</summary>
    /// <exception cref="UnauthorizedAccessException">L'attore non è un <c>TenantAdmin</c> attivo del tenant.</exception>
    Task<SchedaModelloEmail?> LeggiAsync(Guid idAttore, string codice, string lingua, CancellationToken annullamento);

    /// <summary>Valida e salva il modello del tenant per la lingua. Un contenuto non valido non viene salvato.</summary>
    Task<EsitoSalvataggioModelloEmail> SalvaAsync(Guid idAttore, string codice, string lingua, ContenutoModelloEmail contenuto, string? tokenConcorrenza, CancellationToken annullamento);

    /// <summary>Elimina il modello del tenant per la lingua: torna quello di fabbrica.</summary>
    Task<EsitoOperazione> RipristinaAsync(Guid idAttore, string codice, string lingua, CancellationToken annullamento);

    /// <summary>Rende il contenuto proposto con dati d'esempio, senza salvarlo.</summary>
    /// <exception cref="UnauthorizedAccessException">L'attore non è un <c>TenantAdmin</c> attivo del tenant.</exception>
    Task<AnteprimaModelloEmail?> AnteprimaAsync(Guid idAttore, string codice, ContenutoModelloEmail contenuto, CancellationToken annullamento);
}
