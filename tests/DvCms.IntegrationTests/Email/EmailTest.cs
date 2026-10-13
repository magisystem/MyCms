using System.Security.Cryptography;
using System.Text.Json;
using DvCms.Contracts.Email;
using DvCms.Data;
using DvCms.DTO.Email;
using DvCms.Emails;
using DvCms.Extender;
using DvCms.Infrastructure.Outbox;
using DvCms.IntegrationTests.Supporto;
using DvCms.Models;
using DvCms.Models.Email;
using DvCms.Models.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DvCms.IntegrationTests.Email;

/// <summary>Contratto email, canale SMTP, impostazioni per tenant e segreti cifrati (A-005, ADR-0015).</summary>
public class EmailTest
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly string _chiave = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    [Fact]
    public async Task IProtettoreCifraEDecifraEIlTestoProtettoNonContieneIlChiaro()
    {
        await using var s = await Scenario.CreaAsync();
        var protettore = s.Fornitore.GetRequiredService<IProtettoreSegreti>();

        var protetto = protettore.Proteggi("password-molto-segreta");

        Assert.DoesNotContain("molto-segreta", protetto, StringComparison.Ordinal);
        Assert.Equal("password-molto-segreta", protettore.Rivela(protetto));
    }

    [Fact]
    public async Task IlKeyRingEPersistitoNelDatabaseCifratoEUnaNuovaIstanzaLegge()
    {
        await using var s = await Scenario.CreaAsync();
        var protetto = s.Fornitore.GetRequiredService<IProtettoreSegreti>().Proteggi("valore");

        await using var ambito = s.Fornitore.CreateAsyncScope();
        var righe = await ambito.ServiceProvider.GetRequiredService<ContestoDatabase>().ChiaviSegrete.AsNoTracking().ToListAsync(Ct);
        Assert.NotEmpty(righe);
        Assert.All(righe, riga =>
        {
            Assert.Contains("<cifrato", riga.Xml, StringComparison.Ordinal);
            Assert.DoesNotContain("masterKey", riga.Xml, StringComparison.OrdinalIgnoreCase);
        });

        // Un'altra istanza dell'applicazione (stessa chiave di ambiente, stesso database) legge il segreto.
        await using var altra = await ServiziProva.CreaConMigrazioniAsync(s.Connessione, new Dictionary<string, string?> { ["Sicurezza:ChiaveSegreti"] = _chiave }, annullamento: Ct);
        Assert.Equal("valore", altra.GetRequiredService<IProtettoreSegreti>().Rivela(protetto));

        // Con una chiave diversa il key ring non si apre.
        var sbagliata = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        await using var conChiaveSbagliata = await ServiziProva.CreaConMigrazioniAsync(s.Connessione, new Dictionary<string, string?> { ["Sicurezza:ChiaveSegreti"] = sbagliata }, annullamento: Ct);
        Assert.ThrowsAny<Exception>(() => conChiaveSbagliata.GetRequiredService<IProtettoreSegreti>().Rivela(protetto));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("non-base64!!")]
    [InlineData("AAAA")]
    public async Task SenzaChiaveValidaIProtettoreRifiutaSenzaMostrareLaChiave(string? chiave)
    {
        await using var s = await Scenario.CreaAsync(chiave: chiave ?? string.Empty, conChiave: false);

        var errore = Assert.Throws<InvalidOperationException>(() => s.Fornitore.GetRequiredService<IProtettoreSegreti>().Proteggi("x"));

        Assert.Contains("Sicurezza:ChiaveSegreti", errore.Message, StringComparison.Ordinal);
        if (!string.IsNullOrEmpty(chiave))
        {
            Assert.DoesNotContain(chiave, errore.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task SenzaImpostazioniIlTenantNonInvia()
    {
        await using var s = await Scenario.CreaAsync();

        var errore = await Assert.ThrowsAsync<ConfigurazioneEmailNonValidaException>(() => s.InviaAsync(s.A, Messaggio()));

        Assert.Contains("non ha impostazioni", errore.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ImpostazioniIncompleteONonValideNonInviano()
    {
        await using var s = await Scenario.CreaAsync();
        await s.ImpostaAsync(s.A, ProviderEmail.Smtp, new Dictionary<string, string> { ["Host"] = "127.0.0.1" });
        var incomplete = await Assert.ThrowsAsync<ConfigurazioneEmailNonValidaException>(() => s.InviaAsync(s.A, Messaggio()));
        Assert.Contains("Porta", incomplete.Message, StringComparison.Ordinal);

        // Un valore con caratteri da indirizzo è rifiutato prima di arrivare all'adapter, e l'errore non mostra i valori.
        await s.ImpostaAsync(
            s.A,
            ProviderEmail.Graph,
            new Dictionary<string, string> { ["TenantMicrosoft"] = "t/../x", ["IdClient"] = "c", ["SegretoClient"] = "SEGRETO-GRAPH", ["CasellaMittente"] = "noreply@esempio.it" });
        var nonValida = await Assert.ThrowsAsync<ConfigurazioneEmailNonValidaException>(() => s.InviaAsync(s.A, Messaggio()));
        Assert.Contains("TenantMicrosoft", nonValida.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("SEGRETO-GRAPH", nonValida.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OgniCanaleDelTenantHaIlSuoAdapterRegistrato()
    {
        await using var s = await Scenario.CreaAsync();
        await using var ambito = s.Fornitore.CreateAsyncScope();

        var registrati = ambito.ServiceProvider.GetServices<IAdapterEmail>().Select(adapter => adapter.Provider).Order().ToList();

        Assert.Equal(Enum.GetValues<ProviderEmail>().Order(), registrati);
    }

    [Fact]
    public async Task InviaConSmtpUsandoLeImpostazioniEIlSegretoDecifrato()
    {
        await using var s = await Scenario.CreaAsync();
        await s.ImpostaSmtpAsync(s.A, s.Smtp, utente: "mittente", password: "PASSWORD-SMTP");

        await s.InviaAsync(s.A, new MessaggioEmail("anna@esempio.it", "Anna", "Oggetto di prova", "Corpo di testo", "<p>Corpo html</p>"));

        var ricevuta = Assert.Single(s.Smtp.Ricevute);
        Assert.Equal("noreply@a.esempio.it", ricevuta.Mittente);
        Assert.Equal(["anna@esempio.it"], ricevuta.Destinatari);
        Assert.Contains("Subject: Oggetto di prova", ricevuta.Dati, StringComparison.Ordinal);
        Assert.Contains("Corpo di testo", ricevuta.Dati, StringComparison.Ordinal);
        Assert.Contains("<p>Corpo html</p>", ricevuta.Dati, StringComparison.Ordinal);
        Assert.Equal("|mittente|PASSWORD-SMTP", ricevuta.Credenziali);
    }

    [Fact]
    public async Task IlSegretoNonStaInChiaroNelDatabase()
    {
        await using var s = await Scenario.CreaAsync();
        await s.ImpostaSmtpAsync(s.A, s.Smtp, utente: "mittente", password: "PASSWORD-SMTP");

        await using var ambito = s.Fornitore.CreateAsyncScope();
        var riga = await ambito.ServiceProvider.GetRequiredService<ContestoDatabase>().ImpostazioniEmailTenant.IgnoreQueryFilters().AsNoTracking().SingleAsync(Ct);

        Assert.DoesNotContain("PASSWORD-SMTP", riga.ValoriJson, StringComparison.Ordinal);
        Assert.DoesNotContain("Password", riga.ValoriJson, StringComparison.Ordinal);
        Assert.NotNull(riga.SegretiProtetti);
        Assert.DoesNotContain("PASSWORD-SMTP", riga.SegretiProtetti, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OgniTenantUsaLeProprieImpostazioni()
    {
        await using var s = await Scenario.CreaAsync();
        await using var altroServer = new ServerSmtpFinto();
        await s.ImpostaSmtpAsync(s.A, s.Smtp, nomeMittente: "Tenant A");
        await s.ImpostaSmtpAsync(s.B, altroServer, nomeMittente: "Tenant B");

        await s.InviaAsync(s.A, Messaggio());
        await s.InviaAsync(s.B, Messaggio());

        Assert.Equal("noreply@a.esempio.it", Assert.Single(s.Smtp.Ricevute).Mittente);
        Assert.Equal("noreply@b.esempio.it", Assert.Single(altroServer.Ricevute).Mittente);
    }

    [Theory]
    [InlineData("non-una-email")]
    [InlineData("")]
    public async Task UnDestinatarioNonValidoNonInvia(string destinatario)
    {
        await using var s = await Scenario.CreaAsync();
        await s.ImpostaSmtpAsync(s.A, s.Smtp);

        await Assert.ThrowsAsync<ConfigurazioneEmailNonValidaException>(() => s.InviaAsync(s.A, Messaggio() with { Destinatario = destinatario }));
        Assert.Empty(s.Smtp.Ricevute);
    }

    [Fact]
    public async Task NellOggettoNonSiPossonoIniettareIntestazioni()
    {
        await using var s = await Scenario.CreaAsync();
        await s.ImpostaSmtpAsync(s.A, s.Smtp);

        await Assert.ThrowsAsync<ArgumentException>(() => s.InviaAsync(s.A, Messaggio() with { Oggetto = "Ciao\r\nBcc: estraneo@esempio.it" }));
        Assert.Empty(s.Smtp.Ricevute);
    }

    [Fact]
    public async Task LInvioPassaDallOutboxENonDallaRichiesta()
    {
        await using var s = await Scenario.CreaAsync();
        await s.ImpostaSmtpAsync(s.A, s.Smtp);

        await s.AccodaAsync(s.A, Messaggio(), riservato: false);
        Assert.Empty(s.Smtp.Ricevute);

        Assert.Equal(1, await s.Fornitore.GetRequiredService<ElaboratoreOutbox>().ElaboraLottoAsync(Ct));

        Assert.Single(s.Smtp.Ricevute);
        Assert.Equal(StatoMessaggioOutbox.Completato, (await s.LeggiOutboxAsync()).Stato);
    }

    [Fact]
    public async Task UnMessaggioRiservatoEcifratoAncheNellOutbox()
    {
        await using var s = await Scenario.CreaAsync();
        await s.ImpostaSmtpAsync(s.A, s.Smtp);
        var messaggio = Messaggio() with { CorpoTesto = "Il tuo codice è 424242" };

        await s.AccodaAsync(s.A, messaggio, riservato: true);
        await s.AccodaAsync(s.A, messaggio with { CorpoTesto = "Testo pubblico 777" }, riservato: false);

        await using var ambito = s.Fornitore.CreateAsyncScope();
        var righe = await ambito.ServiceProvider.GetRequiredService<ContestoDatabase>().MessaggiOutbox.IgnoreQueryFilters().AsNoTracking().OrderBy(m => m.CreatoIlUtc).ToListAsync(Ct);
        Assert.DoesNotContain("424242", righe[0].Payload, StringComparison.Ordinal);
        Assert.Contains("777", righe[1].Payload, StringComparison.Ordinal);

        await s.Fornitore.GetRequiredService<ElaboratoreOutbox>().ElaboraLottoAsync(Ct);
        Assert.Equal(2, s.Smtp.Ricevute.Count);
        Assert.Contains(s.Smtp.Ricevute, r => r.Dati.Contains("424242", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SeLInvioFallisceIlMessaggioRestaPendenteConUnErroreSenzaSegreti()
    {
        await using var s = await Scenario.CreaAsync();
        s.Smtp.RifiutaInvio = true;
        await s.ImpostaSmtpAsync(s.A, s.Smtp, utente: "mittente", password: "PASSWORD-SMTP");
        await s.AccodaAsync(s.A, Messaggio(), riservato: false);

        await s.Fornitore.GetRequiredService<ElaboratoreOutbox>().ElaboraLottoAsync(Ct);

        var messaggio = await s.LeggiOutboxAsync();
        Assert.Equal(StatoMessaggioOutbox.Pendente, messaggio.Stato);
        Assert.Equal(1, messaggio.Tentativi);
        Assert.NotNull(messaggio.UltimoErrore);
        Assert.DoesNotContain("PASSWORD-SMTP", messaggio.UltimoErrore, StringComparison.Ordinal);
        Assert.Empty(s.Smtp.Ricevute);
    }

    [Fact]
    public async Task UnTenantSenzaConfigurazioneAccumulaErroriVisibiliENonInvia()
    {
        await using var s = await Scenario.CreaAsync(tentativiMassimi: 2);
        await s.AccodaAsync(s.A, Messaggio(), riservato: false);

        await s.Fornitore.GetRequiredService<ElaboratoreOutbox>().ElaboraLottoAsync(Ct);

        var messaggio = await s.LeggiOutboxAsync();
        Assert.Contains("ConfigurazioneEmailNonValidaException", messaggio.UltimoErrore, StringComparison.Ordinal);
        Assert.Empty(s.Smtp.Ricevute);
    }

    [Fact]
    public async Task LaChiaveDiIdempotenzaEvitaDueEmailIdentiche()
    {
        await using var s = await Scenario.CreaAsync();

        await s.AccodaAsync(s.A, Messaggio(), riservato: false, chiave: "otp-1");

        await Assert.ThrowsAsync<DbUpdateException>(() => s.AccodaAsync(s.A, Messaggio(), riservato: false, chiave: "otp-1"));
    }

    private static MessaggioEmail Messaggio() => new("anna@esempio.it", "Anna", "Oggetto", "Testo");

    private sealed class Scenario : IAsyncDisposable
    {
        private readonly DatabaseDiProva _database;

        private Scenario(DatabaseDiProva database, ServiceProvider fornitore, ServerSmtpFinto smtp, DatiProva.TenantCreato a, DatiProva.TenantCreato b)
        {
            _database = database;
            Fornitore = fornitore;
            Smtp = smtp;
            A = a;
            B = b;
        }

        public ServiceProvider Fornitore { get; }

        public ServerSmtpFinto Smtp { get; }

        public DatiProva.TenantCreato A { get; }

        public DatiProva.TenantCreato B { get; }

        public string Connessione => _database.Connessione;

        public static async Task<Scenario> CreaAsync(string chiave = "", bool conChiave = true, int tentativiMassimi = 8)
        {
            var database = await DatabaseDiProva.CreaAsync(Ct);
            var impostazioni = new Dictionary<string, string?> { ["Outbox:TentativiMassimi"] = tentativiMassimi.ToString(System.Globalization.CultureInfo.InvariantCulture) };
            if (conChiave)
            {
                impostazioni["Sicurezza:ChiaveSegreti"] = _chiave;
            }
            else if (chiave.Length > 0)
            {
                impostazioni["Sicurezza:ChiaveSegreti"] = chiave;
            }

            var fornitore = await ServiziProva.CreaConMigrazioniAsync(database.Connessione, impostazioni, annullamento: Ct);
            var a = await DatiProva.CreaTenantAsync(fornitore, "A", "a.esempio.it", annullamento: Ct);
            var b = await DatiProva.CreaTenantAsync(fornitore, "B", "b.esempio.it", annullamento: Ct);
            return new Scenario(database, fornitore, new ServerSmtpFinto(), a, b);
        }

        public Task ImpostaSmtpAsync(DatiProva.TenantCreato tenant, ServerSmtpFinto server, string? utente = null, string? password = null, string nomeMittente = "Mittente") =>
            ImpostaAsync(tenant, ProviderEmail.Smtp, new Dictionary<string, string>
            {
                ["Host"] = "127.0.0.1",
                ["Porta"] = server.Porta.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["ModalitaTls"] = DefinizioniProviderEmail.TlsNessuna,
                ["IndirizzoMittente"] = $"noreply@{tenant.Host}",
                ["NomeMittente"] = nomeMittente,
                ["Utente"] = utente ?? string.Empty,
                ["Password"] = password ?? string.Empty,
            });

        /// <summary>Salva le impostazioni come farà la schermata di B-009: valori in chiaro e segreti cifrati a parte.</summary>
        public async Task ImpostaAsync(DatiProva.TenantCreato tenant, ProviderEmail provider, Dictionary<string, string> valori)
        {
            var campi = DefinizioniProviderEmail.Campi(provider);
            var segreti = valori.Where(v => campi.Single(c => c.Nome == v.Key).Segreto && v.Value.Length > 0).ToDictionary(v => v.Key, v => v.Value);
            var normali = valori.Where(v => !segreti.ContainsKey(v.Key) && v.Value.Length > 0).ToDictionary(v => v.Key, v => v.Value);
            await using var ambito = Fornitore.CreateAsyncScope();
            var contesto = ambito.ServiceProvider.GetRequiredService<ContestoDatabase>();
            var esistente = await contesto.ImpostazioniEmailTenant.IgnoreQueryFilters().SingleOrDefaultAsync(i => i.IdTenant == tenant.IdTenant, Ct);
            if (esistente is null)
            {
                esistente = new ImpostazioniEmailTenant { Id = Identificativi.Nuovo(TimeProvider.System), IdTenant = tenant.IdTenant };
                contesto.ImpostazioniEmailTenant.Add(esistente);
            }

            esistente.Provider = provider;
            esistente.ValoriJson = JsonSerializer.Serialize(normali);
            esistente.SegretiProtetti = segreti.Count == 0 ? null : ambito.ServiceProvider.GetRequiredService<IProtettoreSegreti>().Proteggi(JsonSerializer.Serialize(segreti));
            esistente.AggiornatoIlUtc = DateTime.UtcNow;
            await contesto.SaveChangesAsync(Ct);
        }

        public async Task InviaAsync(DatiProva.TenantCreato tenant, MessaggioEmail messaggio)
        {
            await using var ambito = Fornitore.CreateAsyncScope();
            await ambito.ServiceProvider.GetRequiredService<IInvioEmail>().InviaAsync(tenant.IdTenant, messaggio, Ct);
        }

        public async Task AccodaAsync(DatiProva.TenantCreato tenant, MessaggioEmail messaggio, bool riservato, string? chiave = null)
        {
            await using var ambito = Fornitore.CreateAsyncScope();
            ambito.ServiceProvider.GetRequiredService<IAccodatoreEmail>().Accoda(tenant.IdTenant, messaggio, chiave, riservato);
            await ambito.ServiceProvider.GetRequiredService<ContestoDatabase>().SaveChangesAsync(Ct);
        }

        public async Task<MessaggioOutbox> LeggiOutboxAsync()
        {
            await using var ambito = Fornitore.CreateAsyncScope();
            return await ambito.ServiceProvider.GetRequiredService<ContestoDatabase>().MessaggiOutbox.IgnoreQueryFilters().AsNoTracking().OrderBy(m => m.CreatoIlUtc).FirstAsync(Ct);
        }

        public async ValueTask DisposeAsync()
        {
            await Smtp.DisposeAsync();
            await Fornitore.DisposeAsync();
            await _database.DisposeAsync();
        }
    }
}
