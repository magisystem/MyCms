using DvCms.Infrastructure.Persistenza;
using DvCms.IntegrationTests.Supporto;
using DvCms.Models.Accessi;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DvCms.IntegrationTests.Accessi;

/// <summary>Vincoli della tabella Utenze (ADR-0016): email per tenant, ruolo e tenant coerenti.</summary>
public class ModelloUtenzeTest
{
    [Fact]
    public async Task LaStessaEmailPuoEsistereInTenantDiversiMaNonNelloStessoTenant()
    {
        await using var database = await DatabaseDiProva.CreaAsync(TestContext.Current.CancellationToken);
        await using var fornitore = await ServiziProva.CreaConMigrazioniAsync(database.Connessione, annullamento: TestContext.Current.CancellationToken);
        var a = await DatiProva.CreaTenantAsync(fornitore, "A", "a.esempio.it", annullamento: TestContext.Current.CancellationToken);
        var b = await DatiProva.CreaTenantAsync(fornitore, "B", "b.esempio.it", annullamento: TestContext.Current.CancellationToken);

        await DatiProva.CreaUtenzaAsync(fornitore, RuoloUtenza.TenantAdmin, a.IdTenant, "anna@esempio.it", annullamento: TestContext.Current.CancellationToken);
        await DatiProva.CreaUtenzaAsync(fornitore, RuoloUtenza.TenantAdmin, b.IdTenant, "Anna@Esempio.it", annullamento: TestContext.Current.CancellationToken);
        await DatiProva.CreaUtenzaAsync(fornitore, RuoloUtenza.TenantAdmin, a.IdTenant, "secondo@esempio.it", annullamento: TestContext.Current.CancellationToken);

        var eccezione = await Assert.ThrowsAsync<DbUpdateException>(
            () => DatiProva.CreaUtenzaAsync(fornitore, RuoloUtenza.Utente, a.IdTenant, "ANNA@esempio.it", annullamento: TestContext.Current.CancellationToken));
        Assert.True(eccezione.InnerException is SqlException { Number: 2601 or 2627 });
    }

    [Fact]
    public async Task IPlatformAdminHannoEmailUnicheFraLoro()
    {
        await using var database = await DatabaseDiProva.CreaAsync(TestContext.Current.CancellationToken);
        await using var fornitore = await ServiziProva.CreaConMigrazioniAsync(database.Connessione, annullamento: TestContext.Current.CancellationToken);
        await DatiProva.CreaUtenzaAsync(fornitore, RuoloUtenza.PlatformAdmin, null, "root@esempio.it", annullamento: TestContext.Current.CancellationToken);
        await DatiProva.CreaUtenzaAsync(fornitore, RuoloUtenza.PlatformAdmin, null, "altro@esempio.it", annullamento: TestContext.Current.CancellationToken);

        var eccezione = await Assert.ThrowsAsync<DbUpdateException>(
            () => DatiProva.CreaUtenzaAsync(fornitore, RuoloUtenza.PlatformAdmin, null, "ROOT@esempio.it", annullamento: TestContext.Current.CancellationToken));
        Assert.True(eccezione.InnerException is SqlException { Number: 2601 or 2627 });
    }

    [Fact]
    public async Task IlDatabaseRifiutaRuoloETenantIncoerenti()
    {
        await using var database = await DatabaseDiProva.CreaAsync(TestContext.Current.CancellationToken);
        await using var fornitore = await ServiziProva.CreaConMigrazioniAsync(database.Connessione, annullamento: TestContext.Current.CancellationToken);
        var a = await DatiProva.CreaTenantAsync(fornitore, "A", "a.esempio.it", annullamento: TestContext.Current.CancellationToken);
        await using var ambito = fornitore.CreateAsyncScope();
        var contesto = ambito.ServiceProvider.GetRequiredService<ContestoDatabase>();

        // Il dominio impedisce già queste combinazioni; qui si prova il vincolo del database aggirando la factory.
        var platformConTenant = Utenza.Crea(RuoloUtenza.TenantAdmin, a.IdTenant, "x@esempio.it", "X", "hash", false, TimeProvider.System);
        platformConTenant.Ruolo = RuoloUtenza.PlatformAdmin;
        contesto.Utenze.Add(platformConTenant);
        var primo = await Assert.ThrowsAsync<DbUpdateException>(() => contesto.SaveChangesAsync(TestContext.Current.CancellationToken));
        Assert.Contains("CK_Utenze_RuoloETenant", primo.InnerException?.Message ?? string.Empty, StringComparison.Ordinal);
        contesto.ChangeTracker.Clear();

        var adminSenzaTenant = Utenza.Crea(RuoloUtenza.PlatformAdmin, null, "y@esempio.it", "Y", "hash", false, TimeProvider.System);
        adminSenzaTenant.Ruolo = RuoloUtenza.TenantAdmin;
        contesto.Utenze.Add(adminSenzaTenant);
        var secondo = await Assert.ThrowsAsync<DbUpdateException>(() => contesto.SaveChangesAsync(TestContext.Current.CancellationToken));
        Assert.Contains("CK_Utenze_RuoloETenant", secondo.InnerException?.Message ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task IFiltriDiTenantMostranoSoloGliAccountDelTenantCorrente()
    {
        await using var database = await DatabaseDiProva.CreaAsync(TestContext.Current.CancellationToken);
        await using var fornitore = await ServiziProva.CreaConMigrazioniAsync(database.Connessione, annullamento: TestContext.Current.CancellationToken);
        var a = await DatiProva.CreaTenantAsync(fornitore, "A", "a.esempio.it", annullamento: TestContext.Current.CancellationToken);
        var b = await DatiProva.CreaTenantAsync(fornitore, "B", "b.esempio.it", annullamento: TestContext.Current.CancellationToken);
        await DatiProva.CreaUtenzaAsync(fornitore, RuoloUtenza.TenantAdmin, a.IdTenant, "a@esempio.it", annullamento: TestContext.Current.CancellationToken);
        await DatiProva.CreaUtenzaAsync(fornitore, RuoloUtenza.TenantAdmin, b.IdTenant, "b@esempio.it", annullamento: TestContext.Current.CancellationToken);
        await DatiProva.CreaUtenzaAsync(fornitore, RuoloUtenza.PlatformAdmin, null, "root@esempio.it", annullamento: TestContext.Current.CancellationToken);

        await using var ambito = fornitore.CreateAsyncScope();
        ambito.ServiceProvider.GetRequiredService<DvCms.Application.Tenancy.ContestoTenant>().Imposta(
            new DvCms.Application.Tenancy.DatiTenantRisolto(a.IdTenant, a.IdSito, a.IdDominio, a.Host, TenantPrimario: false));
        var contesto = ambito.ServiceProvider.GetRequiredService<ContestoDatabase>();

        var visibili = await contesto.Utenze.ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal("a@esempio.it", Assert.Single(visibili).Email);
        Assert.Equal(3, await contesto.Utenze.IgnoreQueryFilters().CountAsync(TestContext.Current.CancellationToken));
    }
}
