namespace DvCms.Web.Accessi;

/// <summary>Avvisi di configurazione scritti nel log all'avvio.</summary>
public static partial class AvvisiAvvio
{
    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Sicurezza:ChiaviCookie:Percorso non è impostato: le chiavi di sessione e antiforgery non sono condivise né persistenti (le sessioni si perdono a ogni riavvio del contenitore e non funzionano con più istanze)")]
    public static partial void ChiaviCookieNonPersistenti(ILogger logger);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "ProxyAttendibili non è configurato: se l'applicazione sta dietro un reverse proxy, tutti i client risultano avere l'indirizzo del proxy (i limiti di richieste per indirizzo diventano globali), lo schema resta http (link delle email, HSTS) e l'host non viene letto dalle intestazioni inoltrate")]
    public static partial void ProxyNonConfigurato(ILogger logger);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Sicurezza:DueFattori:Obbligo è 'Nessuno': gli amministratori possono accedere con la sola password (consigliato solo in sviluppo)")]
    public static partial void DueFattoriNonObbligatori(ILogger logger);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "La verifica in due passaggi è obbligatoria ma Sicurezza:ChiaveSegreti non è impostata: nessun amministratore potrà configurarla")]
    public static partial void ChiaveSegretiMancanteConDueFattori(ILogger logger);
}
