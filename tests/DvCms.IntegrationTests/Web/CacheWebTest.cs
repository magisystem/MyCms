using System.Net;
using DvCms.Application.Cms;
using DvCms.Application.Piattaforma;
using DvCms.Application.Temi;
using DvCms.Application.Tenancy;
using DvCms.Domain.Accessi;
using DvCms.Infrastructure.Cache;
using DvCms.Infrastructure.Manutenzione;
using DvCms.Infrastructure.Persistenza;
using DvCms.IntegrationTests.Supporto;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DvCms.IntegrationTests.Web;

/// <summary>Cache dei contenuti pubblici e invalidazione transazionale (G-002).</summary>
public class CacheWebTest
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task LaPaginaPubblicaSiServeDallaCacheFinoALlaPubblicazioneCheLaInvalida()
    {
        await using var c = await Contesto.CreaAsync();
        var id = await c.CreaPubblicaAsync(c.Web.A, "chi-siamo", "Titolo uno");
        Assert.Contains("Titolo uno", await Contesto.PaginaAsync(c.Web.SessioneA(), "/chi-siamo"), StringComparison.Ordinal);

        // Una modifica che non passa dall'applicazione non si vede: la risposta viene dalla cache.
        await c.ModificaTitoloPubblicatoDirettamenteAsync(id, "Titolo scritto sul database");
        Assert.Contains("Titolo uno", await Contesto.PaginaAsync(c.Web.SessioneA(), "/chi-siamo"), StringComparison.Ordinal);

        // Una modifica fatta dall'applicazione (pubblicazione) invalida subito, nella stessa istanza.
        await c.SalvaEPubblicaAsync(c.Web.A, id, "Titolo due");
        var html = await Contesto.PaginaAsync(c.Web.SessioneA(), "/chi-siamo");
        Assert.Contains("Titolo due", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Titolo uno", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task IlRitiroEIlCambioDiSlugSiVedonoSubitoAnchePerRedirectEPagineInCache()
    {
        await using var c = await Contesto.CreaAsync();
        var id = await c.CreaPubblicaAsync(c.Web.A, "vecchio", "Pagina");
        using var inCache = await c.Web.SessioneA().GetAsync("/vecchio");
        Assert.Equal(HttpStatusCode.OK, inCache.StatusCode);

        await c.RinominaAsync(c.Web.A, id, "nuovo");

        using var redirect = await c.Web.SessioneA().GetAsync("/vecchio");
        using var nuovo = await c.Web.SessioneA().GetAsync("/nuovo");
        Assert.Equal(HttpStatusCode.MovedPermanently, redirect.StatusCode);
        Assert.Equal("/nuovo", SessioneProva.Destinazione(redirect));
        Assert.Equal(HttpStatusCode.OK, nuovo.StatusCode);

        // Il reindirizzamento ora è in cache; ritirare la pagina lo rende un 404 senza aspettare.
        await c.RitiraAsync(c.Web.A, id);
        using var dopoRitiro = await c.Web.SessioneA().GetAsync("/vecchio");
        using var nuovoDopoRitiro = await c.Web.SessioneA().GetAsync("/nuovo");
        Assert.Equal(HttpStatusCode.NotFound, dopoRitiro.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, nuovoDopoRitiro.StatusCode);
    }

    [Fact]
    public async Task LaCacheNonMescolaITenantEInvalidareUnoNonSvuotaLAltro()
    {
        await using var c = await Contesto.CreaAsync();
        var idA = await c.CreaPubblicaAsync(c.Web.A, "contatti", "Contatti di A");
        var idB = await c.CreaPubblicaAsync(c.Web.B, "contatti", "Contatti di B");
        Assert.Contains("Contatti di A", await Contesto.PaginaAsync(c.Web.SessioneA(), "/contatti"), StringComparison.Ordinal);
        Assert.Contains("Contatti di B", await Contesto.PaginaAsync(c.Web.SessioneB(), "/contatti"), StringComparison.Ordinal);

        await c.ModificaTitoloPubblicatoDirettamenteAsync(idB, "B scritto sul database");
        await c.SalvaEPubblicaAsync(c.Web.A, idA, "Contatti di A, nuovi");

        var daA = await Contesto.PaginaAsync(c.Web.SessioneA(), "/contatti");
        var daB = await Contesto.PaginaAsync(c.Web.SessioneB(), "/contatti");
        Assert.Contains("Contatti di A, nuovi", daA, StringComparison.Ordinal);
        Assert.DoesNotContain("Contatti di B", daA, StringComparison.Ordinal);
        Assert.Contains("Contatti di B", daB, StringComparison.Ordinal);
        Assert.DoesNotContain("B scritto sul database", daB, StringComparison.Ordinal);
        Assert.DoesNotContain("Contatti di A", daB, StringComparison.Ordinal);
    }

    [Fact]
    public async Task IlCambioDiTemaSiVedeSubito()
    {
        await using var c = await Contesto.CreaAsync();
        await c.CreaPubblicaAsync(c.Web.A, "pagina", "Pagina");
        Assert.Contains("tema-classico", await Contesto.PaginaAsync(c.Web.SessioneA(), "/pagina"), StringComparison.Ordinal);

        var esito = await c.ComeAsync<IServizioTemi, EsitoOperazione>(c.Web.A, (temi, attore) => temi.SalvaAsync(attore, "moderno", new Dictionary<string, string>(), null, Ct));

        Assert.True(esito.Riuscito, esito.Codice.ToString());
        Assert.Contains("tema-moderno", await Contesto.PaginaAsync(c.Web.SessioneA(), "/pagina"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task LaSitemapSiAggiornaConLePubblicazioni()
    {
        await using var c = await Contesto.CreaAsync();
        await c.CreaPubblicaAsync(c.Web.A, "prima", "Prima");
        Assert.DoesNotContain("/seconda", await Contesto.PaginaAsync(c.Web.SessioneA(), "/sitemap.xml"), StringComparison.Ordinal);

        await c.CreaPubblicaAsync(c.Web.A, "seconda", "Seconda");

        var sitemap = await Contesto.PaginaAsync(c.Web.SessioneA(), "/sitemap.xml");
        Assert.Contains("/seconda", sitemap, StringComparison.Ordinal);
        Assert.Contains("/prima", sitemap, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OgniModificaDelleLingueEDelSitoScriveUnaInvalidazione()
    {
        await using var c = await Contesto.CreaAsync();
        var admin = await c.Web.CreaPlatformAdminAsync("root@esempio.it");
        var tenant = c.Web.B;

        var disabilita = await c.ComePlatformAsync(piattaforma => piattaforma.DisabilitaLinguaAsync(admin, tenant.IdTenant, tenant.IdSito, "en", Ct));
        var dopoDisabilita = await c.ContaInvalidazioniAsync();
        var abilita = await c.ComePlatformAsync(piattaforma => piattaforma.AbilitaLinguaAsync(admin, tenant.IdTenant, tenant.IdSito, "en", Ct));
        var dopoAbilita = await c.ContaInvalidazioniAsync();
        var predefinita = await c.ComePlatformAsync(piattaforma => piattaforma.ImpostaLinguaPredefinitaAsync(admin, tenant.IdTenant, tenant.IdSito, "en", Ct));
        var dopoPredefinita = await c.ContaInvalidazioniAsync();
        var sito = await c.ComePlatformAsync(piattaforma => piattaforma.ModificaSitoAsync(admin, tenant.IdTenant, tenant.IdSito, "Nuovo nome", "Europe/Rome", true, Ct));
        var dopoSito = await c.ContaInvalidazioniAsync();

        Assert.True(disabilita.Riuscito && abilita.Riuscito && predefinita.Riuscito && sito.Riuscito);
        Assert.Equal([1, 2, 3, 4], new[] { dopoDisabilita, dopoAbilita, dopoPredefinita, dopoSito });
    }

    [Fact]
    public async Task IlNomeDelSitoCambiatoSiVedeNellIntestazioneSubito()
    {
        await using var c = await Contesto.CreaAsync();
        await c.CreaPubblicaAsync(c.Web.B, "pagina", "Pagina");
        var admin = await c.Web.CreaPlatformAdminAsync("root@esempio.it");
        Assert.Contains("Tenant B", await Contesto.PaginaAsync(c.Web.SessioneB(), "/pagina"), StringComparison.Ordinal);

        var esito = await c.ComePlatformAsync(piattaforma => piattaforma.ModificaSitoAsync(admin, c.Web.B.IdTenant, c.Web.B.IdSito, "Sito rinominato", "Europe/Rome", true, Ct));

        Assert.True(esito.Riuscito, esito.Codice.ToString());
        Assert.Contains("Sito rinominato", await Contesto.PaginaAsync(c.Web.SessioneB(), "/pagina"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task LAnteprimaNonPassaMaiDallaCache()
    {
        await using var c = await Contesto.CreaAsync();
        var id = await c.CreaPubblicaAsync(c.Web.A, "pagina", "Pubblicato");
        Assert.Contains("Pubblicato", await Contesto.PaginaAsync(c.Web.SessioneA(), "/pagina"), StringComparison.Ordinal);
        await c.SalvaBozzaAsync(c.Web.A, id, "Bozza nuova");
        var sessione = c.Web.SessioneA();
        await sessione.AccediAsync("anna@esempio.it", DatiProva.PasswordProva);

        var anteprima = await sessione.GetAsync($"/admin/pagine/{id}/anteprima");
        var pubblica = await Contesto.PaginaAsync(c.Web.SessioneA(), "/pagina");

        Assert.Contains("Bozza nuova", await anteprima.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        Assert.Contains("Pubblicato", pubblica, StringComparison.Ordinal);
        Assert.DoesNotContain("Bozza nuova", pubblica, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnaModificaDiUnAltraIstanzaArrivaConLaLetturaDelRegistro()
    {
        await using var c = await Contesto.CreaAsync();
        var id = await c.CreaPubblicaAsync(c.Web.A, "pagina", "Titolo uno");

        // Prima si mette a pari il lettore del registro (le invalidazioni della creazione), poi si riempie la cache.
        var lettore = c.Web.Fabbrica.Services.GetRequiredService<ServizioInvalidazioneCache>();
        await lettore.LeggiAsync(Ct);
        Assert.Contains("Titolo uno", await Contesto.PaginaAsync(c.Web.SessioneA(), "/pagina"), StringComparison.Ordinal);

        // L'altra istanza modifica il contenuto e scrive la riga di invalidazione: da qui non passa dall'interceptor di questa istanza.
        await c.ModificaTitoloPubblicatoDirettamenteAsync(id, "Titolo altra istanza");
        Assert.Contains("Titolo uno", await Contesto.PaginaAsync(c.Web.SessioneA(), "/pagina"), StringComparison.Ordinal);
        await c.ScriviInvalidazioneDirettamenteAsync(c.Web.A);

        Assert.Equal(1, await lettore.LeggiAsync(Ct));
        Assert.Contains("Titolo altra istanza", await Contesto.PaginaAsync(c.Web.SessioneA(), "/pagina"), StringComparison.Ordinal);
        Assert.Equal(0, await lettore.LeggiAsync(Ct));
    }

    [Fact]
    public async Task LaLetturaDelRegistroInvalidaSoloIlSitoIndicato()
    {
        await using var c = await Contesto.CreaAsync();
        var idA = await c.CreaPubblicaAsync(c.Web.A, "pagina", "A uno");
        var idB = await c.CreaPubblicaAsync(c.Web.B, "pagina", "B uno");
        var lettore = c.Web.Fabbrica.Services.GetRequiredService<ServizioInvalidazioneCache>();
        await lettore.LeggiAsync(Ct);
        await Contesto.PaginaAsync(c.Web.SessioneA(), "/pagina");
        await Contesto.PaginaAsync(c.Web.SessioneB(), "/pagina");
        await c.ModificaTitoloPubblicatoDirettamenteAsync(idA, "A due");
        await c.ModificaTitoloPubblicatoDirettamenteAsync(idB, "B due");

        await c.ScriviInvalidazioneDirettamenteAsync(c.Web.A);
        await lettore.LeggiAsync(Ct);

        Assert.Contains("A due", await Contesto.PaginaAsync(c.Web.SessioneA(), "/pagina"), StringComparison.Ordinal);
        Assert.Contains("B uno", await Contesto.PaginaAsync(c.Web.SessioneB(), "/pagina"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task LInvalidazioneEScrittaConLaModificaENonConLaLettura()
    {
        await using var c = await Contesto.CreaAsync();
        var id = await c.CreaPubblicaAsync(c.Web.A, "pagina", "Titolo");
        var prima = await c.ContaInvalidazioniAsync();

        await c.SalvaBozzaAsync(c.Web.A, id, "Altra bozza");
        var dopoSalvataggio = await c.ContaInvalidazioniAsync();
        await c.ComeAsync<IServizioPagine, DettaglioPagina?>(c.Web.A, (pagine, attore) => pagine.LeggiAsync(attore, id, Ct));
        var dopoLettura = await c.ContaInvalidazioniAsync();

        Assert.True(dopoSalvataggio > prima);
        Assert.Equal(dopoSalvataggio, dopoLettura);
    }

    [Fact]
    public async Task ConLaCacheDisabilitataOgniModificaSiVedeSubito()
    {
        await using var c = await Contesto.CreaAsync(new Dictionary<string, string?> { ["Cache:Abilitata"] = "false" });
        var id = await c.CreaPubblicaAsync(c.Web.A, "pagina", "Titolo uno");
        await Contesto.PaginaAsync(c.Web.SessioneA(), "/pagina");

        await c.ModificaTitoloPubblicatoDirettamenteAsync(id, "Titolo diretto");

        Assert.Contains("Titolo diretto", await Contesto.PaginaAsync(c.Web.SessioneA(), "/pagina"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task LaManutenzioneEliminaLeInvalidazioniVecchieDueOre()
    {
        await using var c = await Contesto.CreaAsync();
        await c.ScriviInvalidazioneDirettamenteAsync(c.Web.A, DateTime.UtcNow.AddHours(-3));
        await c.ScriviInvalidazioneDirettamenteAsync(c.Web.A, DateTime.UtcNow.AddMinutes(-30));

        var eliminati = await c.Web.Fabbrica.Services.GetRequiredService<ManutenzioneDati>().EseguiAsync(Ct);

        Assert.Equal(1, eliminati);
        Assert.Equal(1, await c.ContaInvalidazioniAsync());
    }

    private sealed class Contesto : IAsyncDisposable
    {
        private Contesto(ScenarioWeb web, Guid adminA, Guid adminB)
        {
            Web = web;
            AdminA = adminA;
            AdminB = adminB;
        }

        public ScenarioWeb Web { get; }

        public Guid AdminA { get; }

        public Guid AdminB { get; }

        public static async Task<Contesto> CreaAsync(IReadOnlyDictionary<string, string?>? impostazioni = null)
        {
            var web = await ScenarioWeb.CreaAsync(impostazioni: impostazioni);
            var adminA = await web.CreaTenantAdminAsync("anna@esempio.it");
            var adminB = await DatiProva.CreaUtenzaAsync(web.Fabbrica.Services, RuoloUtenza.TenantAdmin, web.B.IdTenant, "bruno@esempio.it", annullamento: Ct);
            return new Contesto(web, adminA, adminB);
        }

        public static async Task<string> PaginaAsync(SessioneProva sessione, string percorso)
        {
            using var risposta = await sessione.GetAsync(percorso);
            return await risposta.Content.ReadAsStringAsync(Ct);
        }

        public async Task<Guid> CreaPubblicaAsync(DatiProva.TenantCreato tenant, string slug, string titolo, string lingua = "it")
        {
            var esito = await ComeAsync<IServizioPagine, EsitoOperazione>(tenant, (pagine, attore) => pagine.CreaAsync(attore, new DatiNuovaPagina(slug, titolo, "Contenuto", lingua, false), Ct));
            Assert.True(esito.Riuscito, esito.Codice.ToString());
            var id = esito.Id!.Value;
            await PubblicaAsync(tenant, id);
            return id;
        }

        public async Task PubblicaAsync(DatiProva.TenantCreato tenant, Guid id)
        {
            var dettaglio = (await ComeAsync<IServizioPagine, DettaglioPagina?>(tenant, (pagine, attore) => pagine.LeggiAsync(attore, id, Ct)))!;
            var esito = await ComeAsync<IServizioPagine, EsitoOperazione>(tenant, (pagine, attore) => pagine.PubblicaAsync(attore, id, dettaglio.TokenConcorrenza, Ct));
            Assert.True(esito.Riuscito, esito.Codice.ToString());
        }

        public async Task SalvaBozzaAsync(DatiProva.TenantCreato tenant, Guid id, string titolo)
        {
            var dettaglio = (await ComeAsync<IServizioPagine, DettaglioPagina?>(tenant, (pagine, attore) => pagine.LeggiAsync(attore, id, Ct)))!;
            var esito = await ComeAsync<IServizioPagine, EsitoOperazione>(
                tenant,
                (pagine, attore) => pagine.SalvaAsync(attore, id, new DatiModificaPagina(null, titolo, dettaglio.Contenuto, null, dettaglio.PaginaIniziale, dettaglio.TokenConcorrenza), Ct));
            Assert.True(esito.Riuscito, esito.Codice.ToString());
        }

        public async Task SalvaEPubblicaAsync(DatiProva.TenantCreato tenant, Guid id, string titolo)
        {
            await SalvaBozzaAsync(tenant, id, titolo);
            await PubblicaAsync(tenant, id);
        }

        public async Task RinominaAsync(DatiProva.TenantCreato tenant, Guid id, string slug)
        {
            var dettaglio = (await ComeAsync<IServizioPagine, DettaglioPagina?>(tenant, (pagine, attore) => pagine.LeggiAsync(attore, id, Ct)))!;
            var esito = await ComeAsync<IServizioPagine, EsitoOperazione>(
                tenant,
                (pagine, attore) => pagine.SalvaAsync(attore, id, new DatiModificaPagina(slug, dettaglio.Titolo, dettaglio.Contenuto, null, dettaglio.PaginaIniziale, dettaglio.TokenConcorrenza), Ct));
            Assert.True(esito.Riuscito, esito.Codice.ToString());
        }

        public async Task RitiraAsync(DatiProva.TenantCreato tenant, Guid id)
        {
            var dettaglio = (await ComeAsync<IServizioPagine, DettaglioPagina?>(tenant, (pagine, attore) => pagine.LeggiAsync(attore, id, Ct)))!;
            var esito = await ComeAsync<IServizioPagine, EsitoOperazione>(tenant, (pagine, attore) => pagine.RitiraAsync(attore, id, dettaglio.TokenConcorrenza, Ct));
            Assert.True(esito.Riuscito, esito.Codice.ToString());
        }

        /// <summary>Cambia il titolo della revisione pubblicata senza passare dall'applicazione (nessuna invalidazione).</summary>
        public async Task ModificaTitoloPubblicatoDirettamenteAsync(Guid idPagina, string titolo)
        {
            await using var ambito = Web.Fabbrica.Services.CreateAsyncScope();
            var contesto = ambito.ServiceProvider.GetRequiredService<ContestoDatabase>();
            var idRevisione = await contesto.Pagine.IgnoreQueryFilters().Where(p => p.Id == idPagina).Select(p => p.IdRevisionePubblicata).SingleAsync(Ct);
            await contesto.RevisioniPagina.IgnoreQueryFilters().Where(r => r.Id == idRevisione).ExecuteUpdateAsync(s => s.SetProperty(r => r.Titolo, titolo), Ct);
        }

        /// <summary>Scrive la riga di invalidazione come farebbe un'altra istanza: SQL diretto, senza passare dall'interceptor di questa.</summary>
        public async Task ScriviInvalidazioneDirettamenteAsync(DatiProva.TenantCreato tenant, DateTime? creato = null)
        {
            await using var ambito = Web.Fabbrica.Services.CreateAsyncScope();
            var contesto = ambito.ServiceProvider.GetRequiredService<ContestoDatabase>();
            var quando = creato ?? DateTime.UtcNow;
            await contesto.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO InvalidazioniCache (IdTenant, IdSito, CreatoIlUtc) VALUES ({tenant.IdTenant}, {tenant.IdSito}, {quando})", Ct);
        }

        public async Task<int> ContaInvalidazioniAsync()
        {
            await using var ambito = Web.Fabbrica.Services.CreateAsyncScope();
            return await ambito.ServiceProvider.GetRequiredService<ContestoDatabase>().InvalidazioniCache.IgnoreQueryFilters().CountAsync(Ct);
        }

        public async Task<TRisultato> ComeAsync<TServizio, TRisultato>(DatiProva.TenantCreato tenant, Func<TServizio, Guid, Task<TRisultato>> operazione)
            where TServizio : notnull
        {
            await using var ambito = Web.Fabbrica.Services.CreateAsyncScope();
            ambito.ServiceProvider.GetRequiredService<ContestoTenant>().Imposta(new DatiTenantRisolto(tenant.IdTenant, tenant.IdSito, tenant.IdDominio, tenant.Host, false));
            var attore = tenant.IdTenant == Web.A.IdTenant ? AdminA : AdminB;
            return await operazione(ambito.ServiceProvider.GetRequiredService<TServizio>(), attore);
        }

        public async Task<EsitoOperazione> ComePlatformAsync(Func<IServizioPiattaforma, Task<EsitoOperazione>> operazione)
        {
            await using var ambito = Web.Fabbrica.Services.CreateAsyncScope();
            return await operazione(ambito.ServiceProvider.GetRequiredService<IServizioPiattaforma>());
        }

        public ValueTask DisposeAsync() => Web.DisposeAsync();
    }
}
