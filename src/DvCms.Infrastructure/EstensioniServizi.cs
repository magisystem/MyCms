using DvCms.Infrastructure.Persistenza;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DvCms.Infrastructure;

public static class EstensioniServizi
{
    public const string NomeConnessionePredefinita = "Predefinita";

    /// <summary>
    /// Registra persistenza (SQL Server) e servizi di infrastruttura. La stringa di connessione deve arrivare da
    /// configurazione di ambiente, user-secrets o variabile <c>ConnectionStrings__Predefinita</c>, mai dal repository:
    /// se manca l'avvio fallisce subito.
    /// </summary>
    public static IServiceCollection AggiungiInfrastruttura(this IServiceCollection servizi, IConfiguration configurazione)
    {
        ArgumentNullException.ThrowIfNull(servizi);
        ArgumentNullException.ThrowIfNull(configurazione);

        var connessione = configurazione.GetConnectionString(NomeConnessionePredefinita);
        if (string.IsNullOrWhiteSpace(connessione))
        {
            throw new InvalidOperationException(
                $"Stringa di connessione 'ConnectionStrings:{NomeConnessionePredefinita}' mancante: " +
                "impostarla con user-secrets o con la variabile d'ambiente ConnectionStrings__Predefinita.");
        }

        servizi.AddDbContext<ContestoDatabase>(opzioni => opzioni.UseSqlServer(connessione, ConfiguraSqlServer));
        servizi.TryAddSingleton(TimeProvider.System);

        return servizi;
    }

    internal static void ConfiguraSqlServer(SqlServerDbContextOptionsBuilder opzioni)
    {
        opzioni.MigrationsHistoryTable(ContestoDatabase.NomeTabellaStoricoMigrazioni);
    }
}
