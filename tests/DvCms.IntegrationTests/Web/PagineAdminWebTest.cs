using System.Net;
using System.Text.RegularExpressions;
using DvCms.Domain.Cms;
using DvCms.Infrastructure.Persistenza;
using DvCms.IntegrationTests.Supporto;
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
        await DatiProva.CreaUtenzaAsync(scenario.Fabbrica.Services, DvCms.Domain.Accessi.RuoloUtenza.TenantAdmin, scenario.B.IdTenant, "bruno@esempio.it", annullamento: Ct);
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
        await DatiProva.CreaUtenzaAsync(scenario.Fabbrica.Services, DvCms.Domain.Accessi.RuoloUtenza.TenantAdmin, scenario.B.IdTenant, "bruno@esempio.it", annullamento: Ct);
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
