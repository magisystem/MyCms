using DvCms.Core.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace DvCms.Infrastructure.Persistenza;

/// <summary>
/// Usata solo dagli strumenti di EF Core (<c>dotnet ef</c>) per creare le migrazioni. Non apre connessioni:
/// la stringa predefinita è un segnaposto, sostituibile con la variabile d'ambiente <c>ConnectionStrings__Predefinita</c>.
/// </summary>
public sealed class FabbricaContestoDatabaseProgettazione : IDesignTimeDbContextFactory<ContestoDatabase>
{
    private const string ConnessioneSegnaposto = "Server=localhost;Database=DvCms;Integrated Security=false;TrustServerCertificate=True";

    public ContestoDatabase CreateDbContext(string[] args)
    {
        var connessione = Environment.GetEnvironmentVariable("ConnectionStrings__Predefinita");
        if (string.IsNullOrWhiteSpace(connessione))
        {
            connessione = ConnessioneSegnaposto;
        }

        var opzioni = new DbContextOptionsBuilder<ContestoDatabase>()
            .UseSqlServer(connessione, EstensioniServizi.ConfiguraSqlServer)
            .Options;

        return new ContestoDatabase(opzioni, new ContestoTenant());
    }
}
