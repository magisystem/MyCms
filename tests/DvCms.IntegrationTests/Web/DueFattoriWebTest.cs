using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using DvCms.Application.Accessi;
using DvCms.Application.Accessi.DueFattori;
using DvCms.Application.Email;
using DvCms.Application.Piattaforma;
using DvCms.Domain.Accessi;
using DvCms.Infrastructure.Persistenza;
using DvCms.IntegrationTests.Supporto;
using DvCms.Web.Comandi;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DvCms.IntegrationTests.Web;

/// <summary>Autenticazione a due fattori (G-006, ADR-0019): accesso in due passi, anti-replay, blocco, recupero, obbligo e reimpostazione.</summary>
public partial class DueFattoriWebTest
{
    private const string EmailAnna = "anna@esempio.it";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task LaPasswordDaSolaNonApreLaSessioneEIlCodiceGiustoSi()
    {
        await using var c = await Contesto.CreaAsync();
        var id = await c.Web.CreaTenantAdminAsync(EmailAnna);
        var attivazione = await c.AttivaAsync(id);
        var sessione = c.Web.SessioneA();

        using var password = await sessione.AccediAsync(EmailAnna, DatiProva.PasswordProva);

        Assert.Equal("/admin/accesso/verifica", SessioneProva.Destinazione(password));
        Assert.Null(sessione.CookieAdmin);
        var cookie = SessioneProva.IntestazioniSetCookie(password).Single(h => h.StartsWith(".DvCms.SecondoFattore=", StringComparison.Ordinal));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/admin", cookie, StringComparison.OrdinalIgnoreCase);
        using var senzaSessione = await sessione.GetAsync("/admin");
        Assert.True(SessioneProva.EReindirizzamento(senzaSessione));
        Assert.Contains("/admin/accesso", SessioneProva.Destinazione(senzaSessione), StringComparison.Ordinal);

        using var sbagliato = await Verifica(sessione, "000000");
        Assert.Equal(HttpStatusCode.OK, sbagliato.StatusCode);
        Assert.Contains("Codice non valido", await sbagliato.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        Assert.Null(sessione.CookieAdmin);

        using var giusto = await Verifica(sessione, Codice(attivazione.Segreto));
        Assert.Equal("/admin", SessioneProva.Destinazione(giusto));
        Assert.NotNull(sessione.CookieAdmin);
        Assert.False(sessione.Cookie.ContainsKey(".DvCms.SecondoFattore"));
        using var dashboard = await sessione.GetAsync("/admin");
        Assert.Equal(HttpStatusCode.OK, dashboard.StatusCode);
    }

    [Fact]
    public async Task LaDestinazioneSiConservaSoloSeLocaleENellAreaAdmin()
    {
        await using var c = await Contesto.CreaAsync();
        var id = await c.Web.CreaTenantAdminAsync(EmailAnna);
        var attivazione = await c.AttivaAsync(id);

        var locale = c.Web.SessioneA();
        await locale.InviaModuloAsync("/admin/accesso", [new("Email", EmailAnna), new("Password", DatiProva.PasswordProva), new("ReturnUrl", "/admin/media")]);
        using var conDestinazione = await Verifica(locale, Codice(attivazione.Segreto));
        Assert.Equal("/admin/media", SessioneProva.Destinazione(conDestinazione));

        await c.LiberaPassoAsync(id);
        var esterna = c.Web.SessioneA();
        await esterna.InviaModuloAsync("/admin/accesso", [new("Email", EmailAnna), new("Password", DatiProva.PasswordProva), new("ReturnUrl", "https://cattivo.example/")]);
        using var senzaDestinazione = await Verifica(esterna, Codice(attivazione.Segreto));
        Assert.Equal("/admin", SessioneProva.Destinazione(senzaDestinazione));
    }

    [Fact]
    public async Task UnCodiceGiaUsatoNonVale()
    {
        await using var c = await Contesto.CreaAsync();
        var id = await c.Web.CreaTenantAdminAsync(EmailAnna);
        var attivazione = await c.AttivaAsync(id);

        var prima = c.Web.SessioneA();
        await prima.AccediAsync(EmailAnna, DatiProva.PasswordProva);
        using var riuscito = await Verifica(prima, Codice(attivazione.Segreto));
        Assert.Equal("/admin", SessioneProva.Destinazione(riuscito));

        var seconda = c.Web.SessioneA();
        await seconda.AccediAsync(EmailAnna, DatiProva.PasswordProva);
        using var riuso = await Verifica(seconda, Codice(attivazione.Segreto));
        Assert.Equal(HttpStatusCode.OK, riuso.StatusCode);
        Assert.Null(seconda.CookieAdmin);

        // Il passo successivo, invece, vale (finestra di tolleranza).
        using var successivo = await Verifica(seconda, Codice(attivazione.Segreto, passiAvanti: 1));
        Assert.Equal("/admin", SessioneProva.Destinazione(successivo));
    }

    [Fact]
    public async Task IlSecondoFattoreSbagliatoConsumaGliStessiTentativiDellaPasswordFinoAlBlocco()
    {
        await using var c = await Contesto.CreaAsync();
        var id = await c.Web.CreaTenantAdminAsync(EmailAnna);
        var attivazione = await c.AttivaAsync(id);
        var sessione = c.Web.SessioneA();
        await sessione.AccediAsync(EmailAnna, DatiProva.PasswordProva);

        for (var i = 0; i < OpzioniSicurezza.TentativiMassimiPredefiniti; i++)
        {
            using var errato = await Verifica(sessione, "000000");
            Assert.Equal(HttpStatusCode.OK, errato.StatusCode);
        }

        Assert.NotNull((await c.LeggiAsync(id)).BloccatoFinoAlUtc);
        using var dopoBlocco = await Verifica(sessione, Codice(attivazione.Segreto));
        Assert.Null(sessione.CookieAdmin);
        Assert.Contains("Codice non valido", await dopoBlocco.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task LaPasswordCorrettaNonAzzeraIlContatoreFinoAlSecondoFattoreVerificato()
    {
        await using var c = await Contesto.CreaAsync();
        var id = await c.Web.CreaTenantAdminAsync(EmailAnna);
        var attivazione = await c.AttivaAsync(id);
        await c.ImpostaContatoreAsync(id, 3);
        var sessione = c.Web.SessioneA();

        await sessione.AccediAsync(EmailAnna, DatiProva.PasswordProva);
        Assert.Equal(3, (await c.LeggiAsync(id)).ContatoreAccessiFalliti);

        // Alternare password giusta e codice sbagliato non evita mai il blocco.
        using var errato = await Verifica(sessione, "000000");
        Assert.Equal(4, (await c.LeggiAsync(id)).ContatoreAccessiFalliti);
        using var giusto = await Verifica(sessione, Codice(attivazione.Segreto));
        Assert.Equal("/admin", SessioneProva.Destinazione(giusto));
        Assert.Equal(0, (await c.LeggiAsync(id)).ContatoreAccessiFalliti);
    }

    [Fact]
    public async Task UnCodiceDiRecuperoValeUnaVoltaEAccettaFormeDiverse()
    {
        await using var c = await Contesto.CreaAsync();
        var id = await c.Web.CreaTenantAdminAsync(EmailAnna);
        var attivazione = await c.AttivaAsync(id);
        var codice = attivazione.CodiciRecupero[0];
        var forma = codice.Replace("-", string.Empty, StringComparison.Ordinal).ToLowerInvariant();

        var prima = c.Web.SessioneA();
        await prima.AccediAsync(EmailAnna, DatiProva.PasswordProva);
        using var riuscito = await Verifica(prima, forma);
        Assert.Equal("/admin", SessioneProva.Destinazione(riuscito));

        var seconda = c.Web.SessioneA();
        await seconda.AccediAsync(EmailAnna, DatiProva.PasswordProva);
        using var riuso = await Verifica(seconda, codice);
        Assert.Null(seconda.CookieAdmin);
        Assert.Equal(HttpStatusCode.OK, riuso.StatusCode);

        Assert.Equal(CodiciRecupero.Quantita - 1, await c.CodiciResiduiAsync(id));
        Assert.Equal(1, await c.ContaAuditAsync(AzioniAudit.UsoCodiceRecupero));
    }

    [Fact]
    public async Task LaSfidaManomessaOSuUnAltroHostNonVale()
    {
        await using var c = await Contesto.CreaAsync();
        var id = await c.Web.CreaTenantAdminAsync(EmailAnna);
        var attivazione = await c.AttivaAsync(id);
        var sessione = c.Web.SessioneA();
        await sessione.AccediAsync(EmailAnna, DatiProva.PasswordProva);
        var sfida = sessione.Cookie[".DvCms.SecondoFattore"];

        // Cookie manomesso: si riparte dalla password.
        var manomessa = c.Web.SessioneA();
        manomessa.ImpostaCookie(".DvCms.SecondoFattore", sfida[..^4] + (sfida.EndsWith("AAAA", StringComparison.Ordinal) ? "BBBB" : "AAAA"));
        using var rifiutata = await manomessa.GetAsync("/admin/accesso/verifica");
        Assert.True(SessioneProva.EReindirizzamento(rifiutata));
        Assert.Contains("/admin/accesso", SessioneProva.Destinazione(rifiutata), StringComparison.Ordinal);
        Assert.Null(manomessa.CookieAdmin);

        // Stesso cookie, ma sull'host dell'altro tenant: non vale.
        var altroHost = c.Web.SessioneB();
        altroHost.ImpostaCookie(".DvCms.SecondoFattore", sfida);
        using var altroTenant = await altroHost.GetAsync("/admin/accesso/verifica");
        Assert.True(SessioneProva.EReindirizzamento(altroTenant));
        Assert.Null(altroHost.CookieAdmin);

        // Senza alcuna sfida la pagina di verifica rimanda all'accesso.
        using var senzaSfida = await c.Web.SessioneA().GetAsync("/admin/accesso/verifica");
        Assert.True(SessioneProva.EReindirizzamento(senzaSfida));
    }

    [Fact]
    public async Task ConLObbligoAttivoUnAmministratoreSenzaDueFattoriDevePrimaConfigurarla()
    {
        await using var c = await Contesto.CreaAsync(obbligo: "Amministratori");
        await c.Web.CreaTenantAdminAsync(EmailAnna);
        var sessione = c.Web.SessioneA();

        using var accesso = await sessione.AccediAsync(EmailAnna, DatiProva.PasswordProva);
        Assert.Equal("/admin", SessioneProva.Destinazione(accesso));

        foreach (var percorso in new[] { "/admin", "/admin/media", "/admin/tema" })
        {
            using var rimando = await sessione.GetAsync(percorso);
            Assert.Equal("/admin/sicurezza", SessioneProva.Destinazione(rimando));
        }

        using var configurazione = await sessione.GetAsync("/admin/sicurezza");
        Assert.Equal(HttpStatusCode.OK, configurazione.StatusCode);
        Assert.Contains("obbligatoria", await configurazione.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        using var password = await sessione.GetAsync("/admin/password");
        Assert.Equal(HttpStatusCode.OK, password.StatusCode);
    }

    [Fact]
    public async Task ConObbligoSoloPlatformAdminIlTenantAdminNonDeveConfigurarla()
    {
        await using var c = await Contesto.CreaAsync(obbligo: "PlatformAdmin");
        await c.Web.CreaTenantAdminAsync(EmailAnna);
        await c.Web.CreaPlatformAdminAsync("platform@esempio.it");

        var tenantAdmin = c.Web.SessioneA();
        await tenantAdmin.AccediAsync(EmailAnna, DatiProva.PasswordProva);
        using var dashboardTenant = await tenantAdmin.GetAsync("/admin");
        var platform = c.Web.SessioneA();
        await platform.AccediAsync("platform@esempio.it", DatiProva.PasswordProva);
        using var dashboardPlatform = await platform.GetAsync("/admin");

        Assert.Equal(HttpStatusCode.OK, dashboardTenant.StatusCode);
        Assert.Equal("/admin/sicurezza", SessioneProva.Destinazione(dashboardPlatform));
    }

    [Fact]
    public async Task ConfigurazioneConQrConfermaConCodiceCodiciUnaSolaVoltaESessioniAltreInvalidate()
    {
        await using var c = await Contesto.CreaAsync(obbligo: "Amministratori");
        var id = await c.Web.CreaTenantAdminAsync(EmailAnna);
        var sessione = c.Web.SessioneA();
        await sessione.AccediAsync(EmailAnna, DatiProva.PasswordProva);
        var altraSessione = c.Web.SessioneA();
        await altraSessione.AccediAsync(EmailAnna, DatiProva.PasswordProva);

        using var avvio = await sessione.InviaModuloAsync("/admin/sicurezza", [], "/admin/sicurezza?handler=Avvia");
        var html = await avvio.Content.ReadAsStringAsync(Ct);
        Assert.Contains("<svg", html, StringComparison.Ordinal);
        var chiave = ChiaveNellaPagina().Match(html).Groups[1].Value;
        Assert.True(Base32.TentaDecodifica(chiave, out var segreto));
        Assert.Equal(Totp.LunghezzaSegretoByte, segreto.Length);
        Assert.False((await c.LeggiAsync(id)).DueFattoriAttivi);

        // Un codice sbagliato non attiva nulla.
        using var errato = await sessione.InviaModuloAsync("/admin/sicurezza", [new("Codice", "000000")], "/admin/sicurezza?handler=Conferma");
        Assert.Contains("Codice non valido", await errato.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        Assert.False((await c.LeggiAsync(id)).DueFattoriAttivi);

        using var conferma = await sessione.InviaModuloAsync("/admin/sicurezza", [new("Codice", Codice(segreto))], "/admin/sicurezza?handler=Conferma");
        var htmlConferma = await conferma.Content.ReadAsStringAsync(Ct);
        Assert.Equal(CodiciRecupero.Quantita, CodiceDiRecuperoNellaPagina().Count(htmlConferma));
        var utenza = await c.LeggiAsync(id);
        Assert.True(utenza.DueFattoriAttivi);
        Assert.Null(utenza.SegretoTotpInAttesaProtetto);
        Assert.DoesNotContain(chiave.Replace(" ", string.Empty, StringComparison.Ordinal), utenza.SegretoTotpProtetto, StringComparison.Ordinal);
        Assert.Equal(CodiciRecupero.Quantita, await c.CodiciResiduiAsync(id));

        // La sessione corrente continua a funzionare (e non è più costretta a configurare); l'altra è invalidata.
        using var dashboard = await sessione.GetAsync("/admin");
        Assert.Equal(HttpStatusCode.OK, dashboard.StatusCode);
        using var vecchia = await altraSessione.GetAsync("/admin");
        Assert.True(SessioneProva.EReindirizzamento(vecchia));
        Assert.Contains("/admin/accesso", SessioneProva.Destinazione(vecchia), StringComparison.Ordinal);

        // I codici non si rivedono: ricaricando la pagina non ci sono più.
        using var ricarica = await sessione.GetAsync("/admin/sicurezza");
        Assert.Equal(0, CodiceDiRecuperoNellaPagina().Count(await ricarica.Content.ReadAsStringAsync(Ct)));
        Assert.Equal(1, await c.ContaAuditAsync(AzioniAudit.AttivazioneDueFattori));
    }

    [Fact]
    public async Task ConObbligoNonSiDisattivaEDisattivandolaNonObbligatoriaServonoPasswordECodice()
    {
        await using var c = await Contesto.CreaAsync(obbligo: "Amministratori");
        var id = await c.Web.CreaTenantAdminAsync(EmailAnna);
        var attivazione = await c.AttivaAsync(id);
        var sessione = c.Web.SessioneA();
        await sessione.AccediAsync(EmailAnna, DatiProva.PasswordProva);
        await Verifica(sessione, Codice(attivazione.Segreto));
        await c.LiberaPassoAsync(id);

        using var vietata = await sessione.InviaModuloAsync(
            "/admin/sicurezza",
            [new("Password", DatiProva.PasswordProva), new("Codice", Codice(attivazione.Segreto))],
            "/admin/sicurezza?handler=Disattiva");
        Assert.Contains("obbligatoria", WebUtility.HtmlDecode(await vietata.Content.ReadAsStringAsync(Ct)), StringComparison.Ordinal);
        Assert.True((await c.LeggiAsync(id)).DueFattoriAttivi);

        // Senza obbligo: password sbagliata no, password e codice giusti sì (e la sessione si chiude).
        await using var libero = await Contesto.CreaAsync();
        var idLibero = await libero.Web.CreaTenantAdminAsync(EmailAnna);
        var attivazioneLibero = await libero.AttivaAsync(idLibero);
        var sessioneLibera = libero.Web.SessioneA();
        await sessioneLibera.AccediAsync(EmailAnna, DatiProva.PasswordProva);
        await Verifica(sessioneLibera, Codice(attivazioneLibero.Segreto));
        await libero.LiberaPassoAsync(idLibero);

        using var passwordSbagliata = await sessioneLibera.InviaModuloAsync(
            "/admin/sicurezza",
            [new("Password", "sbagliata-sbagliata-1"), new("Codice", Codice(attivazioneLibero.Segreto))],
            "/admin/sicurezza?handler=Disattiva");
        Assert.Contains("non validi", await passwordSbagliata.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        Assert.True((await libero.LeggiAsync(idLibero)).DueFattoriAttivi);
        await libero.LiberaPassoAsync(idLibero);
        await libero.ImpostaContatoreAsync(idLibero, 0);

        using var disattivata = await sessioneLibera.InviaModuloAsync(
            "/admin/sicurezza",
            [new("Password", DatiProva.PasswordProva), new("Codice", Codice(attivazioneLibero.Segreto))],
            "/admin/sicurezza?handler=Disattiva");
        Assert.True(SessioneProva.EReindirizzamento(disattivata));
        var dopo = await libero.LeggiAsync(idLibero);
        Assert.False(dopo.DueFattoriAttivi);
        Assert.Null(dopo.SegretoTotpProtetto);
        Assert.Equal(0, await libero.CodiciResiduiAsync(idLibero));
        using var senzaSessione = await sessioneLibera.GetAsync("/admin");
        Assert.True(SessioneProva.EReindirizzamento(senzaSessione));
    }

    [Fact]
    public async Task LaRigenerazioneInvalidaIVecchiCodiciEServonoPasswordECodice()
    {
        await using var c = await Contesto.CreaAsync();
        var id = await c.Web.CreaTenantAdminAsync(EmailAnna);
        var attivazione = await c.AttivaAsync(id);
        var sessione = c.Web.SessioneA();
        await sessione.AccediAsync(EmailAnna, DatiProva.PasswordProva);
        await Verifica(sessione, Codice(attivazione.Segreto));
        await c.LiberaPassoAsync(id);

        using var senzaCredenziali = await sessione.InviaModuloAsync("/admin/sicurezza", [], "/admin/sicurezza?handler=Rigenera");
        Assert.Equal(0, CodiceDiRecuperoNellaPagina().Count(await senzaCredenziali.Content.ReadAsStringAsync(Ct)));

        using var rigenerati = await sessione.InviaModuloAsync(
            "/admin/sicurezza",
            [new("Password", DatiProva.PasswordProva), new("Codice", Codice(attivazione.Segreto))],
            "/admin/sicurezza?handler=Rigenera");
        var nuovi = CodiceDiRecuperoNellaPagina().Matches(await rigenerati.Content.ReadAsStringAsync(Ct)).Select(m => m.Value).ToList();
        Assert.Equal(CodiciRecupero.Quantita, nuovi.Count);
        Assert.Empty(nuovi.Intersect(attivazione.CodiciRecupero));

        var nuovaSessione = c.Web.SessioneA();
        await nuovaSessione.AccediAsync(EmailAnna, DatiProva.PasswordProva);
        using var vecchio = await Verifica(nuovaSessione, attivazione.CodiciRecupero[0]);
        Assert.Null(nuovaSessione.CookieAdmin);
        using var nuovo = await Verifica(nuovaSessione, nuovi[0]);
        Assert.Equal("/admin", SessioneProva.Destinazione(nuovo));
    }

    [Fact]
    public async Task IlCambioDiDispositivoRichiedeVerificaEIlVecchioSegretoSmetteDiValere()
    {
        await using var c = await Contesto.CreaAsync();
        var id = await c.Web.CreaTenantAdminAsync(EmailAnna);
        var attivazione = await c.AttivaAsync(id);
        var sessione = c.Web.SessioneA();
        await sessione.AccediAsync(EmailAnna, DatiProva.PasswordProva);
        await Verifica(sessione, Codice(attivazione.Segreto));
        await c.LiberaPassoAsync(id);

        using var senza = await sessione.InviaModuloAsync("/admin/sicurezza", [], "/admin/sicurezza?handler=Avvia");
        Assert.DoesNotContain("<svg", await senza.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);

        using var avvio = await sessione.InviaModuloAsync(
            "/admin/sicurezza",
            [new("Password", DatiProva.PasswordProva), new("Codice", Codice(attivazione.Segreto))],
            "/admin/sicurezza?handler=Avvia");
        var chiave = ChiaveNellaPagina().Match(await avvio.Content.ReadAsStringAsync(Ct)).Groups[1].Value;
        Assert.True(Base32.TentaDecodifica(chiave, out var nuovoSegreto));
        await c.LiberaPassoAsync(id);
        using var conferma = await sessione.InviaModuloAsync("/admin/sicurezza", [new("Codice", Codice(nuovoSegreto))], "/admin/sicurezza?handler=Conferma");
        Assert.Equal(CodiciRecupero.Quantita, CodiceDiRecuperoNellaPagina().Count(await conferma.Content.ReadAsStringAsync(Ct)));

        await c.LiberaPassoAsync(id);
        var nuova = c.Web.SessioneA();
        await nuova.AccediAsync(EmailAnna, DatiProva.PasswordProva);
        using var vecchio = await Verifica(nuova, Codice(attivazione.Segreto));
        Assert.Null(nuova.CookieAdmin);
        using var nuovo = await Verifica(nuova, Codice(nuovoSegreto));
        Assert.Equal("/admin", SessioneProva.Destinazione(nuovo));
    }

    [Fact]
    public async Task IlPlatformAdminReimpostaLaDueFattoriDiUnTenantAdminEQuestiDevePoiConfigurarlaDiNuovo()
    {
        await using var c = await Contesto.CreaAsync(obbligo: "Amministratori");
        var id = await c.Web.CreaTenantAdminAsync(EmailAnna);
        var attivazione = await c.AttivaAsync(id);
        var utente = c.Web.SessioneA();
        await utente.AccediAsync(EmailAnna, DatiProva.PasswordProva);
        await Verifica(utente, Codice(attivazione.Segreto));
        await c.Web.CreaPlatformAdminAsync("platform@esempio.it");
        var platform = c.Web.SessioneB();
        var attivazionePlatform = await c.AttivaAsync(await c.IdAsync("platform@esempio.it"));
        await platform.AccediAsync("platform@esempio.it", DatiProva.PasswordProva);
        await Verifica(platform, Codice(attivazionePlatform.Segreto));
        var dettaglio = $"/admin/platform/tenant/{c.Web.A.IdTenant}";

        using var pagina = await platform.GetAsync(dettaglio);
        Assert.Contains("Reimposta 2FA", await pagina.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        using var reimpostata = await platform.InviaModuloAsync(dettaglio, [new("idUtenza", id.ToString())], $"{dettaglio}?handler=ReimpostaDueFattori");
        Assert.True(SessioneProva.EReindirizzamento(reimpostata));

        var dopo = await c.LeggiAsync(id);
        Assert.False(dopo.DueFattoriAttivi);
        Assert.Null(dopo.SegretoTotpProtetto);
        Assert.Equal(0, await c.CodiciResiduiAsync(id));
        Assert.Equal(1, await c.ContaAuditAsync(AzioniAudit.ReimpostazioneDueFattori));
        using var sessioneChiusa = await utente.GetAsync("/admin");
        Assert.Contains("/admin/accesso", SessioneProva.Destinazione(sessioneChiusa), StringComparison.Ordinal);

        // Il vecchio dispositivo non vale più; la password basta per entrare, ma solo per configurare di nuovo.
        var nuova = c.Web.SessioneA();
        await nuova.AccediAsync(EmailAnna, DatiProva.PasswordProva);
        using var rimando = await nuova.GetAsync("/admin");
        Assert.Equal("/admin/sicurezza", SessioneProva.Destinazione(rimando));
    }

    [Fact]
    public async Task LaReimpostazioneDalPlatformValeSoloPerITenantAdminDelTenantIndicato()
    {
        await using var c = await Contesto.CreaAsync();
        var anna = await c.Web.CreaTenantAdminAsync(EmailAnna);
        await c.AttivaAsync(anna);
        var primo = await c.Web.CreaPlatformAdminAsync("platform@esempio.it");
        var secondo = await c.Web.CreaPlatformAdminAsync("platform2@esempio.it");
        await c.AttivaAsync(secondo);
        await using var ambito = c.Web.Fabbrica.Services.CreateAsyncScope();
        var servizio = ambito.ServiceProvider.GetRequiredService<IServizioPiattaforma>();

        var altroPlatform = await servizio.ReimpostaDueFattoriAsync(primo, c.Web.A.IdTenant, secondo, Ct);
        var tenantSbagliato = await servizio.ReimpostaDueFattoriAsync(primo, c.Web.B.IdTenant, anna, Ct);
        var nonPlatform = await servizio.ReimpostaDueFattoriAsync(anna, c.Web.A.IdTenant, anna, Ct);

        Assert.Equal(CodiceEsito.NonTrovato, altroPlatform.Codice);
        Assert.Equal(CodiceEsito.NonTrovato, tenantSbagliato.Codice);
        Assert.Equal(CodiceEsito.NonAutorizzato, nonPlatform.Codice);
        Assert.True((await c.LeggiAsync(anna)).DueFattoriAttivi);
        Assert.True((await c.LeggiAsync(secondo)).DueFattoriAttivi);
    }

    [Fact]
    public async Task IlComandoOperativoReimpostaSoloIPlatformAdmin()
    {
        await using var c = await Contesto.CreaAsync();
        var anna = await c.Web.CreaTenantAdminAsync(EmailAnna);
        await c.AttivaAsync(anna);
        var platform = await c.Web.CreaPlatformAdminAsync("platform@esempio.it");
        await c.AttivaAsync(platform);

        var tenantAdmin = await ComandoReimpostaDueFattori.EseguiAsync(c.Web.Fabbrica.Services, EmailAnna, Ct);
        var sconosciuto = await ComandoReimpostaDueFattori.EseguiAsync(c.Web.Fabbrica.Services, "nessuno@esempio.it", Ct);
        Assert.Equal(1, tenantAdmin);
        Assert.Equal(1, sconosciuto);
        Assert.True((await c.LeggiAsync(anna)).DueFattoriAttivi);
        Assert.True((await c.LeggiAsync(platform)).DueFattoriAttivi);

        var esito = await ComandoReimpostaDueFattori.EseguiAsync(c.Web.Fabbrica.Services, "PLATFORM@esempio.it", Ct);

        Assert.Equal(0, esito);
        var dopo = await c.LeggiAsync(platform);
        Assert.False(dopo.DueFattoriAttivi);
        Assert.Equal(0, await c.CodiciResiduiAsync(platform));
        Assert.Equal(1, await c.ContaAuditAsync(AzioniAudit.ReimpostazioneDueFattori));
    }

    [Fact]
    public async Task IlComandoOperativoNonRichiedeLaChiaveDeiSegreti()
    {
        await using var c = await Contesto.CreaAsync(conChiave: false);
        var platform = await c.Web.CreaPlatformAdminAsync("platform@esempio.it");
        await using (var ambito = c.Web.Fabbrica.Services.CreateAsyncScope())
        {
            await ambito.ServiceProvider.GetRequiredService<ContestoDatabase>().Utenze.IgnoreQueryFilters().Where(u => u.Id == platform)
                .ExecuteUpdateAsync(u => u.SetProperty(x => x.DueFattoriAttivi, true).SetProperty(x => x.SegretoTotpProtetto, "illeggibile"), Ct);
        }

        var esito = await ComandoReimpostaDueFattori.EseguiAsync(c.Web.Fabbrica.Services, "platform@esempio.it", Ct);

        Assert.Equal(0, esito);
        Assert.False((await c.LeggiAsync(platform)).DueFattoriAttivi);
    }

    [Fact]
    public async Task LaDueFattoriNonSiAttivaSuUnaChiaveDeiSegretiMancante()
    {
        await using var c = await Contesto.CreaAsync(conChiave: false);
        await c.Web.CreaTenantAdminAsync(EmailAnna);
        var sessione = c.Web.SessioneA();
        await sessione.AccediAsync(EmailAnna, DatiProva.PasswordProva);

        using var avvio = await sessione.InviaModuloAsync("/admin/sicurezza", [], "/admin/sicurezza?handler=Avvia");

        Assert.Equal(HttpStatusCode.OK, avvio.StatusCode);
        var html = WebUtility.HtmlDecode(await avvio.Content.ReadAsStringAsync(Ct));
        Assert.Contains("chiave dei segreti mancante", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<svg", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnAccountDisabilitatoOCambiatoDopoLaPasswordNonCompletaLAccesso()
    {
        await using var c = await Contesto.CreaAsync();
        var id = await c.Web.CreaTenantAdminAsync(EmailAnna);
        var attivazione = await c.AttivaAsync(id);
        var sessione = c.Web.SessioneA();
        await sessione.AccediAsync(EmailAnna, DatiProva.PasswordProva);

        await c.Web.DisabilitaAsync(id);
        using var risposta = await Verifica(sessione, Codice(attivazione.Segreto));

        Assert.Null(sessione.CookieAdmin);
        Assert.Contains("Codice non valido", await risposta.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
    }

    private static string Codice(byte[] segreto, int passiAvanti = 0) =>
        Totp.CalcolaCodice(segreto, Totp.PassoDi(DateTimeOffset.UtcNow) + passiAvanti);

    private static Task<HttpResponseMessage> Verifica(SessioneProva sessione, string codice) =>
        sessione.InviaModuloAsync("/admin/accesso/verifica", [new("Codice", codice)]);

    [GeneratedRegex("id=\"chiave-totp\">([^<]+)<", RegexOptions.CultureInvariant)]
    private static partial Regex ChiaveNellaPagina();

    [GeneratedRegex("(?<![A-Z2-7-])[A-Z2-7]{4}(-[A-Z2-7]{4}){3}(?![A-Z2-7-])", RegexOptions.CultureInvariant)]
    private static partial Regex CodiceDiRecuperoNellaPagina();

    private sealed record Attivazione(byte[] Segreto, IReadOnlyList<string> CodiciRecupero);

    private sealed class Contesto : IAsyncDisposable
    {
        private Contesto(ScenarioWeb web)
        {
            Web = web;
        }

        public ScenarioWeb Web { get; }

        public static async Task<Contesto> CreaAsync(string obbligo = "Nessuno", bool conChiave = true)
        {
            var impostazioni = new Dictionary<string, string?> { ["Sicurezza:DueFattori:Obbligo"] = obbligo };
            if (conChiave)
            {
                impostazioni["Sicurezza:ChiaveSegreti"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            }

            return new Contesto(await ScenarioWeb.CreaAsync(impostazioni: impostazioni));
        }

        /// <summary>Attiva la 2FA direttamente sul database (come dopo una configurazione completata) e restituisce segreto e codici.</summary>
        public async Task<Attivazione> AttivaAsync(Guid idUtenza)
        {
            var (segreto, codici) = await DatiProva.AttivaDueFattoriAsync(Web.Fabbrica.Services, idUtenza, Ct);
            return new Attivazione(segreto, codici);
        }

        public async Task<Utenza> LeggiAsync(Guid idUtenza)
        {
            await using var ambito = Web.Fabbrica.Services.CreateAsyncScope();
            return await ambito.ServiceProvider.GetRequiredService<ContestoDatabase>().Utenze.IgnoreQueryFilters().AsNoTracking()
                .SingleAsync(u => u.Id == idUtenza, Ct);
        }

        public async Task<Guid> IdAsync(string email)
        {
            await using var ambito = Web.Fabbrica.Services.CreateAsyncScope();
            return await ambito.ServiceProvider.GetRequiredService<ContestoDatabase>().Utenze.IgnoreQueryFilters()
                .Where(u => u.EmailNormalizzata == email).Select(u => u.Id).SingleAsync(Ct);
        }

        /// <summary>Il codice TOTP vale una volta sola per passo: i test che riusano lo stesso passo azzerano l'ultimo passo usato.</summary>
        public async Task LiberaPassoAsync(Guid idUtenza)
        {
            await using var ambito = Web.Fabbrica.Services.CreateAsyncScope();
            await ambito.ServiceProvider.GetRequiredService<ContestoDatabase>().Utenze.IgnoreQueryFilters().Where(u => u.Id == idUtenza)
                .ExecuteUpdateAsync(u => u.SetProperty(x => x.UltimoPassoTotp, 0L), Ct);
        }

        public async Task ImpostaContatoreAsync(Guid idUtenza, int valore)
        {
            await using var ambito = Web.Fabbrica.Services.CreateAsyncScope();
            await ambito.ServiceProvider.GetRequiredService<ContestoDatabase>().Utenze.IgnoreQueryFilters().Where(u => u.Id == idUtenza)
                .ExecuteUpdateAsync(u => u.SetProperty(x => x.ContatoreAccessiFalliti, valore).SetProperty(x => x.BloccatoFinoAlUtc, (DateTime?)null), Ct);
        }

        public async Task<int> CodiciResiduiAsync(Guid idUtenza)
        {
            await using var ambito = Web.Fabbrica.Services.CreateAsyncScope();
            return await ambito.ServiceProvider.GetRequiredService<ContestoDatabase>().CodiciRecuperoUtenza
                .CountAsync(c => c.IdUtenza == idUtenza && c.UsatoIlUtc == null, Ct);
        }

        public async Task<int> ContaAuditAsync(string azione)
        {
            await using var ambito = Web.Fabbrica.Services.CreateAsyncScope();
            return await ambito.ServiceProvider.GetRequiredService<ContestoDatabase>().RegistroAudit.IgnoreQueryFilters()
                .CountAsync(r => r.Azione == azione, Ct);
        }

        public ValueTask DisposeAsync() => Web.DisposeAsync();
    }
}
