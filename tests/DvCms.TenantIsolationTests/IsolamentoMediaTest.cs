using System.Net;
using DvCms.Contracts.Media;
using DvCms.Core.Tenancy;
using DvCms.DTO.Media;
using DvCms.DTO.Piattaforma;
using DvCms.DTO.Tenancy;
using DvCms.IntegrationTests.Supporto;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DvCms.TenantIsolationTests;

/// <summary>Matrice di isolamento (B-004) sui media (D-003): file, metadati e archivio non attraversano i tenant.</summary>
public class IsolamentoMediaTest
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task UnaRisorsaDiBNonSiLeggeNeSiModificaNeSiScaricaDaA()
    {
        await using var d = await DatiIsolamento.CreaAsync();
        var idB = await CaricaAsync(d, d.B, d.AdminB);

        // Dal dominio di A (pubblico) e dall'amministrazione di A.
        using var pubblicaDaA = await d.Web.SessioneA().GetAsync($"/media/{idB}");
        var anna = d.Web.SessioneA();
        await anna.AccediAsync("anna@esempio.it", DatiProva.PasswordProva);
        using var adminDaA = await anna.GetAsync($"/admin/media/{idB}?handler=File");
        using var dettaglioDaA = await anna.GetAsync($"/admin/media/{idB}");
        using var elencoA = await anna.GetAsync("/admin/media");

        Assert.Equal(HttpStatusCode.NotFound, pubblicaDaA.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, adminDaA.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, dettaglioDaA.StatusCode);
        Assert.DoesNotContain(idB.ToString(), await elencoA.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);

        // Dal servizio, con l'attore di A sul contesto di B (mai possibile via web) o sul proprio ma con l'id di B.
        var comeAsulProprio = await ComeAsync(d, d.A, d.AdminA, (s, a) => s.EliminaAsync(a, idB, Ct));
        var comeAsuB = await ComeAsync(d, d.B, d.AdminA, (s, a) => s.EliminaAsync(a, idB, Ct));
        Assert.Equal(CodiceEsito.NonTrovato, comeAsulProprio.Codice);
        Assert.Equal(CodiceEsito.NonAutorizzato, comeAsuB.Codice);

        using var ancoraDiB = await d.Web.SessioneB().GetAsync($"/media/{idB}");
        Assert.Equal(HttpStatusCode.OK, ancoraDiB.StatusCode);
    }

    [Fact]
    public async Task LoStessoNomeSuTenantDiversiDaOggettiDistinti()
    {
        await using var d = await DatiIsolamento.CreaAsync();
        var idA = await CaricaAsync(d, d.A, d.AdminA);
        var idB = await CaricaAsync(d, d.B, d.AdminB, ImmaginiProva.Png(7, 7));

        using var a = await d.Web.SessioneA().GetAsync($"/media/{idA}");
        using var b = await d.Web.SessioneB().GetAsync($"/media/{idB}");

        Assert.NotEqual(await a.Content.ReadAsByteArrayAsync(Ct), await b.Content.ReadAsByteArrayAsync(Ct));
        Assert.NotEqual(idA, idB);
    }

    private static Task<Guid> CaricaAsync(DatiIsolamento d, DatiProva.TenantCreato tenant, Guid attore, byte[]? contenuto = null) =>
        ComeAsync(d, tenant, attore, async (s, a) =>
        {
            var esito = await s.CaricaAsync(a, new FileDaCaricare(new MemoryStream(contenuto ?? ImmaginiProva.Png(5, 5)), "logo.png", "image/png"), Ct);
            Assert.True(esito.Riuscito, esito.Codice.ToString());
            return esito.Id!.Value;
        });

    private static async Task<T> ComeAsync<T>(DatiIsolamento d, DatiProva.TenantCreato tenant, Guid attore, Func<IServizioMedia, Guid, Task<T>> operazione)
    {
        await using var ambito = d.Web.Fabbrica.Services.CreateAsyncScope();
        ambito.ServiceProvider.GetRequiredService<ContestoTenant>().Imposta(new DatiTenantRisolto(tenant.IdTenant, tenant.IdSito, tenant.IdDominio, tenant.Host, false));
        return await operazione(ambito.ServiceProvider.GetRequiredService<IServizioMedia>(), attore);
    }
}
