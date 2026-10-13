using System.Security.Cryptography;
using DvCms.Contracts.Email;
using DvCms.Core.Accessi;
using DvCms.Data;
using DvCms.DTO.Email;
using DvCms.DTO.Piattaforma;
using DvCms.Emails;
using DvCms.Infrastructure.Outbox;
using DvCms.IntegrationTests.Supporto;
using DvCms.Models.Accessi;
using DvCms.Models.Email;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DvCms.IntegrationTests.Email;

/// <summary>Configurazione email del tenant nel menu Platform (B-009): segreti in sola scrittura, validazione, invio di prova.</summary>
public class ServizioImpostazioniEmailTest
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task SalvaIlCanaleSeparandoICampiSegretiENonLiRestituisceMai()
    {
        await using var s = await Scenario.CreaAsync();

        var esito = await s.SalvaAsync(s.Smtp(utente: "mittente", password: "PASSWORD-SMTP"));

        Assert.True(esito.Riuscito);
        var scheda = (await s.LeggiAsync())!;
        var visibili = scheda.Impostazioni!;
        Assert.Equal(ProviderEmail.Smtp, visibili.Provider);
        Assert.Equal("127.0.0.1", visibili.Valori["Host"]);
        Assert.False(visibili.Valori.ContainsKey("Password"));
        Assert.Contains("Password", visibili.SegretiImpostati);
        Assert.DoesNotContain("PASSWORD-SMTP", string.Join('|', visibili.Valori.Values), StringComparison.Ordinal);

        await using var ambito = s.Fornitore.CreateAsyncScope();
        var contesto = ambito.ServiceProvider.GetRequiredService<ContestoDatabase>();
        var riga = await contesto.ImpostazioniEmailTenant.IgnoreQueryFilters().SingleAsync(Ct);
        Assert.DoesNotContain("PASSWORD-SMTP", riga.ValoriJson + riga.SegretiProtetti, StringComparison.Ordinal);
        Assert.Equal(s.Admin, riga.AggiornatoDaIdUtenza);
        var audit = await contesto.RegistroAudit.IgnoreQueryFilters().SingleAsync(a => a.Azione == AzioniAudit.ModificaImpostazioniEmail, Ct);
        Assert.Contains("canale Smtp", audit.Dettagli, StringComparison.Ordinal);
        Assert.Contains("Password", audit.Dettagli, StringComparison.Ordinal);
        Assert.DoesNotContain("PASSWORD-SMTP", audit.Dettagli, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnSegretoVuotoMantieneQuelloSalvatoEUnoNuovoLoSostituisce()
    {
        await using var s = await Scenario.CreaAsync();
        await s.SalvaAsync(s.Smtp(utente: "mittente", password: "PRIMA"));

        // Salvataggio senza ridigitare la password: la vecchia resta in uso.
        Assert.True((await s.SalvaAsync(s.Smtp(utente: "mittente", password: "", nomeMittente: "Nuovo nome"))).Riuscito);
        await s.InviaProvaAsync();
        Assert.Equal("|mittente|PRIMA", Assert.Single(s.Server.Ricevute).Credenziali);

        Assert.True((await s.SalvaAsync(s.Smtp(utente: "mittente", password: "DOPO"))).Riuscito);
        await s.InviaProvaAsync();
        Assert.Equal("|mittente|DOPO", s.Server.Ricevute[^1].Credenziali);
    }

    [Fact]
    public async Task UnSegretoFacoltativoSiRimuoveEsplicitamente()
    {
        await using var s = await Scenario.CreaAsync();
        await s.SalvaAsync(s.Smtp(utente: "mittente", password: "PRIMA"));

        var esito = await s.SalvaAsync(s.Smtp(utente: string.Empty, password: ""), rimuovi: ["Password"]);

        Assert.True(esito.Riuscito);
        Assert.DoesNotContain("Password", (await s.LeggiAsync())!.Impostazioni!.SegretiImpostati);
        await s.InviaProvaAsync();
        Assert.Null(Assert.Single(s.Server.Ricevute).Credenziali);
    }

    [Fact]
    public async Task CambiandoCanaleISegretiDelVecchioNonSiPortanoDietro()
    {
        await using var s = await Scenario.CreaAsync();
        await s.SalvaAsync(s.Smtp(utente: "mittente", password: "PASSWORD-SMTP"));

        var senzaChiave = await s.SalvaAsync(
            ProviderEmail.SendGrid,
            new Dictionary<string, string> { ["IndirizzoMittente"] = "noreply@a.esempio.it" });
        Assert.Equal(CodiceEsito.DatiNonValidi, senzaChiave.Codice);
        Assert.Equal(["ChiaveApi"], senzaChiave.CampiNonValidi);
        Assert.Equal(ProviderEmail.Smtp, (await s.LeggiAsync())!.Impostazioni!.Provider);

        var conChiave = await s.SalvaAsync(
            ProviderEmail.SendGrid,
            new Dictionary<string, string> { ["IndirizzoMittente"] = "noreply@a.esempio.it", ["ChiaveApi"] = "SG.chiave" });
        Assert.True(conChiave.Riuscito);
        var visibili = (await s.LeggiAsync())!.Impostazioni!;
        Assert.Equal(ProviderEmail.SendGrid, visibili.Provider);
        Assert.Equal(new HashSet<string> { "ChiaveApi" }, [.. visibili.SegretiImpostati]);
    }

    [Theory]
    [InlineData("Host", "")]
    [InlineData("Porta", "0")]
    [InlineData("Porta", "tanti")]
    [InlineData("ModalitaTls", "Boh")]
    [InlineData("IndirizzoMittente", "non-una-email")]
    public async Task ValoriNonValidiVengonoRifiutatiPerNomeDelCampoENulaVieneSalvato(string campo, string valore)
    {
        await using var s = await Scenario.CreaAsync();
        var valori = s.Smtp();
        valori[campo] = valore;

        var esito = await s.SalvaAsync(ProviderEmail.Smtp, valori);

        Assert.Equal(CodiceEsito.DatiNonValidi, esito.Codice);
        Assert.Equal([campo], esito.CampiNonValidi);
        Assert.Null((await s.LeggiAsync())!.Impostazioni);
    }

    [Fact]
    public async Task UnCampoNonPrevistoDalCanaleERifiutato()
    {
        await using var s = await Scenario.CreaAsync();
        var valori = s.Smtp();
        valori["ChiaveApi"] = "x";

        var esito = await s.SalvaAsync(ProviderEmail.Smtp, valori);

        Assert.Equal(["ChiaveApi"], esito.CampiNonValidi);
    }

    [Fact]
    public async Task UnProviderSconosciutoERifiutato()
    {
        await using var s = await Scenario.CreaAsync();

        var esito = await s.SalvaAsync((ProviderEmail)42, s.Smtp());

        Assert.Equal(CodiceEsito.DatiNonValidi, esito.Codice);
    }

    [Fact]
    public async Task SenzaChiaveDeiSegretiNonSiSalvaUnSegreto()
    {
        await using var s = await Scenario.CreaAsync(conChiave: false);

        var esito = await s.SalvaAsync(s.Smtp(utente: "mittente", password: "PASSWORD-SMTP"));

        Assert.Equal(CodiceEsito.ChiaveSegretiMancante, esito.Codice);
        Assert.Null((await s.LeggiAsync())!.Impostazioni);
    }

    [Fact]
    public async Task SenzaSegretiLaChiaveNonServe()
    {
        await using var s = await Scenario.CreaAsync(conChiave: false);

        var esito = await s.SalvaAsync(s.Smtp());

        Assert.True(esito.Riuscito);
    }

    [Fact]
    public async Task UnaModificaConUnTokenVecchioDaConflitto()
    {
        await using var s = await Scenario.CreaAsync();
        await s.SalvaAsync(s.Smtp());
        var vecchio = (await s.LeggiAsync())!.Impostazioni!.TokenConcorrenza;
        await s.SalvaAsync(s.Smtp(nomeMittente: "Seconda"), token: vecchio);

        var conflitto = await s.SalvaAsync(s.Smtp(nomeMittente: "Terza"), token: vecchio);

        Assert.Equal(CodiceEsito.Conflitto, conflitto.Codice);
        Assert.Equal("Seconda", (await s.LeggiAsync())!.Impostazioni!.Valori["NomeMittente"]);
    }

    [Fact]
    public async Task SoloIlPlatformAdminPuoLeggereScrivereEProvare()
    {
        await using var s = await Scenario.CreaAsync();
        await s.SalvaAsync(s.Smtp(utente: "mittente", password: "PASSWORD-SMTP"));
        var tenantAdmin = await DatiProva.CreaUtenzaAsync(s.Fornitore, RuoloUtenza.TenantAdmin, s.IdTenant, "anna@esempio.it", annullamento: Ct);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => s.LeggiAsync(tenantAdmin));
        var salva = await s.SalvaAsync(s.Smtp(nomeMittente: "Hacked"), attore: tenantAdmin);
        var prova = await s.InviaProvaAsync(attore: tenantAdmin);

        Assert.Equal(CodiceEsito.NonAutorizzato, salva.Codice);
        Assert.Equal(CodiceEsito.NonAutorizzato, prova.Codice);
        Assert.Equal("Mittente", (await s.LeggiAsync())!.Impostazioni!.Valori["NomeMittente"]);
        await using var ambito = s.Fornitore.CreateAsyncScope();
        Assert.Equal(2, await ambito.ServiceProvider.GetRequiredService<ContestoDatabase>().RegistroAudit.IgnoreQueryFilters().CountAsync(a => a.Azione == AzioniAudit.OperazioneNegata, Ct));
        Assert.Empty(s.Server.Ricevute);
    }

    [Fact]
    public async Task LaProvaRichiedeUnaConfigurazioneEUnDestinatarioValido()
    {
        await using var s = await Scenario.CreaAsync();

        var senzaConfigurazione = await s.InviaProvaAsync();
        await s.SalvaAsync(s.Smtp());
        var destinatarioErrato = await s.InviaProvaAsync(destinatario: "non-una-email");

        Assert.Equal(CodiceEsito.NonConsentito, senzaConfigurazione.Codice);
        Assert.Equal(CodiceEsito.DatiNonValidi, destinatarioErrato.Codice);
    }

    [Fact]
    public async Task LaProvaPassaDallOutboxELErroreDiInvioEVisibileSenzaSegreti()
    {
        await using var s = await Scenario.CreaAsync();
        s.Server.RifiutaInvio = true;
        await s.SalvaAsync(s.Smtp(utente: "mittente", password: "PASSWORD-SMTP"));

        var prova = await s.InviaProvaAsync();
        Assert.True(prova.Riuscito);
        Assert.Empty(s.Server.Ricevute);
        Assert.Empty((await s.LeggiAsync())!.Problemi);

        await s.Fornitore.GetRequiredService<ElaboratoreOutbox>().ElaboraLottoAsync(Ct);

        var problema = Assert.Single((await s.LeggiAsync())!.Problemi);
        Assert.Equal(1, problema.Tentativi);
        Assert.NotNull(problema.UltimoErrore);
        Assert.DoesNotContain("PASSWORD-SMTP", problema.UltimoErrore, StringComparison.Ordinal);

        s.Server.RifiutaInvio = false;
        await s.InviaProvaAsync();
        await s.Fornitore.GetRequiredService<ElaboratoreOutbox>().ElaboraLottoAsync(Ct);
        Assert.Single(s.Server.Ricevute);
    }

    [Fact]
    public async Task UnTenantInesistenteNonVieneTrovato()
    {
        await using var s = await Scenario.CreaAsync();
        await using var ambito = s.Fornitore.CreateAsyncScope();
        var servizio = ambito.ServiceProvider.GetRequiredService<IServizioImpostazioniEmail>();

        Assert.Null(await servizio.LeggiAsync(s.Admin, Guid.NewGuid(), Ct));
        var esito = await servizio.SalvaAsync(s.Admin, Guid.NewGuid(), ProviderEmail.Smtp, s.Smtp(), [], null, Ct);
        Assert.Equal(CodiceEsito.NonTrovato, esito.Codice);
    }

    private sealed class Scenario : IAsyncDisposable
    {
        private static readonly string _chiave = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        private readonly DatabaseDiProva _database;

        private Scenario(DatabaseDiProva database, ServiceProvider fornitore, ServerSmtpFinto server, DatiProva.TenantCreato tenant, Guid admin)
        {
            _database = database;
            Fornitore = fornitore;
            Server = server;
            IdTenant = tenant.IdTenant;
            Host = tenant.Host;
            Admin = admin;
        }

        public ServiceProvider Fornitore { get; }

        public ServerSmtpFinto Server { get; }

        public Guid IdTenant { get; }

        public string Host { get; }

        public Guid Admin { get; }

        public static async Task<Scenario> CreaAsync(bool conChiave = true)
        {
            var database = await DatabaseDiProva.CreaAsync(Ct);
            var impostazioni = new Dictionary<string, string?>();
            if (conChiave)
            {
                impostazioni["Sicurezza:ChiaveSegreti"] = _chiave;
            }

            var fornitore = await ServiziProva.CreaConMigrazioniAsync(database.Connessione, impostazioni, annullamento: Ct);
            var tenant = await DatiProva.CreaTenantAsync(fornitore, "A", "a.esempio.it", annullamento: Ct);
            var admin = await DatiProva.CreaUtenzaAsync(fornitore, RuoloUtenza.PlatformAdmin, null, "platform@esempio.it", annullamento: Ct);
            return new Scenario(database, fornitore, new ServerSmtpFinto(), tenant, admin);
        }

        public Dictionary<string, string> Smtp(string? utente = null, string? password = null, string nomeMittente = "Mittente") => new()
        {
            ["Host"] = "127.0.0.1",
            ["Porta"] = Server.Porta.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["ModalitaTls"] = DefinizioniProviderEmail.TlsNessuna,
            ["IndirizzoMittente"] = $"noreply@{Host}",
            ["NomeMittente"] = nomeMittente,
            ["Utente"] = utente ?? string.Empty,
            ["Password"] = password ?? string.Empty,
        };

        public Task<EsitoSalvataggioEmail> SalvaAsync(Dictionary<string, string> valori, IReadOnlyCollection<string>? rimuovi = null, string? token = null, Guid? attore = null) =>
            SalvaAsync(ProviderEmail.Smtp, valori, rimuovi, token, attore);

        public async Task<EsitoSalvataggioEmail> SalvaAsync(ProviderEmail provider, Dictionary<string, string> valori, IReadOnlyCollection<string>? rimuovi = null, string? token = null, Guid? attore = null)
        {
            await using var ambito = Fornitore.CreateAsyncScope();
            return await ambito.ServiceProvider.GetRequiredService<IServizioImpostazioniEmail>()
                .SalvaAsync(attore ?? Admin, IdTenant, provider, valori, rimuovi ?? [], token, Ct);
        }

        public async Task<SchedaEmailTenant?> LeggiAsync(Guid? attore = null)
        {
            await using var ambito = Fornitore.CreateAsyncScope();
            return await ambito.ServiceProvider.GetRequiredService<IServizioImpostazioniEmail>().LeggiAsync(attore ?? Admin, IdTenant, Ct);
        }

        /// <summary>Accoda la prova e la fa elaborare subito, a meno che serva osservare l'accodamento (vedi i test).</summary>
        public async Task<EsitoOperazione> InviaProvaAsync(Guid? attore = null, string destinatario = "prova@esempio.it")
        {
            await using var ambito = Fornitore.CreateAsyncScope();
            var esito = await ambito.ServiceProvider.GetRequiredService<IServizioImpostazioniEmail>()
                .AccodaProvaAsync(attore ?? Admin, IdTenant, destinatario, new MessaggioEmail("x@esempio.it", null, "Prova", "Messaggio di prova"), Ct);
            if (esito.Riuscito && !Server.RifiutaInvio)
            {
                await Fornitore.GetRequiredService<ElaboratoreOutbox>().ElaboraLottoAsync(Ct);
            }

            return esito;
        }

        public async ValueTask DisposeAsync()
        {
            await Server.DisposeAsync();
            await Fornitore.DisposeAsync();
            await _database.DisposeAsync();
        }
    }
}
