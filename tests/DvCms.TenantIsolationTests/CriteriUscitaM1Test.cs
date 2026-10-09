using System.Net;
using System.Text.RegularExpressions;
using DvCms.Infrastructure.Popolamento;
using DvCms.IntegrationTests.Supporto;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DvCms.TenantIsolationTests;

/// <summary>
/// Criteri di uscita di M1 (plan-m0-m1.md), percorsi per intero dall'interfaccia web: popolamento del tenant primario,
/// un <c>PlatformAdmin</c> crea due tenant con i rispettivi <c>TenantAdmin</c>, i due pubblicano lo stesso slug su domini
/// distinti, nessun accesso incrociato, tentativi di escalation respinti.
/// </summary>
public partial class CriteriUscitaM1Test
{
    private const string HostPrimario = "cms.davidemartelli.dev";
    private const string Host1 = "cms1.davidemartelli.dev";
    private const string Host2 = "cms2.davidemartelli.dev";
    private const string PasswordPlatform = "password-platform-123";
    private const string NuovaPassword1 = "nuova-password-uno-123";
    private const string NuovaPassword2 = "nuova-password-due-456";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task DueTenantCreatiDalPlatformAdminPubblicanoLoStessoSlugSenzaAccessiIncrociati()
    {
        await using var database = await DatabaseDiProva.CreaAsync(Ct);
        using var fabbrica = new FabbricaApplicazioneConConnessione(
            database.Connessione,
            "Production",
            new Dictionary<string, string?>
            {
                ["Seed:PlatformAdmin:Email"] = "platform@esempio.it",
                ["Seed:PlatformAdmin:NomeVisualizzato"] = "Operatore",
                ["Seed:PlatformAdmin:Password"] = PasswordPlatform,
            });
        Assert.Equal(0, await DvCms.Web.Comandi.ComandoMigra.EseguiAsync(fabbrica.Services, Ct));
        await using (var ambito = fabbrica.Services.CreateAsyncScope())
        {
            await ambito.ServiceProvider.GetRequiredService<ServizioPopolamento>().EseguiAsync(null, Ct);
        }

        SessioneProva Nuova(string host) => new(
            fabbrica.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = false, BaseAddress = new Uri("https://localhost") }),
            host);

        // 1. Il PlatformAdmin accede dal dominio del tenant primario e crea i due tenant.
        var platform = Nuova(HostPrimario);
        using (var accesso = await platform.AccediAsync("platform@esempio.it", PasswordPlatform))
        {
            Assert.Equal("/admin", SessioneProva.Destinazione(accesso));
        }

        var dettaglio1 = await CreaTenantAsync(platform, "Cliente 1", Host1);
        var dettaglio2 = await CreaTenantAsync(platform, "Cliente 2", Host2);

        // 2. Crea i rispettivi TenantAdmin: la password temporanea è mostrata una sola volta.
        var temporanea1 = await CreaAmministratoreAsync(platform, dettaglio1, "uno@esempio.it");
        var temporanea2 = await CreaAmministratoreAsync(platform, dettaglio2, "due@esempio.it");

        // 3. Primo accesso: cambio password obbligatorio, poi accesso con la nuova.
        var admin1 = await PrimoAccessoAsync(Nuova(Host1), "uno@esempio.it", temporanea1, NuovaPassword1);
        var admin2 = await PrimoAccessoAsync(Nuova(Host2), "due@esempio.it", temporanea2, NuovaPassword2);

        // 4. Lo stesso slug, su domini distinti, con contenuti diversi.
        await PubblicaPaginaAsync(admin1, "benvenuto", "Benvenuti dal cliente uno", "Contenuto del primo cliente.");
        await PubblicaPaginaAsync(admin2, "benvenuto", "Benvenuti dal cliente due", "Contenuto del secondo cliente.");

        using (var pubblica1 = await Nuova(Host1).GetAsync("/benvenuto"))
        using (var pubblica2 = await Nuova(Host2).GetAsync("/benvenuto"))
        {
            var html1 = await pubblica1.Content.ReadAsStringAsync(Ct);
            var html2 = await pubblica2.Content.ReadAsStringAsync(Ct);
            Assert.Equal(HttpStatusCode.OK, pubblica1.StatusCode);
            Assert.Equal(HttpStatusCode.OK, pubblica2.StatusCode);
            Assert.Contains("primo cliente", html1, StringComparison.Ordinal);
            Assert.DoesNotContain("secondo cliente", html1, StringComparison.Ordinal);
            Assert.Contains("secondo cliente", html2, StringComparison.Ordinal);
            Assert.DoesNotContain("primo cliente", html2, StringComparison.Ordinal);
        }

        // 5. Nessun accesso incrociato.
        using (var incrociato = await Nuova(Host2).AccediAsync("uno@esempio.it", NuovaPassword1))
        {
            Assert.Contains("alert-danger", await incrociato.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        }

        using (var cookieCopiato = new HttpRequestMessage(HttpMethod.Get, "/admin/pagine"))
        {
            cookieCopiato.Headers.TryAddWithoutValidation("Cookie", $".DvCms.Admin={admin1.CookieAdmin}");
            using var risposta = await Nuova(Host2).InviaAsync(cookieCopiato);
            Assert.StartsWith("/admin/accesso", SessioneProva.Destinazione(risposta), StringComparison.Ordinal);
        }

        // 6. Tentativi di escalation respinti: il TenantAdmin non entra nell'area Platform né crea amministratori.
        using (var area = await admin1.GetAsync("/admin/platform/tenant"))
        {
            Assert.Equal(HttpStatusCode.Forbidden, area.StatusCode);
        }

        using (var creazione = await admin1.InviaModuloAsync(
            "/admin",
            [new("email", "complice@esempio.it"), new("nomeVisualizzato", "Complice")],
            $"{dettaglio2}?handler=NuovoAmministratore"))
        {
            Assert.Equal(HttpStatusCode.Forbidden, creazione.StatusCode);
        }
    }

    private static async Task<string> CreaTenantAsync(SessioneProva platform, string nome, string host)
    {
        using var risposta = await platform.InviaModuloAsync(
            "/admin/platform/tenant/nuovo",
            [new("Nome", nome), new("NomeSito", $"Sito di {nome}"), new("FusoOrario", "Europe/Rome"), new("Host", host), new("DominioVerificato", "true")]);
        Assert.True(SessioneProva.EReindirizzamento(risposta));
        var percorso = SessioneProva.Destinazione(risposta)!;
        Assert.Matches("^/admin/platform/tenant/[0-9a-f-]{36}$", percorso);
        return percorso;
    }

    private static async Task<string> CreaAmministratoreAsync(SessioneProva platform, string dettaglio, string email)
    {
        using var risposta = await platform.InviaModuloAsync(
            dettaglio,
            [new("email", email), new("nomeVisualizzato", $"Amministratore {email}")],
            $"{dettaglio}?handler=NuovoAmministratore");
        Assert.Equal(HttpStatusCode.OK, risposta.StatusCode);
        var password = PasswordMostrata().Match(await risposta.Content.ReadAsStringAsync(Ct)).Groups[1].Value;
        Assert.Equal(20, password.Length);
        return password;
    }

    private static async Task<SessioneProva> PrimoAccessoAsync(SessioneProva sessione, string email, string temporanea, string nuova)
    {
        using (var accesso = await sessione.AccediAsync(email, temporanea))
        {
            Assert.True(SessioneProva.EReindirizzamento(accesso));
        }

        using (var cruscotto = await sessione.GetAsync("/admin"))
        {
            Assert.Equal("/admin/password", SessioneProva.Destinazione(cruscotto));
        }

        using (var cambio = await sessione.InviaModuloAsync(
            "/admin/password",
            [new("PasswordAttuale", temporanea), new("NuovaPassword", nuova), new("ConfermaPassword", nuova)]))
        {
            Assert.Equal("/admin/accesso", SessioneProva.Destinazione(cambio));
        }

        using var vecchia = await sessione.AccediAsync(email, temporanea);
        Assert.Contains("alert-danger", await vecchia.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        using var accessoNuovo = await sessione.AccediAsync(email, nuova);
        Assert.Equal("/admin", SessioneProva.Destinazione(accessoNuovo));
        return sessione;
    }

    private static async Task PubblicaPaginaAsync(SessioneProva admin, string slug, string titolo, string contenuto)
    {
        using var creazione = await admin.InviaModuloAsync(
            "/admin/pagine/nuova",
            [new("Titolo", titolo), new("Slug", slug), new("CodiceLingua", "it"), new("Contenuto", contenuto)]);
        var percorso = SessioneProva.Destinazione(creazione)!;
        using var modifica = await admin.GetAsync(percorso);
        // Il valore nell'HTML è codificato (ad esempio "+" come &#x2B;): un browser lo decodifica prima di inviarlo.
        var token = System.Net.WebUtility.HtmlDecode(TokenConcorrenza().Match(await modifica.Content.ReadAsStringAsync(Ct)).Groups[1].Value);
        using var pubblica = await admin.InviaModuloAsync(percorso, [new("TokenConcorrenza", token)], $"{percorso}?handler=Pubblica");
        Assert.True(SessioneProva.EReindirizzamento(pubblica));
        using var dopo = await admin.GetAsync(percorso);
        var html = await dopo.Content.ReadAsStringAsync(Ct);
        var allarme = Regex.Match(html, "alert-danger\"[^>]*>([^<]*)<", RegexOptions.CultureInvariant).Groups[1].Value;
        Assert.True(allarme.Length == 0, allarme);
        Assert.Contains("badge bg-green-lt ms-2", html, StringComparison.Ordinal);
    }

    [GeneratedRegex("id=\"password-temporanea\".*?<code[^>]*>([^<]+)</code>", RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex PasswordMostrata();

    [GeneratedRegex("name=\"TokenConcorrenza\" value=\"([^\"]+)\"", RegexOptions.CultureInvariant)]
    private static partial Regex TokenConcorrenza();
}
