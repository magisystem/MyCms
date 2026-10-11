using DvCms.Application.Negozio;
using DvCms.Application.Piattaforma;
using DvCms.Application.Tenancy;
using DvCms.Domain.Accessi;
using DvCms.Domain.Negozio;
using DvCms.Infrastructure.Persistenza;
using DvCms.IntegrationTests.Supporto;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DvCms.TenantIsolationTests;

/// <summary>Matrice di isolamento (B-004) sui metodi di spedizione (F-002): servizio e vincoli del database.</summary>
public class IsolamentoSpedizioniTest
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task IMetodiDiAENonSiVedonoNeSiToccanoDaBEIlDatabaseNonAccettaCoppieTenantSitoIncoerenti()
    {
        await using var d = await DatiIsolamento.CreaAsync();
        var root = await d.Web.CreaPlatformAdminAsync("root@esempio.it");
        foreach (var tenant in new[] { d.A, d.B })
        {
            await using var ambito = d.Web.Fabbrica.Services.CreateAsyncScope();
            Assert.True((await ambito.ServiceProvider.GetRequiredService<IServizioPiattaforma>().ImpostaCommercioAsync(root, tenant.IdTenant, tenant.IdSito, true, Ct)).Riuscito);
        }

        var clienteB = await DatiProva.CreaUtenzaAsync(d.Web.Fabbrica.Services, RuoloUtenza.Utente, d.B.IdTenant, "cliente@esempio.it", annullamento: Ct);
        var dati = new DatiMetodoSpedizione(null, TipoMetodoSpedizione.Zona, AreaSpedizione.Italia, "Di A", 5m, true);
        var creato = await ComeAsync(d, d.A, s => s.SalvaAsync(d.AdminA, dati, Ct));
        Assert.True(creato.Riuscito);

        Assert.Equal(CodiceEsito.NonAutorizzato, (await ComeAsync(d, d.B, s => s.SalvaAsync(d.AdminA, dati, Ct))).Codice);
        Assert.Equal(CodiceEsito.NonTrovato, (await ComeAsync(d, d.B, s => s.EliminaAsync(d.AdminB, creato.Id!.Value, Ct))).Codice);
        Assert.Equal(CodiceEsito.NonTrovato, (await ComeAsync(d, d.B, s => s.SalvaAsync(d.AdminB, dati with { Id = creato.Id }, Ct))).Codice);
        Assert.Empty(await ComeAsync(d, d.B, s => s.ElencaAsync(d.AdminB, Ct)));
        Assert.Empty(await ComeAsync(d, d.B, s => s.ElencaAttiviAsync(clienteB, Ct)));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => ComeAsync(d, d.A, s => s.ElencaAttiviAsync(clienteB, Ct)));
        Assert.Equal("Di A", (await ComeAsync(d, d.A, s => s.ElencaAsync(d.AdminA, Ct))).Single().Nome);

        // Anche aggirando il servizio, un metodo non può puntare a un sito di un altro tenant.
        await using var scrittura = d.Web.Fabbrica.Services.CreateAsyncScope();
        var db = scrittura.ServiceProvider.GetRequiredService<ContestoDatabase>();
        db.MetodiSpedizione.Add(new MetodoSpedizione { Id = Guid.NewGuid(), IdTenant = d.B.IdTenant, IdSito = d.A.IdSito, Tipo = TipoMetodoSpedizione.Ritiro, Nome = "Incoerente" });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(Ct));
    }

    private static async Task<T> ComeAsync<T>(DatiIsolamento d, DatiProva.TenantCreato tenant, Func<IServizioSpedizioni, Task<T>> operazione)
    {
        await using var ambito = d.Web.Fabbrica.Services.CreateAsyncScope();
        ambito.ServiceProvider.GetRequiredService<ContestoTenant>().Imposta(new DatiTenantRisolto(tenant.IdTenant, tenant.IdSito, tenant.IdDominio, tenant.Host, false));
        return await operazione(ambito.ServiceProvider.GetRequiredService<IServizioSpedizioni>());
    }
}
