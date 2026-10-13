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

/// <summary>Matrice di isolamento (B-004) sui pagamenti manuali (ADR-0023): impostazioni, servizio e vincoli del database.</summary>
public class IsolamentoPagamentiOfflineTest
{
    private const string IbanA = "IT60X0542811101000000123456";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly ImpostazioniPagamentoOffline _dati = ImpostazioniPagamentoOffline.Spente with
    {
        BonificoAttivo = true,
        Intestatario = "Di A",
        Iban = IbanA,
        ContrassegnoAttivo = true,
        CommissioneFissa = 3m,
    };

    [Fact]
    public async Task LeImpostazioniDiANonSiVedonoNeSiToccanoDaBEIlDatabaseNonAccettaCoppieTenantSitoIncoerenti()
    {
        await using var d = await DatiIsolamento.CreaAsync();
        var root = await d.Web.CreaPlatformAdminAsync("root@esempio.it");
        foreach (var tenant in new[] { d.A, d.B })
        {
            await using var ambito = d.Web.Fabbrica.Services.CreateAsyncScope();
            Assert.True((await ambito.ServiceProvider.GetRequiredService<IServizioPiattaforma>().ImpostaCommercioAsync(root, tenant.IdTenant, tenant.IdSito, true, Ct)).Riuscito);
        }

        var clienteA = await DatiProva.CreaUtenzaAsync(d.Web.Fabbrica.Services, RuoloUtenza.Utente, d.A.IdTenant, "cliente@esempio.it", annullamento: Ct);
        var clienteB = await DatiProva.CreaUtenzaAsync(d.Web.Fabbrica.Services, RuoloUtenza.Utente, d.B.IdTenant, "cliente@esempio.it", annullamento: Ct);
        Assert.True((await ComeAsync(d, d.A, s => s.SalvaAsync(d.AdminA, _dati, null, Ct))).Riuscito);

        // B non legge né sovrascrive quelle di A, e un amministratore di A non agisce nel contesto di B.
        Assert.Equal(CodiceEsito.NonAutorizzato, (await ComeAsync(d, d.B, s => s.SalvaAsync(d.AdminA, _dati, null, Ct))).Codice);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => ComeAsync(d, d.B, s => s.LeggiAsync(d.AdminA, Ct)));
        Assert.Equal(ImpostazioniPagamentoOffline.Spente, (await ComeAsync(d, d.B, s => s.LeggiAsync(d.AdminB, Ct))).Impostazioni);
        Assert.Equal(ImpostazioniPagamentoOffline.Spente, await ComeAsync(d, d.B, s => s.LeggiPerClienteAsync(clienteB, Ct)));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => ComeAsync(d, d.A, s => s.LeggiPerClienteAsync(clienteB, Ct)));
        Assert.Equal(IbanA, (await ComeAsync(d, d.A, s => s.LeggiPerClienteAsync(clienteA, Ct))).Iban);

        // Il salvataggio di B non tocca la riga di A.
        Assert.True((await ComeAsync(d, d.B, s => s.SalvaAsync(d.AdminB, _dati with { Intestatario = "Di B", Iban = "DE89370400440532013000" }, null, Ct))).Riuscito);
        Assert.Equal("Di A", (await ComeAsync(d, d.A, s => s.LeggiAsync(d.AdminA, Ct))).Impostazioni.Intestatario);
        Assert.Equal("Di B", (await ComeAsync(d, d.B, s => s.LeggiAsync(d.AdminB, Ct))).Impostazioni.Intestatario);

        // Anche aggirando il servizio, le impostazioni non possono puntare a un sito di un altro tenant né essere due per lo stesso sito.
        await using var scrittura = d.Web.Fabbrica.Services.CreateAsyncScope();
        var db = scrittura.ServiceProvider.GetRequiredService<ContestoDatabase>();
        db.ImpostazioniPagamentoOfflineSiti.Add(new ImpostazioniPagamentoOfflineSito { Id = Guid.NewGuid(), IdTenant = d.B.IdTenant, IdSito = d.A.IdSito, AggiornatoIlUtc = DateTime.UtcNow });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(Ct));
    }

    [Fact]
    public async Task IlDatabaseRifiutaValoriFuoriLimite()
    {
        await using var d = await DatiIsolamento.CreaAsync();
        await using var scrittura = d.Web.Fabbrica.Services.CreateAsyncScope();
        var db = scrittura.ServiceProvider.GetRequiredService<ContestoDatabase>();
        db.ImpostazioniPagamentoOfflineSiti.Add(new ImpostazioniPagamentoOfflineSito
        {
            Id = Guid.NewGuid(),
            IdTenant = d.A.IdTenant,
            IdSito = d.A.IdSito,
            GiorniPerPagare = 90,
            AggiornatoIlUtc = DateTime.UtcNow,
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(Ct));
    }

    private static async Task<T> ComeAsync<T>(DatiIsolamento d, DatiProva.TenantCreato tenant, Func<IServizioPagamentiOffline, Task<T>> operazione)
    {
        await using var ambito = d.Web.Fabbrica.Services.CreateAsyncScope();
        ambito.ServiceProvider.GetRequiredService<ContestoTenant>().Imposta(new DatiTenantRisolto(tenant.IdTenant, tenant.IdSito, tenant.IdDominio, tenant.Host, false));
        return await operazione(ambito.ServiceProvider.GetRequiredService<IServizioPagamentiOffline>());
    }
}
