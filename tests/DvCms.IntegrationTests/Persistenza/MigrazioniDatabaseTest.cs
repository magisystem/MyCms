using DvCms.Data;
using DvCms.Infrastructure;
using DvCms.IntegrationTests.Supporto;
using DvCms.SqlServer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DvCms.IntegrationTests.Persistenza;

/// <summary>
/// Le migrazioni EF Core si applicano a un database vuoto su SQL Server reale e il modello è allineato all'ultima migrazione.
/// </summary>
public class MigrazioniDatabaseTest
{
    [Fact]
    public async Task MigrazioniSiApplicanoADatabaseVuoto()
    {
        await using var database = await DatabaseDiProva.CreaAsync(TestContext.Current.CancellationToken);
        await using var fornitore = CreaFornitore(database.Connessione);
        await using var ambito = fornitore.CreateAsyncScope();
        var contesto = ambito.ServiceProvider.GetRequiredService<ContestoDatabase>();

        var inSospeso = (await contesto.Database.GetPendingMigrationsAsync(TestContext.Current.CancellationToken)).ToList();
        Assert.NotEmpty(inSospeso);

        await contesto.Database.MigrateAsync(TestContext.Current.CancellationToken);

        Assert.Empty(await contesto.Database.GetPendingMigrationsAsync(TestContext.Current.CancellationToken));
        var applicate = (await contesto.Database.GetAppliedMigrationsAsync(TestContext.Current.CancellationToken)).ToList();
        Assert.Equal(inSospeso, applicate);

        var tabelleStorico = await database.LeggiScalareAsync<int>(
            $"SELECT COUNT(*) FROM sys.tables WHERE name = N'{ContestoDatabase.NomeTabellaStoricoMigrazioni}'",
            TestContext.Current.CancellationToken);
        Assert.Equal(1, tabelleStorico);
    }

    [Fact]
    public async Task MigrazioniSiPossonoRiapplicareSenzaErrori()
    {
        await using var database = await DatabaseDiProva.CreaAsync(TestContext.Current.CancellationToken);
        await using var fornitore = CreaFornitore(database.Connessione);
        await using var ambito = fornitore.CreateAsyncScope();
        var contesto = ambito.ServiceProvider.GetRequiredService<ContestoDatabase>();

        await contesto.Database.MigrateAsync(TestContext.Current.CancellationToken);
        await contesto.Database.MigrateAsync(TestContext.Current.CancellationToken);

        Assert.Empty(await contesto.Database.GetPendingMigrationsAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ModelloAllineatoAllUltimaMigrazione()
    {
        await using var database = await DatabaseDiProva.CreaAsync(TestContext.Current.CancellationToken);
        await using var fornitore = CreaFornitore(database.Connessione);
        await using var ambito = fornitore.CreateAsyncScope();
        var contesto = ambito.ServiceProvider.GetRequiredService<ContestoDatabase>();

        // Se il modello cambia senza una nuova migrazione, questo test fallisce.
        Assert.False(
            contesto.Database.HasPendingModelChanges(),
            "Il modello EF Core ha modifiche senza migrazione: generarla con 'dotnet ef migrations add'.");
    }

    private static ServiceProvider CreaFornitore(string connessione)
    {
        var configurazione = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Predefinita"] = connessione })
            .Build();

        return new ServiceCollection()
            .AggiungiSqlServer(configurazione)
            .AggiungiInfrastruttura(configurazione)
            .BuildServiceProvider();
    }
}
