using DvCms.Core.Tenancy;
using DvCms.Infrastructure.Persistenza;
using DvCms.IntegrationTests.Supporto;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DvCms.IntegrationTests.Persistenza;

/// <summary>
/// Convenzioni del modello (database.md): istanti UTC in datetime2 e importi con precisione esplicita.
/// Usa un contesto di prova con una tabella propria, senza toccare le entità dell'applicazione.
/// </summary>
public class ConvenzioniColonneTest
{
    [Fact]
    public async Task ColonneUsanoTipiDelleConvenzioni()
    {
        await using var database = await DatabaseDiProva.CreaAsync(TestContext.Current.CancellationToken);
        await using var contesto = CreaContesto(database.Connessione);
        await contesto.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);

        Assert.Equal("datetime2", await TipoColonnaAsync(database, "CreatoIlUtc"));
        Assert.Equal("datetime2", await TipoColonnaAsync(database, "ScadeIlUtc"));
        Assert.Equal("decimal", await TipoColonnaAsync(database, "Importo"));
        Assert.Equal(18, await database.LeggiScalareAsync<int>(
            "SELECT NUMERIC_PRECISION FROM INFORMATION_SCHEMA.COLUMNS WHERE COLUMN_NAME = N'Importo'",
            TestContext.Current.CancellationToken));
        Assert.Equal(4, await database.LeggiScalareAsync<int>(
            "SELECT NUMERIC_SCALE FROM INFORMATION_SCHEMA.COLUMNS WHERE COLUMN_NAME = N'Importo'",
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task IstantiLocaliSonoConvertitiInUtcELettiComeUtc()
    {
        await using var database = await DatabaseDiProva.CreaAsync(TestContext.Current.CancellationToken);
        await using (var scrittura = CreaContesto(database.Connessione))
        {
            await scrittura.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
            var istanteUtc = new DateTime(2026, 10, 9, 12, 30, 0, DateTimeKind.Utc);
            scrittura.Elementi.Add(new ElementoProva { Id = 1, CreatoIlUtc = istanteUtc.ToLocalTime(), Importo = 12.5m });
            await scrittura.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var lettura = CreaContesto(database.Connessione);
        var letto = await lettura.Elementi.SingleAsync(TestContext.Current.CancellationToken);

        Assert.Equal(DateTimeKind.Utc, letto.CreatoIlUtc.Kind);
        Assert.Equal(new DateTime(2026, 10, 9, 12, 30, 0, DateTimeKind.Utc), letto.CreatoIlUtc);
        Assert.Null(letto.ScadeIlUtc);
        Assert.Equal(12.5m, letto.Importo);
    }

    [Fact]
    public async Task IstantiSenzaFusoOrarioVengonoRifiutati()
    {
        await using var database = await DatabaseDiProva.CreaAsync(TestContext.Current.CancellationToken);
        await using var contesto = CreaContesto(database.Connessione);
        await contesto.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);

        contesto.Elementi.Add(new ElementoProva
        {
            Id = 1,
            CreatoIlUtc = new DateTime(2026, 10, 9, 12, 30, 0, DateTimeKind.Unspecified),
        });

        var eccezione = await Assert.ThrowsAsync<DbUpdateException>(
            () => contesto.SaveChangesAsync(TestContext.Current.CancellationToken));
        Assert.IsType<InvalidOperationException>(eccezione.InnerException);
    }

    private static ContestoProva CreaContesto(string connessione) =>
        new(new DbContextOptionsBuilder<ContestoProva>().UseSqlServer(connessione).Options);

    private static Task<string?> TipoColonnaAsync(DatabaseDiProva database, string colonna) =>
        database.LeggiScalareAsync<string>(
            $"SELECT DATA_TYPE FROM INFORMATION_SCHEMA.COLUMNS WHERE COLUMN_NAME = N'{colonna}'",
            TestContext.Current.CancellationToken);

    private sealed class ElementoProva
    {
        public int Id { get; set; }

        public DateTime CreatoIlUtc { get; set; }

        public DateTime? ScadeIlUtc { get; set; }

        public decimal Importo { get; set; }
    }

    private sealed class ContestoProva : ContestoDatabase
    {
        public ContestoProva(DbContextOptions<ContestoProva> opzioni)
            : base(opzioni, new ContestoTenant())
        {
        }

        public DbSet<ElementoProva> Elementi => Set<ElementoProva>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // Il contesto di prova non applica le configurazioni dell'applicazione: le entità con chiave composta, senza una
            // proprietà "Id" per convenzione, non hanno chiave e vanno escluse.
            modelBuilder.Ignore<DvCms.Models.Media.TraduzioneRisorsaMedia>();
            modelBuilder.Ignore<DvCms.Models.Negozio.TraduzioneProdotto>();
            modelBuilder.Ignore<DvCms.Models.Negozio.ImmagineProdotto>();
            modelBuilder.Ignore<DvCms.Models.Negozio.TraduzioneCategoria>();
            modelBuilder.Ignore<DvCms.Models.Negozio.ProdottoCategoria>();
            modelBuilder.Ignore<DvCms.Models.Negozio.TraduzioneVarianteProdotto>();
            modelBuilder.Ignore<DvCms.Models.Negozio.RigaCarrello>();
            modelBuilder.Ignore<DvCms.Models.Negozio.GiacenzaVariante>();
            modelBuilder.Ignore<DvCms.Models.Negozio.TermineRicercaProdotto>();
            var entita = modelBuilder.Entity<ElementoProva>();
            entita.ToTable("ElementiProva");
            entita.Property(elemento => elemento.Id).ValueGeneratedNever();
        }
    }
}
