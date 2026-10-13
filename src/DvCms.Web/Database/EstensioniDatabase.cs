using DvCms.SqlServer;

namespace DvCms.Web.Database;

/// <summary>Sceglie il provider del database con la configurazione <c>Database:Provider</c> (predefinito <c>SqlServer</c>, ADR-0024).</summary>
public static class EstensioniDatabase
{
    public const string NomeConfigurazione = "Database:Provider";
    public const string ProviderSqlServer = "SqlServer";
    public const string ProviderPostgreSql = "PostgreSql";

    public static IServiceCollection AggiungiDatabase(this IServiceCollection servizi, IConfiguration configurazione)
    {
        ArgumentNullException.ThrowIfNull(servizi);
        ArgumentNullException.ThrowIfNull(configurazione);

        var provider = configurazione[NomeConfigurazione];
        if (string.IsNullOrWhiteSpace(provider) || string.Equals(provider, ProviderSqlServer, StringComparison.OrdinalIgnoreCase))
        {
            return servizi.AggiungiSqlServer(configurazione);
        }

        if (string.Equals(provider, ProviderPostgreSql, StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException("PostgreSQL non è supportato: il progetto DvCms.PostgreSql è un segnaposto vuoto (ADR-0024). Usare 'SqlServer'.");
        }

        throw new InvalidOperationException($"Provider del database '{provider}' sconosciuto in '{NomeConfigurazione}': valori ammessi 'SqlServer'.");
    }
}
