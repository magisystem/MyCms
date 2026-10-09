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

    private static async Task<string> LeggiTokenAsync(SessioneProva sessione, string percorso)
    {
        using var pagina = await sessione.GetAsync(percorso);
        var html = await pagina.Content.ReadAsStringAsync(Ct);
        return TokenConcorrenza().Match(html).Groups[1].Value;
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
