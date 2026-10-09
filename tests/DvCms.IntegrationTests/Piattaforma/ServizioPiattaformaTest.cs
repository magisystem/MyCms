using DvCms.Application.Accessi;
using DvCms.Application.Piattaforma;
using DvCms.Application.Tenancy;
using DvCms.Domain.Accessi;
using DvCms.Domain.Tenancy;
using DvCms.Infrastructure.Persistenza;
using DvCms.IntegrationTests.Supporto;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DvCms.IntegrationTests.Piattaforma;

/// <summary>Menu "Platform" (B-006) e creazione/elevazione dei TenantAdmin con non-escalation (B-007) su SQL Server reale.</summary>
public class ServizioPiattaformaTest
{
    [Fact]
    public async Task CreaTenantConSitoDominioVerificatoEProfiloGuest()
    {
        await using var s = await Scenario.CreaAsync();

        var esito = await s.ChiamaAsync(servizio => servizio.CreaTenantAsync(
            s.IdPlatformAdmin,
            new DatiNuovoTenant("Cliente Uno", "Sito Uno", "Europe/Rome", "Cliente.Esempio.IT", DominioVerificato: true),
            TestContext.Current.CancellationToken));

        Assert.True(esito.Riuscito);
        await using var ambito = s.Fornitore.CreateAsyncScope();
        var contesto = ambito.ServiceProvider.GetRequiredService<ContestoDatabase>();
        var tenant = await contesto.Tenant.IgnoreQueryFilters().SingleAsync(t => t.Id == esito.Id, TestContext.Current.CancellationToken);
        Assert.False(tenant.Primario);
        Assert.Equal("Cliente Uno", tenant.Nome);
        Assert.Single(await contesto.ProfiliGuest.IgnoreQueryFilters().Where(p => p.IdTenant == tenant.Id).ToListAsync(TestContext.Current.CancellationToken));
        var dominio = await contesto.DominiSito.IgnoreQueryFilters().SingleAsync(d => d.IdTenant == tenant.Id, TestContext.Current.CancellationToken);
        Assert.Equal("cliente.esempio.it", dominio.Host);
        Assert.NotNull(dominio.VerificatoIlUtc);
        Assert.Equal(s.IdPlatformAdmin, dominio.VerificatoDaIdUtenza);
        var voce = await contesto.RegistroAudit.IgnoreQueryFilters().SingleAsync(a => a.Azione == AzioniAudit.CreazioneTenant, TestContext.Current.CancellationToken);
        Assert.Equal(s.IdPlatformAdmin, voce.IdUtenzaAttore);
        Assert.Equal(tenant.Id, voce.IdTenant);

        var risoluzione = await ambito.ServiceProvider.GetRequiredService<IRisolutoreTenant>().RisolviAsync("cliente.esempio.it", TestContext.Current.CancellationToken);
        Assert.Equal(EsitoRisoluzioneTenant.Risolto, risoluzione.Esito);
        Assert.Equal(tenant.Id, risoluzione.Dati!.IdTenant);
    }

    [Theory]
    [InlineData("", "Sito", "cliente.esempio.it")]
    [InlineData("Cliente", "", "cliente.esempio.it")]
    [InlineData("Cliente", "Sito", "")]
    [InlineData("Cliente", "Sito", "localhost")]
    [InlineData("Cliente", "Sito", "192.168.1.10")]
    [InlineData("Cliente", "Sito", "host non valido.it")]
    public async Task CreaTenantRifiutaDatiNonValidi(string nome, string nomeSito, string host)
    {
        await using var s = await Scenario.CreaAsync();

        var esito = await s.ChiamaAsync(servizio => servizio.CreaTenantAsync(
            s.IdPlatformAdmin, new DatiNuovoTenant(nome, nomeSito, null, host, true), TestContext.Current.CancellationToken));

        Assert.Equal(CodiceEsito.DatiNonValidi, esito.Codice);
        Assert.Equal(3, await s.ContaAsync(contesto => contesto.Tenant.IgnoreQueryFilters().CountAsync(TestContext.Current.CancellationToken)));
    }

    [Fact]
    public async Task CreaTenantRifiutaUnFusoOrarioSconosciuto()
    {
        await using var s = await Scenario.CreaAsync();

        var esito = await s.ChiamaAsync(servizio => servizio.CreaTenantAsync(
            s.IdPlatformAdmin, new DatiNuovoTenant("Cliente", "Sito", "Marte/Olympus", "cliente.esempio.it", true), TestContext.Current.CancellationToken));

        Assert.Equal(CodiceEsito.DatiNonValidi, esito.Codice);
    }

    [Fact]
    public async Task CreaTenantRifiutaUnHostGiaRegistrato()
    {
        await using var s = await Scenario.CreaAsync();

        var esito = await s.ChiamaAsync(servizio => servizio.CreaTenantAsync(
            s.IdPlatformAdmin, new DatiNuovoTenant("Altro", "Sito", null, "A.Esempio.It", true), TestContext.Current.CancellationToken));

        Assert.Equal(CodiceEsito.GiaPresente, esito.Codice);
        Assert.Equal(3, await s.ContaAsync(contesto => contesto.Tenant.IgnoreQueryFilters().CountAsync(TestContext.Current.CancellationToken)));
    }

    [Fact]
    public async Task UnDominioNonVerificatoNonRisolveFinoAllaVerifica()
    {
        await using var s = await Scenario.CreaAsync();
        var creato = await s.ChiamaAsync(servizio => servizio.CreaTenantAsync(
            s.IdPlatformAdmin, new DatiNuovoTenant("Cliente", "Sito", null, "cliente.esempio.it", DominioVerificato: false), TestContext.Current.CancellationToken));
        Assert.Equal(EsitoRisoluzioneTenant.NonRegistrato, (await s.RisolviAsync("cliente.esempio.it")).Esito);

        var dettaglio = await s.ChiamaAsync(servizio => servizio.LeggiTenantAsync(s.IdPlatformAdmin, creato.Id!.Value, TestContext.Current.CancellationToken));
        var dominio = dettaglio!.Siti.Single().Domini.Single();
        Assert.Null(dominio.VerificatoIlUtc);
        var verifica = await s.ChiamaAsync(servizio => servizio.VerificaDominioAsync(s.IdPlatformAdmin, creato.Id!.Value, dominio.Id, TestContext.Current.CancellationToken));

        Assert.True(verifica.Riuscito);
        Assert.Equal(EsitoRisoluzioneTenant.Risolto, (await s.RisolviAsync("cliente.esempio.it")).Esito);
        dettaglio = await s.ChiamaAsync(servizio => servizio.LeggiTenantAsync(s.IdPlatformAdmin, creato.Id!.Value, TestContext.Current.CancellationToken));
        var verificato = dettaglio!.Siti.Single().Domini.Single();
        Assert.NotNull(verificato.VerificatoIlUtc);
        Assert.Equal("platform@esempio.it", verificato.VerificatoDa);
        var seconda = await s.ChiamaAsync(servizio => servizio.VerificaDominioAsync(s.IdPlatformAdmin, creato.Id!.Value, dominio.Id, TestContext.Current.CancellationToken));
        Assert.Equal(CodiceEsito.NonConsentito, seconda.Codice);
    }

    [Fact]
    public async Task DisattivareUnDominioLoFaSmettereDiRisolvere()
    {
        await using var s = await Scenario.CreaAsync();

        var disattiva = await s.ChiamaAsync(servizio => servizio.ImpostaDominioAttivoAsync(s.IdPlatformAdmin, s.A.IdTenant, s.A.IdDominio, false, TestContext.Current.CancellationToken));

        Assert.True(disattiva.Riuscito);
        Assert.Equal(EsitoRisoluzioneTenant.NonRegistrato, (await s.RisolviAsync(ScenarioHostA)).Esito);
        var riattiva = await s.ChiamaAsync(servizio => servizio.ImpostaDominioAttivoAsync(s.IdPlatformAdmin, s.A.IdTenant, s.A.IdDominio, true, TestContext.Current.CancellationToken));
        Assert.True(riattiva.Riuscito);
        Assert.Equal(EsitoRisoluzioneTenant.Risolto, (await s.RisolviAsync(ScenarioHostA)).Esito);
    }

    [Fact]
    public async Task UnDominioNonSiMuoveConIlTenantSbagliato()
    {
        await using var s = await Scenario.CreaAsync();

        var esito = await s.ChiamaAsync(servizio => servizio.ImpostaDominioAttivoAsync(s.IdPlatformAdmin, s.B.IdTenant, s.A.IdDominio, false, TestContext.Current.CancellationToken));

        Assert.Equal(CodiceEsito.NonTrovato, esito.Codice);
    }

    [Fact]
    public async Task AggiungeUnDominioAUnSitoEnonAccettaUnSitoDiUnAltroTenant()
    {
        await using var s = await Scenario.CreaAsync();

        var buono = await s.ChiamaAsync(servizio => servizio.AggiungiDominioAsync(s.IdPlatformAdmin, s.A.IdTenant, s.A.IdSito, "www.a.esempio.it", true, TestContext.Current.CancellationToken));
        var sbagliato = await s.ChiamaAsync(servizio => servizio.AggiungiDominioAsync(s.IdPlatformAdmin, s.B.IdTenant, s.A.IdSito, "www.b.esempio.it", true, TestContext.Current.CancellationToken));
        var doppio = await s.ChiamaAsync(servizio => servizio.AggiungiDominioAsync(s.IdPlatformAdmin, s.A.IdTenant, s.A.IdSito, "WWW.A.esempio.it", true, TestContext.Current.CancellationToken));

        Assert.True(buono.Riuscito);
        Assert.Equal(CodiceEsito.NonTrovato, sbagliato.Codice);
        Assert.Equal(CodiceEsito.GiaPresente, doppio.Codice);
    }

    [Fact]
    public async Task RinominaESospendeERiattivaUnTenantConAudit()
    {
        await using var s = await Scenario.CreaAsync();

        Assert.True((await s.ChiamaAsync(servizio => servizio.RinominaTenantAsync(s.IdPlatformAdmin, s.A.IdTenant, "  Nome nuovo ", TestContext.Current.CancellationToken))).Riuscito);
        Assert.True((await s.ChiamaAsync(servizio => servizio.ImpostaTenantSospesoAsync(s.IdPlatformAdmin, s.A.IdTenant, true, TestContext.Current.CancellationToken))).Riuscito);
        Assert.Equal(EsitoRisoluzioneTenant.NonDisponibile, (await s.RisolviAsync(ScenarioHostA)).Esito);
        Assert.True((await s.ChiamaAsync(servizio => servizio.ImpostaTenantSospesoAsync(s.IdPlatformAdmin, s.A.IdTenant, false, TestContext.Current.CancellationToken))).Riuscito);
        Assert.Equal(EsitoRisoluzioneTenant.Risolto, (await s.RisolviAsync(ScenarioHostA)).Esito);

        var azioni = await s.ContaAzioniAsync(s.A.IdTenant);
        Assert.Equal(1, azioni[AzioniAudit.ModificaTenant]);
        Assert.Equal(1, azioni[AzioniAudit.SospensioneTenant]);
        Assert.Equal(1, azioni[AzioniAudit.RiattivazioneTenant]);
        var elenco = await s.ChiamaAsync(servizio => servizio.ElencaTenantAsync(s.IdPlatformAdmin, TestContext.Current.CancellationToken));
        Assert.Contains(elenco, t => t.Id == s.A.IdTenant && t.Nome == "Nome nuovo");
    }

    [Fact]
    public async Task IlTenantPrimarioNonSiSospende()
    {
        await using var s = await Scenario.CreaAsync();

        var esito = await s.ChiamaAsync(servizio => servizio.ImpostaTenantSospesoAsync(s.IdPlatformAdmin, s.Primario.IdTenant, true, TestContext.Current.CancellationToken));

        Assert.Equal(CodiceEsito.NonConsentito, esito.Codice);
        Assert.Equal(StatoTenant.Attivo, (await s.ContaAsync(contesto => contesto.Tenant.IgnoreQueryFilters().Where(t => t.Id == s.Primario.IdTenant).Select(t => t.Stato).SingleAsync(TestContext.Current.CancellationToken))));
    }

    [Fact]
    public async Task CreaEModificaUnSito()
    {
        await using var s = await Scenario.CreaAsync();

        var nuovo = await s.ChiamaAsync(servizio => servizio.CreaSitoAsync(s.IdPlatformAdmin, s.A.IdTenant, "Secondo sito", "Europe/London", TestContext.Current.CancellationToken));
        var modifica = await s.ChiamaAsync(servizio => servizio.ModificaSitoAsync(s.IdPlatformAdmin, s.A.IdTenant, nuovo.Id!.Value, "Sito rinominato", null, false, TestContext.Current.CancellationToken));
        var altroTenant = await s.ChiamaAsync(servizio => servizio.ModificaSitoAsync(s.IdPlatformAdmin, s.B.IdTenant, nuovo.Id!.Value, "Furto", null, true, TestContext.Current.CancellationToken));

        Assert.True(nuovo.Riuscito);
        Assert.True(modifica.Riuscito);
        Assert.Equal(CodiceEsito.NonTrovato, altroTenant.Codice);
        var dettaglio = await s.ChiamaAsync(servizio => servizio.LeggiTenantAsync(s.IdPlatformAdmin, s.A.IdTenant, TestContext.Current.CancellationToken));
        var sito = dettaglio!.Siti.Single(x => x.Id == nuovo.Id);
        Assert.Equal("Sito rinominato", sito.Nome);
        Assert.Equal("Europe/Rome", sito.FusoOrario);
        Assert.Equal(StatoSito.Disattivato, sito.Stato);
    }

    [Fact]
    public async Task CreaUnTenantAdminConPasswordTemporaneaDaCambiare()
    {
        await using var s = await Scenario.CreaAsync();

        var esito = await s.ChiamaAsync(servizio => servizio.CreaTenantAdminAsync(s.IdPlatformAdmin, s.A.IdTenant, "Anna@Esempio.it", "Anna", TestContext.Current.CancellationToken));

        Assert.True(esito.Riuscito);
        Assert.Equal(GeneratorePasswordTemporanea.Lunghezza, esito.PasswordTemporanea!.Length);
        await using var ambito = s.Fornitore.CreateAsyncScope();
        var contesto = ambito.ServiceProvider.GetRequiredService<ContestoDatabase>();
        var utenza = await contesto.Utenze.IgnoreQueryFilters().SingleAsync(u => u.Id == esito.IdUtenza, TestContext.Current.CancellationToken);
        Assert.Equal(RuoloUtenza.TenantAdmin, utenza.Ruolo);
        Assert.Equal(s.A.IdTenant, utenza.IdTenant);
        Assert.True(utenza.DeveCambiarePassword);
        Assert.True(ambito.ServiceProvider.GetRequiredService<IHashPassword>().Verifica(esito.PasswordTemporanea, utenza.HashPassword));

        // Può accedere dall'host del proprio tenant con la password temporanea, e non da quello di un altro.
        var accesso = ambito.ServiceProvider.GetRequiredService<IServizioAccesso>();
        var contestoTenant = ambito.ServiceProvider.GetRequiredService<ContestoTenant>();
        contestoTenant.Imposta(Scenario.Dati(s.A));
        var riuscito = await accesso.AccediAsync("anna@esempio.it", esito.PasswordTemporanea, TestContext.Current.CancellationToken);
        Assert.True(riuscito.EsitoPositivo);
        Assert.True(riuscito.Identita!.DeveCambiarePassword);

        var voce = await contesto.RegistroAudit.IgnoreQueryFilters().SingleAsync(a => a.Azione == AzioniAudit.CreazioneTenantAdmin, TestContext.Current.CancellationToken);
        Assert.DoesNotContain(esito.PasswordTemporanea, voce.Dettagli ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task IlTenantAdminPuoEssereSoloUnoPerEmailNelTenantMaPiuTenantPossonoAverloUguale()
    {
        await using var s = await Scenario.CreaAsync();
        Assert.True((await s.ChiamaAsync(servizio => servizio.CreaTenantAdminAsync(s.IdPlatformAdmin, s.A.IdTenant, "anna@esempio.it", "Anna", TestContext.Current.CancellationToken))).Riuscito);

        var doppio = await s.ChiamaAsync(servizio => servizio.CreaTenantAdminAsync(s.IdPlatformAdmin, s.A.IdTenant, "ANNA@esempio.it", "Anna bis", TestContext.Current.CancellationToken));
        var altroTenant = await s.ChiamaAsync(servizio => servizio.CreaTenantAdminAsync(s.IdPlatformAdmin, s.B.IdTenant, "anna@esempio.it", "Anna", TestContext.Current.CancellationToken));

        Assert.Equal(CodiceEsito.GiaPresente, doppio.Codice);
        Assert.Null(doppio.PasswordTemporanea);
        Assert.True(altroTenant.Riuscito);
    }

    [Fact]
    public async Task UnTenantAdminNonPuoUsareLEmailDiUnPlatformAdmin()
    {
        await using var s = await Scenario.CreaAsync();

        var esito = await s.ChiamaAsync(servizio => servizio.CreaTenantAdminAsync(s.IdPlatformAdmin, s.A.IdTenant, "platform@esempio.it", "Falso", TestContext.Current.CancellationToken));

        Assert.Equal(CodiceEsito.GiaPresente, esito.Codice);
    }

    [Theory]
    [InlineData("non-una-email", "Anna")]
    [InlineData("anna@esempio.it", "")]
    public async Task CreaTenantAdminRifiutaDatiNonValidi(string email, string nome)
    {
        await using var s = await Scenario.CreaAsync();

        var esito = await s.ChiamaAsync(servizio => servizio.CreaTenantAdminAsync(s.IdPlatformAdmin, s.A.IdTenant, email, nome, TestContext.Current.CancellationToken));

        Assert.Equal(CodiceEsito.DatiNonValidi, esito.Codice);
    }

    [Fact]
    public async Task CreaTenantAdminSuUnTenantInesistenteNonTrovato()
    {
        await using var s = await Scenario.CreaAsync();

        var esito = await s.ChiamaAsync(servizio => servizio.CreaTenantAdminAsync(s.IdPlatformAdmin, Guid.NewGuid(), "anna@esempio.it", "Anna", TestContext.Current.CancellationToken));

        Assert.Equal(CodiceEsito.NonTrovato, esito.Codice);
    }

    [Fact]
    public async Task ElevaUnUtenteATenantAdminEInvalidaLeSueSessioni()
    {
        await using var s = await Scenario.CreaAsync();
        var idUtente = await DatiProva.CreaUtenzaAsync(s.Fornitore, RuoloUtenza.Utente, s.A.IdTenant, "cliente@esempio.it", annullamento: TestContext.Current.CancellationToken);
        var timbro = await s.ContaAsync(contesto => contesto.Utenze.IgnoreQueryFilters().Where(u => u.Id == idUtente).Select(u => u.TimbroSicurezza).SingleAsync(TestContext.Current.CancellationToken));

        var esito = await s.ChiamaAsync(servizio => servizio.ElevaATenantAdminAsync(s.IdPlatformAdmin, s.A.IdTenant, idUtente, TestContext.Current.CancellationToken));

        Assert.True(esito.Riuscito);
        var dopo = await s.ContaAsync(contesto => contesto.Utenze.IgnoreQueryFilters().SingleAsync(u => u.Id == idUtente, TestContext.Current.CancellationToken));
        Assert.Equal(RuoloUtenza.TenantAdmin, dopo.Ruolo);
        Assert.NotEqual(timbro, dopo.TimbroSicurezza);
        Assert.Equal(1, (await s.ContaAzioniAsync(s.A.IdTenant))[AzioniAudit.ElevazioneATenantAdmin]);
    }

    [Fact]
    public async Task NonSiElevaChiNonEUnUtenteNeUnAccountDiUnAltroTenant()
    {
        await using var s = await Scenario.CreaAsync();
        var idAdmin = await DatiProva.CreaUtenzaAsync(s.Fornitore, RuoloUtenza.TenantAdmin, s.A.IdTenant, "anna@esempio.it", annullamento: TestContext.Current.CancellationToken);
        var idUtente = await DatiProva.CreaUtenzaAsync(s.Fornitore, RuoloUtenza.Utente, s.A.IdTenant, "cliente@esempio.it", annullamento: TestContext.Current.CancellationToken);

        var giaAdmin = await s.ChiamaAsync(servizio => servizio.ElevaATenantAdminAsync(s.IdPlatformAdmin, s.A.IdTenant, idAdmin, TestContext.Current.CancellationToken));
        var altroTenant = await s.ChiamaAsync(servizio => servizio.ElevaATenantAdminAsync(s.IdPlatformAdmin, s.B.IdTenant, idUtente, TestContext.Current.CancellationToken));
        var platform = await s.ChiamaAsync(servizio => servizio.ElevaATenantAdminAsync(s.IdPlatformAdmin, s.A.IdTenant, s.IdPlatformAdmin, TestContext.Current.CancellationToken));

        Assert.Equal(CodiceEsito.NonConsentito, giaAdmin.Codice);
        Assert.Equal(CodiceEsito.NonTrovato, altroTenant.Codice);
        Assert.Equal(CodiceEsito.NonTrovato, platform.Codice);
    }

    [Fact]
    public async Task DisabilitareUnAccountInvalidaLeSessioniERiabilitarloLoRipristina()
    {
        await using var s = await Scenario.CreaAsync();
        var idAdmin = await DatiProva.CreaUtenzaAsync(s.Fornitore, RuoloUtenza.TenantAdmin, s.A.IdTenant, "anna@esempio.it", annullamento: TestContext.Current.CancellationToken);
        var timbro = await s.ContaAsync(contesto => contesto.Utenze.IgnoreQueryFilters().Where(u => u.Id == idAdmin).Select(u => u.TimbroSicurezza).SingleAsync(TestContext.Current.CancellationToken));

        var disabilita = await s.ChiamaAsync(servizio => servizio.ImpostaUtenzaAttivaAsync(s.IdPlatformAdmin, s.A.IdTenant, idAdmin, false, TestContext.Current.CancellationToken));

        Assert.True(disabilita.Riuscito);
        await using (var ambito = s.Fornitore.CreateAsyncScope())
        {
            ambito.ServiceProvider.GetRequiredService<ContestoTenant>().Imposta(Scenario.Dati(s.A));
            var accesso = ambito.ServiceProvider.GetRequiredService<IServizioAccesso>();
            Assert.Null(await accesso.ConvalidaSessioneAsync(idAdmin, timbro, TestContext.Current.CancellationToken));
            Assert.False((await accesso.AccediAsync("anna@esempio.it", DatiProva.PasswordProva, TestContext.Current.CancellationToken)).EsitoPositivo);
        }

        Assert.True((await s.ChiamaAsync(servizio => servizio.ImpostaUtenzaAttivaAsync(s.IdPlatformAdmin, s.A.IdTenant, idAdmin, true, TestContext.Current.CancellationToken))).Riuscito);
        await using var ambitoDopo = s.Fornitore.CreateAsyncScope();
        ambitoDopo.ServiceProvider.GetRequiredService<ContestoTenant>().Imposta(Scenario.Dati(s.A));
        Assert.True((await ambitoDopo.ServiceProvider.GetRequiredService<IServizioAccesso>().AccediAsync("anna@esempio.it", DatiProva.PasswordProva, TestContext.Current.CancellationToken)).EsitoPositivo);
    }

    [Fact]
    public async Task IlPlatformAdminNonSiDisabilitaDaQuiNeSiCercaInUnAltroTenant()
    {
        await using var s = await Scenario.CreaAsync();

        var esito = await s.ChiamaAsync(servizio => servizio.ImpostaUtenzaAttivaAsync(s.IdPlatformAdmin, s.A.IdTenant, s.IdPlatformAdmin, false, TestContext.Current.CancellationToken));

        Assert.Equal(CodiceEsito.NonTrovato, esito.Codice);
    }

    [Fact]
    public async Task LeScrittureDiUnTenantAdminVengonoNegateERegistrate()
    {
        await using var s = await Scenario.CreaAsync();
        var idAdmin = await DatiProva.CreaUtenzaAsync(s.Fornitore, RuoloUtenza.TenantAdmin, s.A.IdTenant, "anna@esempio.it", annullamento: TestContext.Current.CancellationToken);
        var idUtente = await DatiProva.CreaUtenzaAsync(s.Fornitore, RuoloUtenza.Utente, s.A.IdTenant, "cliente@esempio.it", annullamento: TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;
        var tentativi = new List<Func<IServizioPiattaforma, Task<EsitoOperazione>>>
        {
            servizio => servizio.CreaTenantAsync(idAdmin, new DatiNuovoTenant("X", "Y", null, "x.esempio.it", true), ct),
            servizio => servizio.RinominaTenantAsync(idAdmin, s.A.IdTenant, "X", ct),
            servizio => servizio.ImpostaTenantSospesoAsync(idAdmin, s.B.IdTenant, true, ct),
            servizio => servizio.CreaSitoAsync(idAdmin, s.A.IdTenant, "X", null, ct),
            servizio => servizio.ModificaSitoAsync(idAdmin, s.A.IdTenant, s.A.IdSito, "X", null, true, ct),
            servizio => servizio.AggiungiDominioAsync(idAdmin, s.A.IdTenant, s.A.IdSito, "x.esempio.it", true, ct),
            servizio => servizio.VerificaDominioAsync(idAdmin, s.A.IdTenant, s.A.IdDominio, ct),
            servizio => servizio.ImpostaDominioAttivoAsync(idAdmin, s.A.IdTenant, s.A.IdDominio, false, ct),
            servizio => servizio.ElevaATenantAdminAsync(idAdmin, s.A.IdTenant, idUtente, ct),
            servizio => servizio.ElevaATenantAdminAsync(idAdmin, s.A.IdTenant, idAdmin, ct),
            servizio => servizio.ImpostaUtenzaAttivaAsync(idAdmin, s.A.IdTenant, idUtente, false, ct),
        };

        foreach (var tentativo in tentativi)
        {
            var esito = await s.ChiamaAsync(tentativo);
            Assert.Equal(CodiceEsito.NonAutorizzato, esito.Codice);
        }

        var nuovoAdmin = await s.ChiamaAsync(servizio => servizio.CreaTenantAdminAsync(idAdmin, s.A.IdTenant, "complice@esempio.it", "Complice", ct));
        Assert.Equal(CodiceEsito.NonAutorizzato, nuovoAdmin.Codice);
        Assert.Null(nuovoAdmin.PasswordTemporanea);

        // Nessun effetto: stesso numero di tenant e di account, nessun nuovo TenantAdmin, nessun elevato.
        Assert.Equal(3, await s.ContaAsync(contesto => contesto.Tenant.IgnoreQueryFilters().CountAsync(ct)));
        Assert.Equal(RuoloUtenza.Utente, await s.ContaAsync(contesto => contesto.Utenze.IgnoreQueryFilters().Where(u => u.Id == idUtente).Select(u => u.Ruolo).SingleAsync(ct)));
        Assert.Equal(0, await s.ContaAsync(contesto => contesto.Utenze.IgnoreQueryFilters().CountAsync(u => u.EmailNormalizzata == "complice@esempio.it", ct)));
        Assert.Equal(EsitoRisoluzioneTenant.Risolto, (await s.RisolviAsync(ScenarioHostA)).Esito);

        var negati = await s.ContaAsync(contesto => contesto.RegistroAudit.IgnoreQueryFilters()
            .CountAsync(a => a.Azione == AzioniAudit.OperazioneNegata && a.Esito == RegistroAudit.EsitoNegato && a.IdUtenzaAttore == idAdmin, ct));
        Assert.Equal(tentativi.Count + 1, negati);
    }

    [Fact]
    public async Task LeLettureDiUnTenantAdminLanciano()
    {
        await using var s = await Scenario.CreaAsync();
        var idAdmin = await DatiProva.CreaUtenzaAsync(s.Fornitore, RuoloUtenza.TenantAdmin, s.A.IdTenant, "anna@esempio.it", annullamento: TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => s.ChiamaAsync(servizio => servizio.ElencaTenantAsync(idAdmin, TestContext.Current.CancellationToken)));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => s.ChiamaAsync(servizio => servizio.LeggiTenantAsync(idAdmin, s.B.IdTenant, TestContext.Current.CancellationToken)));
    }

    [Fact]
    public async Task UnPlatformAdminDisabilitatoOSconosciutoNonPuoOperare()
    {
        await using var s = await Scenario.CreaAsync();
        var disabilitato = await DatiProva.CreaUtenzaAsync(s.Fornitore, RuoloUtenza.PlatformAdmin, null, "vecchio@esempio.it", stato: StatoUtenza.Disabilitata, annullamento: TestContext.Current.CancellationToken);

        var daDisabilitato = await s.ChiamaAsync(servizio => servizio.RinominaTenantAsync(disabilitato, s.A.IdTenant, "X", TestContext.Current.CancellationToken));
        var sconosciuto = await s.ChiamaAsync(servizio => servizio.RinominaTenantAsync(Guid.NewGuid(), s.A.IdTenant, "X", TestContext.Current.CancellationToken));

        Assert.Equal(CodiceEsito.NonAutorizzato, daDisabilitato.Codice);
        Assert.Equal(CodiceEsito.NonAutorizzato, sconosciuto.Codice);
        Assert.Equal(2, await s.ContaAsync(contesto => contesto.RegistroAudit.IgnoreQueryFilters().CountAsync(a => a.Azione == AzioniAudit.OperazioneNegata, TestContext.Current.CancellationToken)));
    }

    [Fact]
    public async Task ElencaIlTenantPrimarioPerPrimoConIConteggi()
    {
        await using var s = await Scenario.CreaAsync();
        await DatiProva.CreaUtenzaAsync(s.Fornitore, RuoloUtenza.TenantAdmin, s.A.IdTenant, "anna@esempio.it", annullamento: TestContext.Current.CancellationToken);

        var elenco = await s.ChiamaAsync(servizio => servizio.ElencaTenantAsync(s.IdPlatformAdmin, TestContext.Current.CancellationToken));

        Assert.Equal(3, elenco.Count);
        Assert.True(elenco[0].Primario);
        var a = elenco.Single(t => t.Id == s.A.IdTenant);
        Assert.Equal(1, a.NumeroSiti);
        Assert.Equal(1, a.NumeroAmministratori);
    }

    private static async Task<(Guid IdTenant, Guid IdSito)> NuovoTenantSoloItalianoAsync(Scenario s)
    {
        var creato = await s.ChiamaAsync(servizio => servizio.CreaTenantAsync(
            s.IdPlatformAdmin, new DatiNuovoTenant("Cliente", "Sito", null, "cliente.esempio.it", true), TestContext.Current.CancellationToken));
        var dettaglio = (await s.ChiamaAsync(servizio => servizio.LeggiTenantAsync(s.IdPlatformAdmin, creato.Id!.Value, TestContext.Current.CancellationToken)))!;
        return (creato.Id!.Value, dettaglio.Siti.Single().Id);
    }

    [Fact]
    public async Task UnSitoNuovoParlaItalianoEPuoAbilitareAltreLingue()
    {
        await using var s = await Scenario.CreaAsync();
        var (idTenant, idSito) = await NuovoTenantSoloItalianoAsync(s);
        var iniziale = (await s.ChiamaAsync(servizio => servizio.LeggiTenantAsync(s.IdPlatformAdmin, idTenant, TestContext.Current.CancellationToken)))!;
        Assert.Equal([("it", true)], iniziale.Siti.Single().Lingue.Select(l => (l.Codice, l.Predefinita)));
        Assert.Contains(iniziale.LingueDisponibili, l => l.Codice == "en");

        var esito = await s.ChiamaAsync(servizio => servizio.AbilitaLinguaAsync(s.IdPlatformAdmin, idTenant, idSito, "EN", TestContext.Current.CancellationToken));
        var doppia = await s.ChiamaAsync(servizio => servizio.AbilitaLinguaAsync(s.IdPlatformAdmin, idTenant, idSito, "en", TestContext.Current.CancellationToken));
        var sconosciuta = await s.ChiamaAsync(servizio => servizio.AbilitaLinguaAsync(s.IdPlatformAdmin, idTenant, idSito, "xx", TestContext.Current.CancellationToken));
        var altroTenant = await s.ChiamaAsync(servizio => servizio.AbilitaLinguaAsync(s.IdPlatformAdmin, s.B.IdTenant, idSito, "en", TestContext.Current.CancellationToken));

        Assert.True(esito.Riuscito);
        Assert.Equal(CodiceEsito.GiaPresente, doppia.Codice);
        Assert.Equal(CodiceEsito.DatiNonValidi, sconosciuta.Codice);
        Assert.Equal(CodiceEsito.NonTrovato, altroTenant.Codice);
        var dopo = (await s.ChiamaAsync(servizio => servizio.LeggiTenantAsync(s.IdPlatformAdmin, idTenant, TestContext.Current.CancellationToken)))!;
        Assert.Equal([("it", true), ("en", false)], dopo.Siti.Single().Lingue.Select(l => (l.Codice, l.Predefinita)));
    }

    [Fact]
    public async Task LaLinguaPredefinitaEQuellaConPagineNonSiDisabilitano()
    {
        await using var s = await Scenario.CreaAsync();
        var (idTenant, idSito) = await NuovoTenantSoloItalianoAsync(s);
        await s.ChiamaAsync(servizio => servizio.AbilitaLinguaAsync(s.IdPlatformAdmin, idTenant, idSito, "en", TestContext.Current.CancellationToken));

        var predefinita = await s.ChiamaAsync(servizio => servizio.DisabilitaLinguaAsync(s.IdPlatformAdmin, idTenant, idSito, "it", TestContext.Current.CancellationToken));
        var nonAbilitata = await s.ChiamaAsync(servizio => servizio.DisabilitaLinguaAsync(s.IdPlatformAdmin, idTenant, idSito, "xx", TestContext.Current.CancellationToken));
        Assert.Equal(CodiceEsito.NonConsentito, predefinita.Codice);
        Assert.Equal(CodiceEsito.NonTrovato, nonAbilitata.Codice);

        // Con una pagina inglese la lingua non si disabilita; senza sì.
        var admin = await DatiProva.CreaUtenzaAsync(s.Fornitore, RuoloUtenza.TenantAdmin, idTenant, "anna@esempio.it", annullamento: TestContext.Current.CancellationToken);
        await using (var ambito = s.Fornitore.CreateAsyncScope())
        {
            ambito.ServiceProvider.GetRequiredService<ContestoTenant>().Imposta(new DatiTenantRisolto(idTenant, idSito, Guid.NewGuid(), "cliente.esempio.it", false));
            var pagine = ambito.ServiceProvider.GetRequiredService<DvCms.Application.Cms.IServizioPagine>();
            Assert.True((await pagine.CreaAsync(admin, new DvCms.Application.Cms.DatiNuovaPagina("page", "Page", "x", "en", false), TestContext.Current.CancellationToken)).Riuscito);
        }

        var conPagine = await s.ChiamaAsync(servizio => servizio.DisabilitaLinguaAsync(s.IdPlatformAdmin, idTenant, idSito, "en", TestContext.Current.CancellationToken));
        Assert.Equal(CodiceEsito.NonConsentito, conPagine.Codice);

        var (altroTenant, altroSito) = (s.B.IdTenant, s.B.IdSito);
        var libera = await s.ChiamaAsync(servizio => servizio.DisabilitaLinguaAsync(s.IdPlatformAdmin, altroTenant, altroSito, "en", TestContext.Current.CancellationToken));
        Assert.True(libera.Riuscito);
    }

    [Fact]
    public async Task LaLinguaPredefinitaSiCambiaSoloSenzaPagineEUnaSolaResta()
    {
        await using var s = await Scenario.CreaAsync();
        var (idTenant, idSito) = await NuovoTenantSoloItalianoAsync(s);
        await s.ChiamaAsync(servizio => servizio.AbilitaLinguaAsync(s.IdPlatformAdmin, idTenant, idSito, "en", TestContext.Current.CancellationToken));

        var disabilitata = await s.ChiamaAsync(servizio => servizio.ImpostaLinguaPredefinitaAsync(s.IdPlatformAdmin, idTenant, idSito, "xx", TestContext.Current.CancellationToken));
        var cambio = await s.ChiamaAsync(servizio => servizio.ImpostaLinguaPredefinitaAsync(s.IdPlatformAdmin, idTenant, idSito, "en", TestContext.Current.CancellationToken));

        Assert.Equal(CodiceEsito.NonTrovato, disabilitata.Codice);
        Assert.True(cambio.Riuscito);
        var dopo = (await s.ChiamaAsync(servizio => servizio.LeggiTenantAsync(s.IdPlatformAdmin, idTenant, TestContext.Current.CancellationToken)))!;
        Assert.Equal("en", Assert.Single(dopo.Siti.Single().Lingue, l => l.Predefinita).Codice);

        // Con una pagina pubblicata il cambio non è più consentito (cambierebbe tutti gli indirizzi).
        var admin = await DatiProva.CreaUtenzaAsync(s.Fornitore, RuoloUtenza.TenantAdmin, idTenant, "anna@esempio.it", annullamento: TestContext.Current.CancellationToken);
        await using (var ambito = s.Fornitore.CreateAsyncScope())
        {
            ambito.ServiceProvider.GetRequiredService<ContestoTenant>().Imposta(new DatiTenantRisolto(idTenant, idSito, Guid.NewGuid(), "cliente.esempio.it", false));
            var pagine = ambito.ServiceProvider.GetRequiredService<DvCms.Application.Cms.IServizioPagine>();
            var id = (await pagine.CreaAsync(admin, new DvCms.Application.Cms.DatiNuovaPagina("page", "Page", "x", null, false), TestContext.Current.CancellationToken)).Id!.Value;
            var letta = (await pagine.LeggiAsync(admin, id, TestContext.Current.CancellationToken))!;
            Assert.True((await pagine.PubblicaAsync(admin, id, letta.TokenConcorrenza, TestContext.Current.CancellationToken)).Riuscito);
        }

        var bloccato = await s.ChiamaAsync(servizio => servizio.ImpostaLinguaPredefinitaAsync(s.IdPlatformAdmin, idTenant, idSito, "it", TestContext.Current.CancellationToken));
        Assert.Equal(CodiceEsito.NonConsentito, bloccato.Codice);
        var invariato = (await s.ChiamaAsync(servizio => servizio.LeggiTenantAsync(s.IdPlatformAdmin, idTenant, TestContext.Current.CancellationToken)))!;
        Assert.Equal("en", Assert.Single(invariato.Siti.Single().Lingue, l => l.Predefinita).Codice);
    }

    [Fact]
    public async Task LeLingueDelSitoSonoSoloDelPlatformAdmin()
    {
        await using var s = await Scenario.CreaAsync();
        var admin = await DatiProva.CreaUtenzaAsync(s.Fornitore, RuoloUtenza.TenantAdmin, s.A.IdTenant, "anna@esempio.it", annullamento: TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;

        var a = await s.ChiamaAsync(servizio => servizio.AbilitaLinguaAsync(admin, s.A.IdTenant, s.A.IdSito, "en", ct));
        var b = await s.ChiamaAsync(servizio => servizio.DisabilitaLinguaAsync(admin, s.A.IdTenant, s.A.IdSito, "en", ct));
        var c = await s.ChiamaAsync(servizio => servizio.ImpostaLinguaPredefinitaAsync(admin, s.A.IdTenant, s.A.IdSito, "en", ct));

        Assert.All(new[] { a, b, c }, esito => Assert.Equal(CodiceEsito.NonAutorizzato, esito.Codice));
        Assert.Equal(["it", "en"], (await s.ChiamaAsync(servizio => servizio.LeggiTenantAsync(s.IdPlatformAdmin, s.A.IdTenant, ct)))!.Siti.Single().Lingue.Select(l => l.Codice));
    }

    private const string ScenarioHostA = "a.esempio.it";

    private sealed class Scenario : IAsyncDisposable
    {
        private readonly DatabaseDiProva _database;

        private Scenario(DatabaseDiProva database, ServiceProvider fornitore, Guid idPlatformAdmin, DatiProva.TenantCreato primario, DatiProva.TenantCreato a, DatiProva.TenantCreato b)
        {
            _database = database;
            Fornitore = fornitore;
            IdPlatformAdmin = idPlatformAdmin;
            Primario = primario;
            A = a;
            B = b;
        }

        public ServiceProvider Fornitore { get; }

        public Guid IdPlatformAdmin { get; }

        public DatiProva.TenantCreato Primario { get; }

        public DatiProva.TenantCreato A { get; }

        public DatiProva.TenantCreato B { get; }

        public static async Task<Scenario> CreaAsync()
        {
            var database = await DatabaseDiProva.CreaAsync(TestContext.Current.CancellationToken);
            var fornitore = await ServiziProva.CreaConMigrazioniAsync(database.Connessione, annullamento: TestContext.Current.CancellationToken);
            var primario = await DatiProva.CreaTenantAsync(fornitore, "Primario", "cms.esempio.it", primario: true, annullamento: TestContext.Current.CancellationToken);
            var a = await DatiProva.CreaTenantAsync(fornitore, "A", ScenarioHostA, annullamento: TestContext.Current.CancellationToken);
            var b = await DatiProva.CreaTenantAsync(fornitore, "B", "b.esempio.it", annullamento: TestContext.Current.CancellationToken);
            var admin = await DatiProva.CreaUtenzaAsync(fornitore, RuoloUtenza.PlatformAdmin, null, "platform@esempio.it", annullamento: TestContext.Current.CancellationToken);
            return new Scenario(database, fornitore, admin, primario, a, b);
        }

        public static DatiTenantRisolto Dati(DatiProva.TenantCreato tenant) =>
            new(tenant.IdTenant, tenant.IdSito, tenant.IdDominio, tenant.Host, TenantPrimario: false);

        public async Task<T> ChiamaAsync<T>(Func<IServizioPiattaforma, Task<T>> operazione)
        {
            await using var ambito = Fornitore.CreateAsyncScope();
            return await operazione(ambito.ServiceProvider.GetRequiredService<IServizioPiattaforma>());
        }

        public async Task<T> ContaAsync<T>(Func<ContestoDatabase, Task<T>> lettura)
        {
            await using var ambito = Fornitore.CreateAsyncScope();
            return await lettura(ambito.ServiceProvider.GetRequiredService<ContestoDatabase>());
        }

        public async Task<RisultatoRisoluzioneTenant> RisolviAsync(string host)
        {
            await using var ambito = Fornitore.CreateAsyncScope();
            return await ambito.ServiceProvider.GetRequiredService<IRisolutoreTenant>().RisolviAsync(host, TestContext.Current.CancellationToken);
        }

        public Task<Dictionary<string, int>> ContaAzioniAsync(Guid idTenant) =>
            ContaAsync(contesto => contesto.RegistroAudit.IgnoreQueryFilters()
                .Where(voce => voce.IdTenant == idTenant)
                .GroupBy(voce => voce.Azione)
                .ToDictionaryAsync(gruppo => gruppo.Key, gruppo => gruppo.Count(), TestContext.Current.CancellationToken));

        public async ValueTask DisposeAsync()
        {
            await Fornitore.DisposeAsync();
            await _database.DisposeAsync();
        }
    }
}
