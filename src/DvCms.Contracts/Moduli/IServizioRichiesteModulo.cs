using DvCms.DTO.Moduli;
using DvCms.DTO.Piattaforma;
using DvCms.Models.Moduli;

namespace DvCms.Contracts.Moduli;

/// <summary>
/// Casella delle richieste ricevute dai moduli (ADR-0027). Contiene dati personali di persone che non sono utenti del sistema: è riservata al
/// <c>TenantAdmin</c> del tenant (non al <c>PlatformAdmin</c>, D5), riverificato sul database a ogni operazione. Cancellazioni ed esportazioni
/// sono nel registro di audit con i numeri, mai con i dati.
/// </summary>
public interface IServizioRichiesteModulo
{
    /// <summary>Numero massimo di richieste per esportazione.</summary>
    const int MassimoEsportabili = 20_000;

    /// <returns><c>null</c> se il modulo non esiste nel sito.</returns>
    /// <exception cref="UnauthorizedAccessException">L'attore non è un <c>TenantAdmin</c> attivo del tenant.</exception>
    Task<ElencoRichieste?> ElencaAsync(Guid idAttore, Guid idModulo, StatoInvioModulo? stato, int pagina, CancellationToken annullamento);

    /// <exception cref="UnauthorizedAccessException">L'attore non è un <c>TenantAdmin</c> attivo del tenant.</exception>
    Task<VistaRichiesta?> LeggiAsync(Guid idAttore, Guid idModulo, Guid idRichiesta, CancellationToken annullamento);

    Task<EsitoOperazione> ImpostaStatoAsync(Guid idAttore, Guid idModulo, Guid idRichiesta, StatoInvioModulo stato, CancellationToken annullamento);

    Task<EsitoOperazione> EliminaAsync(Guid idAttore, Guid idModulo, Guid idRichiesta, CancellationToken annullamento);

    /// <exception cref="UnauthorizedAccessException">L'attore non è un <c>TenantAdmin</c> attivo del tenant.</exception>
    Task<EsportazioneRichieste?> EsportaAsync(Guid idAttore, Guid idModulo, StatoInvioModulo? stato, CancellationToken annullamento);
}
