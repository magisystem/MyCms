using DvCms.Contracts.Negozio;
using DvCms.Contracts.Piattaforma;
using DvCms.Core.Tenancy;
using DvCms.DTO.Negozio;
using DvCms.DTO.Piattaforma;
using DvCms.DTO.Tenancy;
using DvCms.IntegrationTests.Supporto;
using DvCms.Models.Negozio;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DvCms.TenantIsolationTests;

/// <summary>Matrice di isolamento (B-004) sul catalogo del negozio (F-001): prodotti, varianti e codici non attraversano i tenant.</summary>
public class IsolamentoCatalogoTest
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static DatiProdotto Dati(string nome, string sku, Guid? id = null, string? token = null, Guid? idVariante = null) =>
        new(id, token, StatoProdotto.Pubblicato, 22m, [new("it", nome, null)], [new(idVariante, sku, 10m, null, true, null)]);

    [Fact]
    public async Task UnProdottoDiBNonSiLeggeNeSiModificaNeSiAgganciaDaA()
    {
        await using var d = await DatiIsolamento.CreaAsync();
        var root = await d.Web.CreaPlatformAdminAsync("root@esempio.it");
        await AttivaAsync(d, root, d.A);
        await AttivaAsync(d, root, d.B);
        var idB = (await ComeAsync(d, d.B, d.AdminB, s => s.SalvaAsync(d.AdminB, Dati("Segreto di B", "SEG-B"), Ct))).Id!.Value;
        var dettaglioB = (await ComeAsync(d, d.B, d.AdminB, s => s.LeggiAsync(d.AdminB, idB, Ct)))!;

        // Dal contesto di A: non si vede, non si legge, non si modifica (anche col token giusto), non si elenca.
        var leggi = await ComeAsync(d, d.A, d.AdminA, s => s.LeggiAsync(d.AdminA, idB, Ct));
        var modifica = await ComeAsync(d, d.A, d.AdminA, s => s.SalvaAsync(d.AdminA, Dati("Rubato", "SEG-B", idB, dettaglioB.TokenConcorrenza), Ct));
        var elenco = await ComeAsync(d, d.A, d.AdminA, s => s.ElencaAsync(d.AdminA, Ct));
        // Una variante di B agganciata a un prodotto nuovo di A.
        var aggancio = await ComeAsync(d, d.A, d.AdminA, s => s.SalvaAsync(d.AdminA, Dati("Mio", "MIO-A", idVariante: dettaglioB.Varianti[0].Id), Ct));
        // L'attore di A sul contesto di B (mai possibile via web).
        var comeAsuB = await ComeAsync(d, d.B, d.AdminA, s => s.SalvaAsync(d.AdminA, Dati("Intruso", "INTRUSO"), Ct));

        Assert.Null(leggi);
        Assert.Equal(CodiceEsito.NonTrovato, modifica.Codice);
        Assert.Empty(elenco);
        Assert.Equal(CodiceEsito.DatiNonValidi, aggancio.Codice);
        Assert.Equal(CodiceEsito.NonAutorizzato, comeAsuB.Codice);
        var ancoraDiB = (await ComeAsync(d, d.B, d.AdminB, s => s.LeggiAsync(d.AdminB, idB, Ct)))!;
        Assert.Equal("Segreto di B", ancoraDiB.Traduzioni.Single().Nome);
        Assert.Equal(["SEG-B"], ancoraDiB.Varianti.Select(v => v.Sku));
    }

    [Fact]
    public async Task LoStessoCodiceArticoloVaBeneSuTenantDiversi()
    {
        await using var d = await DatiIsolamento.CreaAsync();
        var root = await d.Web.CreaPlatformAdminAsync("root@esempio.it");
        await AttivaAsync(d, root, d.A);
        await AttivaAsync(d, root, d.B);

        var a = await ComeAsync(d, d.A, d.AdminA, s => s.SalvaAsync(d.AdminA, Dati("Di A", "STESSO"), Ct));
        var b = await ComeAsync(d, d.B, d.AdminB, s => s.SalvaAsync(d.AdminB, Dati("Di B", "STESSO"), Ct));

        Assert.True(a.Riuscito && b.Riuscito);
        Assert.NotEqual(a.Id, b.Id);
        Assert.Single(await ComeAsync(d, d.A, d.AdminA, s => s.ElencaAsync(d.AdminA, Ct)));
        Assert.Single(await ComeAsync(d, d.B, d.AdminB, s => s.ElencaAsync(d.AdminB, Ct)));
    }

    private static async Task AttivaAsync(DatiIsolamento d, Guid root, DatiProva.TenantCreato tenant)
    {
        await using var ambito = d.Web.Fabbrica.Services.CreateAsyncScope();
        var esito = await ambito.ServiceProvider.GetRequiredService<IServizioPiattaforma>().ImpostaCommercioAsync(root, tenant.IdTenant, tenant.IdSito, true, Ct);
        Assert.True(esito.Riuscito, esito.Codice.ToString());
    }

    private static async Task<T> ComeAsync<T>(DatiIsolamento d, DatiProva.TenantCreato tenant, Guid attore, Func<IServizioCatalogo, Task<T>> operazione)
    {
        await using var ambito = d.Web.Fabbrica.Services.CreateAsyncScope();
        ambito.ServiceProvider.GetRequiredService<ContestoTenant>().Imposta(new DatiTenantRisolto(tenant.IdTenant, tenant.IdSito, tenant.IdDominio, tenant.Host, false));
        return await operazione(ambito.ServiceProvider.GetRequiredService<IServizioCatalogo>());
    }
}
