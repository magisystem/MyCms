using DvCms.Application.Negozio;
using DvCms.IntegrationTests.Supporto;
using DvCms.Models.Negozio;
using Microsoft.EntityFrameworkCore;
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
    public async Task BastaUnProviderProntoConSoloNexiPayPalNonBloccaEInProduzioneServeLInterruttore()
    {
        await using var c = await ContestoNegozio.CreaAsync(produzioneAbilitata: false);
        await c.CreaProdottoAsync(c.A, "Libro", 4m, ("LIB-1", 10m, null));
        await c.CreaRitiroAsync(c.A);

        var nessuno = (await VerificaAsync(c, c.Platform, c.A))!;
        Assert.Equal(StatoControllo.Bloccante, Stato(nessuno, "CredenzialiPayPal"));
        Assert.Equal(StatoControllo.Attenzione, Stato(nessuno, "CredenzialiNexi"));

        await c.ConfiguraNexiAsync(c.A);
        var soloNexi = (await VerificaAsync(c, c.Platform, c.A))!;
        Assert.Equal(StatoControllo.Attenzione, Stato(soloNexi, "CredenzialiPayPal"));
        Assert.Equal(StatoControllo.Attenzione, Stato(soloNexi, "WebhookPayPal"));
        Assert.Equal(StatoControllo.Ok, Stato(soloNexi, "CredenzialiNexi"));
        Assert.DoesNotContain(soloNexi.Controlli, x => x.Stato == StatoControllo.Bloccante && x.Codice is "CredenzialiPayPal" or "WebhookPayPal" or "CredenzialiNexi");
        Assert.Contains("/webhooks/nexi", soloNexi.Controlli.Single(x => x.Codice == "DominioPubblico").Dettaglio, StringComparison.Ordinal);

        await c.ConfiguraNexiAsync(c.A, AmbientePagamento.Live);
        var produzione = (await VerificaAsync(c, c.Platform, c.A))!;
        Assert.Equal(StatoControllo.Bloccante, Stato(produzione, "InterruttoreProduzione"));
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
    public async Task IPagamentiManualiContanoComeMetodoProntoESenzaProviderOnlineNonCeAmbienteDiProva()
    {
        await using var c = await ContestoNegozio.CreaAsync();
        await c.CreaProdottoAsync(c.A, "Libro", 4m, ("LIB-1", 10m, null));
        await c.CreaRitiroAsync(c.A);

        var nessuno = (await VerificaAsync(c, c.Platform, c.A))!;
        Assert.Equal(StatoControllo.Bloccante, Stato(nessuno, "CredenzialiPayPal"));
        Assert.DoesNotContain(nessuno.Controlli, x => x.Codice == "PagamentiManuali");

        await c.ConfiguraOfflineAsync(c.A, ContestoNegozio.ImpostazioniOffline with { ContrassegnoAttivo = false });
        var bonifico = (await VerificaAsync(c, c.Platform, c.A))!;
        Assert.Equal(StatoControllo.Attenzione, Stato(bonifico, "CredenzialiPayPal"));
        Assert.Equal(StatoControllo.Ok, Stato(bonifico, "PagamentiManuali"));
        Assert.Equal("bonifico", bonifico.Controlli.Single(x => x.Codice == "PagamentiManuali").Dettaglio);
        Assert.DoesNotContain(bonifico.Controlli, x => x.Codice is "AmbienteProva" or "InterruttoreProduzione" or "BonificoIncompleto");

        await c.ConfiguraOfflineAsync(c.A, ContestoNegozio.ImpostazioniOffline);
        Assert.Equal("bonifico, contrassegno", (await VerificaAsync(c, c.Platform, c.A))!.Controlli.Single(x => x.Codice == "PagamentiManuali").Dettaglio);

        // Con un provider online accanto, l'ambiente di prova torna a contare.
        await c.ConfiguraPagamentoAsync(c.A);
        Assert.Equal(StatoControllo.Attenzione, Stato((await VerificaAsync(c, c.Platform, c.A))!, "AmbienteProva"));
    }

    [Fact]
    public async Task UnBonificoAttivoSenzaCoordinateNonRendePronto()
    {
        await using var c = await ContestoNegozio.CreaAsync();
        await c.ConfiguraOfflineAsync(c.A, ImpostazioniPagamentoOffline.Spente with { ContrassegnoAttivo = true });
        await using (var ambito = c.Web.Fabbrica.Services.CreateAsyncScope())
        {
            // Aggira il servizio, che non lo permetterebbe: bonifico attivo ma senza IBAN.
            var db = ambito.ServiceProvider.GetRequiredService<DvCms.Infrastructure.Persistenza.ContestoDatabase>();
            await db.ImpostazioniPagamentoOfflineSiti.IgnoreQueryFilters().ExecuteUpdateAsync(x => x.SetProperty(i => i.BonificoAttivo, true), Ct);
        }

        var rapporto = (await VerificaAsync(c, c.Platform, c.A))!;

        Assert.Equal(StatoControllo.Bloccante, Stato(rapporto, "BonificoIncompleto"));
        Assert.Equal("contrassegno", rapporto.Controlli.Single(x => x.Codice == "PagamentiManuali").Dettaglio);
        Assert.False(rapporto.Pronto);
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
