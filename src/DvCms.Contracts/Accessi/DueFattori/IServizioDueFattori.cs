using DvCms.DTO.Accessi;
using DvCms.DTO.Accessi.DueFattori;
using DvCms.Models.Accessi;

namespace DvCms.Contracts.Accessi.DueFattori;

/// <summary>Gestione e verifica del secondo fattore (ADR-0019). Ogni operazione riverifica account e tenant sul database.</summary>
public interface IServizioDueFattori
{
    /// <summary>
    /// Completa l'accesso dopo la password: verifica un codice TOTP o un codice di recupero per l'account della sfida.
    /// Ogni codice errato conta come tentativo fallito (stesso contatore e stesso blocco della password).
    /// </summary>
    Task<RisultatoAccesso> CompletaAccessoAsync(Guid idUtenza, Guid timbroSicurezza, string? codice, CancellationToken annullamento);

    Task<StatoDueFattori?> LeggiStatoAsync(Guid idUtenza, CancellationToken annullamento);

    /// <summary>
    /// Genera un segreto in attesa di conferma. Se la 2FA è già attiva (cambio di dispositivo) servono password e codice attuali.
    /// </summary>
    Task<RisultatoDueFattori<ConfigurazioneDueFattori>> AvviaConfigurazioneAsync(
        Guid idUtenza,
        string? password,
        string? codice,
        CancellationToken annullamento);

    /// <summary>Conferma il segreto in attesa con un codice generato da esso: attiva la 2FA e genera i codici di recupero.</summary>
    Task<RisultatoDueFattori<AttivazioneDueFattori>> ConfermaAttivazioneAsync(Guid idUtenza, string? codice, CancellationToken annullamento);

    Task<RisultatoDueFattori<RigenerazioneCodiciRecupero>> RigeneraCodiciRecuperoAsync(
        Guid idUtenza,
        string? password,
        string? codice,
        CancellationToken annullamento);

    /// <summary>Disattiva la 2FA (password e codice); vietato se è obbligatoria per il ruolo. Invalida le sessioni.</summary>
    Task<EsitoDueFattori> DisattivaAsync(Guid idUtenza, string? password, string? codice, CancellationToken annullamento);

    /// <summary>
    /// Comando operativo: azzera la 2FA di un <c>PlatformAdmin</c> (per email). Agisce solo su <c>PlatformAdmin</c> e lascia
    /// traccia nell'audit; chi lo esegue ha già accesso al server e al database. Falso se l'account non esiste.
    /// </summary>
    Task<bool> ReimpostaPlatformAdminAsync(string? email, CancellationToken annullamento);
}
