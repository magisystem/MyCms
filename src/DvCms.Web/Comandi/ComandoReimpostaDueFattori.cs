using DvCms.Application.Accessi.DueFattori;

namespace DvCms.Web.Comandi;

/// <summary>
/// Comando <c>DvCms.Web reset-2fa &lt;email&gt;</c> (ADR-0019): azzera la 2FA di un <c>PlatformAdmin</c> che ha perso dispositivo e codici
/// di recupero. Chi lo esegue ha già accesso al server e al database (come per <c>migra</c> e <c>seed</c>); agisce solo sui
/// <c>PlatformAdmin</c>, chiude le sue sessioni e lascia traccia nell'audit. Al successivo accesso dovrà configurare la 2FA di nuovo.
/// Codice di uscita 0 se riuscito, 1 se l'account non esiste o l'operazione fallisce.
/// </summary>
public static partial class ComandoReimpostaDueFattori
{
    public const string Nome = "reset-2fa";

    public static async Task<int> EseguiAsync(IServiceProvider servizi, string email, CancellationToken annullamento = default)
    {
        ArgumentNullException.ThrowIfNull(servizi);

        var logger = servizi.GetRequiredService<ILoggerFactory>().CreateLogger("DvCms.ReimpostaDueFattori");
        try
        {
            await using var ambito = servizi.CreateAsyncScope();
            var riuscito = await ambito.ServiceProvider.GetRequiredService<IServizioDueFattori>().ReimpostaPlatformAdminAsync(email, annullamento);
            if (!riuscito)
            {
                RegistraNonTrovato(logger);
                return 1;
            }

            RegistraRiuscito(logger);
            return 0;
        }
        catch (Exception eccezione)
        {
            RegistraErrore(logger, eccezione);
            return 1;
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "2FA reimpostata: il PlatformAdmin dovrà configurarla di nuovo al prossimo accesso")]
    private static partial void RegistraRiuscito(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Nessun PlatformAdmin con quell'email: nulla è stato modificato")]
    private static partial void RegistraNonTrovato(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Reimpostazione della 2FA non riuscita")]
    private static partial void RegistraErrore(ILogger logger, Exception eccezione);
}
