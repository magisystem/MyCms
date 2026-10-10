using System.Net;
using System.Text.RegularExpressions;
using DvCms.IntegrationTests.Supporto;
using Xunit;

namespace DvCms.IntegrationTests.Web;

/// <summary>Intestazioni di sicurezza, CSP e chiavi dei cookie (G-005).</summary>
public sealed partial class IntestazioniSicurezzaWebTest
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task OgniRispostaHaLeIntestazioniDiBase()
    {
        await using var s = await ScenarioWeb.CreaAsync();
        await s.CreaTenantAdminAsync("anna@esempio.it");

        foreach (var percorso in new[] { "/", "/non-esiste", "/admin/accesso", "/sitemap.xml", "/robots.txt" })
        {
            using var risposta = await s.SessioneA().GetAsync(percorso);
            Assert.Equal("nosniff", risposta.Headers.GetValues("X-Content-Type-Options").Single());
            Assert.Equal("strict-origin-when-cross-origin", risposta.Headers.GetValues("Referrer-Policy").Single());
            Assert.Equal("same-origin", risposta.Headers.GetValues("Cross-Origin-Opener-Policy").Single());
            Assert.Contains("camera=()", risposta.Headers.GetValues("Permissions-Policy").Single(), StringComparison.Ordinal);
            Assert.True(risposta.Headers.Contains("Content-Security-Policy"), percorso);
        }

        // Anche l'host sconosciuto (risposta del middleware del tenant) ha le intestazioni.
        using var sconosciuto = await s.SessioneSu("sconosciuto.esempio.it").GetAsync("/");
        Assert.Equal(HttpStatusCode.NotFound, sconosciuto.StatusCode);
        Assert.Equal("nosniff", sconosciuto.Headers.GetValues("X-Content-Type-Options").Single());
    }

    [Fact]
    public async Task LaCspNonAmmetteScriptInLineaNeEvalEIlSitoPubblicoNonSiIncorniciaDaAltri()
    {
        await using var s = await ScenarioWeb.CreaAsync();

        using var pubblica = await s.SessioneA().GetAsync("/");
        using var admin = await s.SessioneA().GetAsync("/admin/accesso");

        var cspPubblica = pubblica.Headers.GetValues("Content-Security-Policy").Single();
        var cspAdmin = admin.Headers.GetValues("Content-Security-Policy").Single();
        foreach (var csp in new[] { cspPubblica, cspAdmin })
        {
            Assert.Contains("script-src 'self';", csp, StringComparison.Ordinal);
            Assert.DoesNotContain("unsafe-eval", csp, StringComparison.Ordinal);
            Assert.Contains("object-src 'none'", csp, StringComparison.Ordinal);
            Assert.Contains("base-uri 'self'", csp, StringComparison.Ordinal);
            Assert.Contains("form-action 'self'", csp, StringComparison.Ordinal);
            Assert.Contains("default-src 'self'", csp, StringComparison.Ordinal);
        }

        Assert.Contains("frame-ancestors 'self'", cspPubblica, StringComparison.Ordinal);
        Assert.Contains("frame-ancestors 'none'", cspAdmin, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LAdminNonSiIncorniciaNonVaNellaCacheEIlSitoPubblicoNonHaXFrameOptions()
    {
        await using var s = await ScenarioWeb.CreaAsync();
        await s.CreaTenantAdminAsync("anna@esempio.it");
        var anna = s.SessioneA();
        await anna.AccediAsync("anna@esempio.it", DatiProva.PasswordProva);

        using var accesso = await s.SessioneA().GetAsync("/admin/accesso");
        using var pagine = await anna.GetAsync("/admin/pagine");
        using var pubblica = await s.SessioneA().GetAsync("/");

        foreach (var risposta in new[] { accesso, pagine })
        {
            Assert.Equal("DENY", risposta.Headers.GetValues("X-Frame-Options").Single());
            Assert.Contains("no-store", risposta.Headers.CacheControl?.ToString() ?? string.Empty, StringComparison.Ordinal);
        }

        Assert.False(pubblica.Headers.Contains("X-Frame-Options"));
    }

    [Fact]
    public async Task UnaRispostaCheImpostaGiaUnaIntestazioneLaMantiene()
    {
        await using var s = await ScenarioWeb.CreaAsync();
        var admin = await s.CreaTenantAdminAsync("anna@esempio.it");
        var anna = s.SessioneA();
        await anna.AccediAsync("anna@esempio.it", DatiProva.PasswordProva);
        using var invito = await s.SessioneA().GetAsync("/admin/invito?token=x");

        // La pagina dell'invito non rivela il token negli indirizzi di destinazione: la sua politica più stretta resta.
        Assert.Equal("no-referrer", invito.Headers.GetValues("Referrer-Policy").Single());
        Assert.NotEqual(Guid.Empty, admin);
    }

    [Fact]
    public async Task InProduzioneCeHstsSenzaSottodominiENonInSviluppo()
    {
        await using var produzione = await ScenarioWeb.CreaAsync("Production");
        await using var sviluppo = await ScenarioWeb.CreaAsync();

        using var inProduzione = await produzione.SessioneA().GetAsync("/");
        using var inSviluppo = await sviluppo.SessioneA().GetAsync("/");

        Assert.Equal("max-age=15552000", inProduzione.Headers.GetValues("Strict-Transport-Security").Single());
        Assert.False(inSviluppo.Headers.Contains("Strict-Transport-Security"));
    }

    [Fact]
    public async Task LaModalitaSoloSegnalazioneUsaLIntestazioneReportOnly()
    {
        await using var s = await ScenarioWeb.CreaAsync(impostazioni: new Dictionary<string, string?> { ["Sicurezza:Csp:SoloSegnalazione"] = "true" });

        using var risposta = await s.SessioneA().GetAsync("/");

        Assert.False(risposta.Headers.Contains("Content-Security-Policy"));
        Assert.True(risposta.Headers.Contains("Content-Security-Policy-Report-Only"));
    }

    [Fact]
    public async Task LePagineNonContengonoScriptNeGestoriDiEventiInLineaCosiLaCspNonLeRompe()
    {
        await using var s = await ScenarioWeb.CreaAsync();
        await s.CreaTenantAdminAsync("anna@esempio.it");
        var anna = s.SessioneA();
        await anna.AccediAsync("anna@esempio.it", DatiProva.PasswordProva);

        foreach (var percorso in new[] { "/admin", "/admin/pagine", "/admin/pagine/nuova", "/admin/pagine/nuova?modello=presentazione", "/admin/tema", "/admin/tema?tema=moderno", "/admin/media" })
        {
            using var risposta = await anna.GetAsync(percorso);
            Verifica(await risposta.Content.ReadAsStringAsync(Ct), percorso);
        }

        using var accesso = await s.SessioneA().GetAsync("/admin/accesso");
        Verifica(await accesso.Content.ReadAsStringAsync(Ct), "/admin/accesso");
        using var recupero = await s.SessioneA().GetAsync("/admin/recupero");
        Verifica(await recupero.Content.ReadAsStringAsync(Ct), "/admin/recupero");
        using var pubblica = await s.SessioneA().GetAsync("/");
        Verifica(await pubblica.Content.ReadAsStringAsync(Ct), "/");
    }

    [Fact]
    public async Task LeChiaviDeiCookieSiSalvanoNellaCartellaConfigurataEAntiforgeryFunzionaCosi()
    {
        var cartella = Path.Combine(Path.GetTempPath(), "dvcms-chiavi-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using var s = await ScenarioWeb.CreaAsync(impostazioni: new Dictionary<string, string?> { ["Sicurezza:ChiaviCookie:Percorso"] = cartella });
            await s.CreaTenantAdminAsync("anna@esempio.it");

            using var accesso = await s.SessioneA().AccediAsync("anna@esempio.it", DatiProva.PasswordProva);

            Assert.True(SessioneProva.EReindirizzamento(accesso));
            var chiavi = Directory.EnumerateFiles(cartella, "key-*.xml").ToList();
            Assert.NotEmpty(chiavi);
            Assert.Contains("<key ", await File.ReadAllTextAsync(chiavi[0], Ct), StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(cartella))
            {
                Directory.Delete(cartella, recursive: true);
            }
        }
    }

    private static void Verifica(string html, string percorso)
    {
        Assert.False(ScriptInLinea().IsMatch(html), $"Script in linea in {percorso}");
        Assert.False(GestoreEvento().IsMatch(html), $"Gestore di evento in linea in {percorso}");
        Assert.DoesNotContain("javascript:", html, StringComparison.OrdinalIgnoreCase);
    }

    [GeneratedRegex("<script(?![^>]*\\bsrc=)[^>]*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ScriptInLinea();

    [GeneratedRegex("<[a-z][^>]*\\s(on[a-z]+)\\s*=", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex GestoreEvento();
}
