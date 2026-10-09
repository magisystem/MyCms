using System.Net;
using DvCms.Application.Cms;
using DvCms.Application.Tenancy;
using DvCms.Domain.Accessi;
using DvCms.IntegrationTests.Supporto;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DvCms.IntegrationTests.Web;

/// <summary>Rendering pubblico delle pagine per host e slug (C-003): una bozza non è mai pubblica.</summary>
public class SitoPubblicoWebTest
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task UnaBozzaNonEMaiPubblica()
    {
        await using var s = await Contesto.CreaAsync();
        await s.CreaPaginaAsync(s.A, "segreta", "Segreta", "Non ancora");

        using var risposta = await s.SessioneA().GetAsync("/segreta");

        Assert.Equal(HttpStatusCode.NotFound, risposta.StatusCode);
        Assert.DoesNotContain("Non ancora", await risposta.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnaPaginaPubblicataSiVedePerHostESlug()
    {
        await using var s = await Contesto.CreaAsync();
        await s.CreaPaginaAsync(s.A, "chi-siamo", "Chi siamo", "Primo paragrafo.\n\nSecondo\ncon due righe.", pubblica: true);

        using var risposta = await s.SessioneA().GetAsync("/chi-siamo");

        Assert.Equal(HttpStatusCode.OK, risposta.StatusCode);
        var html = await risposta.Content.ReadAsStringAsync(Ct);
        Assert.Contains("<h1>Chi siamo</h1>", html, StringComparison.Ordinal);
        Assert.Contains("Primo paragrafo.", html, StringComparison.Ordinal);
        Assert.Contains("Secondo<br />con due righe.", html.Replace("\n", string.Empty, StringComparison.Ordinal).Replace("  ", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.Contains("<html lang=\"it\">", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoStessoSlugSuTenantDiversiMostraContenutiDiversi()
    {
        await using var s = await Contesto.CreaAsync();
        await s.CreaPaginaAsync(s.A, "contatti", "Contatti di A", "Testo A", pubblica: true);
        await s.CreaPaginaAsync(s.B, "contatti", "Contatti di B", "Testo B", pubblica: true);
        await s.CreaPaginaAsync(s.A, "solo-a", "Solo A", "x", pubblica: true);

        var daA = await (await s.SessioneA().GetAsync("/contatti")).Content.ReadAsStringAsync(Ct);
        var daB = await (await s.SessioneB().GetAsync("/contatti")).Content.ReadAsStringAsync(Ct);
        using var altroTenant = await s.SessioneB().GetAsync("/solo-a");

        Assert.Contains("Contatti di A", daA, StringComparison.Ordinal);
        Assert.DoesNotContain("Contatti di B", daA, StringComparison.Ordinal);
        Assert.Contains("Contatti di B", daB, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NotFound, altroTenant.StatusCode);
    }

    [Fact]
    public async Task LaRadiceMostraLaPaginaInizialeEBastaChePubblicata()
    {
        await using var s = await Contesto.CreaAsync();
        using var senzaIniziale = await s.SessioneA().GetAsync("/");
        Assert.Equal(HttpStatusCode.NotFound, senzaIniziale.StatusCode);

        var id = await s.CreaPaginaAsync(s.A, "home", "Benvenuti", "Home page", iniziale: true);
        using var bozza = await s.SessioneA().GetAsync("/");
        Assert.Equal(HttpStatusCode.NotFound, bozza.StatusCode);

        await s.PubblicaAsync(s.A, id);
        using var pubblicata = await s.SessioneA().GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, pubblicata.StatusCode);
        Assert.Contains("Benvenuti", await pubblicata.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task LeModificheInBozzaNonSiVedonoFinoAllaPubblicazione()
    {
        await using var s = await Contesto.CreaAsync();
        var id = await s.CreaPaginaAsync(s.A, "pagina", "Versione pubblica", "Testo pubblico", pubblica: true);

        await s.ModificaAsync(s.A, id, "Versione in bozza", "Testo riservato");
        var prima = await (await s.SessioneA().GetAsync("/pagina")).Content.ReadAsStringAsync(Ct);
        await s.PubblicaAsync(s.A, id);
        var dopo = await (await s.SessioneA().GetAsync("/pagina")).Content.ReadAsStringAsync(Ct);

        Assert.Contains("Versione pubblica", prima, StringComparison.Ordinal);
        Assert.DoesNotContain("Testo riservato", prima, StringComparison.Ordinal);
        Assert.Contains("Versione in bozza", dopo, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnaPaginaRitirataTornaA404()
    {
        await using var s = await Contesto.CreaAsync();
        var id = await s.CreaPaginaAsync(s.A, "pagina", "Titolo", "Testo", pubblica: true);
        using var visibile = await s.SessioneA().GetAsync("/pagina");
        Assert.Equal(HttpStatusCode.OK, visibile.StatusCode);

        await s.RitiraAsync(s.A, id);

        using var ritirata = await s.SessioneA().GetAsync("/pagina");
        Assert.Equal(HttpStatusCode.NotFound, ritirata.StatusCode);
    }

    [Theory]
    [InlineData("/inesistente")]
    [InlineData("/file.html")]
    [InlineData("/%2e%2e")]
    [InlineData("/con%20spazio")]
    [InlineData("/due/segmenti")]
    [InlineData("/-iniziale")]
    public async Task SlugInesistentiONonValidiRispondono404ConPaginaNoindex(string percorso)
    {
        await using var s = await Contesto.CreaAsync();

        using var risposta = await s.SessioneA().GetAsync(percorso);

        Assert.Equal(HttpStatusCode.NotFound, risposta.StatusCode);
        var html = await risposta.Content.ReadAsStringAsync(Ct);
        if (html.Length > 0)
        {
            Assert.Contains("noindex", html, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task LoSlugSiRiconosceSenzaDistinguereMaiuscole()
    {
        await using var s = await Contesto.CreaAsync();
        await s.CreaPaginaAsync(s.A, "chi-siamo", "Chi siamo", "x", pubblica: true);

        using var risposta = await s.SessioneA().GetAsync("/Chi-Siamo");

        Assert.Equal(HttpStatusCode.OK, risposta.StatusCode);
    }

    [Fact]
    public async Task IlContenutoNonEMaiEmessoComeHtml()
    {
        await using var s = await Contesto.CreaAsync();
        await s.CreaPaginaAsync(s.A, "xss", "<script>alert(1)</script>", "<img src=x onerror=alert(2)>\n\n<b>grassetto</b>", pubblica: true);

        var html = await (await s.SessioneA().GetAsync("/xss")).Content.ReadAsStringAsync(Ct);

        Assert.DoesNotContain("<script>alert(1)", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<img src=x", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<b>grassetto", html, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LaLinguaPredefinitaNonHaPrefissoLeAltreSi()
    {
        await using var s = await Contesto.CreaAsync();
        var it = await s.CreaPaginaAsync(s.A, "news", "Notizie", "x", lingua: "it", pubblica: true);
        await s.CreaPaginaAsync(s.A, "news", "News", "x", lingua: "en", pubblica: true, traduzioneDi: it);

        var italiano = await (await s.SessioneA().GetAsync("/news")).Content.ReadAsStringAsync(Ct);
        var inglese = await (await s.SessioneA().GetAsync("/en/news")).Content.ReadAsStringAsync(Ct);

        Assert.Contains("<h1>Notizie</h1>", italiano, StringComparison.Ordinal);
        Assert.Contains("<html lang=\"it\">", italiano, StringComparison.Ordinal);
        Assert.Contains("<h1>News</h1>", inglese, StringComparison.Ordinal);
        Assert.Contains("<html lang=\"en\">", inglese, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/en/solo-it")]
    [InlineData("/solo-en")]
    [InlineData("/fr/qualcosa")]
    [InlineData("/it/solo-it")]
    [InlineData("/en/solo-it/altro")]
    [InlineData("/EN/")]
    public async Task UnaPaginaCheNonEsisteNellaLinguaRichiestaDa404SenzaRipiego(string percorso)
    {
        await using var s = await Contesto.CreaAsync();
        await s.CreaPaginaAsync(s.A, "solo-it", "Solo italiano", "x", lingua: "it", pubblica: true);
        await s.CreaPaginaAsync(s.A, "solo-en", "Only English", "x", lingua: "en", pubblica: true);

        using var risposta = await s.SessioneA().GetAsync(percorso);

        Assert.Equal(HttpStatusCode.NotFound, risposta.StatusCode);
    }

    [Fact]
    public async Task OgniLinguaHaLaPropriaPaginaIniziale()
    {
        await using var s = await Contesto.CreaAsync();
        using var vuoto = await s.SessioneA().GetAsync("/en");
        Assert.Equal(HttpStatusCode.NotFound, vuoto.StatusCode);
        var it = await s.CreaPaginaAsync(s.A, "home", "Benvenuti", "x", lingua: "it", iniziale: true, pubblica: true);
        await s.CreaPaginaAsync(s.A, "home", "Welcome", "x", lingua: "en", iniziale: true, pubblica: true, traduzioneDi: it);

        var radice = await (await s.SessioneA().GetAsync("/")).Content.ReadAsStringAsync(Ct);
        var radiceInglese = await (await s.SessioneA().GetAsync("/en")).Content.ReadAsStringAsync(Ct);

        Assert.Contains("Benvenuti", radice, StringComparison.Ordinal);
        Assert.Contains("Welcome", radiceInglese, StringComparison.Ordinal);
        Assert.Contains("<link rel=\"canonical\" href=\"http://a.esempio.it/\" />", radice, StringComparison.Ordinal);
        Assert.Contains("<link rel=\"canonical\" href=\"http://a.esempio.it/en\" />", radiceInglese, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CanonicalHreflangESelettoreMostranoSoloLeTraduzioniPubblicate()
    {
        await using var s = await Contesto.CreaAsync();
        var it = await s.CreaPaginaAsync(s.A, "news", "Notizie", "x", lingua: "it", pubblica: true);
        await s.CreaPaginaAsync(s.A, "news", "News", "x", lingua: "en", pubblica: true, traduzioneDi: it);
        var senzaTraduzione = await s.CreaPaginaAsync(s.A, "contatti", "Contatti", "x", lingua: "it", pubblica: true);
        var bozzaEn = await s.CreaPaginaAsync(s.A, "contatti", "Contacts", "x", lingua: "en", pubblica: false, traduzioneDi: senzaTraduzione);
        Assert.NotEqual(Guid.Empty, bozzaEn);

        var news = await (await s.SessioneA().GetAsync("/news")).Content.ReadAsStringAsync(Ct);
        var contatti = await (await s.SessioneA().GetAsync("/contatti")).Content.ReadAsStringAsync(Ct);

        Assert.Contains("<link rel=\"canonical\" href=\"http://a.esempio.it/news\" />", news, StringComparison.Ordinal);
        Assert.Contains("<link rel=\"alternate\" hreflang=\"it\" href=\"http://a.esempio.it/news\" />", news, StringComparison.Ordinal);
        Assert.Contains("<link rel=\"alternate\" hreflang=\"en\" href=\"http://a.esempio.it/en/news\" />", news, StringComparison.Ordinal);
        Assert.Contains("<link rel=\"alternate\" hreflang=\"x-default\" href=\"http://a.esempio.it/news\" />", news, StringComparison.Ordinal);
        Assert.Contains("<nav class=\"lingue\"", news, StringComparison.Ordinal);
        Assert.Contains("hreflang=\"en\" lang=\"en\"", news, StringComparison.Ordinal);

        // La traduzione è ancora bozza: né hreflang né selettore, ma il canonical c'è.
        Assert.Contains("<link rel=\"canonical\" href=\"http://a.esempio.it/contatti\" />", contatti, StringComparison.Ordinal);
        Assert.DoesNotContain("hreflang", contatti, StringComparison.Ordinal);
        Assert.DoesNotContain("class=\"lingue\"", contatti, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnaLinguaNonPiuAbilitataSparisceDalSitoEDaiCollegamenti()
    {
        await using var s = await Contesto.CreaAsync();
        var it = await s.CreaPaginaAsync(s.A, "news", "Notizie", "x", lingua: "it", pubblica: true);
        await s.CreaPaginaAsync(s.A, "news", "News", "x", lingua: "en", pubblica: true, traduzioneDi: it);
        await using (var ambito = s.Web.Fabbrica.Services.CreateAsyncScope())
        {
            await ambito.ServiceProvider.GetRequiredService<DvCms.Infrastructure.Persistenza.ContestoDatabase>().LingueSito.IgnoreQueryFilters()
                .Where(ls => ls.IdSito == s.A.IdSito && !ls.Predefinita).ExecuteDeleteAsync(Ct);
        }

        using var inglese = await s.SessioneA().GetAsync("/en/news");
        var italiano = await (await s.SessioneA().GetAsync("/news")).Content.ReadAsStringAsync(Ct);

        Assert.Equal(HttpStatusCode.NotFound, inglese.StatusCode);
        Assert.DoesNotContain("hreflang", italiano, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GliSlugRiservatiNonSonoPagineEUnHostSconosciutoRisponde404()
    {
        await using var s = await Contesto.CreaAsync();

        using var admin = await s.SessioneA().GetAsync("/admin");
        using var lib = await s.SessioneA().GetAsync("/lib");
        using var sconosciuto = await s.SessioneSconosciuta().GetAsync("/");

        Assert.True(SessioneProva.EReindirizzamento(admin));
        Assert.Equal(HttpStatusCode.NotFound, lib.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, sconosciuto.StatusCode);
    }

    [Fact]
    public async Task LaPaginaDiUnTenantSospesoNonSiVede()
    {
        await using var s = await Contesto.CreaAsync();
        await s.CreaPaginaAsync(s.A, "pagina", "Titolo", "x", pubblica: true);
        await s.Web.DisabilitaTenantAsync(s.A.IdTenant);

        using var risposta = await s.SessioneA().GetAsync("/pagina");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, risposta.StatusCode);
    }

    [Fact]
    public async Task UnVecchioSlugRispondeConRedirectPermanenteAlloSlugAttuale()
    {
        await using var s = await Contesto.CreaAsync();
        var id = await s.CreaPaginaAsync(s.A, "vecchio", "Titolo", "x", pubblica: true);
        await s.RinominaAsync(s.A, id, "nuovo");
        await s.PubblicaAsync(s.A, id);

        using var vecchio = await s.SessioneA().GetAsync("/vecchio");
        using var nuovo = await s.SessioneA().GetAsync("/nuovo");

        Assert.Equal(HttpStatusCode.MovedPermanently, vecchio.StatusCode);
        Assert.Equal("/nuovo", SessioneProva.Destinazione(vecchio));
        Assert.Equal(HttpStatusCode.OK, nuovo.StatusCode);
    }

    [Fact]
    public async Task IlRedirectSegueLaPaginaAncheDopoPiuCambiENonFormaCatene()
    {
        await using var s = await Contesto.CreaAsync();
        var id = await s.CreaPaginaAsync(s.A, "a", "Titolo", "x", pubblica: true);
        await s.RinominaAsync(s.A, id, "b");
        await s.RinominaAsync(s.A, id, "c");
        await s.PubblicaAsync(s.A, id);

        using var primo = await s.SessioneA().GetAsync("/a");
        using var secondo = await s.SessioneA().GetAsync("/b");

        Assert.Equal("/c", SessioneProva.Destinazione(primo));
        Assert.Equal("/c", SessioneProva.Destinazione(secondo));
    }

    [Fact]
    public async Task TornareAlloSlugOriginaleNonCreaCicli()
    {
        await using var s = await Contesto.CreaAsync();
        var id = await s.CreaPaginaAsync(s.A, "a", "Titolo", "x", pubblica: true);
        await s.RinominaAsync(s.A, id, "b");
        await s.RinominaAsync(s.A, id, "a");
        await s.PubblicaAsync(s.A, id);

        using var originale = await s.SessioneA().GetAsync("/a");
        using var intermedio = await s.SessioneA().GetAsync("/b");

        Assert.Equal(HttpStatusCode.OK, originale.StatusCode);
        Assert.Equal("/a", SessioneProva.Destinazione(intermedio));
    }

    [Fact]
    public async Task UnVecchioSlugDiUnaPaginaRitirataEUn404()
    {
        await using var s = await Contesto.CreaAsync();
        var id = await s.CreaPaginaAsync(s.A, "vecchio", "Titolo", "x", pubblica: true);
        await s.RinominaAsync(s.A, id, "nuovo");
        await s.PubblicaAsync(s.A, id);
        await s.RitiraAsync(s.A, id);

        using var risposta = await s.SessioneA().GetAsync("/vecchio");

        Assert.Equal(HttpStatusCode.NotFound, risposta.StatusCode);
    }

    [Fact]
    public async Task IRedirectNonSuperanoTenantNeLingue()
    {
        await using var s = await Contesto.CreaAsync();
        var id = await s.CreaPaginaAsync(s.A, "vecchio", "Titolo", "x", pubblica: true);
        await s.RinominaAsync(s.A, id, "nuovo");
        await s.PubblicaAsync(s.A, id);

        using var altroTenant = await s.SessioneB().GetAsync("/vecchio");
        using var altraLingua = await s.SessioneA().GetAsync("/en/vecchio");

        Assert.Equal(HttpStatusCode.NotFound, altroTenant.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, altraLingua.StatusCode);
    }

    [Fact]
    public async Task IlRedirectInUnaLinguaNonDefaultMantieneIlPrefisso()
    {
        await using var s = await Contesto.CreaAsync();
        var id = await s.CreaPaginaAsync(s.A, "old-news", "News", "x", lingua: "en", pubblica: true);
        await s.RinominaAsync(s.A, id, "news");
        await s.PubblicaAsync(s.A, id);

        using var risposta = await s.SessioneA().GetAsync("/en/old-news");

        Assert.Equal(HttpStatusCode.MovedPermanently, risposta.StatusCode);
        Assert.Equal("/en/news", SessioneProva.Destinazione(risposta));
    }

    [Fact]
    public async Task LaPaginaInizialeChiamataPerSlugReindirizzaAllaRadice()
    {
        await using var s = await Contesto.CreaAsync();
        await s.CreaPaginaAsync(s.A, "home", "Benvenuti", "x", iniziale: true, pubblica: true);

        using var perSlug = await s.SessioneA().GetAsync("/home");
        using var radice = await s.SessioneA().GetAsync("/");

        Assert.Equal(HttpStatusCode.MovedPermanently, perSlug.StatusCode);
        Assert.Equal("/", SessioneProva.Destinazione(perSlug));
        Assert.Equal(HttpStatusCode.OK, radice.StatusCode);
    }

    [Fact]
    public async Task UnaPaginaInizialeRinominataReindirizzaAllaRadice()
    {
        await using var s = await Contesto.CreaAsync();
        var id = await s.CreaPaginaAsync(s.A, "home", "Benvenuti", "x", iniziale: true, pubblica: true);
        await s.RinominaAsync(s.A, id, "inizio");
        await s.PubblicaAsync(s.A, id);

        using var vecchio = await s.SessioneA().GetAsync("/home");

        Assert.Equal("/", SessioneProva.Destinazione(vecchio));
    }

    [Fact]
    public async Task LeInformazioniSeoSonoNelDocumentoPubblico()
    {
        await using var s = await Contesto.CreaAsync();
        var id = await s.CreaPaginaAsync(s.A, "seo", "Titolo pagina", "x");
        await s.ImpostaSeoAsync(s.A, id, "Titolo per Google", "Una <descrizione> \"corta\"", false);
        await s.PubblicaAsync(s.A, id);

        using var risposta = await s.SessioneA().GetAsync("/seo");
        var html = await risposta.Content.ReadAsStringAsync(Ct);

        Assert.Contains("<title>Titolo per Google</title>", html, StringComparison.Ordinal);
        Assert.Contains("<h1>Titolo pagina</h1>", html, StringComparison.Ordinal);
        Assert.Contains("<meta name=\"description\" content=\"Una &lt;descrizione&gt; &quot;corta&quot;\" />", html, StringComparison.Ordinal);
        Assert.Contains("<link rel=\"canonical\" href=\"http://a.esempio.it/seo\" />", html, StringComparison.Ordinal);
        Assert.Contains("<meta property=\"og:title\" content=\"Titolo per Google\" />", html, StringComparison.Ordinal);
        Assert.Contains("<meta property=\"og:url\" content=\"http://a.esempio.it/seo\" />", html, StringComparison.Ordinal);
        Assert.DoesNotContain("noindex", html, StringComparison.Ordinal);
        Assert.False(risposta.Headers.Contains("X-Robots-Tag"));
    }

    [Fact]
    public async Task SenzaTitoloSeoSiUsaIlTitoloDellaPaginaESenzaDescrizioneNonCeMeta()
    {
        await using var s = await Contesto.CreaAsync();
        await s.CreaPaginaAsync(s.A, "base", "Titolo base", "x", pubblica: true);

        var html = await (await s.SessioneA().GetAsync("/base")).Content.ReadAsStringAsync(Ct);

        Assert.Contains("<title>Titolo base</title>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("name=\"description\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnaPaginaNonIndicizzabileHaNoindexNeiMetaENellIntestazioneENessunHreflang()
    {
        await using var s = await Contesto.CreaAsync();
        var it = await s.CreaPaginaAsync(s.A, "privata", "Privata", "x", pubblica: true);
        await s.CreaPaginaAsync(s.A, "private", "Private", "x", lingua: "en", pubblica: true, traduzioneDi: it);
        await s.ImpostaSeoAsync(s.A, it, null, null, true);
        await s.PubblicaAsync(s.A, it);

        using var risposta = await s.SessioneA().GetAsync("/privata");
        var html = await risposta.Content.ReadAsStringAsync(Ct);

        Assert.Equal(HttpStatusCode.OK, risposta.StatusCode);
        Assert.Contains("<meta name=\"robots\" content=\"noindex, follow\" />", html, StringComparison.Ordinal);
        Assert.Equal("noindex, follow", Assert.Single(risposta.Headers.GetValues("X-Robots-Tag")));
        Assert.DoesNotContain("hreflang", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LaBozzaSeoNonSiVedeFinoAllaPubblicazione()
    {
        await using var s = await Contesto.CreaAsync();
        var id = await s.CreaPaginaAsync(s.A, "seo", "Titolo", "x", pubblica: true);
        await s.ImpostaSeoAsync(s.A, id, "Titolo nuovo", "Descrizione nuova", true);

        var html = await (await s.SessioneA().GetAsync("/seo")).Content.ReadAsStringAsync(Ct);

        Assert.Contains("<title>Titolo</title>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Descrizione nuova", html, StringComparison.Ordinal);
        Assert.DoesNotContain("noindex", html, StringComparison.Ordinal);
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

        public DatiProva.TenantCreato A => Web.A;

        public DatiProva.TenantCreato B => Web.B;

        public static async Task<Contesto> CreaAsync()
        {
            var web = await ScenarioWeb.CreaAsync();
            var adminA = await web.CreaTenantAdminAsync("anna@esempio.it");
            var adminB = await DatiProva.CreaUtenzaAsync(web.Fabbrica.Services, RuoloUtenza.TenantAdmin, web.B.IdTenant, "bruno@esempio.it", annullamento: Ct);
            return new Contesto(web, adminA, adminB);
        }

        public SessioneProva SessioneA() => Web.SessioneA();

        public SessioneProva SessioneB() => Web.SessioneB();

        public SessioneProva SessioneSconosciuta() => Web.SessioneSu("sconosciuto.esempio.it");

        public async Task<Guid> CreaPaginaAsync(
            DatiProva.TenantCreato tenant,
            string slug,
            string titolo,
            string contenuto,
            string lingua = "it",
            bool iniziale = false,
            bool pubblica = false,
            Guid? traduzioneDi = null)
        {
            var esito = await ComeAsync(tenant, (servizio, attore) => servizio.CreaAsync(attore, new DatiNuovaPagina(slug, titolo, contenuto, lingua, iniziale, null, null, traduzioneDi), Ct));
            Assert.True(esito.Riuscito, esito.Codice.ToString());
            if (pubblica)
            {
                await PubblicaAsync(tenant, esito.Id!.Value);
            }

            return esito.Id!.Value;
        }

        public async Task PubblicaAsync(DatiProva.TenantCreato tenant, Guid id)
        {
            var dettaglio = (await ComeAsync(tenant, (servizio, attore) => servizio.LeggiAsync(attore, id, Ct)))!;
            var esito = await ComeAsync(tenant, (servizio, attore) => servizio.PubblicaAsync(attore, id, dettaglio.TokenConcorrenza, Ct));
            Assert.True(esito.Riuscito, esito.Codice.ToString());
        }

        public async Task RitiraAsync(DatiProva.TenantCreato tenant, Guid id)
        {
            var dettaglio = (await ComeAsync(tenant, (servizio, attore) => servizio.LeggiAsync(attore, id, Ct)))!;
            var esito = await ComeAsync(tenant, (servizio, attore) => servizio.RitiraAsync(attore, id, dettaglio.TokenConcorrenza, Ct));
            Assert.True(esito.Riuscito, esito.Codice.ToString());
        }

        public async Task ModificaAsync(DatiProva.TenantCreato tenant, Guid id, string titolo, string contenuto)
        {
            var dettaglio = (await ComeAsync(tenant, (servizio, attore) => servizio.LeggiAsync(attore, id, Ct)))!;
            var esito = await ComeAsync(tenant, (servizio, attore) =>
                servizio.SalvaAsync(attore, id, new DatiModificaPagina(null, titolo, contenuto, null, dettaglio.PaginaIniziale, dettaglio.TokenConcorrenza), Ct));
            Assert.True(esito.Riuscito, esito.Codice.ToString());
        }

        public async Task RinominaAsync(DatiProva.TenantCreato tenant, Guid id, string slug)
        {
            var dettaglio = (await ComeAsync(tenant, (servizio, attore) => servizio.LeggiAsync(attore, id, Ct)))!;
            var esito = await ComeAsync(tenant, (servizio, attore) =>
                servizio.SalvaAsync(attore, id, new DatiModificaPagina(slug, dettaglio.Titolo, dettaglio.Contenuto, null, dettaglio.PaginaIniziale, dettaglio.TokenConcorrenza), Ct));
            Assert.True(esito.Riuscito, esito.Codice.ToString());
        }

        public async Task ImpostaSeoAsync(DatiProva.TenantCreato tenant, Guid id, string? titoloSeo, string? descrizioneSeo, bool nonIndicizzare)
        {
            var dettaglio = (await ComeAsync(tenant, (servizio, attore) => servizio.LeggiAsync(attore, id, Ct)))!;
            var esito = await ComeAsync(tenant, (servizio, attore) =>
                servizio.SalvaAsync(
                    attore,
                    id,
                    new DatiModificaPagina(null, dettaglio.Titolo, dettaglio.Contenuto, null, dettaglio.PaginaIniziale, dettaglio.TokenConcorrenza, null, titoloSeo, descrizioneSeo, nonIndicizzare),
                    Ct));
            Assert.True(esito.Riuscito, esito.Codice.ToString());
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
}
