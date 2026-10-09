using DvCms.Application.Tenancy;
using DvCms.Domain.Accessi;
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

    private readonly IContestoTenant _contestoTenant;

    public ContestoDatabase(DbContextOptions<ContestoDatabase> opzioni, IContestoTenant contestoTenant)
        : base(opzioni)
    {
        _contestoTenant = contestoTenant;
    }

    protected ContestoDatabase(DbContextOptions opzioni, IContestoTenant contestoTenant)
        : base(opzioni)
    {
        _contestoTenant = contestoTenant;
    }

    /// <summary>
    /// Tenant della richiesta, usato dai filtri globali: i dati con <c>IdTenant</c> sono visibili solo per questo tenant;
    /// senza tenant risolto non è visibile nessuna riga. Le letture di piattaforma (tutti i tenant) usano
    /// esplicitamente <c>IgnoreQueryFilters()</c> in casi d'uso privilegiati e verificati da test.
    /// </summary>
    public Guid? IdTenantCorrente => _contestoTenant.IdTenantOpzionale;

    public DbSet<Lingua> Lingue => Set<Lingua>();

    public DbSet<TestoStatico> TestiStatici => Set<TestoStatico>();

    public DbSet<Tenant> Tenant => Set<Tenant>();

    public DbSet<Sito> Siti => Set<Sito>();

    public DbSet<DominioSito> DominiSito => Set<DominioSito>();

    public DbSet<ProfiloGuest> ProfiliGuest => Set<ProfiloGuest>();

    public DbSet<Utenza> Utenze => Set<Utenza>();

    public DbSet<RegistroAudit> RegistroAudit => Set<RegistroAudit>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ContestoDatabase).Assembly);

        // Isolamento tenant (difesa a più livelli, docs/architecture/multi-tenancy.md): filtro globale sui dati con IdTenant.
        modelBuilder.Entity<Sito>().HasQueryFilter(sito => sito.IdTenant == IdTenantCorrente);
        modelBuilder.Entity<DominioSito>().HasQueryFilter(dominio => dominio.IdTenant == IdTenantCorrente);
        modelBuilder.Entity<ProfiloGuest>().HasQueryFilter(profilo => profilo.IdTenant == IdTenantCorrente);
        modelBuilder.Entity<Utenza>().HasQueryFilter(utenza => utenza.IdTenant == IdTenantCorrente);
        modelBuilder.Entity<RegistroAudit>().HasQueryFilter(voce => voce.IdTenant == IdTenantCorrente);
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
