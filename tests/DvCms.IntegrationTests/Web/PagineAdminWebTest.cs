using System.Net;
using System.Text.RegularExpressions;
using DvCms.Infrastructure.Persistenza;
using DvCms.IntegrationTests.Supporto;
using DvCms.Models.Cms;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DvCms.IntegrationTests.Web;

/// <summary>Gestione delle pagine dell'admin attraverso l'applicazione web (C-002).</summary>
public partial class PagineAdminWebTest
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task SenzaSessioneLeSchermateDelleSaltanoAllAccesso()
    {
        await using var scenario = await ScenarioWeb.CreaAsync();

        using var risposta = await scenario.SessioneA().GetAsync("/admin/pagine");

        Assert.True(SessioneProva.EReindirizzamento(risposta));
        Assert.StartsWith("/admin/accesso", SessioneProva.Destinazione(risposta), StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreaUnaBozzaLaMostraNellElencoEnellaModifica()
    {
        await using var scenario = await ScenarioWeb.CreaAsync();
        await scenario.CreaTenantAdminAsync("anna@esempio.it");
        var sessione = scenario.SessioneA();
        await sessione.AccediAsync("anna@esempio.it", DatiProva.PasswordProva);

        using var creazione = await sessione.InviaModuloAsync(
            "/admin/pagine/nuova",
            [new("Titolo", "Chi siamo"), new("Slug", ""), new("CodiceLingua", "it"), new("Contenuto", "Primo paragrafo.\n\nSecondo.")]);

        Assert.True(SessioneProva.EReindirizzamento(creazione));
        var destinazione = SessioneProva.Destinazione(creazione)!;
        Assert.Matches("^/admin/pagine/[0-9a-f-]{36}$", destinazione);
        using var elenco = await sessione.GetAsync("/admin/pagine");
        var html = await elenco.Content.ReadAsStringAsync(Ct);
        Assert.Contains("Chi siamo", html, StringComparison.Ordinal);
        Assert.Contains("/chi-siamo", html, StringComparison.Ordinal);
        using var modifica = await sessione.GetAsync(destinazione);
        Assert.Equal(HttpStatusCode.OK, modifica.StatusCode);
        Assert.Contains("Primo paragrafo.", await modifica.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task SeoCambioSlugERedirectDalModulo()
    {
        await using var scenario = await ScenarioWeb.CreaAsync();
        await scenario.CreaTenantAdminAsync("anna@esempio.it");
        var sessione = scenario.SessioneA();
        await sessione.AccediAsync("anna@esempio.it", DatiProva.PasswordProva);
        using var creazione = await sessione.InviaModuloAsync(
            "/admin/pagine/nuova",
            [new("Titolo", "Pagina"), new("Slug", "vecchia"), new("CodiceLingua", "it"), new("Contenuto", "uno"), new("TitoloSeo", "Titolo SEO"), new("DescrizioneSeo", "Descrizione SEO"), new("NonIndicizzare", "true")]);
        var percorso = SessioneProva.Destinazione(creazione)!;
        var token = await LeggiTokenAsync(sessione, percorso);
        using var pubblica = await sessione.InviaModuloAsync(percorso, [new("TokenConcorrenza", token)], $"{percorso}?handler=Pubblica");
        Assert.True(SessioneProva.EReindirizzamento(pubblica));

        // I campi SEO tornano nel modulo; un valore troppo lungo è rifiutato senza perdere ciò che si è scritto.
        using var modulo = await sessione.GetAsync(percorso);
        var html = await modulo.Content.ReadAsStringAsync(Ct);
        Assert.Contains("value=\"Titolo SEO\"", html, StringComparison.Ordinal);
        Assert.Contains("Descrizione SEO</textarea>", html, StringComparison.Ordinal);
        token = await LeggiTokenAsync(sessione, percorso);
        using var troppoLungo = await sessione.InviaModuloAsync(
            percorso,
            [new("Titolo", "Pagina"), new("Slug", "vecchia"), new("Contenuto", "uno"), new("TitoloSeo", new string('x', 201)), new("TokenConcorrenza", token)],
            $"{percorso}?handler=Salva");
        Assert.Equal(HttpStatusCode.OK, troppoLungo.StatusCode);
        Assert.Contains("is-invalid", await troppoLungo.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);

        // Una pagina pubblicata può cambiare indirizzo: il vecchio compare tra i reindirizzamenti e si può eliminare.
        token = await LeggiTokenAsync(sessione, percorso);
        using var rinomina = await sessione.InviaModuloAsync(
            percorso,
            [new("Titolo", "Pagina"), new("Slug", "nuova"), new("Contenuto", "uno"), new("TitoloSeo", "Titolo SEO"), new("TokenConcorrenza", token)],
            $"{percorso}?handler=Salva");
        Assert.True(SessioneProva.EReindirizzamento(rinomina));
        using var dopo = await sessione.GetAsync(percorso);
        var htmlDopo = await dopo.Content.ReadAsStringAsync(Ct);
        Assert.Contains("/vecchia", htmlDopo, StringComparison.Ordinal);
        using var pubblico = await scenario.SessioneA().GetAsync("/vecchia");
        Assert.Equal(HttpStatusCode.MovedPermanently, pubblico.StatusCode);
        Assert.Equal("/nuova", SessioneProva.Destinazione(pubblico));

        var idRedirect = System.Text.RegularExpressions.Regex.Match(htmlDopo, "idReindirizzamento=([0-9a-f-]{36})").Groups[1].Value;
        Assert.NotEmpty(idRedirect);
        using var elimina = await sessione.InviaModuloAsync(percorso, [], $"{percorso}?handler=EliminaReindirizzamento&idReindirizzamento={idRedirect}");
        Assert.True(SessioneProva.EReindirizzamento(elimina));
        using var eliminato = await scenario.SessioneA().GetAsync("/vecchia");
        Assert.Equal(HttpStatusCode.NotFound, eliminato.StatusCode);
    }

    [Fact]
    public async Task IlContenutoEMostratoCodificatoNonComeMarkup()
    {
        await using var scenario = await ScenarioWeb.CreaAsync();
        await scenario.CreaTenantAdminAsync("anna@esempio.it");
        var sessione = scenario.SessioneA();
        await sessione.AccediAsync("anna@esempio.it", DatiProva.PasswordProva);

        using var creazione = await sessione.InviaModuloAsync(
            "/admin/pagine/nuova",
            [new("Titolo", "<script>alert(1)</script>"), new("Slug", "xss"), new("CodiceLingua", "it"), new("Contenuto", "<img src=x onerror=alert(2)>")]);
        using var modifica = await sessione.GetAsync(SessioneProva.Destinazione(creazione)!);

        var html = await modifica.Content.ReadAsStringAsync(Ct);
        Assert.DoesNotContain("<script>alert(1)</script>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<img src=x", html, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnErroreDiValidazioneMostraIlMessaggioENonCreaLaPagina()
    {
        await using var scenario = await ScenarioWeb.CreaAsync();
        await scenario.CreaTenantAdminAsync("anna@esempio.it");
        var sessione = scenario.SessioneA();
        await sessione.AccediAsync("anna@esempio.it", DatiProva.PasswordProva);

        using var risposta = await sessione.InviaModuloAsync(
            "/admin/pagine/nuova",
            [new("Titolo", "Riservata"), new("Slug", "admin"), new("CodiceLingua", "it"), new("Contenuto", "x")]);

        Assert.Equal(HttpStatusCode.OK, risposta.StatusCode);
        Assert.Contains("alert-danger", await risposta.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        Assert.Equal(0, await ContaPagineAsync(scenario));
    }

    [Fact]
    public async Task UnaScritturaSenzaTokenAntiforgeryVieneRifiutata()
    {
        await using var scenario = await ScenarioWeb.CreaAsync();
        await scenario.CreaTenantAdminAsync("anna@esempio.it");
        var sessione = scenario.SessioneA();
        await sessione.AccediAsync("anna@esempio.it", DatiProva.PasswordProva);

        using var risposta = await sessione.InviaModuloAsync(
            "/admin/pagine/nuova",
            [new("Titolo", "Senza token"), new("Slug", "senza-token"), new("CodiceLingua", "it"), new("Contenuto", "x")],
            conToken: false);

        Assert.Equal(HttpStatusCode.BadRequest, risposta.StatusCode);
        Assert.Equal(0, await ContaPagineAsync(scenario));
    }

    [Fact]
    public async Task SalvarePubblicareERitirareDalModulo()
    {
        await using var scenario = await ScenarioWeb.CreaAsync();
        await scenario.CreaTenantAdminAsync("anna@esempio.it");
        var sessione = scenario.SessioneA();
        await sessione.AccediAsync("anna@esempio.it", DatiProva.PasswordProva);
        using var creazione = await sessione.InviaModuloAsync("/admin/pagine/nuova", [new("Titolo", "Pagina"), new("Slug", "pagina"), new("CodiceLingua", "it"), new("Contenuto", "uno")]);
        var percorso = SessioneProva.Destinazione(creazione)!;

        var token = await LeggiTokenAsync(sessione, percorso);
        using var salva = await sessione.InviaModuloAsync(percorso, [new("Titolo", "Pagina bis"), new("Slug", "pagina"), new("Contenuto", "due"), new("TokenConcorrenza", token)], $"{percorso}?handler=Salva");
        Assert.True(SessioneProva.EReindirizzamento(salva));

        token = await LeggiTokenAsync(sessione, percorso);
        using var pubblica = await sessione.InviaModuloAsync(percorso, [new("TokenConcorrenza", token)], $"{percorso}?handler=Pubblica");
        Assert.True(SessioneProva.EReindirizzamento(pubblica));
        Assert.Equal(StatoPagina.Pubblicata, await StatoAsync(scenario));

        // Con un token ormai vecchio la modifica viene respinta con un messaggio, senza perdere dati.
        using var vecchio = await sessione.InviaModuloAsync(percorso, [new("Titolo", "Sovrascritta"), new("Contenuto", "x"), new("TokenConcorrenza", token)], $"{percorso}?handler=Salva");
        using var dopoVecchio = await sessione.GetAsync(percorso);
        Assert.Contains("alert-danger", await dopoVecchio.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);

        token = await LeggiTokenAsync(sessione, percorso);
        using var ritira = await sessione.InviaModuloAsync(percorso, [new("TokenConcorrenza", token)], $"{percorso}?handler=Ritira");
        Assert.True(SessioneProva.EReindirizzamento(ritira));
        Assert.Equal(StatoPagina.Bozza, await StatoAsync(scenario));
    }

    [Fact]
    public async Task LaPaginaDiUnAltroTenantNonSiApreDallAltroHost()
    {
        await using var scenario = await ScenarioWeb.CreaAsync();
        await scenario.CreaTenantAdminAsync("anna@esempio.it");
        await DatiProva.CreaUtenzaAsync(scenario.Fabbrica.Services, DvCms.Models.Accessi.RuoloUtenza.TenantAdmin, scenario.B.IdTenant, "bruno@esempio.it", annullamento: Ct);
        var anna = scenario.SessioneA();
        var bruno = scenario.SessioneB();
        await anna.AccediAsync("anna@esempio.it", DatiProva.PasswordProva);
        await bruno.AccediAsync("bruno@esempio.it", DatiProva.PasswordProva);
        using var creazione = await anna.InviaModuloAsync("/admin/pagine/nuova", [new("Titolo", "Riservata"), new("Slug", "riservata"), new("CodiceLingua", "it"), new("Contenuto", "x")]);
        var percorso = SessioneProva.Destinazione(creazione)!;

        using var letturaB = await bruno.GetAsync(percorso);
        var token = await LeggiTokenAsync(anna, percorso);
        using var scritturaB = await bruno.InviaModuloAsync("/admin", [new("Titolo", "Hacked"), new("Contenuto", "x"), new("TokenConcorrenza", token)], $"{percorso}?handler=Salva");
        using var elencoB = await bruno.GetAsync("/admin/pagine");

        Assert.Equal(HttpStatusCode.NotFound, letturaB.StatusCode);
        Assert.True(SessioneProva.EReindirizzamento(scritturaB));
        Assert.DoesNotContain("Riservata", await elencoB.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        Assert.DoesNotContain("Hacked", await (await anna.GetAsync(percorso)).Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConPasswordTemporaneaLeSchermateDelleSaltanoAlCambioPassword()
    {
        await using var scenario = await ScenarioWeb.CreaAsync();
        await scenario.CreaTenantAdminAsync("anna@esempio.it", deveCambiarePassword: true);
        var sessione = scenario.SessioneA();
        await sessione.AccediAsync("anna@esempio.it", DatiProva.PasswordProva);

        using var risposta = await sessione.GetAsync("/admin/pagine");

        Assert.Equal("/admin/password", SessioneProva.Destinazione(risposta));
    }

    [Fact]
    public async Task LAnteprimaMostraLaBozzaNonPubblicaConIntestazioniNoindexENoStore()
    {
        await using var scenario = await ScenarioWeb.CreaAsync();
        await scenario.CreaTenantAdminAsync("anna@esempio.it");
        var anna = scenario.SessioneA();
        await anna.AccediAsync("anna@esempio.it", DatiProva.PasswordProva);
        using var creazione = await anna.InviaModuloAsync("/admin/pagine/nuova", [new("Titolo", "Segreta"), new("Slug", "segreta"), new("CodiceLingua", "it"), new("Contenuto", "Testo ancora riservato")]);
        var percorso = SessioneProva.Destinazione(creazione)!;

        using var anteprima = await anna.GetAsync($"{percorso}/anteprima");
        using var pubblica = await scenario.SessioneA().GetAsync("/segreta");

        Assert.Equal(HttpStatusCode.OK, anteprima.StatusCode);
        Assert.Contains("noindex", string.Join(',', anteprima.Headers.GetValues("X-Robots-Tag")), StringComparison.Ordinal);
        Assert.Contains("no-store", anteprima.Headers.CacheControl?.ToString() ?? string.Empty, StringComparison.Ordinal);
        var html = await anteprima.Content.ReadAsStringAsync(Ct);
        Assert.Contains("Testo ancora riservato", html, StringComparison.Ordinal);
        Assert.Contains("<meta name=\"robots\" content=\"noindex, nofollow\"", html, StringComparison.Ordinal);
        Assert.Contains("non è ancora pubblica", System.Net.WebUtility.HtmlDecode(html), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NotFound, pubblica.StatusCode);
    }

    [Fact]
    public async Task LAnteprimaRichiedeLaSessioneEUnAltroTenantNonLaVede()
    {
        await using var scenario = await ScenarioWeb.CreaAsync();
        await scenario.CreaTenantAdminAsync("anna@esempio.it");
        await DatiProva.CreaUtenzaAsync(scenario.Fabbrica.Services, DvCms.Models.Accessi.RuoloUtenza.TenantAdmin, scenario.B.IdTenant, "bruno@esempio.it", annullamento: Ct);
        var anna = scenario.SessioneA();
        var bruno = scenario.SessioneB();
        await anna.AccediAsync("anna@esempio.it", DatiProva.PasswordProva);
        await bruno.AccediAsync("bruno@esempio.it", DatiProva.PasswordProva);
        using var creazione = await anna.InviaModuloAsync("/admin/pagine/nuova", [new("Titolo", "Riservata"), new("Slug", "riservata"), new("CodiceLingua", "it"), new("Contenuto", "x")]);
        var percorso = SessioneProva.Destinazione(creazione)!;

        using var anonima = await scenario.SessioneA().GetAsync($"{percorso}/anteprima");
        using var altroTenant = await bruno.GetAsync($"{percorso}/anteprima");
        using var altraRevisione = await anna.GetAsync($"{percorso}/anteprima?revisione={Guid.NewGuid()}");

        Assert.True(SessioneProva.EReindirizzamento(anonima));
        Assert.StartsWith("/admin/accesso", SessioneProva.Destinazione(anonima), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NotFound, altroTenant.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, altraRevisione.StatusCode);
    }

    [Fact]
    public async Task RollbackEdRipristinoDalleRevisioni()
    {
        await using var scenario = await ScenarioWeb.CreaAsync();
        await scenario.CreaTenantAdminAsync("anna@esempio.it");
        var anna = scenario.SessioneA();
        await anna.AccediAsync("anna@esempio.it", DatiProva.PasswordProva);
        using var creazione = await anna.InviaModuloAsync("/admin/pagine/nuova", [new("Titolo", "Versione uno"), new("Slug", "pagina"), new("CodiceLingua", "it"), new("Contenuto", "Testo uno")]);
        var percorso = SessioneProva.Destinazione(creazione)!;
        using var pubblica1 = await anna.InviaModuloAsync(percorso, [new("TokenConcorrenza", await LeggiTokenAsync(anna, percorso))], $"{percorso}?handler=Pubblica");
        using var salva = await anna.InviaModuloAsync(percorso, [new("Titolo", "Versione due"), new("Slug", "pagina"), new("Contenuto", "Testo due"), new("TokenConcorrenza", await LeggiTokenAsync(anna, percorso))], $"{percorso}?handler=Salva");
        using var pubblica2 = await anna.InviaModuloAsync(percorso, [new("TokenConcorrenza", await LeggiTokenAsync(anna, percorso))], $"{percorso}?handler=Pubblica");
        Assert.Contains("Testo due", await (await scenario.SessioneA().GetAsync("/pagina")).Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);

        // Rollback: si pubblica la revisione 1 dalla lista delle revisioni.
        var idPrima = await IdRevisioneAsync(anna, percorso, 1);
        using var rollback = await anna.InviaModuloAsync(percorso, [new("idRevisione", idPrima), new("TokenConcorrenza", await LeggiTokenAsync(anna, percorso))], $"{percorso}?handler=PubblicaRevisione");
        var pubblica = await (await scenario.SessioneA().GetAsync("/pagina")).Content.ReadAsStringAsync(Ct);
        Assert.Contains("Testo uno", pubblica, StringComparison.Ordinal);
        Assert.DoesNotContain("Testo due", pubblica, StringComparison.Ordinal);

        // Ripristino: il contenuto della revisione 2 torna come nuova bozza (la pagina pubblica non cambia).
        var idSeconda = await IdRevisioneAsync(anna, percorso, 2);
        using var ripristino = await anna.InviaModuloAsync(percorso, [new("idRevisione", idSeconda), new("TokenConcorrenza", await LeggiTokenAsync(anna, percorso))], $"{percorso}?handler=Ripristina");
        var modifica = await (await anna.GetAsync(percorso)).Content.ReadAsStringAsync(Ct);
        Assert.Contains("Testo due", modifica, StringComparison.Ordinal);
        Assert.Contains("Ripristino della revisione 2", modifica, StringComparison.Ordinal);
        Assert.Contains("Testo uno", await (await scenario.SessioneA().GetAsync("/pagina")).Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ICampiDelModelloSceltoSonoNelModuloEIlSitoLiMostra()
    {
        await using var scenario = await ScenarioWeb.CreaAsync();
        await scenario.CreaTenantAdminAsync("anna@esempio.it");
        var anna = scenario.SessioneA();
        await anna.AccediAsync("anna@esempio.it", DatiProva.PasswordProva);

        var semplice = await (await anna.GetAsync("/admin/pagine/nuova")).Content.ReadAsStringAsync(Ct);
        var presentazione = await (await anna.GetAsync("/admin/pagine/nuova?modello=presentazione")).Content.ReadAsStringAsync(Ct);
        Assert.DoesNotContain("name=\"m_Sottotitolo\"", semplice, StringComparison.Ordinal);
        Assert.Contains("name=\"m_Sottotitolo\"", presentazione, StringComparison.Ordinal);
        Assert.Contains("name=\"m_UrlPulsante\"", presentazione, StringComparison.Ordinal);

        using var creazione = await anna.InviaModuloAsync(
            "/admin/pagine/nuova?modello=presentazione",
            [
                new("Titolo", "Vetrina"), new("Slug", "vetrina"), new("CodiceLingua", "it"), new("Contenuto", "Corpo della pagina"),
                new("m_Sottotitolo", "Un <b>sottotitolo</b>"), new("m_EtichettaPulsante", "Scopri"), new("m_UrlPulsante", "/prodotti"),
            ]);
        var percorso = SessioneProva.Destinazione(creazione)!;
        Assert.Matches("^/admin/pagine/[0-9a-f-]{36}$", percorso);
        using var pubblica = await anna.InviaModuloAsync(percorso, [new("TokenConcorrenza", await LeggiTokenAsync(anna, percorso))], $"{percorso}?handler=Pubblica");

        var html = await (await scenario.SessioneA().GetAsync("/vetrina")).Content.ReadAsStringAsync(Ct);
        Assert.Contains("class=\"sottotitolo\"", html, StringComparison.Ordinal);
        Assert.Contains("Un &lt;b&gt;sottotitolo&lt;/b&gt;", html, StringComparison.Ordinal);
        Assert.Contains("<a class=\"pulsante\" href=\"/prodotti\">Scopri</a>", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnIndirizzoPericolosoDelPulsanteEvidenziatoENonSalvato()
    {
        await using var scenario = await ScenarioWeb.CreaAsync();
        await scenario.CreaTenantAdminAsync("anna@esempio.it");
        var anna = scenario.SessioneA();
        await anna.AccediAsync("anna@esempio.it", DatiProva.PasswordProva);

        using var risposta = await anna.InviaModuloAsync(
            "/admin/pagine/nuova?modello=presentazione",
            [new("Titolo", "Vetrina"), new("Slug", "vetrina"), new("CodiceLingua", "it"), new("Contenuto", "x"), new("m_EtichettaPulsante", "Clicca"), new("m_UrlPulsante", "javascript:alert(1)")]);

        Assert.Equal(HttpStatusCode.OK, risposta.StatusCode);
        var html = await risposta.Content.ReadAsStringAsync(Ct);
        Assert.Contains("is-invalid", html, StringComparison.Ordinal);
        Assert.Contains("Clicca", html, StringComparison.Ordinal);
        Assert.Equal(0, await ContaPagineAsync(scenario));
    }

    [Fact]
    public async Task UnAttributoNelPulsanteNonPuoEssereIniettato()
    {
        await using var scenario = await ScenarioWeb.CreaAsync();
        await scenario.CreaTenantAdminAsync("anna@esempio.it");
        var anna = scenario.SessioneA();
        await anna.AccediAsync("anna@esempio.it", DatiProva.PasswordProva);
        using var creazione = await anna.InviaModuloAsync(
            "/admin/pagine/nuova?modello=presentazione",
            [new("Titolo", "Vetrina"), new("Slug", "vetrina"), new("CodiceLingua", "it"), new("Contenuto", "x"), new("m_EtichettaPulsante", "Vai"), new("m_UrlPulsante", "/x\"onmouseover=\"alert(1)")]);
        var percorso = SessioneProva.Destinazione(creazione)!;
        using var pubblica = await anna.InviaModuloAsync(percorso, [new("TokenConcorrenza", await LeggiTokenAsync(anna, percorso))], $"{percorso}?handler=Pubblica");

        var html = await (await scenario.SessioneA().GetAsync("/vetrina")).Content.ReadAsStringAsync(Ct);

        Assert.DoesNotContain("\"onmouseover=\"", html, StringComparison.Ordinal);
        Assert.Contains("&quot;onmouseover=&quot;", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LaTraduzioneSiCreaDallaPaginaDOrigineESiVedeNelleLingue()
    {
        await using var scenario = await ScenarioWeb.CreaAsync();
        await scenario.CreaTenantAdminAsync("anna@esempio.it");
        var anna = scenario.SessioneA();
        await anna.AccediAsync("anna@esempio.it", DatiProva.PasswordProva);
        using var creazione = await anna.InviaModuloAsync("/admin/pagine/nuova", [new("Titolo", "Chi siamo"), new("Slug", "chi-siamo"), new("CodiceLingua", "it"), new("Contenuto", "x")]);
        var percorso = SessioneProva.Destinazione(creazione)!;
        var idOrigine = percorso[(percorso.LastIndexOf('/') + 1)..];

        var modifica = await (await anna.GetAsync(percorso)).Content.ReadAsStringAsync(Ct);
        Assert.Contains($"/admin/pagine/nuova?traduzioneDi={idOrigine}", modifica, StringComparison.Ordinal);

        var modulo = await (await anna.GetAsync($"/admin/pagine/nuova?traduzioneDi={idOrigine}")).Content.ReadAsStringAsync(Ct);
        Assert.Contains("Traduzione di", modulo, StringComparison.Ordinal);
        Assert.Contains("value=\"en\"", modulo, StringComparison.Ordinal);
        Assert.DoesNotContain("<option value=\"it\"", modulo, StringComparison.Ordinal);

        using var traduzione = await anna.InviaModuloAsync(
            $"/admin/pagine/nuova?traduzioneDi={idOrigine}",
            [new("Titolo", "About us"), new("Slug", "chi-siamo"), new("CodiceLingua", "en"), new("Contenuto", "y")]);
        Assert.True(SessioneProva.EReindirizzamento(traduzione));
        Assert.Matches("^/admin/pagine/[0-9a-f-]{36}$", SessioneProva.Destinazione(traduzione)!);

        var dopo = await (await anna.GetAsync(percorso)).Content.ReadAsStringAsync(Ct);
        Assert.Contains("/chi-siamo", dopo, StringComparison.Ordinal);
        Assert.DoesNotContain("Aggiungi una traduzione", dopo, StringComparison.Ordinal);
        Assert.Contains($"href=\"{SessioneProva.Destinazione(traduzione)}\"", dopo, StringComparison.Ordinal);
        using var altroOrigine = await anna.GetAsync($"/admin/pagine/nuova?traduzioneDi={Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, altroOrigine.StatusCode);
    }

    private static async Task<string> IdRevisioneAsync(SessioneProva sessione, string percorso, int numero)
    {
        using var pagina = await sessione.GetAsync(percorso);
        var html = await pagina.Content.ReadAsStringAsync(Ct);
        var trovato = Regex.Match(html, $"#{numero} - [^<]*.*?anteprima\\?revisione=([0-9a-f-]{{36}})", RegexOptions.Singleline | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));
        Assert.True(trovato.Success, $"revisione {numero} non trovata");
        return trovato.Groups[1].Value;
    }

    private static async Task<string> LeggiTokenAsync(SessioneProva sessione, string percorso)
    {
        using var pagina = await sessione.GetAsync(percorso);
        var html = await pagina.Content.ReadAsStringAsync(Ct);
        // Il valore nell'HTML è codificato ("+" come &#x2B;): un browser lo decodifica prima di inviarlo.
        return System.Net.WebUtility.HtmlDecode(TokenConcorrenza().Match(html).Groups[1].Value);
    }

    private static async Task<int> ContaPagineAsync(ScenarioWeb scenario)
    {
        await using var ambito = scenario.Fabbrica.Services.CreateAsyncScope();
        return await ambito.ServiceProvider.GetRequiredService<ContestoDatabase>().Pagine.IgnoreQueryFilters().CountAsync(Ct);
    }

    private static async Task<StatoPagina> StatoAsync(ScenarioWeb scenario)
    {
        await using var ambito = scenario.Fabbrica.Services.CreateAsyncScope();
        return await ambito.ServiceProvider.GetRequiredService<ContestoDatabase>().Pagine.IgnoreQueryFilters().Select(p => p.Stato).SingleAsync(Ct);
    }

    [GeneratedRegex("name=\"TokenConcorrenza\" value=\"([^\"]+)\"", RegexOptions.CultureInvariant)]
    private static partial Regex TokenConcorrenza();
}
