using DvCms.Data;
using DvCms.Infrastructure.Persistenza;
using DvCms.IntegrationTests.Supporto;
using DvCms.Models.Tenancy;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DvCms.IntegrationTests.Tenancy;

/// <summary>Vincoli e relazioni del modello tenant, sito, dominio e profilo guest su SQL Server reale (B-001).</summary>
public class ModelloTenancyTest
{
    private static readonly TimeProvider _orologio = TimeProvider.System;

    [Fact]
    public async Task TenantSitoDominioEProfiloSiSalvanoEIStatiTornanoCorretti()
    {
        await using var database = await DatabaseDiProva.CreaAsync(TestContext.Current.CancellationToken);
        await using var fornitore = await ServiziProva.CreaConMigrazioniAsync(database.Connessione, annullamento: TestContext.Current.CancellationToken);
        Guid idTenant;
        await using (var ambito = fornitore.CreateAsyncScope())
        {
            var contesto = ambito.ServiceProvider.GetRequiredService<ContestoDatabase>();
            var tenant = Tenant.Crea("Cliente Uno", primario: false, _orologio);
            tenant.Stato = StatoTenant.Sospeso;
            var sito = Sito.Crea(tenant.Id, "Sito principale", null, _orologio);
            sito.Stato = StatoSito.Disattivato;
            contesto.Tenant.Add(tenant);
            contesto.Siti.Add(sito);
            contesto.DominiSito.Add(DominioSito.CreaVerificato(tenant.Id, sito.Id, "Cliente1.Esempio.IT", false, null, _orologio));
            contesto.ProfiliGuest.Add(ProfiloGuest.Crea(tenant.Id, _orologio));
            await contesto.SaveChangesAsync(TestContext.Current.CancellationToken);
            idTenant = tenant.Id;
        }

        await using var lettura = fornitore.CreateAsyncScope();
        var letto = lettura.ServiceProvider.GetRequiredService<ContestoDatabase>();
        var tenantLetto = await letto.Tenant.SingleAsync(TestContext.Current.CancellationToken);
        var sitoLetto = await letto.Siti.IgnoreQueryFilters().SingleAsync(TestContext.Current.CancellationToken);
        var dominioLetto = await letto.DominiSito.IgnoreQueryFilters().SingleAsync(TestContext.Current.CancellationToken);

        Assert.Equal(idTenant, tenantLetto.Id);
        Assert.Equal(StatoTenant.Sospeso, tenantLetto.Stato);
        Assert.Equal(StatoSito.Disattivato, sitoLetto.Stato);
        Assert.Equal("Europe/Rome", sitoLetto.FusoOrario);
        Assert.False(sitoLetto.RegistrazioneAcquirentiAbilitata);
        Assert.Equal("cliente1.esempio.it", dominioLetto.Host);
        Assert.NotNull(dominioLetto.VerificatoIlUtc);
        Assert.Equal(DateTimeKind.Utc, dominioLetto.VerificatoIlUtc!.Value.Kind);
        Assert.NotEmpty(tenantLetto.TokenConcorrenza);
        Assert.Equal(
            "Sospeso",
            await database.LeggiScalareAsync<string>("SELECT Stato FROM Tenant", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task HostDuplicatoVieneRifiutatoAncheTraTenantDiversi()
    {
        await using var database = await DatabaseDiProva.CreaAsync(TestContext.Current.CancellationToken);
        await using var fornitore = await ServiziProva.CreaConMigrazioniAsync(database.Connessione, annullamento: TestContext.Current.CancellationToken);
        await using var ambito = fornitore.CreateAsyncScope();
        var contesto = ambito.ServiceProvider.GetRequiredService<ContestoDatabase>();
        var (_, sitoA) = AggiungiTenantESito(contesto, "A");
        var (_, sitoB) = AggiungiTenantESito(contesto, "B");
        contesto.DominiSito.Add(DominioSito.CreaVerificato(sitoA.IdTenant, sitoA.Id, "condiviso.esempio.it", false, null, _orologio));
        await contesto.SaveChangesAsync(TestContext.Current.CancellationToken);

        contesto.DominiSito.Add(DominioSito.CreaVerificato(sitoB.IdTenant, sitoB.Id, "CONDIVISO.esempio.it", false, null, _orologio));

        var eccezione = await Assert.ThrowsAsync<DbUpdateException>(() => contesto.SaveChangesAsync(TestContext.Current.CancellationToken));
        Assert.True(ViolazioneUnivocita(eccezione));
    }

    [Fact]
    public async Task HostNonMinuscoloViolaIlVincoloSulDatabase()
    {
        await using var database = await DatabaseDiProva.CreaAsync(TestContext.Current.CancellationToken);
        await using var fornitore = await ServiziProva.CreaConMigrazioniAsync(database.Connessione, annullamento: TestContext.Current.CancellationToken);
        await using var ambito = fornitore.CreateAsyncScope();
        var contesto = ambito.ServiceProvider.GetRequiredService<ContestoDatabase>();
        var (_, sito) = AggiungiTenantESito(contesto, "A");
        var dominio = DominioSito.CreaVerificato(sito.IdTenant, sito.Id, "minuscolo.esempio.it", false, null, _orologio);
        dominio.Host = "Maiuscolo.Esempio.it";
        contesto.DominiSito.Add(dominio);

        var eccezione = await Assert.ThrowsAsync<DbUpdateException>(() => contesto.SaveChangesAsync(TestContext.Current.CancellationToken));
        Assert.Contains("CK_DominiSito_HostMinuscolo", eccezione.InnerException?.Message ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DominioNonPuoPuntareASitoDiUnAltroTenant()
    {
        await using var database = await DatabaseDiProva.CreaAsync(TestContext.Current.CancellationToken);
        await using var fornitore = await ServiziProva.CreaConMigrazioniAsync(database.Connessione, annullamento: TestContext.Current.CancellationToken);
        await using var ambito = fornitore.CreateAsyncScope();
        var contesto = ambito.ServiceProvider.GetRequiredService<ContestoDatabase>();
        var (tenantA, _) = AggiungiTenantESito(contesto, "A");
        var (_, sitoB) = AggiungiTenantESito(contesto, "B");
        await contesto.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Il dominio dichiara il tenant A ma punta al sito del tenant B: la chiave esterna composta lo impedisce.
        contesto.DominiSito.Add(DominioSito.CreaVerificato(tenantA.Id, sitoB.Id, "incrociato.esempio.it", false, null, _orologio));

        var eccezione = await Assert.ThrowsAsync<DbUpdateException>(() => contesto.SaveChangesAsync(TestContext.Current.CancellationToken));
        Assert.True(eccezione.InnerException is SqlException { Number: 547 });
    }

    [Fact]
    public async Task ILTenantPrimarioEUnoSolo()
    {
        await using var database = await DatabaseDiProva.CreaAsync(TestContext.Current.CancellationToken);
        await using var fornitore = await ServiziProva.CreaConMigrazioniAsync(database.Connessione, annullamento: TestContext.Current.CancellationToken);
        await using var ambito = fornitore.CreateAsyncScope();
        var contesto = ambito.ServiceProvider.GetRequiredService<ContestoDatabase>();
        contesto.Tenant.Add(Tenant.Crea("Gestione", primario: true, _orologio));
        contesto.Tenant.Add(Tenant.Crea("Altro cliente", primario: false, _orologio));
        contesto.Tenant.Add(Tenant.Crea("Altro cliente 2", primario: false, _orologio));
        await contesto.SaveChangesAsync(TestContext.Current.CancellationToken);

        contesto.Tenant.Add(Tenant.Crea("Secondo primario", primario: true, _orologio));

        var eccezione = await Assert.ThrowsAsync<DbUpdateException>(() => contesto.SaveChangesAsync(TestContext.Current.CancellationToken));
        Assert.True(ViolazioneUnivocita(eccezione));
    }

    [Fact]
    public async Task OgniTenantHaAlPiuUnProfiloGuest()
    {
        await using var database = await DatabaseDiProva.CreaAsync(TestContext.Current.CancellationToken);
        await using var fornitore = await ServiziProva.CreaConMigrazioniAsync(database.Connessione, annullamento: TestContext.Current.CancellationToken);
        await using var ambito = fornitore.CreateAsyncScope();
        var contesto = ambito.ServiceProvider.GetRequiredService<ContestoDatabase>();
        var tenant = Tenant.Crea("A", primario: false, _orologio);
        contesto.Tenant.Add(tenant);
        contesto.ProfiliGuest.Add(ProfiloGuest.Crea(tenant.Id, _orologio));
        await contesto.SaveChangesAsync(TestContext.Current.CancellationToken);

        contesto.ProfiliGuest.Add(ProfiloGuest.Crea(tenant.Id, _orologio));

        var eccezione = await Assert.ThrowsAsync<DbUpdateException>(() => contesto.SaveChangesAsync(TestContext.Current.CancellationToken));
        Assert.True(ViolazioneUnivocita(eccezione));
    }

    [Fact]
    public async Task NonSiPuoEliminareUnTenantConSiti()
    {
        await using var database = await DatabaseDiProva.CreaAsync(TestContext.Current.CancellationToken);
        await using var fornitore = await ServiziProva.CreaConMigrazioniAsync(database.Connessione, annullamento: TestContext.Current.CancellationToken);
        await using var ambito = fornitore.CreateAsyncScope();
        var contesto = ambito.ServiceProvider.GetRequiredService<ContestoDatabase>();
        var (tenant, _) = AggiungiTenantESito(contesto, "A");
        await contesto.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Eliminazione diretta sul database, per provare il vincolo della chiave esterna e non il tracciamento di EF.
        var eccezione = await Assert.ThrowsAnyAsync<Exception>(
            () => contesto.Tenant.Where(candidato => candidato.Id == tenant.Id).ExecuteDeleteAsync(TestContext.Current.CancellationToken));
        Assert.True(ContieneErroreSql(eccezione, 547));
    }

    private static (Tenant Tenant, Sito Sito) AggiungiTenantESito(ContestoDatabase contesto, string nome)
    {
        var tenant = Tenant.Crea($"Tenant {nome}", primario: false, _orologio);
        var sito = Sito.Crea(tenant.Id, $"Sito {nome}", null, _orologio);
        contesto.Tenant.Add(tenant);
        contesto.Siti.Add(sito);
        return (tenant, sito);
    }

    private static bool ContieneErroreSql(Exception? eccezione, int numero)
    {
        for (var corrente = eccezione; corrente is not null; corrente = corrente.InnerException)
        {
            if (corrente is SqlException sql && sql.Number == numero)
            {
                return true;
            }
        }

        return false;
    }

    private static bool ViolazioneUnivocita(DbUpdateException eccezione) =>
        eccezione.InnerException is SqlException { Number: 2601 or 2627 };
}
