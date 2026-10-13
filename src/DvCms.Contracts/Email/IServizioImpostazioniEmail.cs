using DvCms.DTO.Email;
using DvCms.DTO.Piattaforma;
using DvCms.Models.Email;
using DvCms.Models.Outbox;

namespace DvCms.Contracts.Email;

/// <summary>
/// Configurazione email di un tenant (B-009, ADR-0015), solo per il <c>PlatformAdmin</c> (riverificato sul database a ogni
/// operazione). I segreti sono in sola scrittura: si possono impostare o sostituire, mai rileggere.
/// </summary>
public interface IServizioImpostazioniEmail
{
    Task<SchedaEmailTenant?> LeggiAsync(Guid idAttore, Guid idTenant, CancellationToken annullamento);

    /// <summary>
    /// Salva il canale e i suoi valori. Un segreto lasciato vuoto resta quello già salvato (se il canale non cambia);
    /// <paramref name="segretiDaRimuovere"/> elenca i segreti facoltativi da cancellare. Valori non validi o mancanti
    /// sono rifiutati e restituiti per nome di campo.
    /// </summary>
    Task<EsitoSalvataggioEmail> SalvaAsync(
        Guid idAttore,
        Guid idTenant,
        ProviderEmail provider,
        IReadOnlyDictionary<string, string> valori,
        IReadOnlyCollection<string> segretiDaRimuovere,
        string? tokenConcorrenza,
        CancellationToken annullamento);

    /// <summary>
    /// Accoda un messaggio di prova per il tenant (l'invio avviene nel worker, come ogni email). Il tenant deve avere
    /// una configurazione valida.
    /// </summary>
    Task<EsitoOperazione> AccodaProvaAsync(Guid idAttore, Guid idTenant, string? destinatario, MessaggioEmail messaggio, CancellationToken annullamento);
}
