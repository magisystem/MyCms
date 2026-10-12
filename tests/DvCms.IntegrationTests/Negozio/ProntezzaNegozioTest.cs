using DvCms.Application.Negozio;
using DvCms.Domain.Negozio;
using DvCms.IntegrationTests.Supporto;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DvCms.IntegrationTests.Negozio;

/// <summary>Verifica di prontezza del negozio: solo per il PlatformAdmin, sola lettura, segnala ciò che manca prima dei pagamenti reali.</summary>
public class ProntezzaNegozioTest
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<RapportoProntezza?> VerificaAsync(ContestoNegozio c, Guid attore, DatiProva.TenantCreato tenant)
    {
        await using var ambito = c.Web.Fabbrica.Services.CreateAsyncScope();
        return await ambito.ServiceProvider.GetRequiredService<IServizioProntezzaNegozio>().VerificaAsync(attore, tenant.IdTenant, tenant.IdSito, Ct);
    }

    private static StatoControllo Stato(RapportoProntezza rapporto, string codice) => rapporto.Controlli.Single(x => x.Codice == codice).Stato;

    [Fact]
    public async Task UnNegozioVuotoMostraCiCheMancaEUnoCompletoERisultaProntoSenzaMostrareSegreti()
    {
        await using var c = await ContestoNegozio.CreaAsync(produzioneAbilitata: true);

        var vuoto = (await VerificaAsync(c, c.Platform, c.A))!;

        Assert.False(vuoto.Pronto);
        foreach (var codice in new[] { "CredenzialiPayPal", "WebhookPayPal", "ProdottiPubblicati", "MetodiSpedizione" })
        {
            Assert.Equal(StatoControllo.Bloccante, Stato(vuoto, codice));
        }

        Assert.Equal(StatoControllo.Ok, Stato(vuoto, "NegozioAttivo"));
        Assert.Equal(StatoControllo.Attenzione, Stato(vuoto, "CanaleEmail"));
        Assert.Equal(StatoControllo.Ok, Stato(vuoto, "AmministratoriAttivi"));
        Assert.Equal(StatoControllo.Attenzione, Stato(vuoto, "PaginaPrivacy"));

        await c.CreaProdottoAsync(c.A, "Libro", 4m, ("LIB-1", 10m, null));
        await c.CreaRitiroAsync(c.A);
        await c.ConfiguraPagamentoAsync(c.A, AmbientePagamento.Live);
        var completo = (await VerificaAsync(c, c.Platform, c.A))!;

        foreach (var codice in new[] { "CredenzialiPayPal", "WebhookPayPal", "ProdottiPubblicati", "MetodiSpedizione", "InterruttoreProduzione" })
        {
            Assert.Equal(StatoControllo.Ok, Stato(completo, codice));
        }

        Assert.Equal(AmbientePagamento.Live, completo.Ambiente);
        Assert.DoesNotContain(completo.Controlli, x => x.Dettaglio?.Contains("segreto", StringComparison.OrdinalIgnoreCase) == true);
    }

    [Fact]
    public async Task InProduzioneSenzaInterruttoreEBloccanteMentreInProvaSoloUnAvviso()
    {
        await using var c = await ContestoNegozio.CreaAsync(produzioneAbilitata: false);
        await c.ConfiguraPagamentoAsync(c.A, AmbientePagamento.Live);
        await c.ConfiguraPagamentoAsync(c.B, AmbientePagamento.Sandbox);

        var live = (await VerificaAsync(c, c.Platform, c.A))!;
        var prova = (await VerificaAsync(c, c.Platform, c.B))!;

        Assert.Equal(StatoControllo.Bloccante, Stato(live, "InterruttoreProduzione"));
        Assert.DoesNotContain(live.Controlli, x => x.Codice == "AmbienteProva");
        Assert.Equal(StatoControllo.Attenzione, Stato(prova, "AmbienteProva"));
        Assert.DoesNotContain(prova.Controlli, x => x.Codice == "InterruttoreProduzione");
    }

    [Fact]
    public async Task SoloIlPlatformAdminVedeIlRapportoEUnSitoDiUnAltroTenantNonSiTrova()
    {
        await using var c = await ContestoNegozio.CreaAsync();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => VerificaAsync(c, c.AdminA, c.A));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => VerificaAsync(c, c.Cliente, c.A));
        await using var ambito = c.Web.Fabbrica.Services.CreateAsyncScope();
        var incrociato = await ambito.ServiceProvider.GetRequiredService<IServizioProntezzaNegozio>().VerificaAsync(c.Platform, c.A.IdTenant, c.B.IdSito, Ct);

        Assert.Null(incrociato);
    }

    [Fact]
    public async Task LaPaginaPagamentiDellaPiattaformaMostraLaVerificaDiEsercizio()
    {
        await using var c = await ContestoNegozio.CreaAsync();
        var root = c.Web.SessioneSu(c.A.Host);
        await root.AccediAsync("root@esempio.it", DatiProva.PasswordProva);

        var html = await (await root.GetAsync($"/admin/platform/tenant/{c.A.IdTenant}/pagamenti?idSito={c.A.IdSito}")).Content.ReadAsStringAsync(Ct);

        Assert.Contains("Verifica di esercizio", html, StringComparison.Ordinal);
        Assert.Contains("Non ancora pronto", html, StringComparison.Ordinal);
        Assert.Contains("Credenziali PayPal complete e leggibili", html, StringComparison.Ordinal);
    }
}
