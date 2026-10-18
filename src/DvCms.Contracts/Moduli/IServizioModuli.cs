using DvCms.DTO.Moduli;
using DvCms.DTO.Piattaforma;
using DvCms.Models.Moduli;

namespace DvCms.Contracts.Moduli;

/// <summary>
/// Gestione dei moduli di contatto del sito dell'host da parte del suo <c>TenantAdmin</c> (ADR-0027). L'attore è riverificato sul
/// database a ogni operazione, nel tenant dell'host: il <c>PlatformAdmin</c> non vi accede. Ogni scrittura va nel registro di audit.
/// </summary>
public interface IServizioModuli
{
    /// <exception cref="UnauthorizedAccessException">L'attore non è un <c>TenantAdmin</c> attivo del tenant.</exception>
    Task<IReadOnlyList<LinguaModulo>> LingueAsync(Guid idAttore, CancellationToken annullamento);

    /// <exception cref="UnauthorizedAccessException">L'attore non è un <c>TenantAdmin</c> attivo del tenant.</exception>
    Task<IReadOnlyList<RiepilogoModulo>> ElencaAsync(Guid idAttore, CancellationToken annullamento);

    /// <exception cref="UnauthorizedAccessException">L'attore non è un <c>TenantAdmin</c> attivo del tenant.</exception>
    Task<VistaModulo?> LeggiAsync(Guid idAttore, Guid idModulo, CancellationToken annullamento);

    /// <summary>Crea un modulo. Un modulo <c>Attivo</c> deve essere completo; il nome è unico nel sito; al massimo 50 moduli per sito.</summary>
    Task<EsitoModulo> CreaAsync(Guid idAttore, DatiModulo dati, CancellationToken annullamento);

    Task<EsitoModulo> AggiornaAsync(Guid idAttore, Guid idModulo, DatiModulo dati, string? tokenConcorrenza, CancellationToken annullamento);

    /// <summary>Cambia lo stato (pubblica, riporta in bozza, archivia). Pubblicare richiede un modulo completo.</summary>
    Task<EsitoModulo> ImpostaStatoAsync(Guid idAttore, Guid idModulo, StatoModulo stato, string? tokenConcorrenza, CancellationToken annullamento);

    /// <summary>Elimina un modulo senza richieste; se ne ha, <see cref="CodiceEsito.NonConsentito"/> (si archivia, e le richieste scadono da sole).</summary>
    Task<EsitoOperazione> EliminaAsync(Guid idAttore, Guid idModulo, CancellationToken annullamento);
}
