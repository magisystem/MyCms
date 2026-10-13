using System.Net;
using DvCms.IntegrationTests.Supporto;
using Xunit;

namespace DvCms.IntegrationTests.Web;

/// <summary>Pagine dei modelli email (ADR-0025) attraverso l'applicazione web: accesso, modifica, anteprima isolata e ripristino.</summary>
public class ModelliEmailWebTest
{
    private const string Elenco = "/admin/email/modelli";
    private const string Pagina = "/admin/email/modelli/recupero-codice?lingua=it";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static List<KeyValuePair<string, string>> Campi(string oggetto, string html, string testo, string? token = null)
    {
        var campi = new List<KeyValuePair<string, string>> { new("Oggetto", oggetto), new("CorpoHtml", html), new("CorpoTesto", testo) };
        if (token is not null)
        {
            campi.Add(new("TokenConcorrenza", token));
        }

        return campi;
    }

    [Fact]
    public async Task SoloIlTenantAdminAccedeEVedeLElenco()
    {
        await using var scenario = await ScenarioWeb.CreaAsync();
        await scenario.CreaTenantAdminAsync("anna@esempio.it");
        await scenario.CreaPlatformAdminAsync("root@esempio.it");

        using var anonimo = await scenario.SessioneA().GetAsync(Elenco);
        var platform = scenario.SessioneA();
        await platform.AccediAsync("root@esempio.it", DatiProva.PasswordProva);
        using var comePlatform = await platform.GetAsync(Elenco);
        using var comePlatformModifica = await platform.GetAsync(Pagina);
        var anna = scenario.SessioneA();
        await anna.AccediAsync("anna@esempio.it", DatiProva.PasswordProva);
        using var comeAnna = await anna.GetAsync(Elenco);

        Assert.True(SessioneProva.EReindirizzamento(anonimo) || anonimo.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden);
        Assert.Equal(HttpStatusCode.Forbidden, comePlatform.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, comePlatformModifica.StatusCode);
        Assert.Equal(HttpStatusCode.OK, comeAnna.StatusCode);
        var html = await comeAnna.Content.ReadAsStringAsync(Ct);
        Assert.Contains("Recupero password: codice", html, StringComparison.Ordinal);
        Assert.Contains("/admin/email/modelli/invito-amministratore?lingua=it", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnTenantAdminDiUnAltroTenantNonModificaIModelli()
    {
        await using var scenario = await ScenarioWeb.CreaAsync();
        await scenario.CreaTenantAdminAsync("anna@esempio.it");
        var anna = scenario.SessioneB();

        // L'account di A sull'host di B non entra (sessione non valida per quel tenant).
        var accesso = await anna.AccediAsync("anna@esempio.it", DatiProva.PasswordProva);
        using var elenco = await anna.GetAsync(Elenco);
        using var salva = await anna.InviaModuloAsync(Pagina, Campi("x {{ codice }}", "{{ codice }}", "{{ codice }}"), $"{Pagina}&handler=Salva", conToken: false);

        Assert.NotEqual(HttpStatusCode.OK, elenco.StatusCode);
        Assert.NotEqual(HttpStatusCode.OK, salva.StatusCode);
        Assert.NotNull(accesso);
    }

    [Fact]
    public async Task MostraIlModelloDiFabbricaESalvaQuelloNuovoConLAnteprimaIsolata()
    {
        await using var scenario = await ScenarioWeb.CreaAsync();
        await scenario.CreaTenantAdminAsync("anna@esempio.it");
        var anna = scenario.SessioneA();
        await anna.AccediAsync("anna@esempio.it", DatiProva.PasswordProva);

        var aperta = WebUtility.HtmlDecode(await (await anna.GetAsync(Pagina)).Content.ReadAsStringAsync(Ct));
        Assert.Contains("Il tuo codice di verifica è", aperta, StringComparison.Ordinal);
        Assert.Contains("Obbligatoria", aperta, StringComparison.Ordinal);

        var campi = Campi("PIN {{ codice }}", "<p>PIN <b>{{ codice }}</b></p>", "PIN {{ codice }}");
        using var anteprima = await anna.InviaModuloAsync(Pagina, campi, $"{Pagina}&handler=Anteprima");
        var htmlAnteprima = await anteprima.Content.ReadAsStringAsync(Ct);
        Assert.Equal(HttpStatusCode.OK, anteprima.StatusCode);
        Assert.Contains("sandbox=\"\"", htmlAnteprima, StringComparison.Ordinal);
        Assert.Contains("PIN 483920", htmlAnteprima, StringComparison.Ordinal);

        using var salva = await anna.InviaModuloAsync(Pagina, campi, $"{Pagina}&handler=Salva");
        Assert.True(SessioneProva.EReindirizzamento(salva));
        var dopo = await (await anna.GetAsync(Pagina)).Content.ReadAsStringAsync(Ct);
        Assert.Contains("Modello salvato.", dopo, StringComparison.Ordinal);
        Assert.Contains("PIN {{ codice }}", dopo, StringComparison.Ordinal);
        Assert.Contains("Ripristina quello di fabbrica", dopo, StringComparison.Ordinal);

        using var ripristina = await anna.InviaModuloAsync(Pagina, [], $"{Pagina}&handler=Ripristina");
        Assert.True(SessioneProva.EReindirizzamento(ripristina));
        var finale = WebUtility.HtmlDecode(await (await anna.GetAsync(Pagina)).Content.ReadAsStringAsync(Ct));
        Assert.Contains("Il tuo codice di verifica è", finale, StringComparison.Ordinal);
        Assert.DoesNotContain("Ripristina quello di fabbrica", finale, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnModelloNonValidoMostraGliErroriENonSiSalva()
    {
        await using var scenario = await ScenarioWeb.CreaAsync();
        await scenario.CreaTenantAdminAsync("anna@esempio.it");
        var anna = scenario.SessioneA();
        await anna.AccediAsync("anna@esempio.it", DatiProva.PasswordProva);

        var senzaCodice = Campi("Codice", "<p>Ciao</p>", "Ciao");
        var conScript = Campi("Codice {{ codice }}", "<script>alert(1)</script>{{ codice }}", "{{ codice }}");

        foreach (var campi in new[] { senzaCodice, conScript })
        {
            using var risposta = await anna.InviaModuloAsync(Pagina, campi, $"{Pagina}&handler=Salva");
            var html = await risposta.Content.ReadAsStringAsync(Ct);
            Assert.Equal(HttpStatusCode.OK, risposta.StatusCode);
            Assert.Contains("alert-danger", html, StringComparison.Ordinal);
        }

        var elenco = await (await anna.GetAsync(Elenco)).Content.ReadAsStringAsync(Ct);
        Assert.DoesNotContain("btn-primary me-1", elenco, StringComparison.Ordinal);
        using var anteprima = await anna.InviaModuloAsync(Pagina, conScript, $"{Pagina}&handler=Anteprima");
        var htmlAnteprima = await anteprima.Content.ReadAsStringAsync(Ct);
        Assert.DoesNotContain("srcdoc", htmlAnteprima, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CodiceOLinguaSconosciutiDanno404()
    {
        await using var scenario = await ScenarioWeb.CreaAsync();
        await scenario.CreaTenantAdminAsync("anna@esempio.it");
        var anna = scenario.SessioneA();
        await anna.AccediAsync("anna@esempio.it", DatiProva.PasswordProva);

        using var codice = await anna.GetAsync("/admin/email/modelli/non-esiste?lingua=it");
        using var lingua = await anna.GetAsync("/admin/email/modelli/recupero-codice?lingua=fr");

        Assert.Equal(HttpStatusCode.NotFound, codice.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, lingua.StatusCode);
    }
}
