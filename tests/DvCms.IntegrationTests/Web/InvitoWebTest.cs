using System.Net;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using DvCms.Contracts.Piattaforma;
using DvCms.Data;
using DvCms.DTO.Piattaforma;
using DvCms.Infrastructure.Outbox;
using DvCms.IntegrationTests.Supporto;
using DvCms.Models.Accessi;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DvCms.IntegrationTests.Web;

/// <summary>Invito via email dei nuovi TenantAdmin (M1b): nessuna password comunicata, link monouso sul dominio del tenant.</summary>
public partial class InvitoWebTest
{
    private const string Scelta = "una-password-scelta-da-me-1";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task CreazioneConInvitoLinkMonousoEPasswordScelta()
    {
        await using var c = await Contesto.CreaAsync();
        var platform = await c.AccediComePlatformAsync();
        var dettaglio = $"/admin/platform/tenant/{c.Web.A.IdTenant}";

        using var creazione = await platform.InviaModuloAsync(
            dettaglio,
            [new("email", "nuova@esempio.it"), new("nomeVisualizzato", "Nuova"), new("conInvito", "true")],
            $"{dettaglio}?handler=NuovoAmministratore");

        // Nessuna password a schermo: si torna al dettaglio con un messaggio.
        Assert.True(SessioneProva.EReindirizzamento(creazione));
        using var pagina = await platform.GetAsync(dettaglio);
        var html = await pagina.Content.ReadAsStringAsync(Ct);
        Assert.DoesNotContain("password-temporanea", html, StringComparison.Ordinal);
        Assert.Contains("invito", html, StringComparison.OrdinalIgnoreCase);

        // L'email parte dal worker, cifrata a riposo, con il link sul dominio del tenant (non su quello della piattaforma).
        var email = Assert.Single(await c.ElaboraEmailAsync());
        Assert.Contains("invitato", email.Testo, StringComparison.Ordinal);
        var link = LinkNelTesto().Match(email.Testo).Groups[1].Value;
        Assert.StartsWith($"http://{c.Web.A.Host}/admin/invito?token=", link, StringComparison.Ordinal);
        var percorso = new Uri(link).PathAndQuery;
        var token = TokenNelTesto().Match(link).Groups[1].Value;
        Assert.Equal(0, await c.ContaAsync(m => m.Payload.Contains(token)));

        // Pagina del link: nessuna perdita del token, niente cache; sull'host di un altro tenant il link non vale.
        var anonimo = c.Web.SessioneA();
        using var modulo = await anonimo.GetAsync(percorso);
        Assert.Equal(HttpStatusCode.OK, modulo.StatusCode);
        Assert.Equal("no-referrer", modulo.Headers.GetValues("Referrer-Policy").Single());
        Assert.Contains("no-store", modulo.Headers.CacheControl?.ToString() ?? string.Empty, StringComparison.Ordinal);
        Assert.Contains("NuovaPassword", await modulo.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        using var altroTenant = await c.Web.SessioneB().GetAsync(percorso);
        Assert.Contains("Invito non valido", await altroTenant.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);

        // Conferma diversa e password debole non consumano il link; poi si completa.
        using var diversa = await anonimo.InviaModuloAsync(percorso, [new("Token", token), new("NuovaPassword", Scelta), new("ConfermaPassword", "un-altra-cosa-12345")]);
        Assert.Contains("non coincide", await diversa.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        using var debole = await anonimo.InviaModuloAsync(percorso, [new("Token", token), new("NuovaPassword", "corta"), new("ConfermaPassword", "corta")]);
        Assert.Contains("non è valida", WebUtility.HtmlDecode(await debole.Content.ReadAsStringAsync(Ct)), StringComparison.Ordinal);
        using var fatto = await anonimo.InviaModuloAsync(percorso, [new("Token", token), new("NuovaPassword", Scelta), new("ConfermaPassword", Scelta)]);
        Assert.Contains("Account attivato", await fatto.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);

        // Accede subito alla dashboard, senza cambio password forzato; il link non si riusa.
        using var accesso = await c.Web.SessioneA().AccediAsync("nuova@esempio.it", Scelta);
        Assert.Equal("/admin", SessioneProva.Destinazione(accesso));
        using var riuso = await c.Web.SessioneA().GetAsync(percorso);
        Assert.Contains("Invito non valido", await riuso.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task SenzaCanaleEmailLInvitoERifiutatoENonCreaAccount()
    {
        await using var c = await Contesto.CreaAsync();
        var platform = await c.AccediComePlatformAsync();
        var dettaglio = $"/admin/platform/tenant/{c.Web.B.IdTenant}";

        using var creazione = await platform.InviaModuloAsync(
            dettaglio,
            [new("email", "nuova@esempio.it"), new("nomeVisualizzato", "Nuova"), new("conInvito", "true")],
            $"{dettaglio}?handler=NuovoAmministratore");
        using var pagina = await platform.GetAsync(dettaglio);

        Assert.True(SessioneProva.EReindirizzamento(creazione));
        Assert.Contains("canale email", await pagina.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        Assert.Equal(0, await c.ContaUtenzeAsync("nuova@esempio.it"));
        Assert.Empty(await c.ElaboraEmailAsync());
    }

    [Fact]
    public async Task IlReinvioAnnullaIlLinkPrecedenteESoloPerChiNonHaAncoraUnaPassword()
    {
        await using var c = await Contesto.CreaAsync();
        var platform = await c.AccediComePlatformAsync();
        var dettaglio = $"/admin/platform/tenant/{c.Web.A.IdTenant}";
        await platform.InviaModuloAsync(
            dettaglio,
            [new("email", "nuova@esempio.it"), new("nomeVisualizzato", "Nuova"), new("conInvito", "true")],
            $"{dettaglio}?handler=NuovoAmministratore");
        var primo = LinkNelTesto().Match(Assert.Single(await c.ElaboraEmailAsync()).Testo).Groups[1].Value;
        var idUtenza = await c.IdUtenzaAsync("nuova@esempio.it");

        using var reinvio = await platform.InviaModuloAsync(dettaglio, [new("idUtenza", idUtenza.ToString())], $"{dettaglio}?handler=InviaInvito");
        var secondo = LinkNelTesto().Match(Assert.Single(await c.ElaboraEmailAsync()).Testo).Groups[1].Value;

        Assert.True(SessioneProva.EReindirizzamento(reinvio));
        Assert.NotEqual(primo, secondo);
        using var vecchio = await c.Web.SessioneA().GetAsync(new Uri(primo).PathAndQuery);
        using var nuovo = await c.Web.SessioneA().GetAsync(new Uri(secondo).PathAndQuery);
        Assert.Contains("Invito non valido", await vecchio.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        Assert.Contains("NuovaPassword", await nuovo.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);

        // Dopo aver scelto la password l'invito non si può più reinviare (la via è il recupero password).
        var token = TokenNelTesto().Match(secondo).Groups[1].Value;
        await c.Web.SessioneA().InviaModuloAsync(new Uri(secondo).PathAndQuery, [new("Token", token), new("NuovaPassword", Scelta), new("ConfermaPassword", Scelta)]);
        using var dopo = await platform.InviaModuloAsync(dettaglio, [new("idUtenza", idUtenza.ToString())], $"{dettaglio}?handler=InviaInvito");
        using var pagina = await platform.GetAsync(dettaglio);
        Assert.True(SessioneProva.EReindirizzamento(dopo));
        Assert.Contains("non è consentita", WebUtility.HtmlDecode(await pagina.Content.ReadAsStringAsync(Ct)), StringComparison.Ordinal);
        Assert.Empty(await c.ElaboraEmailAsync());
    }

    [Fact]
    public async Task UnInvitoScadutoNonSiAccetta()
    {
        await using var c = await Contesto.CreaAsync();
        var platform = await c.AccediComePlatformAsync();
        var dettaglio = $"/admin/platform/tenant/{c.Web.A.IdTenant}";
        await platform.InviaModuloAsync(
            dettaglio,
            [new("email", "nuova@esempio.it"), new("nomeVisualizzato", "Nuova"), new("conInvito", "true")],
            $"{dettaglio}?handler=NuovoAmministratore");
        var link = LinkNelTesto().Match(Assert.Single(await c.ElaboraEmailAsync()).Testo).Groups[1].Value;

        await using (var ambito = c.Web.Fabbrica.Services.CreateAsyncScope())
        {
            await ambito.ServiceProvider.GetRequiredService<ContestoDatabase>().InvitiAccount.IgnoreQueryFilters()
                .ExecuteUpdateAsync(setters => setters.SetProperty(i => i.ScadeIlUtc, DateTime.UtcNow.AddMinutes(-1)), Ct);
        }

        using var pagina = await c.Web.SessioneA().GetAsync(new Uri(link).PathAndQuery);
        Assert.Contains("Invito non valido", await pagina.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task SoloUnPlatformAdminPuoInvitare()
    {
        await using var c = await Contesto.CreaAsync();
        var admin = await c.Web.CreaTenantAdminAsync("anna@esempio.it");
        await using var ambito = c.Web.Fabbrica.Services.CreateAsyncScope();
        var servizio = ambito.ServiceProvider.GetRequiredService<IServizioPiattaforma>();

        var creazione = await servizio.CreaTenantAdminConInvitoAsync(admin, c.Web.A.IdTenant, "x@esempio.it", "X", new DatiLinkInvito("https", null), Ct);
        var reinvio = await servizio.InviaInvitoAsync(admin, c.Web.A.IdTenant, admin, new DatiLinkInvito("https", null), Ct);

        Assert.Equal(CodiceEsito.NonAutorizzato, creazione.Codice);
        Assert.Equal(CodiceEsito.NonAutorizzato, reinvio.Codice);
        Assert.Equal(0, await c.ContaUtenzeAsync("x@esempio.it"));
    }

    [GeneratedRegex("(https?://[^\\s]+/admin/invito\\?token=[A-Za-z0-9_-]+)", RegexOptions.CultureInvariant)]
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

        public static async Task<Contesto> CreaAsync()
        {
            var web = await ScenarioWeb.CreaAsync(
                impostazioni: new Dictionary<string, string?> { ["Sicurezza:ChiaveSegreti"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) });
            var server = new ServerSmtpFinto();
            await DatiProva.ConfiguraSmtpAsync(web.Fabbrica.Services, web.A.IdTenant, server.Porta, Ct);
            await web.CreaPlatformAdminAsync("platform@esempio.it");
            return new Contesto(web, server);
        }

        /// <summary>Il PlatformAdmin lavora dall'host del tenant B (che non ha il canale email): il link deve comunque usare il dominio del tenant invitato.</summary>
        public async Task<SessioneProva> AccediComePlatformAsync()
        {
            var sessione = Web.SessioneB();
            await sessione.AccediAsync("platform@esempio.it", DatiProva.PasswordProva);
            return sessione;
        }

        public async Task<IReadOnlyList<EmailRicevuta>> ElaboraEmailAsync()
        {
            var prima = Server.Ricevute.Count;
            while (await Web.Fabbrica.Services.GetRequiredService<ElaboratoreOutbox>().ElaboraLottoAsync(Ct) > 0)
            {
            }

            return [.. Server.Ricevute.Skip(prima)];
        }

        public async Task<int> ContaAsync(System.Linq.Expressions.Expression<Func<DvCms.Models.Outbox.MessaggioOutbox, bool>> condizione)
        {
            await using var ambito = Web.Fabbrica.Services.CreateAsyncScope();
            return await ambito.ServiceProvider.GetRequiredService<ContestoDatabase>().MessaggiOutbox.IgnoreQueryFilters().CountAsync(condizione, Ct);
        }

        public async Task<int> ContaUtenzeAsync(string email)
        {
            await using var ambito = Web.Fabbrica.Services.CreateAsyncScope();
            return await ambito.ServiceProvider.GetRequiredService<ContestoDatabase>().Utenze.IgnoreQueryFilters().CountAsync(u => u.EmailNormalizzata == email, Ct);
        }

        public async Task<Guid> IdUtenzaAsync(string email)
        {
            await using var ambito = Web.Fabbrica.Services.CreateAsyncScope();
            return await ambito.ServiceProvider.GetRequiredService<ContestoDatabase>().Utenze.IgnoreQueryFilters().Where(u => u.EmailNormalizzata == email).Select(u => u.Id).SingleAsync(Ct);
        }

        public async ValueTask DisposeAsync()
        {
            await Server.DisposeAsync();
            await Web.DisposeAsync();
        }
    }
}
