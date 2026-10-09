using DvCms.Infrastructure.Persistenza;
using DvCms.IntegrationTests.Supporto;
using DvCms.Web.Comandi;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DvCms.IntegrationTests.Comandi;

public class ComandoMigraTest
{
    [Fact]
    public async Task ApplicaLeMigrazioniAlDatabaseConfigurato()
    {
        await using var database = await DatabaseDiProva.CreaAsync(TestContext.Current.CancellationToken);
        using var fabbrica = new FabbricaApplicazioneConConnessione(database.Connessione);

        var esito = await ComandoMigra.EseguiAsync(fabbrica.Services, TestContext.Current.CancellationToken);

        Assert.Equal(0, esito);
        await using var ambito = fabbrica.Services.CreateAsyncScope();
        var contesto = ambito.ServiceProvider.GetRequiredService<ContestoDatabase>();
        Assert.Empty(await contesto.Database.GetPendingMigrationsAsync(TestContext.Current.CancellationToken));
        Assert.NotEmpty(await contesto.Database.GetAppliedMigrationsAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RestituisceErroreSeIlDatabaseNonEDisponibile()
    {
        using var fabbrica = new FabbricaApplicazioneConConnessione(
            "Server=localhost,1;Database=NonEsiste;User Id=sa;Password=x;TrustServerCertificate=True;Connect Timeout=2");

        var esito = await ComandoMigra.EseguiAsync(fabbrica.Services, TestContext.Current.CancellationToken);

        Assert.Equal(1, esito);
    }
}
