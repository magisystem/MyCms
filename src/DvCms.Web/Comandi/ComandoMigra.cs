using DvCms.Infrastructure.Persistenza;
using Microsoft.EntityFrameworkCore;

namespace DvCms.Web.Comandi;

/// <summary>
/// Comando <c>DvCms.Web migra</c>: applica al database le migrazioni EF Core non ancora applicate e termina.
/// Le migrazioni non partono mai in automatico all'avvio dell'applicazione.
/// </summary>
public static partial class ComandoMigra
{
    public const string Nome = "migra";

    public static async Task<int> EseguiAsync(IServiceProvider servizi, CancellationToken annullamento = default)
    {
        ArgumentNullException.ThrowIfNull(servizi);

        var logger = servizi.GetRequiredService<ILoggerFactory>().CreateLogger("DvCms.Migrazioni");
        try
        {
            await using var ambito = servizi.CreateAsyncScope();
            var contesto = ambito.ServiceProvider.GetRequiredService<ContestoDatabase>();

            var inSospeso = (await contesto.Database.GetPendingMigrationsAsync(annullamento)).ToList();
            RegistraMigrazioniDaApplicare(logger, inSospeso.Count);

            await contesto.Database.MigrateAsync(annullamento);

            RegistraMigrazioniApplicate(logger, inSospeso);
            return 0;
        }
        catch (Exception eccezione)
        {
            RegistraErrore(logger, eccezione);
            return 1;
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Migrazioni da applicare: {Numero}")]
    private static partial void RegistraMigrazioniDaApplicare(ILogger logger, int numero);

    [LoggerMessage(Level = LogLevel.Information, Message = "Migrazioni applicate: {Elenco}")]
    private static partial void RegistraMigrazioniApplicate(ILogger logger, IReadOnlyCollection<string> elenco);

    [LoggerMessage(Level = LogLevel.Error, Message = "Applicazione delle migrazioni non riuscita")]
    private static partial void RegistraErrore(ILogger logger, Exception eccezione);
}
