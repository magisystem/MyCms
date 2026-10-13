using DvCms.Application.Piattaforma;
using DvCms.Models.Email;
using DvCms.Models.Outbox;

namespace DvCms.Application.Email;

/// <summary>Impostazioni di un tenant come si mostrano nella UI: i segreti non ci sono mai, solo il fatto che siano impostati.</summary>
public sealed record ImpostazioniEmailVisibili(
    ProviderEmail Provider,
    IReadOnlyDictionary<string, string> Valori,
    IReadOnlySet<string> SegretiImpostati,
    string TokenConcorrenza,
    DateTime AggiornatoIlUtc);

/// <summary>Un invio non riuscito o in ritentativo, visibile ai <c>PlatformAdmin</c>.</summary>
public sealed record ProblemaInvioEmail(Guid IdMessaggio, StatoMessaggioOutbox Stato, int Tentativi, string? UltimoErrore, DateTime CreatoIlUtc);

public sealed record SchedaEmailTenant(string NomeTenant, ImpostazioniEmailVisibili? Impostazioni, IReadOnlyList<ProblemaInvioEmail> Problemi);

public sealed record EsitoSalvataggioEmail(CodiceEsito Codice, IReadOnlyList<string> CampiNonValidi)
{
    public bool Riuscito => Codice == CodiceEsito.Riuscito;
}

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
