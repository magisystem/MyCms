using System.Security.Cryptography;
using System.Text;
using System.Web;
using DvCms.Contracts.Email;
using DvCms.Core.Accessi;
using DvCms.Core.Tenancy;
using DvCms.DTO.Email;
using DvCms.DTO.Piattaforma;
using DvCms.DTO.Tenancy;
using DvCms.Infrastructure.Outbox;
using DvCms.Infrastructure.Persistenza;
using DvCms.IntegrationTests.Supporto;
using DvCms.IntegrationTests.Web;
using DvCms.Models.Accessi;
using DvCms.Models.Email;
using DvCms.Models.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DvCms.IntegrationTests.Email;

/// <summary>
/// Canali email con consenso OAuth (A-010, ADR-0021): l'app OAuth è del tenant e la gestisce solo il suo <c>TenantAdmin</c>;
/// consenso con PKCE, rinnovo dei token, revoca e scollegamento, con Microsoft 365 e Google finti.
/// </summary>
public class ConnessioneEmailOAuthTest
{
    private const string UrlRitorno = "https://a.esempio.it/admin/email/collegamento";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task SoloIlTenantAdminDelTenantLeggeEModificaLApp()
    {
        await using var c = await Contesto.CreaAsync();

        // Platform, TenantAdmin di un altro tenant e acquirente: nessuna lettura e nessuna scrittura.
        foreach (var attore in new[] { c.Platform, c.AdminB, c.Cliente })
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => c.ComeAsync(c.A, s => s.LeggiAsync(attore, Ct)));
            var esito = await c.ComeAsync(c.A, s => s.SalvaAppAsync(attore, ProviderEmail.MicrosoftOAuth, Contesto.AppMicrosoft(), null, false, Ct));
            Assert.Equal(CodiceEsito.NonAutorizzato, esito.Codice);
            Assert.Equal(CodiceEsito.NonAutorizzato, (await c.ComeAsync(c.A, s => s.AvviaCollegamentoAsync(attore, UrlRitorno, Ct))).Codice);
            Assert.Equal(CodiceEsito.NonAutorizzato, (await c.ComeAsync(c.A, s => s.ScollegaAsync(attore, Ct))).Codice);
            Assert.Equal(CodiceEsito.NonAutorizzato, (await c.ComeAsync(c.A, s => s.RimuoviCanaleAsync(attore, Ct))).Codice);
        }

        Assert.Empty(await c.RigheEmailAsync());
        await using var ambito = c.Web.Fabbrica.Services.CreateAsyncScope();
        var negati = await ambito.ServiceProvider.GetRequiredService<ContestoDatabase>().RegistroAudit.IgnoreQueryFilters().CountAsync(a => a.Azione == AzioniAudit.OperazioneNegata, Ct);
        Assert.True(negati >= 12);
    }

    [Fact]
    public async Task SalvaLAppCifrandoIlSegretoENonLoRestituisceMai()
    {
        await using var c = await Contesto.CreaAsync();

        var esito = await c.ComeAsync(c.A, s => s.SalvaAppAsync(c.AdminA, ProviderEmail.MicrosoftOAuth, Contesto.AppMicrosoft(), null, false, Ct));

        Assert.True(esito.Riuscito, string.Join(',', esito.CampiNonValidi));
        var scheda = await c.ComeAsync(c.A, s => s.LeggiAsync(c.AdminA, Ct));
        Assert.Equal(ProviderEmail.MicrosoftOAuth, scheda.CanaleAttivo);
        Assert.Equal("client-di-prova", scheda.App!.Valori["IdClient"]);
        Assert.True(scheda.App.SegretoImpostato);
        Assert.DoesNotContain("SEGRETO-DEL-CLIENT", string.Join('|', scheda.App.Valori.Values), StringComparison.Ordinal);

        var riga = Assert.Single(await c.RigheEmailAsync());
        Assert.DoesNotContain("SEGRETO-DEL-CLIENT", riga.ValoriJson + riga.SegretiProtetti, StringComparison.Ordinal);
        await using var ambito = c.Web.Fabbrica.Services.CreateAsyncScope();
        var audit = await ambito.ServiceProvider.GetRequiredService<ContestoDatabase>().RegistroAudit.IgnoreQueryFilters().SingleAsync(a => a.Azione == AzioniAudit.ModificaAppEmailOAuth, Ct);
        Assert.Equal(c.AdminA, audit.IdUtenzaAttore);
        Assert.DoesNotContain("SEGRETO-DEL-CLIENT", audit.Dettagli, StringComparison.Ordinal);

        // Senza ridigitare il segreto, quello salvato resta; senza identificativo del client il salvataggio è rifiutato.
        var senzaSegreto = Contesto.AppMicrosoft(segreto: string.Empty);
        Assert.True((await c.ComeAsync(c.A, s => s.SalvaAppAsync(c.AdminA, ProviderEmail.MicrosoftOAuth, senzaSegreto, scheda.App.TokenConcorrenza, false, Ct))).Riuscito);
        var mancante = Contesto.AppMicrosoft();
        mancante.Remove("IdClient");
        var rifiutato = await c.ComeAsync(c.A, s => s.SalvaAppAsync(c.AdminA, ProviderEmail.MicrosoftOAuth, mancante, null, false, Ct));
        Assert.Equal(CodiceEsito.DatiNonValidi, rifiutato.Codice);
        Assert.Contains("IdClient", rifiutato.CampiNonValidi);
        Assert.Equal(CodiceEsito.DatiNonValidi, (await c.ComeAsync(c.A, s => s.SalvaAppAsync(c.AdminA, ProviderEmail.Smtp, Contesto.AppMicrosoft(), null, false, Ct))).Codice);
    }

    [Fact]
    public async Task IlPlatformAdminVedeIlCanaleMaNonLAppENonPuoSovrascriverlo()
    {
        await using var c = await Contesto.CreaAsync();
        Assert.True((await c.ComeAsync(c.A, s => s.SalvaAppAsync(c.AdminA, ProviderEmail.GoogleOAuth, Contesto.AppGoogle(), null, false, Ct))).Riuscito);

        await using var ambito = c.Web.Fabbrica.Services.CreateAsyncScope();
        var piattaforma = ambito.ServiceProvider.GetRequiredService<IServizioImpostazioniEmail>();
        var scheda = (await piattaforma.LeggiAsync(c.Platform, c.A.IdTenant, Ct))!;
        Assert.Equal(ProviderEmail.GoogleOAuth, scheda.Impostazioni!.Provider);
        Assert.Empty(scheda.Impostazioni.Valori);
        Assert.Empty(scheda.Impostazioni.SegretiImpostati);

        var smtp = new Dictionary<string, string> { ["Host"] = "smtp.esempio.it", ["Porta"] = "587", ["ModalitaTls"] = "StartTls", ["IndirizzoMittente"] = "noreply@esempio.it" };
        Assert.Equal(CodiceEsito.NonConsentito, (await piattaforma.SalvaAsync(c.Platform, c.A.IdTenant, ProviderEmail.Smtp, smtp, [], null, Ct)).Codice);
        Assert.Equal(CodiceEsito.NonConsentito, (await piattaforma.SalvaAsync(c.Platform, c.A.IdTenant, ProviderEmail.MicrosoftOAuth, Contesto.AppMicrosoft(), [], null, Ct)).Codice);
        Assert.Equal(ProviderEmail.GoogleOAuth, Assert.Single(await c.RigheEmailAsync()).Provider);

        // Tolto il canale dal TenantAdmin, la piattaforma può configurarne un altro.
        Assert.True((await c.ComeAsync(c.A, s => s.RimuoviCanaleAsync(c.AdminA, Ct))).Riuscito);
        Assert.True((await piattaforma.SalvaAsync(c.Platform, c.A.IdTenant, ProviderEmail.Smtp, smtp, [], null, Ct)).Riuscito);
    }

    [Fact]
    public async Task SostituireUnCanaleDellaPiattaformaRichiedeLaConferma()
    {
        await using var c = await Contesto.CreaAsync();
        await using (var ambito = c.Web.Fabbrica.Services.CreateAsyncScope())
        {
            var smtp = new Dictionary<string, string> { ["Host"] = "smtp.esempio.it", ["Porta"] = "587", ["ModalitaTls"] = "StartTls", ["IndirizzoMittente"] = "noreply@esempio.it" };
            Assert.True((await ambito.ServiceProvider.GetRequiredService<IServizioImpostazioniEmail>().SalvaAsync(c.Platform, c.A.IdTenant, ProviderEmail.Smtp, smtp, [], null, Ct)).Riuscito);
        }

        var scheda = await c.ComeAsync(c.A, s => s.LeggiAsync(c.AdminA, Ct));
        Assert.Equal(ProviderEmail.Smtp, scheda.CanaleAttivo);
        Assert.Null(scheda.App);

        Assert.Equal(CodiceEsito.NonConsentito, (await c.ComeAsync(c.A, s => s.SalvaAppAsync(c.AdminA, ProviderEmail.MicrosoftOAuth, Contesto.AppMicrosoft(), null, false, Ct))).Codice);
        Assert.Equal(ProviderEmail.Smtp, Assert.Single(await c.RigheEmailAsync()).Provider);
        Assert.Equal(CodiceEsito.NonConsentito, (await c.ComeAsync(c.A, s => s.RimuoviCanaleAsync(c.AdminA, Ct))).Codice);
        Assert.True((await c.ComeAsync(c.A, s => s.SalvaAppAsync(c.AdminA, ProviderEmail.MicrosoftOAuth, Contesto.AppMicrosoft(), null, true, Ct))).Riuscito);
        Assert.Equal(ProviderEmail.MicrosoftOAuth, Assert.Single(await c.RigheEmailAsync()).Provider);
    }

    [Fact]
    public async Task IlConsensoMicrosoftUsaPkceEStatoEFaPartireLEmailDallaCasellaCollegata()
    {
        await using var c = await Contesto.CreaAsync();
        Assert.True((await c.ComeAsync(c.A, s => s.SalvaAppAsync(c.AdminA, ProviderEmail.MicrosoftOAuth, Contesto.AppMicrosoft(), null, false, Ct))).Riuscito);

        var avvio = await c.ComeAsync(c.A, s => s.AvviaCollegamentoAsync(c.AdminA, UrlRitorno, Ct));

        Assert.True(avvio.Riuscito);
        var url = new Uri(avvio.UrlAutorizzazione!);
        Assert.Equal("login.microsoftonline.com", url.Host);
        Assert.StartsWith("/tenant-di-prova/oauth2/v2.0/authorize", url.AbsolutePath, StringComparison.Ordinal);
        var query = HttpUtility.ParseQueryString(url.Query);
        Assert.Equal("client-di-prova", query["client_id"]);
        Assert.Equal("code", query["response_type"]);
        Assert.Equal(UrlRitorno, query["redirect_uri"]);
        Assert.Equal("S256", query["code_challenge_method"]);
        Assert.Contains("Mail.Send", query["scope"], StringComparison.Ordinal);
        Assert.Contains("offline_access", query["scope"], StringComparison.Ordinal);
        Assert.DoesNotContain("SEGRETO-DEL-CLIENT", avvio.UrlAutorizzazione, StringComparison.Ordinal);
        Assert.DoesNotContain("SEGRETO-DEL-CLIENT", avvio.StatoProtetto, StringComparison.Ordinal);

        var esito = await c.ComeAsync(c.A, s => s.CompletaCollegamentoAsync(c.AdminA, UrlRitorno, ProviderOAuthFinto.CodiceValido, query["state"], null, avvio.StatoProtetto, Ct));

        Assert.True(esito.Riuscito, esito.Codice.ToString());
        Assert.Equal("mittente@esempio.it", esito.Casella);
        // Lo scambio del codice presenta il verificatore PKCE che corrisponde alla sfida data all'autorizzazione.
        Assert.Equal(Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(c.Provider.UltimoVerificatore!))), query["code_challenge"]);

        var scheda = await c.ComeAsync(c.A, s => s.LeggiAsync(c.AdminA, Ct));
        Assert.Equal(StatoConnessioneEmail.Attiva, scheda.Connessione!.Stato);
        Assert.Equal("mittente@esempio.it", scheda.Connessione.Casella);
        Assert.True(scheda.Connessione.CorrispondeAllaApp);

        await using var ambito = c.Web.Fabbrica.Services.CreateAsyncScope();
        var connessione = await ambito.ServiceProvider.GetRequiredService<ContestoDatabase>().ConnessioniEmailOAuth.IgnoreQueryFilters().SingleAsync(Ct);
        Assert.DoesNotContain("RINNOVO-0", connessione.TokenAggiornamentoProtetto, StringComparison.Ordinal);
        Assert.DoesNotContain("ACCESSO-1", connessione.TokenAccessoProtetto!, StringComparison.Ordinal);
        Assert.Equal(c.AdminA, connessione.CollegataDaIdUtenza);

        // L'email di prova parte dalla casella collegata con il token appena ottenuto, senza rinnovarlo.
        var prova = await c.ComeAsync(c.A, s => s.AccodaProvaAsync(c.AdminA, "dest@esempio.it", new MessaggioEmail("x@esempio.it", null, "Prova", "Corpo"), Ct));
        Assert.True(prova.Riuscito);
        await c.Web.Fabbrica.Services.GetRequiredService<ElaboratoreOutbox>().ElaboraLottoAsync(Ct);
        var invio = Assert.Single(c.Provider.Richieste, r => r.Indirizzo.AbsolutePath == "/v1.0/me/sendMail");
        Assert.Equal("Bearer ACCESSO-1", invio.Autorizzazione);
        Assert.Contains("dest@esempio.it", invio.Corpo, StringComparison.Ordinal);
        Assert.Equal(0, c.Provider.ContaRinnovi());
    }

    [Fact]
    public async Task IlConsensoGoogleInviaIlMessaggioMimeDallaCasellaCollegata()
    {
        await using var c = await Contesto.CreaAsync();
        c.Provider.Casella = "ufficio@gmail.com";
        Assert.True((await c.ComeAsync(c.A, s => s.SalvaAppAsync(c.AdminA, ProviderEmail.GoogleOAuth, Contesto.AppGoogle(), null, false, Ct))).Riuscito);

        var avvio = await c.ComeAsync(c.A, s => s.AvviaCollegamentoAsync(c.AdminA, UrlRitorno, Ct));
        var query = HttpUtility.ParseQueryString(new Uri(avvio.UrlAutorizzazione!).Query);
        Assert.Equal("accounts.google.com", new Uri(avvio.UrlAutorizzazione!).Host);
        Assert.Equal("offline", query["access_type"]);
        Assert.Equal("consent", query["prompt"]);
        Assert.Contains("https://www.googleapis.com/auth/gmail.send", query["scope"], StringComparison.Ordinal);

        var esito = await c.ComeAsync(c.A, s => s.CompletaCollegamentoAsync(c.AdminA, UrlRitorno, ProviderOAuthFinto.CodiceValido, query["state"], null, avvio.StatoProtetto, Ct));
        Assert.True(esito.Riuscito, esito.Codice.ToString());
        Assert.Equal("ufficio@gmail.com", esito.Casella);

        await c.ComeAsync(c.A, s => s.AccodaProvaAsync(c.AdminA, "dest@esempio.it", new MessaggioEmail("x@esempio.it", null, "Prova", "Corpo"), Ct));
        await c.Web.Fabbrica.Services.GetRequiredService<ElaboratoreOutbox>().ElaboraLottoAsync(Ct);
        var invio = Assert.Single(c.Provider.Richieste, r => r.Indirizzo.AbsolutePath == "/gmail/v1/users/me/messages/send");
        Assert.Equal("Bearer ACCESSO-1", invio.Autorizzazione);
        var raw = System.Text.Json.JsonDocument.Parse(invio.Corpo).RootElement.GetProperty("raw").GetString()!;
        var mime = Encoding.UTF8.GetString(Convert.FromBase64String(raw.Replace('-', '+').Replace('_', '/').PadRight(raw.Length + ((4 - (raw.Length % 4)) % 4), '=')));
        Assert.Contains("ufficio@gmail.com", mime, StringComparison.Ordinal);
        Assert.Contains("dest@esempio.it", mime, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnRitornoNonValidoNonCollegaNienteEIlConsensoNegatoNonLascia()
    {
        await using var c = await Contesto.CreaAsync();
        var secondoAdmin = await DatiProva.CreaUtenzaAsync(c.Web.Fabbrica.Services, RuoloUtenza.TenantAdmin, c.A.IdTenant, "secondo@esempio.it", annullamento: Ct);
        Assert.True((await c.ComeAsync(c.A, s => s.SalvaAppAsync(c.AdminA, ProviderEmail.MicrosoftOAuth, Contesto.AppMicrosoft(), null, false, Ct))).Riuscito);
        var avvio = await c.ComeAsync(c.A, s => s.AvviaCollegamentoAsync(c.AdminA, UrlRitorno, Ct));
        var stato = HttpUtility.ParseQueryString(new Uri(avvio.UrlAutorizzazione!).Query)["state"];

        var casi = new (string Nome, Guid Attore, string? Codice, string? Stato, string? Errore, string? Cookie, string Ritorno)[]
        {
            ("stato diverso", c.AdminA, ProviderOAuthFinto.CodiceValido, "stato-falso", null, avvio.StatoProtetto, UrlRitorno),
            ("senza stato", c.AdminA, ProviderOAuthFinto.CodiceValido, null, null, avvio.StatoProtetto, UrlRitorno),
            ("senza cookie", c.AdminA, ProviderOAuthFinto.CodiceValido, stato, null, null, UrlRitorno),
            ("cookie manomesso", c.AdminA, ProviderOAuthFinto.CodiceValido, stato, null, "manomesso", UrlRitorno),
            ("altro amministratore", secondoAdmin, ProviderOAuthFinto.CodiceValido, stato, null, avvio.StatoProtetto, UrlRitorno),
            ("altro indirizzo di ritorno", c.AdminA, ProviderOAuthFinto.CodiceValido, stato, null, avvio.StatoProtetto, "https://a.esempio.it/altro"),
            ("consenso rifiutato", c.AdminA, null, stato, "access_denied", avvio.StatoProtetto, UrlRitorno),
            ("senza codice", c.AdminA, null, stato, null, avvio.StatoProtetto, UrlRitorno),
        };
        foreach (var caso in casi)
        {
            var esito = await c.ComeAsync(c.A, s => s.CompletaCollegamentoAsync(caso.Attore, caso.Ritorno, caso.Codice, caso.Stato, caso.Errore, caso.Cookie, Ct));
            Assert.True(esito.Codice == CodiceEsito.ConsensoNegato, caso.Nome);
        }

        Assert.Empty(c.Provider.Richieste);
        Assert.Equal(0, await c.ContaConnessioniAsync());
    }

    [Fact]
    public async Task UnCodiceRifiutatoOUnPermessoDiInvioMancanteNonCreanoIlCollegamento()
    {
        await using var c = await Contesto.CreaAsync();
        Assert.True((await c.ComeAsync(c.A, s => s.SalvaAppAsync(c.AdminA, ProviderEmail.GoogleOAuth, Contesto.AppGoogle(), null, false, Ct))).Riuscito);

        // Codice sbagliato: il provider risponde invalid_grant.
        var avvio = await c.ComeAsync(c.A, s => s.AvviaCollegamentoAsync(c.AdminA, UrlRitorno, Ct));
        var stato = HttpUtility.ParseQueryString(new Uri(avvio.UrlAutorizzazione!).Query)["state"];
        Assert.Equal(CodiceEsito.ServizioEsternoNonRiuscito, (await c.ComeAsync(c.A, s => s.CompletaCollegamentoAsync(c.AdminA, UrlRitorno, "codice-sbagliato", stato, null, avvio.StatoProtetto, Ct))).Codice);

        // L'utente ha tolto il permesso di invio dalla schermata del consenso.
        c.Provider.AmbitiConcessi = "openid https://www.googleapis.com/auth/userinfo.email";
        avvio = await c.ComeAsync(c.A, s => s.AvviaCollegamentoAsync(c.AdminA, UrlRitorno, Ct));
        stato = HttpUtility.ParseQueryString(new Uri(avvio.UrlAutorizzazione!).Query)["state"];
        Assert.Equal(CodiceEsito.ConsensoNegato, (await c.ComeAsync(c.A, s => s.CompletaCollegamentoAsync(c.AdminA, UrlRitorno, ProviderOAuthFinto.CodiceValido, stato, null, avvio.StatoProtetto, Ct))).Codice);

        // Il provider non ha emesso un refresh token.
        c.Provider.AmbitiConcessi = null;
        c.Provider.EmetteRefreshToken = false;
        avvio = await c.ComeAsync(c.A, s => s.AvviaCollegamentoAsync(c.AdminA, UrlRitorno, Ct));
        stato = HttpUtility.ParseQueryString(new Uri(avvio.UrlAutorizzazione!).Query)["state"];
        Assert.Equal(CodiceEsito.ServizioEsternoNonRiuscito, (await c.ComeAsync(c.A, s => s.CompletaCollegamentoAsync(c.AdminA, UrlRitorno, ProviderOAuthFinto.CodiceValido, stato, null, avvio.StatoProtetto, Ct))).Codice);

        Assert.Equal(0, await c.ContaConnessioniAsync());
    }

    [Fact]
    public async Task IlTokenScadutoSiRinnovaRuotandoIlRefreshTokenEUnoValidoSiRiusa()
    {
        await using var c = await Contesto.CreaAsync();
        await c.CollegaAsync(ProviderEmail.MicrosoftOAuth);

        await c.InviaProvaAsync();
        Assert.Equal(0, c.Provider.ContaRinnovi());

        await c.ScadiAccessoAsync();
        await c.InviaProvaAsync();
        Assert.Equal(1, c.Provider.ContaRinnovi());
        Assert.Equal("RINNOVO-0", c.Provider.UltimoRefreshTokenUsato);
        Assert.Equal("Bearer ACCESSO-2", c.Provider.Richieste.Last(r => r.Indirizzo.AbsolutePath == "/v1.0/me/sendMail").Autorizzazione);

        // Il nuovo token di accesso è valido: nessun altro rinnovo.
        await c.InviaProvaAsync();
        Assert.Equal(1, c.Provider.ContaRinnovi());

        // Microsoft ha ruotato il refresh token: il prossimo rinnovo usa quello nuovo.
        await c.ScadiAccessoAsync();
        await c.InviaProvaAsync();
        Assert.Equal("RINNOVO-1", c.Provider.UltimoRefreshTokenUsato);
        Assert.Equal(4, c.Provider.ContaInvii());
    }

    [Fact]
    public async Task IlConsensoRevocatoFermaGliInviSenzaRichiamareIlProviderFinoAlRicollegamento()
    {
        await using var c = await Contesto.CreaAsync();
        await c.CollegaAsync(ProviderEmail.MicrosoftOAuth);
        await c.ScadiAccessoAsync();
        c.Provider.ConsensoRevocato = true;

        await c.InviaProvaAsync();

        Assert.Equal(0, c.Provider.ContaInvii());
        var scheda = await c.ComeAsync(c.A, s => s.LeggiAsync(c.AdminA, Ct));
        Assert.Equal(StatoConnessioneEmail.Revocata, scheda.Connessione!.Stato);
        Assert.Equal("invalid_grant", scheda.Connessione.UltimoErrore);
        Assert.DoesNotContain("DESCRIZIONE-RISERVATA", scheda.Connessione.UltimoErrore, StringComparison.Ordinal);
        Assert.Equal(1, c.Provider.ContaRinnovi());

        // Gli invii successivi falliscono subito, senza richiamare il provider; l'errore è visibile al TenantAdmin.
        await c.InviaProvaAsync(verificaAccodamento: false);
        Assert.Equal(1, c.Provider.ContaRinnovi());
        var dopo = await c.ComeAsync(c.A, s => s.LeggiAsync(c.AdminA, Ct));
        Assert.NotEmpty(dopo.Problemi);
        Assert.Contains("revocato", dopo.Problemi[0].UltimoErrore, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(CodiceEsito.NonConsentito, (await c.ComeAsync(c.A, s => s.AccodaProvaAsync(c.AdminA, "d@esempio.it", new MessaggioEmail("x@esempio.it", null, "P", "C"), Ct))).Codice);

        // Ricollegando la casella il canale riparte.
        c.Provider.ConsensoRevocato = false;
        await c.CompletaConsensoAsync();
        Assert.Equal(StatoConnessioneEmail.Attiva, (await c.ComeAsync(c.A, s => s.LeggiAsync(c.AdminA, Ct))).Connessione!.Stato);
    }

    [Fact]
    public async Task CambiareClientInvalidaIlCollegamentoMaRuotareSoloIlSegretoNo()
    {
        await using var c = await Contesto.CreaAsync();
        await c.CollegaAsync(ProviderEmail.MicrosoftOAuth);
        var scheda = await c.ComeAsync(c.A, s => s.LeggiAsync(c.AdminA, Ct));

        // Nuovo segreto, stesso client: il consenso vale ancora.
        Assert.True((await c.ComeAsync(c.A, s => s.SalvaAppAsync(c.AdminA, ProviderEmail.MicrosoftOAuth, Contesto.AppMicrosoft(segreto: "NUOVO-SEGRETO"), scheda.App!.TokenConcorrenza, false, Ct))).Riuscito);
        Assert.Equal(1, await c.ContaConnessioniAsync());

        // Un altro client: il consenso era per l'app precedente e si elimina.
        scheda = await c.ComeAsync(c.A, s => s.LeggiAsync(c.AdminA, Ct));
        var altra = Contesto.AppMicrosoft();
        altra["IdClient"] = "altro-client";
        Assert.True((await c.ComeAsync(c.A, s => s.SalvaAppAsync(c.AdminA, ProviderEmail.MicrosoftOAuth, altra, scheda.App!.TokenConcorrenza, false, Ct))).Riuscito);
        Assert.Equal(0, await c.ContaConnessioniAsync());
    }

    [Fact]
    public async Task ScollegareRevocaPressoGoogleEdEliminaIlCollegamentoLasciandoLApp()
    {
        await using var c = await Contesto.CreaAsync();
        await c.CollegaAsync(ProviderEmail.GoogleOAuth);

        var esito = await c.ComeAsync(c.A, s => s.ScollegaAsync(c.AdminA, Ct));

        Assert.True(esito.Riuscito);
        Assert.Equal(0, await c.ContaConnessioniAsync());
        Assert.Single(c.Provider.Richieste, r => r.Indirizzo.AbsolutePath == "/revoke");
        var scheda = await c.ComeAsync(c.A, s => s.LeggiAsync(c.AdminA, Ct));
        Assert.NotNull(scheda.App);
        Assert.Null(scheda.Connessione);
        Assert.Equal(CodiceEsito.NonTrovato, (await c.ComeAsync(c.A, s => s.ScollegaAsync(c.AdminA, Ct))).Codice);
    }

    [Fact]
    public async Task UnTenantNonVedeNeUsaLaCasellaDiUnAltro()
    {
        await using var c = await Contesto.CreaAsync();
        await c.CollegaAsync(ProviderEmail.MicrosoftOAuth);

        // Nel contesto del tenant B il TenantAdmin di B non trova nulla del tenant A e quello di A non ha alcun ruolo.
        var schedaB = await c.ComeAsync(c.B, s => s.LeggiAsync(c.AdminB, Ct));
        Assert.Null(schedaB.CanaleAttivo);
        Assert.Null(schedaB.App);
        Assert.Null(schedaB.Connessione);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => c.ComeAsync(c.B, s => s.LeggiAsync(c.AdminA, Ct)));
        Assert.Equal(CodiceEsito.NonConsentito, (await c.ComeAsync(c.B, s => s.AccodaProvaAsync(c.AdminB, "d@esempio.it", new MessaggioEmail("x@esempio.it", null, "P", "C"), Ct))).Codice);
        Assert.Equal(CodiceEsito.NonConsentito, (await c.ComeAsync(c.B, s => s.AvviaCollegamentoAsync(c.AdminB, "https://b.esempio.it/admin/email/collegamento", Ct))).Codice);
    }

    private static string Base64Url(byte[] dati) => Convert.ToBase64String(dati).Replace('+', '-').Replace('/', '_').TrimEnd('=');

    private sealed class Contesto : IAsyncDisposable
    {
        private Contesto(ScenarioWeb web, ProviderOAuthFinto provider)
        {
            Web = web;
            Provider = provider;
        }

        public ScenarioWeb Web { get; }

        public ProviderOAuthFinto Provider { get; }

        public DatiProva.TenantCreato A => Web.A;

        public DatiProva.TenantCreato B => Web.B;

        public Guid Platform { get; private set; }

        public Guid AdminA { get; private set; }

        public Guid AdminB { get; private set; }

        public Guid Cliente { get; private set; }

        public static async Task<Contesto> CreaAsync()
        {
            var provider = new ProviderOAuthFinto();
            var impostazioni = new Dictionary<string, string?> { ["Sicurezza:ChiaveSegreti"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) };
            var c = new Contesto(await ScenarioWeb.CreaAsync(impostazioni: impostazioni, oauthEmail: provider), provider);
            c.Platform = await c.Web.CreaPlatformAdminAsync("root@esempio.it");
            c.AdminA = await c.Web.CreaTenantAdminAsync("anna@esempio.it");
            c.AdminB = await DatiProva.CreaUtenzaAsync(c.Web.Fabbrica.Services, RuoloUtenza.TenantAdmin, c.B.IdTenant, "bruno@esempio.it", annullamento: Ct);
            c.Cliente = await DatiProva.CreaUtenzaAsync(c.Web.Fabbrica.Services, RuoloUtenza.Utente, c.A.IdTenant, "cliente@esempio.it", annullamento: Ct);
            return c;
        }

        public static Dictionary<string, string> AppMicrosoft(string segreto = "SEGRETO-DEL-CLIENT") => new()
        {
            ["TenantMicrosoft"] = "tenant-di-prova",
            ["IdClient"] = "client-di-prova",
            ["SegretoClient"] = segreto,
        };

        public static Dictionary<string, string> AppGoogle(string segreto = "SEGRETO-DEL-CLIENT") => new()
        {
            ["IdClient"] = "client-google.apps.googleusercontent.com",
            ["SegretoClient"] = segreto,
        };

        public async Task<T> ComeAsync<T>(DatiProva.TenantCreato tenant, Func<IServizioConnessioneEmail, Task<T>> operazione)
        {
            await using var ambito = Web.Fabbrica.Services.CreateAsyncScope();
            ambito.ServiceProvider.GetRequiredService<ContestoTenant>().Imposta(new DatiTenantRisolto(tenant.IdTenant, tenant.IdSito, tenant.IdDominio, tenant.Host, false));
            return await operazione(ambito.ServiceProvider.GetRequiredService<IServizioConnessioneEmail>());
        }

        /// <summary>Salva l'app e completa il consenso, come farebbe il TenantAdmin del tenant A.</summary>
        public async Task CollegaAsync(ProviderEmail provider)
        {
            var app = provider == ProviderEmail.MicrosoftOAuth ? AppMicrosoft() : AppGoogle();
            var esito = await ComeAsync(A, s => s.SalvaAppAsync(AdminA, provider, app, null, false, Ct));
            Assert.True(esito.Riuscito, string.Join(',', esito.CampiNonValidi));
            await CompletaConsensoAsync();
        }

        public async Task CompletaConsensoAsync()
        {
            var avvio = await ComeAsync(A, s => s.AvviaCollegamentoAsync(AdminA, UrlRitorno, Ct));
            var stato = HttpUtility.ParseQueryString(new Uri(avvio.UrlAutorizzazione!).Query)["state"];
            var esito = await ComeAsync(A, s => s.CompletaCollegamentoAsync(AdminA, UrlRitorno, ProviderOAuthFinto.CodiceValido, stato, null, avvio.StatoProtetto, Ct));
            Assert.True(esito.Riuscito, esito.Codice.ToString());
        }

        public async Task InviaProvaAsync(bool verificaAccodamento = true)
        {
            var esito = await ComeAsync(A, s => s.AccodaProvaAsync(AdminA, "dest@esempio.it", new MessaggioEmail("x@esempio.it", null, "Prova", "Corpo"), Ct));
            if (verificaAccodamento)
            {
                Assert.True(esito.Riuscito, esito.Codice.ToString());
            }
            else if (!esito.Riuscito)
            {
                // Con il collegamento revocato la prova non si accoda: si inserisce direttamente un messaggio nell'outbox.
                await using var ambito = Web.Fabbrica.Services.CreateAsyncScope();
                ambito.ServiceProvider.GetRequiredService<IAccodatoreEmail>().Accoda(A.IdTenant, new MessaggioEmail("dest@esempio.it", null, "Prova", "Corpo"));
                await ambito.ServiceProvider.GetRequiredService<ContestoDatabase>().SaveChangesAsync(Ct);
            }

            await Web.Fabbrica.Services.GetRequiredService<ElaboratoreOutbox>().ElaboraLottoAsync(Ct);
        }

        public async Task ScadiAccessoAsync()
        {
            await using var ambito = Web.Fabbrica.Services.CreateAsyncScope();
            await ambito.ServiceProvider.GetRequiredService<ContestoDatabase>().ConnessioniEmailOAuth.IgnoreQueryFilters()
                .ExecuteUpdateAsync(x => x.SetProperty(c => c.ScadenzaAccessoUtc, DateTime.UtcNow.AddHours(-1)), Ct);
        }

        public async Task<int> ContaConnessioniAsync()
        {
            await using var ambito = Web.Fabbrica.Services.CreateAsyncScope();
            return await ambito.ServiceProvider.GetRequiredService<ContestoDatabase>().ConnessioniEmailOAuth.IgnoreQueryFilters().CountAsync(Ct);
        }

        public async Task<List<ImpostazioniEmailTenant>> RigheEmailAsync()
        {
            await using var ambito = Web.Fabbrica.Services.CreateAsyncScope();
            return await ambito.ServiceProvider.GetRequiredService<ContestoDatabase>().ImpostazioniEmailTenant.IgnoreQueryFilters().AsNoTracking().ToListAsync(Ct);
        }

        public ValueTask DisposeAsync() => Web.DisposeAsync();
    }
}
