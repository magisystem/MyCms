using DvCms.Contracts.Cms;
using DvCms.Core.Tenancy;
using DvCms.DTO.Cms;
using DvCms.DTO.Tenancy;
using DvCms.IntegrationTests.Supporto;
using DvCms.IntegrationTests.Web;
using DvCms.Models.Accessi;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DvCms.TenantIsolationTests;

/// <summary>Scenario a due tenant (A e B) con un amministratore e una pagina pubblicata ciascuno.</summary>
internal sealed class DatiIsolamento : IAsyncDisposable
{
    public const string PasswordA = DatiProva.PasswordProva;

    private DatiIsolamento(ScenarioWeb web, Guid adminA, Guid adminB)
    {
        Web = web;
        AdminA = adminA;
        AdminB = adminB;
    }

    public ScenarioWeb Web { get; }

    public Guid AdminA { get; }

    public Guid AdminB { get; }

    public Guid PaginaA { get; private set; }

    public Guid PaginaB { get; private set; }

    public DatiProva.TenantCreato A => Web.A;

    public DatiProva.TenantCreato B => Web.B;

    public static async Task<DatiIsolamento> CreaAsync()
    {
        var web = await ScenarioWeb.CreaAsync();
        var adminA = await web.CreaTenantAdminAsync("anna@esempio.it");
        var adminB = await DatiProva.CreaUtenzaAsync(web.Fabbrica.Services, RuoloUtenza.TenantAdmin, web.B.IdTenant, "bruno@esempio.it", annullamento: TestContext.Current.CancellationToken);
        var dati = new DatiIsolamento(web, adminA, adminB);
        dati.PaginaA = await dati.CreaPaginaAsync(web.A, "contatti", "Contatti di A", "Riservato ad A");
        dati.PaginaB = await dati.CreaPaginaAsync(web.B, "contatti", "Contatti di B", "Riservato a B");
        return dati;
    }

    public async Task<Guid> CreaPaginaAsync(DatiProva.TenantCreato tenant, string slug, string titolo, string contenuto)
    {
        var esito = await ComeAsync(tenant, (servizio, attore) => servizio.CreaAsync(attore, new DatiNuovaPagina(slug, titolo, contenuto, "it", false), TestContext.Current.CancellationToken));
        Assert.True(esito.Riuscito, esito.Codice.ToString());
        var id = esito.Id!.Value;
        var dettaglio = (await ComeAsync(tenant, (servizio, attore) => servizio.LeggiAsync(attore, id, TestContext.Current.CancellationToken)))!;
        var pubblica = await ComeAsync(tenant, (servizio, attore) => servizio.PubblicaAsync(attore, id, dettaglio.TokenConcorrenza, TestContext.Current.CancellationToken));
        Assert.True(pubblica.Riuscito, pubblica.Codice.ToString());
        return id;
    }

    public async Task<T> ComeAsync<T>(DatiProva.TenantCreato tenant, Func<IServizioPagine, Guid, Task<T>> operazione)
    {
        await using var ambito = Web.Fabbrica.Services.CreateAsyncScope();
        ambito.ServiceProvider.GetRequiredService<ContestoTenant>()
            .Imposta(new DatiTenantRisolto(tenant.IdTenant, tenant.IdSito, tenant.IdDominio, tenant.Host, false));
        var attore = tenant.IdTenant == A.IdTenant ? AdminA : AdminB;
        return await operazione(ambito.ServiceProvider.GetRequiredService<IServizioPagine>(), attore);
    }

    public ValueTask DisposeAsync() => Web.DisposeAsync();
}
