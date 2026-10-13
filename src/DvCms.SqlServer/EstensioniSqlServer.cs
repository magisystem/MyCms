using DvCms.Contracts.Persistenza;
using DvCms.Data;
using DvCms.Data.Cache;
using DvCms.Data.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DvCms.SqlServer;

/// <summary>Provider SQL Server (ADR-0012, ADR-0024): registrazione del contesto, delle migrazioni e delle parti che dipendono dal motore.</summary>
public static class EstensioniSqlServer
{
    public const string NomeConnessionePredefinita = "Predefinita";

    /// <summary>
    /// Registra il contesto del database su SQL Server. La stringa di connessione deve arrivare dalla configurazione di ambiente
    /// (<c>appsettings.Local.json</c>, user-secrets o variabile <c>ConnectionStrings__Predefinita</c>), mai dal repository:
    /// se manca l'avvio fallisce subito.
    /// </summary>
    public static IServiceCollection AggiungiSqlServer(this IServiceCollection servizi, IConfiguration configurazione)
    {
        ArgumentNullException.ThrowIfNull(servizi);
        ArgumentNullException.ThrowIfNull(configurazione);

        var connessione = configurazione.GetConnectionString(NomeConnessionePredefinita);
        if (string.IsNullOrWhiteSpace(connessione))
        {
            throw new InvalidOperationException(
                $"Stringa di connessione 'ConnectionStrings:{NomeConnessionePredefinita}' mancante: " +
                "impostarla in appsettings.Local.json, con user-secrets o con la variabile d'ambiente ConnectionStrings__Predefinita.");
        }

        servizi.AddSingleton<IPersonalizzazioneModello, PersonalizzazioneSqlServer>();
        servizi.AddSingleton<IRilevatoreErroriDatabase, RilevatoreErroriSqlServer>();
        servizi.AddSingleton<IPrenotatoreMessaggiOutbox, PrenotatoreMessaggiOutboxSqlServer>();
        servizi.AddDbContext<ContestoDatabase>((fornitore, opzioni) => opzioni
            .UseSqlServer(connessione, ConfiguraSqlServer)
            .AddInterceptors(fornitore.GetRequiredService<InterceptorInvalidazioneCache>()));
        return servizi;
    }

    internal static void ConfiguraSqlServer(SqlServerDbContextOptionsBuilder opzioni)
    {
        opzioni.MigrationsHistoryTable(ContestoDatabase.NomeTabellaStoricoMigrazioni);

        // Le migrazioni stanno in questo assembly, non in quello del contesto (DvCms.Data).
        opzioni.MigrationsAssembly(typeof(EstensioniSqlServer).Assembly.GetName().Name);
    }
}
