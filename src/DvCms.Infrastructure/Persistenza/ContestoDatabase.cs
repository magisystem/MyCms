using DvCms.Domain.Localizzazione;
using DvCms.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace DvCms.Infrastructure.Persistenza;

/// <summary>
/// Contesto EF Core dell'applicazione (SQL Server). Le entità e le mappature delle tabelle si aggiungono qui,
/// con nomi di tabelle e colonne in italiano (ADR-0017) e mappatura esplicita.
/// </summary>
public class ContestoDatabase : DbContext
{
    public const string NomeTabellaStoricoMigrazioni = "StoricoMigrazioni";

    public ContestoDatabase(DbContextOptions<ContestoDatabase> opzioni)
        : base(opzioni)
    {
    }

    protected ContestoDatabase(DbContextOptions opzioni)
        : base(opzioni)
    {
    }

    public DbSet<Lingua> Lingue => Set<Lingua>();

    public DbSet<TestoStatico> TestiStatici => Set<TestoStatico>();

    public DbSet<Tenant> Tenant => Set<Tenant>();

    public DbSet<Sito> Siti => Set<Sito>();

    public DbSet<DominioSito> DominiSito => Set<DominioSito>();

    public DbSet<ProfiloGuest> ProfiliGuest => Set<ProfiloGuest>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ContestoDatabase).Assembly);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        ArgumentNullException.ThrowIfNull(configurationBuilder);

        // Convenzioni del modello (docs/architecture/database.md): istanti UTC in datetime2 e importi con precisione esplicita.
        configurationBuilder.Properties<DateTime>()
            .HaveColumnType("datetime2")
            .HaveConversion<ConvertitoreDataOraUtc>();
        configurationBuilder.Properties<decimal>()
            .HavePrecision(18, 4);
    }
}
