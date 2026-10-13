using DvCms.Core.Tenancy;
using DvCms.DTO.Tenancy;
using DvCms.Infrastructure.Persistenza;
using DvCms.IntegrationTests.Supporto;
using DvCms.Models.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DvCms.IntegrationTests.Tenancy;

/// <summary>Filtri globali di tenant: ogni query vede solo i dati del tenant della richiesta (B-002).</summary>
public class FiltriTenantTest
{
    [Fact]
    public async Task OgniTenantVedeSoloIPropriDati()
    {
        await using var database = await DatabaseDiProva.CreaAsync(TestContext.Current.CancellationToken);
        await using var fornitore = await ServiziProva.CreaConMigrazioniAsync(database.Connessione, annullamento: TestContext.Current.CancellationToken);
        var a = await DatiProva.CreaTenantAsync(fornitore, "A", "a.esempio.it", annullamento: TestContext.Current.CancellationToken);
        var b = await DatiProva.CreaTenantAsync(fornitore, "B", "b.esempio.it", annullamento: TestContext.Current.CancellationToken);

        foreach (var (visto, altro) in new[] { (a, b), (b, a) })
        {
            await using var ambito = fornitore.CreateAsyncScope();
            ambito.ServiceProvider.GetRequiredService<ContestoTenant>().Imposta(
                new DatiTenantRisolto(visto.IdTenant, visto.IdSito, visto.IdDominio, visto.Host, TenantPrimario: false));
            var contesto = ambito.ServiceProvider.GetRequiredService<ContestoDatabase>();

            var siti = await contesto.Siti.ToListAsync(TestContext.Current.CancellationToken);
            var domini = await contesto.DominiSito.ToListAsync(TestContext.Current.CancellationToken);
            var profili = await contesto.ProfiliGuest.ToListAsync(TestContext.Current.CancellationToken);

            Assert.Equal(visto.IdSito, Assert.Single(siti).Id);
            Assert.Equal(visto.Host, Assert.Single(domini).Host);
            Assert.Equal(visto.IdTenant, Assert.Single(profili).IdTenant);

            // Lettura di una risorsa dell'altro tenant per identificatore: non trovata.
            Assert.Null(await contesto.Siti.FirstOrDefaultAsync(sito => sito.Id == altro.IdSito, TestContext.Current.CancellationToken));
            Assert.Null(await contesto.DominiSito.FirstOrDefaultAsync(dominio => dominio.Id == altro.IdDominio, TestContext.Current.CancellationToken));
        }
    }

    [Fact]
    public async Task SenzaTenantRisoltoNonSiVedeNessunDato()
    {
        await using var database = await DatabaseDiProva.CreaAsync(TestContext.Current.CancellationToken);
        await using var fornitore = await ServiziProva.CreaConMigrazioniAsync(database.Connessione, annullamento: TestContext.Current.CancellationToken);
        await DatiProva.CreaTenantAsync(fornitore, "A", "a.esempio.it", annullamento: TestContext.Current.CancellationToken);

        await using var ambito = fornitore.CreateAsyncScope();
        var contesto = ambito.ServiceProvider.GetRequiredService<ContestoDatabase>();

        Assert.Empty(await contesto.Siti.ToListAsync(TestContext.Current.CancellationToken));
        Assert.Empty(await contesto.DominiSito.ToListAsync(TestContext.Current.CancellationToken));
        Assert.Empty(await contesto.ProfiliGuest.ToListAsync(TestContext.Current.CancellationToken));
        Assert.Single(await contesto.Tenant.ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task LeLettureDiPiattaformaVedonoTuttiISoloConIgnoreQueryFilters()
    {
        await using var database = await DatabaseDiProva.CreaAsync(TestContext.Current.CancellationToken);
        await using var fornitore = await ServiziProva.CreaConMigrazioniAsync(database.Connessione, annullamento: TestContext.Current.CancellationToken);
        await DatiProva.CreaTenantAsync(fornitore, "A", "a.esempio.it", annullamento: TestContext.Current.CancellationToken);
        await DatiProva.CreaTenantAsync(fornitore, "B", "b.esempio.it", annullamento: TestContext.Current.CancellationToken);

        await using var ambito = fornitore.CreateAsyncScope();
        var contesto = ambito.ServiceProvider.GetRequiredService<ContestoDatabase>();

        Assert.Equal(2, await contesto.Siti.IgnoreQueryFilters().CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(2, await contesto.DominiSito.IgnoreQueryFilters().CountAsync(TestContext.Current.CancellationToken));
    }
}
