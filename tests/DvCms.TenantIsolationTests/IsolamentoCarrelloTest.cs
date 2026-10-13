using DvCms.Contracts.Negozio;
using DvCms.Contracts.Piattaforma;
using DvCms.Core.Tenancy;
using DvCms.DTO.Negozio;
using DvCms.DTO.Piattaforma;
using DvCms.DTO.Tenancy;
using DvCms.IntegrationTests.Supporto;
using DvCms.Models.Accessi;
using DvCms.Models.Negozio;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DvCms.TenantIsolationTests;

/// <summary>Matrice di isolamento (B-004) sul carrello (F-002): righe, varianti e account non attraversano i tenant.</summary>
public class IsolamentoCarrelloTest
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task IlCarrelloDiAENonSiVedeNeSiToccaDaBEUnaVarianteDiBNonSiAgganciaAdA()
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
        var varianteA = await CreaVarianteAsync(d, d.A, d.AdminA, "Di A", "SKU-A");
        var varianteB = await CreaVarianteAsync(d, d.B, d.AdminB, "Di B", "SKU-B");

        Assert.True((await ComeAsync(d, d.A, s => s.AggiungiAsync(clienteA, varianteA, 2, Ct))).Riuscito);
        Assert.True((await ComeAsync(d, d.B, s => s.AggiungiAsync(clienteB, varianteB, 1, Ct))).Riuscito);

        // La variante dell'altro tenant non si aggancia; l'account dell'altro tenant non opera; i carrelli restano separati.
        Assert.Equal(CodiceEsito.NonTrovato, (await ComeAsync(d, d.A, s => s.AggiungiAsync(clienteA, varianteB, 1, Ct))).Codice);
        Assert.Equal(CodiceEsito.NonAutorizzato, (await ComeAsync(d, d.A, s => s.AggiungiAsync(clienteB, varianteA, 1, Ct))).Codice);
        Assert.Equal(CodiceEsito.NonAutorizzato, (await ComeAsync(d, d.B, s => s.SvuotaAsync(clienteA, Ct))).Codice);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => ComeAsync(d, d.B, s => s.LeggiAsync(clienteA, "it", Ct)));

        var vistaA = await ComeAsync(d, d.A, s => s.LeggiAsync(clienteA, "it", Ct));
        var vistaB = await ComeAsync(d, d.B, s => s.LeggiAsync(clienteB, "it", Ct));
        Assert.Equal(("SKU-A", 2), (vistaA.Righe.Single().Sku, vistaA.Righe.Single().Quantita));
        Assert.Equal(("SKU-B", 1), (vistaB.Righe.Single().Sku, vistaB.Righe.Single().Quantita));
    }

    private static async Task<Guid> CreaVarianteAsync(DatiIsolamento d, DatiProva.TenantCreato tenant, Guid admin, string nome, string sku)
    {
        await using var ambito = d.Web.Fabbrica.Services.CreateAsyncScope();
        ambito.ServiceProvider.GetRequiredService<ContestoTenant>().Imposta(new DatiTenantRisolto(tenant.IdTenant, tenant.IdSito, tenant.IdDominio, tenant.Host, false));
        var catalogo = ambito.ServiceProvider.GetRequiredService<IServizioCatalogo>();
        var esito = await catalogo.SalvaAsync(admin, new DatiProdotto(null, null, StatoProdotto.Pubblicato, 22m, [new("it", nome, null)], [new(null, sku, 10m, null, true, null)]), Ct);
        Assert.True(esito.Riuscito, esito.Codice.ToString());
        return (await catalogo.LeggiAsync(admin, esito.Id!.Value, Ct))!.Varianti.Single().Id;
    }

    private static async Task<T> ComeAsync<T>(DatiIsolamento d, DatiProva.TenantCreato tenant, Func<IServizioCarrello, Task<T>> operazione)
    {
        await using var ambito = d.Web.Fabbrica.Services.CreateAsyncScope();
        ambito.ServiceProvider.GetRequiredService<ContestoTenant>().Imposta(new DatiTenantRisolto(tenant.IdTenant, tenant.IdSito, tenant.IdDominio, tenant.Host, false));
        return await operazione(ambito.ServiceProvider.GetRequiredService<IServizioCarrello>());
    }
}
