using System.Net;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using DvCms.Contracts.Piattaforma;
using DvCms.Data;
using DvCms.DTO.Piattaforma;
using DvCms.Infrastructure.Manutenzione;
using DvCms.Infrastructure.Outbox;
using DvCms.IntegrationTests.Supporto;
using DvCms.Models.Accessi;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DvCms.IntegrationTests.Web;

/// <summary>Registrazione e accesso degli acquirenti (F-000, ADR-0011): conferma via email, password scelta dal link, area cliente separata.</summary>
public partial class AcquirentiWebTest
{
    private const string Scelta = "una-password-scelta-da-me-1";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static List<KeyValuePair<string, string>> Registrazione(string email = "cliente@esempio.it", string nome = "Carla") =>
        [new("Email", email), new("Nome", nome)];

    [Fact]
    public async Task LeSchermateDellAreaClienteEsistonoSoloSeIlNegozioELaRegistrazioneSonoAttivi()
    {
        await using var c = await Contesto.CreaAsync();
        var anonimo = c.Web.SessioneA();

        Assert.Equal(HttpStatusCode.NotFound, (await anonimo.GetAsync("/account/accesso")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anonimo.GetAsync("/account/registrazione")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anonimo.GetAsync("/account/conferma?token=x")).StatusCode);

        // La registrazione non si può abilitare senza negozio.
        var senzaNegozio = await c.ComePlatformAsync(p => p.ImpostaRegistrazioneAcquirentiAsync(c.Platform, c.Web.A.IdTenant, c.Web.A.IdSito, true, Ct));
        Assert.Equal(CodiceEsito.NonConsentito, senzaNegozio.Codice);

        await c.AttivaNegozioAsync(c.Web.A, registrazione: false);
        Assert.Equal(HttpStatusCode.OK, (await anonimo.GetAsync("/account/accesso")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anonimo.GetAsync("/account/registrazione")).StatusCode);

        await c.AttivaNegozioAsync(c.Web.A, registrazione: true);
        Assert.Equal(HttpStatusCode.OK, (await anonimo.GetAsync("/account/registrazione")).StatusCode);

        // Altri tenant: nulla.
        Assert.Equal(HttpStatusCode.NotFound, (await c.Web.SessioneB().GetAsync("/account/registrazione")).StatusCode);
    }

    [Fact]
    public async Task RegistrazioneConfermaConPasswordScelteDalLinkEPoiAccesso()
    {
        await using var c = await Contesto.CreaAsync();
        await c.AttivaNegozioAsync(c.Web.A, registrazione: true);
        var anonimo = c.Web.SessioneA();

        using var registrazione = await anonimo.InviaModuloAsync("/account/registrazione", Registrazione());
        var htmlRegistrazione = await registrazione.Content.ReadAsStringAsync(Ct);
        Assert.Equal(HttpStatusCode.OK, registrazione.StatusCode);
        Assert.Contains("role=\"status\"", htmlRegistrazione, StringComparison.Ordinal);

        // L'account esiste solo "in attesa", con ruolo Utente nel tenant dell'host e nessuna password utilizzabile.
        var utenza = await c.UtenzaAsync("cliente@esempio.it");
        Assert.Equal((RuoloUtenza.Utente, StatoUtenza.InAttesaDiConferma, c.Web.A.IdTenant), (utenza.Ruolo, utenza.Stato, utenza.IdTenant));
        using var prematuro = await anonimo.InviaModuloAsync("/account/accesso", [new("Email", "cliente@esempio.it"), new("Password", Scelta)]);
        Assert.Contains("Credenziali non valide", await prematuro.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);

        // L'email, cifrata a riposo, ha il link sul dominio del tenant.
        var email = Assert.Single(await c.ElaboraEmailAsync());
        var link = LinkNelTesto().Match(email.Testo).Groups[1].Value;
        Assert.StartsWith($"http://{c.Web.A.Host}/account/conferma?token=", link, StringComparison.Ordinal);
        var percorso = new Uri(link).PathAndQuery;
        var token = TokenNelTesto().Match(link).Groups[1].Value;
        Assert.Equal(0, await c.ContaMessaggiAsync(m => m.Payload.Contains(token)));

        // Il link: niente referrer né cache; altrove non vale; un'anteprima (GET) non lo consuma.
        using var modulo = await anonimo.GetAsync(percorso);
        Assert.Equal("no-referrer", modulo.Headers.GetValues("Referrer-Policy").Single());
        Assert.Contains("no-store", modulo.Headers.CacheControl?.ToString() ?? string.Empty, StringComparison.Ordinal);
        Assert.Contains("ConfermaPassword", await modulo.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        using var altroHost = await c.Web.SessioneB().GetAsync(percorso);
        Assert.Equal(HttpStatusCode.NotFound, altroHost.StatusCode);

        // Conferma diversa e password debole non consumano il link; poi si completa.
        using var diversa = await anonimo.InviaModuloAsync(percorso, [new("Token", token), new("Password", Scelta), new("ConfermaPassword", "un-altra-cosa-12345")]);
        Assert.Contains("non coincide", await diversa.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        using var debole = await anonimo.InviaModuloAsync(percorso, [new("Token", token), new("Password", "corta"), new("ConfermaPassword", "corta")]);
        Assert.Contains("non è valida", await Testo(debole), StringComparison.Ordinal);
        using var fatto = await anonimo.InviaModuloAsync(percorso, [new("Token", token), new("Password", Scelta), new("ConfermaPassword", Scelta)]);
        Assert.Contains("Account attivato", await fatto.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        Assert.Equal(StatoUtenza.Attiva, (await c.UtenzaAsync("cliente@esempio.it")).Stato);

        // Il link non si riusa; con la password scelta si accede all'area cliente.
        // (la pagina del link, ormai senza modulo, non fornisce il token antiforgery: lo si prende da un'altra pagina con un modulo)
        using var riuso = await anonimo.InviaModuloAsync("/account/accesso", [new("Token", token), new("Password", "un-altra-password-12345"), new("ConfermaPassword", "un-altra-password-12345")], percorso);
        Assert.Contains("non è valido", await Testo(riuso), StringComparison.Ordinal);
        var cliente = c.Web.SessioneA();
        using var accesso = await cliente.InviaModuloAsync("/account/accesso", [new("Email", "cliente@esempio.it"), new("Password", Scelta)]);
        Assert.True(SessioneProva.EReindirizzamento(accesso), await accesso.Content.ReadAsStringAsync(Ct));
        Assert.Equal("/account", SessioneProva.Destinazione(accesso));
        using var area = await cliente.GetAsync("/account");
        Assert.Equal(HttpStatusCode.OK, area.StatusCode);
        Assert.Contains("Carla", await area.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        Assert.NotNull(cliente.CookieCliente);
        Assert.Null(cliente.CookieAdmin);

        // Il cookie del cliente non apre l'admin; l'uscita lo cancella.
        using var admin = await cliente.GetAsync("/admin");
        Assert.NotEqual(HttpStatusCode.OK, admin.StatusCode);
        using var esce = await cliente.InviaModuloAsync("/account", [], "/account/esci");
        Assert.True(SessioneProva.EReindirizzamento(esce), $"{esce.StatusCode}");
        using var dopo = await cliente.GetAsync("/account");
        Assert.NotEqual(HttpStatusCode.OK, dopo.StatusCode);
    }

    [Fact]
    public async Task LaRispostaDellaRegistrazioneNonRivelaSeLIndirizzoEsiste()
    {
        await using var c = await Contesto.CreaAsync();
        await c.AttivaNegozioAsync(c.Web.A, registrazione: true);
        var tenantAdmin = await c.Web.CreaTenantAdminAsync("anna@esempio.it");
        var attivo = await DatiProva.CreaUtenzaAsync(c.Web.Fabbrica.Services, RuoloUtenza.Utente, c.Web.A.IdTenant, "attivo@esempio.it", annullamento: Ct);
        var anonimo = c.Web.SessioneA();

        async Task<string> Invia(string email)
        {
            using var risposta = await anonimo.InviaModuloAsync("/account/registrazione", Registrazione(email));
            Assert.Equal(HttpStatusCode.OK, risposta.StatusCode);
            return Regex.Replace(await risposta.Content.ReadAsStringAsync(Ct), "__RequestVerificationToken\" type=\"hidden\" value=\"[^\"]+\"", string.Empty);
        }

        var nuovo = await Invia("nuovo@esempio.it");
        var comeAmministratore = await Invia("anna@esempio.it");
        var comeAttivo = await Invia("ATTIVO@esempio.it");

        Assert.Equal(nuovo, comeAmministratore);
        Assert.Equal(nuovo, comeAttivo);
        // Un'email sola, solo per il nuovo indirizzo; nessun account in più per gli altri, ruoli e password invariati.
        Assert.Single(await c.ElaboraEmailAsync());
        Assert.Equal(0, await c.ContaUtenzeAsync("anna@esempio.it", RuoloUtenza.Utente));
        Assert.Equal((RuoloUtenza.TenantAdmin, StatoUtenza.Attiva), ((await c.UtenzaAsync("anna@esempio.it")).Ruolo, (await c.UtenzaAsync("anna@esempio.it")).Stato));
        Assert.True(tenantAdmin != Guid.Empty && attivo != Guid.Empty);
    }

    [Fact]
    public async Task UnIndirizzoInAttesaRiceveAlPiuTreLinkOrariENessunAltroPuoPrendereloConUnaPasswordSua()
    {
        await using var c = await Contesto.CreaAsync();
        await c.AttivaNegozioAsync(c.Web.A, registrazione: true);
        var anonimo = c.Web.SessioneA();

        for (var i = 0; i < 5; i++)
        {
            using var risposta = await anonimo.InviaModuloAsync("/account/registrazione", Registrazione("vittima@esempio.it", $"Tentativo {i}"));
            Assert.Equal(HttpStatusCode.OK, risposta.StatusCode);
        }

        Assert.Equal(3, (await c.ElaboraEmailAsync()).Count);
        Assert.Equal(1, await c.ContaUtenzeAsync("vittima@esempio.it", RuoloUtenza.Utente));
        // Il nome del primo tentativo resta: chi riscrive l'indirizzo non cambia l'account in attesa.
        Assert.Equal("Tentativo 0", (await c.UtenzaAsync("vittima@esempio.it")).NomeVisualizzato);
        // Solo l'ultimo link è valido (gli altri sono annullati).
        Assert.Equal(1, await c.ContaConfermeAsync(StatoConfermaEmail.Inviata));
    }

    [Theory]
    [InlineData("non-un-indirizzo", "Carla")]
    [InlineData("a@esempio.it", "")]
    [InlineData("a@esempio.it", "   ")]
    public async Task DatiNonValidiNonCreanoNullaESonoSegnalati(string email, string nome)
    {
        await using var c = await Contesto.CreaAsync();
        await c.AttivaNegozioAsync(c.Web.A, registrazione: true);

        using var risposta = await c.Web.SessioneA().InviaModuloAsync("/account/registrazione", Registrazione(email, nome));

        Assert.Contains("role=\"alert\"", await risposta.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        Assert.Equal(0, await c.ContaUtenzeAsync("a@esempio.it", RuoloUtenza.Utente));
        Assert.Empty(await c.ElaboraEmailAsync());
    }

    [Fact]
    public async Task SenzaCanaleEmailLaRegistrazioneNonEDisponibileENonCreaAccount()
    {
        await using var c = await Contesto.CreaAsync();
        await c.AttivaNegozioAsync(c.Web.B, registrazione: true);
        var anonimo = c.Web.SessioneB();

        using var pagina = await anonimo.GetAsync("/account/registrazione");
        using var invio = await anonimo.InviaModuloAsync("/account/accesso", Registrazione(), "/account/registrazione");

        Assert.Contains("non è al momento disponibile", await Testo(pagina), StringComparison.Ordinal);
        Assert.Contains("non è al momento disponibile", await Testo(invio), StringComparison.Ordinal);
        Assert.Equal(0, await c.ContaUtenzeAsync("cliente@esempio.it", RuoloUtenza.Utente));
    }

    [Fact]
    public async Task SenzaLaChiaveDeiSegretiLaRegistrazioneNonEDisponibileENonCreaAccountOrfani()
    {
        // Senza Sicurezza:ChiaveSegreti l'email con il link non si può accodare (è riservata): nessun account in attesa che nessuno può confermare.
        await using var web = await ScenarioWeb.CreaAsync();
        await using var server = new ServerSmtpFinto();
        await DatiProva.ConfiguraSmtpAsync(web.Fabbrica.Services, web.A.IdTenant, server.Porta, Ct);
        var platform = await web.CreaPlatformAdminAsync("platform@esempio.it");
        await using (var ambito = web.Fabbrica.Services.CreateAsyncScope())
        {
            var piattaforma = ambito.ServiceProvider.GetRequiredService<IServizioPiattaforma>();
            Assert.True((await piattaforma.ImpostaCommercioAsync(platform, web.A.IdTenant, web.A.IdSito, true, Ct)).Riuscito);
            Assert.True((await piattaforma.ImpostaRegistrazioneAcquirentiAsync(platform, web.A.IdTenant, web.A.IdSito, true, Ct)).Riuscito);
        }

        using var risposta = await web.SessioneA().InviaModuloAsync("/account/registrazione", Registrazione());

        Assert.Equal(HttpStatusCode.OK, risposta.StatusCode);
        Assert.Contains("non è al momento disponibile", WebUtility.HtmlDecode(await risposta.Content.ReadAsStringAsync(Ct)), StringComparison.Ordinal);
        await using var verifica = web.Fabbrica.Services.CreateAsyncScope();
        Assert.Equal(0, await verifica.ServiceProvider.GetRequiredService<ContestoDatabase>().Utenze.IgnoreQueryFilters().CountAsync(u => u.Ruolo == RuoloUtenza.Utente, Ct));
    }

    [Fact]
    public async Task GliAmministratoriNonEntranoDallAreaClienteEUnUtenteNonEntraNellAdmin()
    {
        await using var c = await Contesto.CreaAsync();
        await c.AttivaNegozioAsync(c.Web.A, registrazione: true);
        await c.Web.CreaTenantAdminAsync("anna@esempio.it");
        await DatiProva.CreaUtenzaAsync(c.Web.Fabbrica.Services, RuoloUtenza.Utente, c.Web.A.IdTenant, "cliente@esempio.it", annullamento: Ct);

        var comeAdmin = c.Web.SessioneA();
        using var rifiutato = await comeAdmin.InviaModuloAsync("/account/accesso", [new("Email", "anna@esempio.it"), new("Password", DatiProva.PasswordProva)]);
        Assert.Equal(HttpStatusCode.OK, rifiutato.StatusCode);
        Assert.Contains("Credenziali non valide", await rifiutato.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        Assert.Null(comeAdmin.CookieCliente);

        // L'amministratore può comunque entrare nell'admin (nessun blocco, nessun contatore toccato).
        var admin = c.Web.SessioneA();
        await admin.AccediAsync("anna@esempio.it", DatiProva.PasswordProva);
        using var pannello = await admin.GetAsync("/admin");
        Assert.Equal(HttpStatusCode.OK, pannello.StatusCode);
        Assert.Equal(0, (await c.UtenzaAsync("anna@esempio.it", RuoloUtenza.TenantAdmin)).ContatoreAccessiFalliti);

        // Un acquirente che prova l'accesso admin non ottiene alcuna pagina dell'admin.
        var acquirente = c.Web.SessioneA();
        await acquirente.AccediAsync("cliente@esempio.it", DatiProva.PasswordProva);
        using var vietata = await acquirente.GetAsync("/admin");
        Assert.NotEqual(HttpStatusCode.OK, vietata.StatusCode);
    }

    [Fact]
    public async Task LaSessioneDelClienteNonValeSuUnAltroTenantNeDopoIlCambioDiTimbro()
    {
        await using var c = await Contesto.CreaAsync();
        await c.AttivaNegozioAsync(c.Web.A, registrazione: true);
        await c.AttivaNegozioAsync(c.Web.B, registrazione: true);
        await DatiProva.CreaUtenzaAsync(c.Web.Fabbrica.Services, RuoloUtenza.Utente, c.Web.A.IdTenant, "cliente@esempio.it", annullamento: Ct);
        var cliente = c.Web.SessioneA();
        using var accesso = await cliente.InviaModuloAsync("/account/accesso", [new("Email", "cliente@esempio.it"), new("Password", DatiProva.PasswordProva)]);
        Assert.True(SessioneProva.EReindirizzamento(accesso));

        // Lo stesso cookie presentato all'host di un altro tenant non apre nulla.
        var altroHost = c.Web.SessioneB();
        altroHost.ImpostaCookie(".DvCms.Cliente", cliente.CookieCliente!);
        using var daB = await altroHost.GetAsync("/account");
        Assert.NotEqual(HttpStatusCode.OK, daB.StatusCode);

        // Un cambio di credenziali (nuovo timbro) o la disabilitazione chiudono la sessione.
        await c.ImpostaStatoAsync("cliente@esempio.it", StatoUtenza.Disabilitata);
        using var disabilitato = await cliente.GetAsync("/account");
        Assert.NotEqual(HttpStatusCode.OK, disabilitato.StatusCode);
    }

    [Fact]
    public async Task LeRegistrazioniMaiConfermateSiRipulisconoDopoQualcheGiornoSenzaToccareGliAccountAttivi()
    {
        await using var c = await Contesto.CreaAsync();
        await c.AttivaNegozioAsync(c.Web.A, registrazione: true);
        await DatiProva.CreaUtenzaAsync(c.Web.Fabbrica.Services, RuoloUtenza.Utente, c.Web.A.IdTenant, "attivo@esempio.it", annullamento: Ct);
        await c.Web.SessioneA().InviaModuloAsync("/account/registrazione", Registrazione("pendente@esempio.it"));
        await c.Web.SessioneA().InviaModuloAsync("/account/registrazione", Registrazione("recente@esempio.it"));
        await c.InvecchiaAsync("pendente@esempio.it", TimeSpan.FromDays(8));
        await c.InvecchiaAsync("attivo@esempio.it", TimeSpan.FromDays(60));

        await using var ambito = c.Web.Fabbrica.Services.CreateAsyncScope();
        await ambito.ServiceProvider.GetRequiredService<ManutenzioneDati>().EseguiAsync(Ct);

        Assert.Equal(0, await c.ContaUtenzeAsync("pendente@esempio.it", RuoloUtenza.Utente));
        Assert.Equal(1, await c.ContaUtenzeAsync("recente@esempio.it", RuoloUtenza.Utente));
        Assert.Equal(1, await c.ContaUtenzeAsync("attivo@esempio.it", RuoloUtenza.Utente));
        Assert.Equal(1, await c.ContaConfermeAsync(StatoConfermaEmail.Inviata));
    }

    [GeneratedRegex("(https?://[^\\s]+/account/conferma\\?token=[A-Za-z0-9_-]+)", RegexOptions.CultureInvariant)]
    private static partial Regex LinkNelTesto();

    [GeneratedRegex("token=([A-Za-z0-9_-]+)", RegexOptions.CultureInvariant)]
    private static partial Regex TokenNelTesto();

    private static async Task<string> Testo(HttpResponseMessage risposta) => WebUtility.HtmlDecode(await risposta.Content.ReadAsStringAsync(Ct));

    private static string Normalizza(string email) => email.ToLowerInvariant();

    private sealed class Contesto : IAsyncDisposable
    {
        private Contesto(ScenarioWeb web, ServerSmtpFinto server, Guid platform)
        {
            Web = web;
            Server = server;
            Platform = platform;
        }

        public ScenarioWeb Web { get; }

        public ServerSmtpFinto Server { get; }

        public Guid Platform { get; }

        public static async Task<Contesto> CreaAsync()
        {
            var web = await ScenarioWeb.CreaAsync(
                impostazioni: new Dictionary<string, string?> { ["Sicurezza:ChiaveSegreti"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) });
            var server = new ServerSmtpFinto();
            await DatiProva.ConfiguraSmtpAsync(web.Fabbrica.Services, web.A.IdTenant, server.Porta, Ct);
            var platform = await web.CreaPlatformAdminAsync("platform@esempio.it");
            return new Contesto(web, server, platform);
        }

        public async Task<T> ComePlatformAsync<T>(Func<IServizioPiattaforma, Task<T>> operazione)
        {
            await using var ambito = Web.Fabbrica.Services.CreateAsyncScope();
            return await operazione(ambito.ServiceProvider.GetRequiredService<IServizioPiattaforma>());
        }

        public async Task AttivaNegozioAsync(DatiProva.TenantCreato tenant, bool registrazione)
        {
            var commercio = await ComePlatformAsync(p => p.ImpostaCommercioAsync(Platform, tenant.IdTenant, tenant.IdSito, true, Ct));
            Assert.True(commercio.Riuscito, commercio.Codice.ToString());
            var esito = await ComePlatformAsync(p => p.ImpostaRegistrazioneAcquirentiAsync(Platform, tenant.IdTenant, tenant.IdSito, registrazione, Ct));
            Assert.True(esito.Riuscito, esito.Codice.ToString());
        }

        public async Task<IReadOnlyList<EmailRicevuta>> ElaboraEmailAsync()
        {
            var prima = Server.Ricevute.Count;
            while (await Web.Fabbrica.Services.GetRequiredService<ElaboratoreOutbox>().ElaboraLottoAsync(Ct) > 0)
            {
            }

            return [.. Server.Ricevute.Skip(prima)];
        }

        public async Task<int> ContaMessaggiAsync(System.Linq.Expressions.Expression<Func<DvCms.Models.Outbox.MessaggioOutbox, bool>> condizione)
        {
            await using var ambito = Web.Fabbrica.Services.CreateAsyncScope();
            return await ambito.ServiceProvider.GetRequiredService<ContestoDatabase>().MessaggiOutbox.IgnoreQueryFilters().CountAsync(condizione, Ct);
        }

        public async Task<int> ContaUtenzeAsync(string email, RuoloUtenza ruolo)
        {
            await using var ambito = Web.Fabbrica.Services.CreateAsyncScope();
            return await ambito.ServiceProvider.GetRequiredService<ContestoDatabase>().Utenze.IgnoreQueryFilters().CountAsync(u => u.EmailNormalizzata == AcquirentiWebTest.Normalizza(email) && u.Ruolo == ruolo, Ct);
        }

        public async Task<int> ContaConfermeAsync(StatoConfermaEmail stato)
        {
            await using var ambito = Web.Fabbrica.Services.CreateAsyncScope();
            return await ambito.ServiceProvider.GetRequiredService<ContestoDatabase>().ConfermeEmailUtenza.IgnoreQueryFilters().CountAsync(x => x.Stato == stato, Ct);
        }

        public async Task<Utenza> UtenzaAsync(string email, RuoloUtenza? ruolo = null)
        {
            await using var ambito = Web.Fabbrica.Services.CreateAsyncScope();
            return await ambito.ServiceProvider.GetRequiredService<ContestoDatabase>().Utenze.IgnoreQueryFilters().AsNoTracking()
                .Where(u => u.EmailNormalizzata == AcquirentiWebTest.Normalizza(email) && (ruolo == null || u.Ruolo == ruolo))
                .SingleAsync(Ct);
        }

        public async Task ImpostaStatoAsync(string email, StatoUtenza stato)
        {
            await using var ambito = Web.Fabbrica.Services.CreateAsyncScope();
            await ambito.ServiceProvider.GetRequiredService<ContestoDatabase>().Utenze.IgnoreQueryFilters()
                .Where(u => u.EmailNormalizzata == AcquirentiWebTest.Normalizza(email))
                .ExecuteUpdateAsync(x => x.SetProperty(u => u.Stato, stato).SetProperty(u => u.TimbroSicurezza, Guid.NewGuid()), Ct);
        }

        public async Task InvecchiaAsync(string email, TimeSpan eta)
        {
            await using var ambito = Web.Fabbrica.Services.CreateAsyncScope();
            var data = DateTime.UtcNow - eta;
            await ambito.ServiceProvider.GetRequiredService<ContestoDatabase>().Utenze.IgnoreQueryFilters()
                .Where(u => u.EmailNormalizzata == AcquirentiWebTest.Normalizza(email))
                .ExecuteUpdateAsync(x => x.SetProperty(u => u.CreatoIlUtc, data), Ct);
        }

        public async ValueTask DisposeAsync()
        {
            await Server.DisposeAsync();
            await Web.DisposeAsync();
        }
    }
}
