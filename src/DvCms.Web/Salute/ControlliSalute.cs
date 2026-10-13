using DvCms.Contracts.Media;
using DvCms.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace DvCms.Web.Salute;

/// <summary>
/// Controllo di prontezza del database: raggiungibile e con tutte le migrazioni applicate (un'istanza avviata prima della migrazione
/// non deve ricevere traffico). Il risultato non riporta mai messaggi di errore né dettagli di connessione: solo sano o non sano.
/// </summary>
public sealed partial class ControlloDatabase : IHealthCheck
{
    private readonly IServiceScopeFactory _ambiti;
    private readonly ILogger<ControlloDatabase> _logger;

    public ControlloDatabase(IServiceScopeFactory ambiti, ILogger<ControlloDatabase> logger)
    {
        _ambiti = ambiti;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var ambito = _ambiti.CreateAsyncScope();
            var contesto = ambito.ServiceProvider.GetRequiredService<ContestoDatabase>();
            if (!await contesto.Database.CanConnectAsync(cancellationToken))
            {
                return HealthCheckResult.Unhealthy("non raggiungibile");
            }

            return (await contesto.Database.GetPendingMigrationsAsync(cancellationToken)).Any()
                ? HealthCheckResult.Unhealthy("migrazioni da applicare")
                : HealthCheckResult.Healthy();
        }
        catch (Exception eccezione) when (eccezione is not OperationCanceledException)
        {
            ControlloNonRiuscito(_logger, eccezione);
            return HealthCheckResult.Unhealthy("errore");
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Controllo di prontezza del database non riuscito")]
    private static partial void ControlloNonRiuscito(ILogger logger, Exception eccezione);
}

/// <summary>Controllo di prontezza dell'archivio degli oggetti: si deve poter aprire l'elenco delle chiavi.</summary>
public sealed partial class ControlloArchivio : IHealthCheck
{
    private readonly IArchivioOggetti _archivio;
    private readonly ILogger<ControlloArchivio> _logger;

    public ControlloArchivio(IArchivioOggetti archivio, ILogger<ControlloArchivio> logger)
    {
        _archivio = archivio;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            // Basta la prima chiave (o nessuna): si verifica l'accesso, senza leggere l'archivio intero.
            await foreach (var _ in _archivio.ElencaChiaviAsync(cancellationToken).WithCancellation(cancellationToken))
            {
                break;
            }

            return HealthCheckResult.Healthy();
        }
        catch (Exception eccezione) when (eccezione is not OperationCanceledException)
        {
            ControlloNonRiuscito(_logger, eccezione);
            return HealthCheckResult.Unhealthy("errore");
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Controllo di prontezza dell'archivio non riuscito")]
    private static partial void ControlloNonRiuscito(ILogger logger, Exception eccezione);
}
