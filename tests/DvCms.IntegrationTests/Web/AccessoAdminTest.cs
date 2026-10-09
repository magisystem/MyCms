using System.Net;
using DvCms.Domain.Accessi;
using DvCms.IntegrationTests.Supporto;
using Xunit;

namespace DvCms.IntegrationTests.Web;

/// <summary>Accesso all'area /admin (B-003): login, sessione a cookie, tenant dall'host, uscita e cambio password.</summary>
public class AccessoAdminTest
{
    [Fact]
    public async Task SenzaSessioneLAdminRimandaAllAccessoConLUrlDiRitorno()
    {
        await using var scenario = await ScenarioWeb.CreaAsync();

        using var risposta = await scenario.SessioneA().GetAsync("/admin");

        Assert.True(SessioneProva.EReindirizzamento(risposta));
        Assert.Equal("/admin/accesso?ReturnUrl=%2Fadmin", SessioneProva.Destinazione(risposta));
    }

    [Fact]
    public async Task AccessoRiuscitoCreaLaSessioneEMostraLAdmin()
    {
        await using var scenario = await ScenarioWeb.CreaAsync();
        await scenario.CreaTenantAdminAsync("anna@esempio.it");
        var sessione = scenario.SessioneA();

        using var accesso = await sessione.AccediAsync("anna@esempio.it", DatiProva.PasswordProva);

        Assert.True(SessioneProva.EReindirizzamento(accesso));
        Assert.Equal("/admin", SessioneProva.Destinazione(accesso));
        Assert.NotNull(sessione.CookieAdmin);

        using var cruscotto = await sessione.GetAsync("/admin");
        var html = await cruscotto.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, cruscotto.StatusCode);
        Assert.Contains("navbar-vertical", html, StringComparison.Ordinal);
        Assert.Contains("Cruscotto", html, StringComparison.Ordinal);
        Assert.Contains("Utente anna@esempio.it", html, StringComparison.Ordinal);
        Assert.Contains("TenantAdmin", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CookieDiSessioneHaGliAttributiDiSicurezza()
    {
        await using var scenario = await ScenarioWeb.CreaAsync(ambiente: "Production");
        await scenario.CreaTenantAdminAsync("anna@esempio.it");

        using var accesso = await scenario.SessioneA().AccediAsync("anna@esempio.it", DatiProva.PasswordProva);

        var cookie = SessioneProva.IntestazioniSetCookie(accesso).Single(valore => valore.StartsWith(".DvCms.Admin=", StringComparison.Ordinal));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/admin", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("expires", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InSviluppoIlCookieNonRichiedeHttps()
    {
        await using var scenario = await ScenarioWeb.CreaAsync(ambiente: "Development");
        await scenario.CreaTenantAdminAsync("anna@esempio.it");

        using var accesso = await scenario.SessioneA().AccediAsync("anna@esempio.it", DatiProva.PasswordProva);

        var cookie = SessioneProva.IntestazioniSetCookie(accesso).Single(valore => valore.StartsWith(".DvCms.Admin=", StringComparison.Ordinal));
        Assert.DoesNotContain("secure", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("anna@esempio.it", "password-sbagliata-123")]
    [InlineData("nessuno@esempio.it", DatiProva.PasswordProva)]
    [InlineData("", "")]
    public async Task OgniFallimentoDaLaStessaRispostaESenzaCookie(string email, string password)
    {
        await using var scenario = await ScenarioWeb.CreaAsync();
        await scenario.CreaTenantAdminAsync("anna@esempio.it");
        var sessione = scenario.SessioneA();

        using var risposta = await sessione.AccediAsync(email, password);
        var html = await risposta.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, risposta.StatusCode);
        Assert.Contains("Credenziali non valide.", html, StringComparison.Ordinal);
        Assert.Null(sessione.CookieAdmin);
        Assert.DoesNotContain(password.Length > 0 ? password : "\u0001", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TenantAdminDelTenantANonAccedeDallHostB()
    {
        await using var scenario = await ScenarioWeb.CreaAsync();
        await scenario.CreaTenantAdminAsync("anna@esempio.it");
        var sessioneB = scenario.SessioneB();

        using var risposta = await sessioneB.AccediAsync("anna@esempio.it", DatiProva.PasswordProva);
        var html = await risposta.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, risposta.StatusCode);
        Assert.Contains("Credenziali non valide.", html, StringComparison.Ordinal);
        Assert.Null(sessioneB.CookieAdmin);
    }

    [Fact]
    public async Task CookieDelTenantANonVaTraSullHostB()
    {
        await using var scenario = await ScenarioWeb.CreaAsync();
        await scenario.CreaTenantAdminAsync("anna@esempio.it");
        var sessioneA = scenario.SessioneA();
        await sessioneA.AccediAsync("anna@esempio.it", DatiProva.PasswordProva);

        // Stesso cookie, presentato all'host dell'altro tenant (come se fosse stato copiato).
        var sessioneB = scenario.SessioneB();
        using var richiesta = new HttpRequestMessage(HttpMethod.Get, "/admin");
        richiesta.Headers.TryAddWithoutValidation("Cookie", $".DvCms.Admin={sessioneA.CookieAdmin}");
        using var risposta = await sessioneB.InviaAsync(richiesta);

        Assert.True(SessioneProva.EReindirizzamento(risposta));
        Assert.StartsWith("/admin/accesso", SessioneProva.Destinazione(risposta), StringComparison.Ordinal);
    }

    [Fact]
    public async Task PlatformAdminAccedeDaOgniHostComeUnTenantAdmin()
    {
        await using var scenario = await ScenarioWeb.CreaAsync();
        await DatiProva.CreaUtenzaAsync(scenario.Fabbrica.Services, RuoloUtenza.PlatformAdmin, null, "root@esempio.it", annullamento: TestContext.Current.CancellationToken);

        foreach (var sessione in new[] { scenario.SessioneA(), scenario.SessioneB() })
        {
            using var accesso = await sessione.AccediAsync("root@esempio.it", DatiProva.PasswordProva);
            Assert.True(SessioneProva.EReindirizzamento(accesso));

            using var cruscotto = await sessione.GetAsync("/admin");
            var html = await cruscotto.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, cruscotto.StatusCode);
            Assert.Contains("PlatformAdmin", html, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task UscitaCancellaLaSessione()
    {
        await using var scenario = await ScenarioWeb.CreaAsync();
        await scenario.CreaTenantAdminAsync("anna@esempio.it");
        var sessione = scenario.SessioneA();
        await sessione.AccediAsync("anna@esempio.it", DatiProva.PasswordProva);

        using var uscita = await sessione.InviaModuloAsync("/admin", [], "/admin/esci");

        Assert.True(SessioneProva.EReindirizzamento(uscita), $"uscita: {uscita.StatusCode}");
        Assert.Null(sessione.CookieAdmin);
        using var dopo = await sessione.GetAsync("/admin");
        Assert.True(SessioneProva.EReindirizzamento(dopo), $"dopo: {dopo.StatusCode}");
    }

    [Fact]
    public async Task UscitaSenzaTokenAntiforgeryViene400()
    {
        await using var scenario = await ScenarioWeb.CreaAsync();
        await scenario.CreaTenantAdminAsync("anna@esempio.it");
        var sessione = scenario.SessioneA();
        await sessione.AccediAsync("anna@esempio.it", DatiProva.PasswordProva);

        using var uscita = await sessione.InviaModuloAsync("/admin", [], "/admin/esci", conToken: false);

        Assert.Equal(HttpStatusCode.BadRequest, uscita.StatusCode);
        Assert.NotNull(sessione.CookieAdmin);
    }

    [Fact]
    public async Task AccessoSenzaTokenAntiforgeryViene400()
    {
        await using var scenario = await ScenarioWeb.CreaAsync();
        await scenario.CreaTenantAdminAsync("anna@esempio.it");

        using var risposta = await scenario.SessioneA().InviaModuloAsync(
            "/admin/accesso",
            [new("Email", "anna@esempio.it"), new("Password", DatiProva.PasswordProva)],
            conToken: false);

        Assert.Equal(HttpStatusCode.BadRequest, risposta.StatusCode);
    }

    [Theory]
    [InlineData("https://sito-esterno.example/admin", "/admin")]
    [InlineData("//sito-esterno.example/admin", "/admin")]
    [InlineData("/altro-percorso", "/admin")]
    [InlineData("/admin/password", "/admin/password")]
    [InlineData("/admin?x=1", "/admin?x=1")]
    public async Task IlRitornoDopoLAccessoEPossibileSoloVersoLAdmin(string ritorno, string atteso)
    {
        await using var scenario = await ScenarioWeb.CreaAsync();
        await scenario.CreaTenantAdminAsync("anna@esempio.it");

        using var risposta = await scenario.SessioneA().InviaModuloAsync(
            "/admin/accesso",
            [new("Email", "anna@esempio.it"), new("Password", DatiProva.PasswordProva), new("ReturnUrl", ritorno)]);

        Assert.True(SessioneProva.EReindirizzamento(risposta));
        Assert.Equal(atteso, SessioneProva.Destinazione(risposta));
    }

    [Fact]
    public async Task PasswordTemporaneaObbligaAlCambioEPoiLaVecchiaSessioneNonVale()
    {
        await using var scenario = await ScenarioWeb.CreaAsync();
        await scenario.CreaTenantAdminAsync("anna@esempio.it", deveCambiarePassword: true);
        var sessione = scenario.SessioneA();
        await sessione.AccediAsync("anna@esempio.it", DatiProva.PasswordProva);

        using var cruscotto = await sessione.GetAsync("/admin");
        Assert.True(SessioneProva.EReindirizzamento(cruscotto));
        Assert.Equal("/admin/password", SessioneProva.Destinazione(cruscotto));

        using var cambio = await sessione.InviaModuloAsync(
            "/admin/password",
            [new("PasswordAttuale", DatiProva.PasswordProva), new("NuovaPassword", "una-nuova-password-sicura-45"), new("ConfermaPassword", "una-nuova-password-sicura-45")]);
        Assert.True(SessioneProva.EReindirizzamento(cambio));
        Assert.Equal("/admin/accesso", SessioneProva.Destinazione(cambio));

        var nuova = scenario.SessioneA();
        using var vecchiaPassword = await nuova.AccediAsync("anna@esempio.it", DatiProva.PasswordProva);
        Assert.Null(nuova.CookieAdmin);
        using var conNuovaPassword = await nuova.AccediAsync("anna@esempio.it", "una-nuova-password-sicura-45");
        Assert.NotNull(nuova.CookieAdmin);
        using var libero = await nuova.GetAsync("/admin");
        Assert.Equal(HttpStatusCode.OK, libero.StatusCode);
    }

    [Fact]
    public async Task CambioPasswordConConfermaDiversaOPasswordCortaRestaSullaPagina()
    {
        await using var scenario = await ScenarioWeb.CreaAsync();
        await scenario.CreaTenantAdminAsync("anna@esempio.it");
        var sessione = scenario.SessioneA();
        await sessione.AccediAsync("anna@esempio.it", DatiProva.PasswordProva);

        using var diversa = await sessione.InviaModuloAsync(
            "/admin/password",
            [new("PasswordAttuale", DatiProva.PasswordProva), new("NuovaPassword", "una-nuova-password-sicura-45"), new("ConfermaPassword", "altra-conferma-sicura-99")]);
        using var corta = await sessione.InviaModuloAsync(
            "/admin/password",
            [new("PasswordAttuale", DatiProva.PasswordProva), new("NuovaPassword", "corta"), new("ConfermaPassword", "corta")]);

        Assert.Equal(HttpStatusCode.OK, diversa.StatusCode);
        Assert.Contains("La conferma non coincide", await diversa.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, corta.StatusCode);
        using var ancoraValida = await sessione.GetAsync("/admin");
        Assert.Equal(HttpStatusCode.OK, ancoraValida.StatusCode);
    }

    [Fact]
    public async Task AccountDisabilitatoPerdeLaSessioneAlleRichiesteSuccessive()
    {
        await using var scenario = await ScenarioWeb.CreaAsync();
        var idUtenza = await scenario.CreaTenantAdminAsync("anna@esempio.it");
        var sessione = scenario.SessioneA();
        await sessione.AccediAsync("anna@esempio.it", DatiProva.PasswordProva);
        await scenario.DisabilitaAsync(idUtenza);

        using var risposta = await sessione.GetAsync("/admin");

        Assert.True(SessioneProva.EReindirizzamento(risposta));
        Assert.StartsWith("/admin/accesso", SessioneProva.Destinazione(risposta), StringComparison.Ordinal);
    }

    [Fact]
    public async Task OltreIlLimiteDiRichiesteLAccessoRispondeTroppeRichieste()
    {
        await using var scenario = await ScenarioWeb.CreaAsync(richiesteAlMinuto: 3);
        var sessione = scenario.SessioneA();

        var codici = new List<HttpStatusCode>();
        for (var tentativo = 0; tentativo < 5; tentativo++)
        {
            using var risposta = await sessione.GetAsync("/admin/accesso");
            codici.Add(risposta.StatusCode);
        }

        Assert.Equal(3, codici.Count(codice => codice == HttpStatusCode.OK));
        Assert.Equal(2, codici.Count(codice => codice == HttpStatusCode.TooManyRequests));
    }

    [Fact]
    public async Task PaginaDiAccessoNonEIndicizzabileEDichiaraLaLingua()
    {
        await using var scenario = await ScenarioWeb.CreaAsync();

        using var risposta = await scenario.SessioneA().GetAsync("/admin/accesso");
        var html = await risposta.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, risposta.StatusCode);
        Assert.Contains("<html lang=\"it\"", html, StringComparison.Ordinal);
        Assert.Contains("noindex", html, StringComparison.Ordinal);
        Assert.Contains("Accedi al tuo account", html, StringComparison.Ordinal);
    }
}
