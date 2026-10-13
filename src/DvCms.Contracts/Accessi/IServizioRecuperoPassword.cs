using DvCms.DTO.Accessi;

namespace DvCms.Contracts.Accessi;

/// <summary>
/// Recupero password per <c>TenantAdmin</c> e <c>Utente</c> (mai per il <c>PlatformAdmin</c>), nel tenant dell'host.
/// Ogni risposta è uniforme: chi non conosce un account non può dedurre se esiste, è disabilitato o ha superato i limiti.
/// </summary>
public interface IServizioRecuperoPassword
{
    /// <summary>
    /// Registra la richiesta e, se l'account è ammesso e i limiti lo consentono, accoda la prima email con il codice.
    /// Restituisce sempre un riferimento da tenere in un cookie per la fase successiva.
    /// </summary>
    Task<string> RichiediAsync(string? email, string? indirizzoIp, CancellationToken annullamento);

    /// <summary>Verifica il codice; se corretto accoda la seconda email con il link monouso (costruito su <paramref name="urlBase"/>).</summary>
    Task<EsitoVerificaCodice> VerificaCodiceAsync(string? riferimento, string? codice, string urlBase, CancellationToken annullamento);

    /// <summary>Vero se il link è ancora utilizzabile (serve a mostrare il modulo o un errore generico).</summary>
    Task<bool> LinkValidoAsync(string? token, CancellationToken annullamento);

    /// <summary>
    /// Imposta la nuova password con il link: il link e le altre richieste aperte dell'account si invalidano, le sessioni
    /// terminano, il blocco per tentativi falliti si azzera e parte una notifica.
    /// </summary>
    Task<EsitoReimpostazione> ReimpostaAsync(string? token, string? nuovaPassword, CancellationToken annullamento);
}
