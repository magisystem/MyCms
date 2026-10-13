using System.Net;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using DvCms.IntegrationTests.Supporto;
using DvCms.Services.Outbox;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DvCms.IntegrationTests.Web;

/// <summary>Recupero password attraverso l'applicazione web (B-008): moduli, cookie, intestazioni e risposte uniformi.</summary>
public partial class RecuperoPasswordWebTest
{
    private const string Nuova = "una-nuova-password-sicura-1";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task FlussoCompletoDalleDueEmailAllaNuovaPassword()
    {
        await using var c = await Contesto.CreaAsync();
        await c.Web.CreaTenantAdminAsync("anna@esempio.it");
        var sessioneVecchia = c.Web.SessioneA();
        await sessioneVecchia.AccediAsync("anna@esempio.it", DatiProva.PasswordProva);
        var anonimo = c.Web.SessioneA();

        // 1. Richiesta: cookie temporaneo con attributi di sicurezza e reindirizzamento alla pagina del codice.
        using var richiesta = await anonimo.InviaModuloAsync("/admin/recupero", [new("Email", "anna@esempio.it")]);
        Assert.Equal("/admin/recupero/codice", SessioneProva.Destinazione(richiesta));
        var cookie = Assert.Single(SessioneProva.IntestazioniSetCookie(richiesta), valore => valore.StartsWith(".DvCms.Recupero=", StringComparison.Ordinal));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/admin/recupero", cookie, StringComparison.OrdinalIgnoreCase);

        // 2. Prima email: il codice. Un codice sbagliato è rifiutato, quello giusto accettato.
        var codice = CodiceNelTesto().Match(Assert.Single(await c.ElaboraEmailAsync()).Testo).Groups[1].Value;
        using var errato = await anonimo.InviaModuloAsync("/admin/recupero/codice", [new("Codice", codice == "000000" ? "111111" : "000000")]);
        Assert.Contains("Codice non valido", await TestoAsync(errato), StringComparison.Ordinal);
        using var giusto = await anonimo.InviaModuloAsync("/admin/recupero/codice", [new("Codice", codice)]);
        Assert.Contains("seconda email", await TestoAsync(giusto), StringComparison.Ordinal);
        Assert.DoesNotContain(".DvCms.Recupero", anonimo.Cookie.Keys);

        // 3. Seconda email: il link. La pagina del link non perde il token e non va in cache.
        var emailLink = Assert.Single(await c.ElaboraEmailAsync());
        var link = LinkNelTesto().Match(emailLink.Testo).Groups[1].Value;
        var percorso = new Uri(link).PathAndQuery;
        var token = TokenNelTesto().Match(link).Groups[1].Value;
        using var modulo = await anonimo.GetAsync(percorso);
        Assert.Equal(HttpStatusCode.OK, modulo.StatusCode);
        Assert.Equal("no-referrer", modulo.Headers.GetValues("Referrer-Policy").Single());
        Assert.Contains("no-store", modulo.Headers.CacheControl?.ToString() ?? string.Empty, StringComparison.Ordinal);
        Assert.Contains("NuovaPassword", await TestoAsync(modulo), StringComparison.Ordinal);

        // 4. Conferma diversa e password debole sono rifiutate senza consumare il link; poi si completa.
        using var diversa = await anonimo.InviaModuloAsync(percorso, [new("Token", token), new("NuovaPassword", Nuova), new("ConfermaPassword", "altra-cosa-ancora-12")]);
        Assert.Contains("non coincide", await TestoAsync(diversa), StringComparison.Ordinal);
        using var debole = await anonimo.InviaModuloAsync(percorso, [new("Token", token), new("NuovaPassword", "corta"), new("ConfermaPassword", "corta")]);
        Assert.Contains("non è valida", await TestoAsync(debole), StringComparison.Ordinal);
        using var fatto = await anonimo.InviaModuloAsync(percorso, [new("Token", token), new("NuovaPassword", Nuova), new("ConfermaPassword", Nuova)]);
        Assert.Contains("Password cambiata", await TestoAsync(fatto), StringComparison.Ordinal);

        // 5. Effetti: nuova password valida, vecchia no, sessione precedente invalidata, link inutilizzabile, notifica inviata.
        using var accessoNuovo = await c.Web.SessioneA().AccediAsync("anna@esempio.it", Nuova);
        Assert.Equal("/admin", SessioneProva.Destinazione(accessoNuovo));
        using var accessoVecchio = await c.Web.SessioneA().AccediAsync("anna@esempio.it", DatiProva.PasswordProva);
        Assert.Contains("alert-danger", await TestoAsync(accessoVecchio), StringComparison.Ordinal);
        using var sessioneRubata = await sessioneVecchia.GetAsync("/admin");
        Assert.StartsWith("/admin/accesso", SessioneProva.Destinazione(sessioneRubata), StringComparison.Ordinal);
        using var riuso = await anonimo.GetAsync(percorso);
        Assert.Contains("Link non valido", await TestoAsync(riuso), StringComparison.Ordinal);
        Assert.Contains("cambiata", Assert.Single(await c.ElaboraEmailAsync()).Oggetto, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LeRispostePerAccountRealiEInesistentiSonoIdentiche()
    {
        await using var c = await Contesto.CreaAsync();
        await c.Web.CreaTenantAdminAsync("anna@esempio.it");
        await c.Web.CreaPlatformAdminAsync("root@esempio.it");
        var risposte = new List<(string? Destinazione, string Cookie, string Codice, string Errato)>();

        foreach (var email in new[] { "anna@esempio.it", "nessuno@esempio.it", "root@esempio.it", "non-una-email" })
        {
            var sessione = c.Web.SessioneA();
            using var richiesta = await sessione.InviaModuloAsync("/admin/recupero", [new("Email", email)]);
            var cookie = Assert.Single(SessioneProva.IntestazioniSetCookie(richiesta));
            using var pagina = await sessione.GetAsync("/admin/recupero/codice");
            using var errato = await sessione.InviaModuloAsync("/admin/recupero/codice", [new("Codice", "123456")]);
            risposte.Add((
                SessioneProva.Destinazione(richiesta),
                Regex.Replace(cookie, "=[^;]+", "=X", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2)),
                Normalizza(await TestoAsync(pagina)),
                Normalizza(await TestoAsync(errato))));
        }

        Assert.All(risposte, r => Assert.Equal(risposte[0], r));
        Assert.Single(await c.ElaboraEmailAsync());
    }

    [Fact]
    public async Task SenzaCookieLaPaginaDelCodiceRimandaAllaRichiesta()
    {
        await using var c = await Contesto.CreaAsync();

        using var risposta = await c.Web.SessioneA().GetAsync("/admin/recupero/codice");

        Assert.Equal("/admin/recupero", SessioneProva.Destinazione(risposta));
    }

    [Theory]
    [InlineData("/admin/recupero/reimposta")]
    [InlineData("/admin/recupero/reimposta?token=inventato")]
    [InlineData("/admin/recupero/reimposta?token=")]
    public async Task UnLinkInventatoMostraUnErroreGenerico(string percorso)
    {
        await using var c = await Contesto.CreaAsync();

        using var risposta = await c.Web.SessioneA().GetAsync(percorso);

        Assert.Equal(HttpStatusCode.OK, risposta.StatusCode);
        var html = await TestoAsync(risposta);
        Assert.Contains("Link non valido", html, StringComparison.Ordinal);
        Assert.DoesNotContain("NuovaPassword", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/admin/recupero")]
    [InlineData("/admin/recupero/codice")]
    [InlineData("/admin/recupero/reimposta?token=x")]
    public async Task LeScrittureSenzaTokenAntiforgeryVengonoRifiutate(string percorso)
    {
        await using var c = await Contesto.CreaAsync();

        using var risposta = await c.Web.SessioneA().InviaModuloAsync(percorso, [new("Email", "anna@esempio.it"), new("Codice", "123456"), new("Token", "x")], conToken: false);

        Assert.Equal(HttpStatusCode.BadRequest, risposta.StatusCode);
    }

    [Fact]
    public async Task LaPaginaDiAccessoHaIlLinkAlRecuperoEChiEGiaDentroVieneRimandato()
    {
        await using var c = await Contesto.CreaAsync();
        await c.Web.CreaTenantAdminAsync("anna@esempio.it");
        var sessione = c.Web.SessioneA();

        using var accesso = await sessione.GetAsync("/admin/accesso");
        Assert.Contains("href=\"/admin/recupero\"", await TestoAsync(accesso), StringComparison.Ordinal);

        await sessione.AccediAsync("anna@esempio.it", DatiProva.PasswordProva);
        using var dentro = await sessione.GetAsync("/admin/recupero");
        Assert.Equal("/admin", SessioneProva.Destinazione(dentro));
    }

    [Fact]
    public async Task LeRichiesteSonoLimitatePerIndirizzoIp()
    {
        await using var c = await Contesto.CreaAsync(richiesteAlMinuto: 3);
        var sessione = c.Web.SessioneA();
        var esiti = new List<HttpStatusCode>();

        for (var i = 0; i < 5; i++)
        {
            using var risposta = await sessione.InviaModuloAsync("/admin/recupero", [new("Email", $"x{i}@esempio.it")], conToken: false);
            esiti.Add(risposta.StatusCode);
        }

        Assert.Contains(HttpStatusCode.TooManyRequests, esiti);
    }

    /// <summary>Corpo della risposta con le entità HTML decodificate (Razor codifica i caratteri accentati).</summary>
    private static async Task<string> TestoAsync(HttpResponseMessage risposta) =>
        WebUtility.HtmlDecode(await risposta.Content.ReadAsStringAsync(Ct));

    private static string Normalizza(string html) =>
        Regex.Replace(html, "(name=\"__RequestVerificationToken\"[^>]*value=\")[^\"]+", "$1", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));

    [GeneratedRegex("codice di verifica è (\\d{6})", RegexOptions.CultureInvariant)]
    private static partial Regex CodiceNelTesto();

    [GeneratedRegex("(https?://[^\\s]+/admin/recupero/reimposta\\?token=[A-Za-z0-9_-]+)", RegexOptions.CultureInvariant)]
    private static partial Regex LinkNelTesto();

    [GeneratedRegex("token=([A-Za-z0-9_-]+)", RegexOptions.CultureInvariant)]
    private static partial Regex TokenNelTesto();

    private sealed class Contesto : IAsyncDisposable
    {
        private Contesto(ScenarioWeb web, ServerSmtpFinto server)
        {
            Web = web;
            Server = server;
        }

        public ScenarioWeb Web { get; }

        public ServerSmtpFinto Server { get; }

        public static async Task<Contesto> CreaAsync(int richiesteAlMinuto = 1000)
        {
            var web = await ScenarioWeb.CreaAsync(
                richiesteAlMinuto: richiesteAlMinuto,
                impostazioni: new Dictionary<string, string?> { ["Sicurezza:ChiaveSegreti"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) });
            var server = new ServerSmtpFinto();
            await DatiProva.ConfiguraSmtpAsync(web.Fabbrica.Services, web.A.IdTenant, server.Porta, Ct);
            return new Contesto(web, server);
        }

        /// <summary>Fa lavorare il worker (nell'applicazione è un servizio in background) e restituisce le nuove email.</summary>
        public async Task<IReadOnlyList<EmailRicevuta>> ElaboraEmailAsync()
        {
            var prima = Server.Ricevute.Count;
            while (await Web.Fabbrica.Services.GetRequiredService<ElaboratoreOutbox>().ElaboraLottoAsync(Ct) > 0)
            {
            }

            return [.. Server.Ricevute.Skip(prima)];
        }

        public async ValueTask DisposeAsync()
        {
            await Server.DisposeAsync();
            await Web.DisposeAsync();
        }
    }
}
