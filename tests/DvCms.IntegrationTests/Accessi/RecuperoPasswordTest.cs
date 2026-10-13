using System.Security.Cryptography;
using System.Text.RegularExpressions;
using DvCms.Contracts.Accessi;
using DvCms.Contracts.Email;
using DvCms.Core.Accessi;
using DvCms.Core.Tenancy;
using DvCms.Data;
using DvCms.DTO.Accessi;
using DvCms.DTO.Email;
using DvCms.DTO.Piattaforma;
using DvCms.DTO.Tenancy;
using DvCms.IntegrationTests.Supporto;
using DvCms.Models.Accessi;
using DvCms.Services.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DvCms.IntegrationTests.Accessi;

/// <summary>Recupero password in due email (B-008, ADR-0011) su SQL Server reale con un server SMTP finto.</summary>
public partial class RecuperoPasswordTest
{
    private const string UrlBase = "https://a.esempio.it";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task FlussoCompletoInDueEmailEPasswordCambiata()
    {
        await using var s = await Scenario.CreaAsync();
        var timbroVecchio = await s.TimbroAsync(s.IdAnna);

        var rif = await s.RichiediAsync("Anna@Esempio.it");
        var emailCodice = Assert.Single(await s.InviaEmailAsync());
        Assert.Equal(["anna@esempio.it"], emailCodice.Destinatari);
        var codice = CodiceNelTesto().Match(emailCodice.Testo).Groups[1].Value;
        Assert.Equal(6, codice.Length);

        Assert.Equal(EsitoVerificaCodice.Accettato, await s.VerificaAsync(rif, codice));
        var emailLink = Assert.Single(await s.InviaEmailAsync());
        Assert.DoesNotContain(codice, emailLink.Testo, StringComparison.Ordinal);
        var token = TokenNelTesto().Match(emailLink.Testo).Groups[1].Value;
        Assert.True(token.Length >= 40);
        Assert.StartsWith($"{UrlBase}/admin/recupero/reimposta?token=", LinkNelTesto().Match(emailLink.Testo).Value, StringComparison.Ordinal);
        Assert.True(await s.LinkValidoAsync(token));

        Assert.Equal(EsitoReimpostazione.Riuscita, await s.ReimpostaAsync(token, "una-nuova-password-sicura-1"));

        Assert.True((await s.AccediAsync("anna@esempio.it", "una-nuova-password-sicura-1")).EsitoPositivo);
        Assert.False((await s.AccediAsync("anna@esempio.it", DatiProva.PasswordProva)).EsitoPositivo);
        Assert.NotEqual(timbroVecchio, await s.TimbroAsync(s.IdAnna));
        var notifica = Assert.Single(await s.InviaEmailAsync());
        Assert.Contains("cambiata", notifica.Oggetto, StringComparison.Ordinal);
        Assert.False(await s.LinkValidoAsync(token));
        Assert.Equal(1, await s.ContaAuditAsync(AzioniAudit.RecuperoPasswordCompletato));
    }

    [Fact]
    public async Task UnModelloDelTenantCambiaLEmailDelCodice()
    {
        await using var s = await Scenario.CreaAsync();
        var personalizzato = new ContenutoModelloEmail("PIN {{ codice }}", "<p>{{ codice }}</p>", "Il tuo PIN è {{ codice }}, vale {{ minuti_validita }} minuti.");
        var salvato = await s.ComeAsync(false, p => p.GetRequiredService<IServizioModelliEmail>().SalvaAsync(s.IdAnna, CodiciModelloEmail.RecuperoCodice, "it", personalizzato, null, Ct));
        Assert.Equal(CodiceEsito.Riuscito, salvato.Codice);

        await s.RichiediAsync("cliente@esempio.it");
        var email = Assert.Single(await s.InviaEmailAsync());

        Assert.Matches(@"^PIN \d{6}$", email.Oggetto);
        Assert.Matches(@"^Il tuo PIN è \d{6}, vale 10 minuti\.", email.Testo);
    }

    [Fact]
    public async Task FunzionaAncheConGliUtentiAcquirenti()
    {
        await using var s = await Scenario.CreaAsync();

        var rif = await s.RichiediAsync("cliente@esempio.it");
        var codice = CodiceNelTesto().Match(Assert.Single(await s.InviaEmailAsync()).Testo).Groups[1].Value;

        Assert.Equal(EsitoVerificaCodice.Accettato, await s.VerificaAsync(rif, codice));
    }

    [Theory]
    [InlineData("nessuno@esempio.it")]
    [InlineData("root@esempio.it")]
    [InlineData("disabilitato@esempio.it")]
    [InlineData("non-una-email")]
    [InlineData("")]
    [InlineData(null)]
    public async Task RichiesteNonRealiDannoUnRiferimentoMaNonInviaNullaENonSiCompletanoMai(string? email)
    {
        await using var s = await Scenario.CreaAsync();

        var rif = await s.RichiediAsync(email);

        Assert.False(string.IsNullOrEmpty(rif));
        Assert.Empty(await s.InviaEmailAsync());
        Assert.Equal(0, await s.ContaOutboxAsync());
        for (var i = 0; i < 6; i++)
        {
            Assert.Equal(EsitoVerificaCodice.NonValido, await s.VerificaAsync(rif, "000000"));
        }

        Assert.Equal(StatoRecuperoPassword.Ignorata, await s.StatoUltimaRichiestaAsync());
    }

    [Fact]
    public async Task IlPlatformAdminNonHaRecuperoPassword()
    {
        await using var s = await Scenario.CreaAsync();

        var rif = await s.RichiediAsync("root@esempio.it");

        Assert.Empty(await s.InviaEmailAsync());
        Assert.Equal(EsitoVerificaCodice.NonValido, await s.VerificaAsync(rif, "123456"));
        Assert.Equal(0, await s.ContaAsync(r => r.IdUtenza != null));
    }

    [Fact]
    public async Task DopoCinqueCodiciErratiNemmenoQuelloGiustoFunziona()
    {
        await using var s = await Scenario.CreaAsync();
        var rif = await s.RichiediAsync("anna@esempio.it");
        var codice = CodiceNelTesto().Match(Assert.Single(await s.InviaEmailAsync()).Testo).Groups[1].Value;
        var sbagliato = codice == "999999" ? "999998" : "999999";

        for (var i = 0; i < 5; i++)
        {
            Assert.Equal(EsitoVerificaCodice.NonValido, await s.VerificaAsync(rif, sbagliato));
        }

        Assert.Equal(EsitoVerificaCodice.NonValido, await s.VerificaAsync(rif, codice));
        Assert.Equal(StatoRecuperoPassword.Annullata, await s.StatoUltimaRichiestaAsync());
        Assert.Empty(await s.InviaEmailAsync());
    }

    [Theory]
    [InlineData("12345")]
    [InlineData("1234567")]
    [InlineData("abcdef")]
    [InlineData("12 456")]
    [InlineData("")]
    [InlineData(null)]
    public async Task UnCodiceDiFormatoErratoNonVieneAccettato(string? codice)
    {
        await using var s = await Scenario.CreaAsync();
        var rif = await s.RichiediAsync("anna@esempio.it");
        await s.InviaEmailAsync();

        Assert.Equal(EsitoVerificaCodice.NonValido, await s.VerificaAsync(rif, codice));
    }

    [Fact]
    public async Task IlCodiceScadeDopoDieciMinuti()
    {
        await using var s = await Scenario.CreaAsync();
        var rif = await s.RichiediAsync("anna@esempio.it");
        var codice = CodiceNelTesto().Match(Assert.Single(await s.InviaEmailAsync()).Testo).Groups[1].Value;

        s.Orologio.Avanza(TimeSpan.FromMinutes(10) + TimeSpan.FromSeconds(1));

        Assert.Equal(EsitoVerificaCodice.NonValido, await s.VerificaAsync(rif, codice));
        Assert.Empty(await s.InviaEmailAsync());
    }

    [Fact]
    public async Task IlLinkScadeDopoTrentaMinutiEUnaVoltaUsatoNonSiRiusa()
    {
        await using var s = await Scenario.CreaAsync();
        var (rif, _) = await s.CodiceAccettatoAsync("anna@esempio.it");
        var token = TokenNelTesto().Match(Assert.Single(await s.InviaEmailAsync()).Testo).Groups[1].Value;
        s.Orologio.Avanza(TimeSpan.FromMinutes(31));
        Assert.False(await s.LinkValidoAsync(token));
        Assert.Equal(EsitoReimpostazione.LinkNonValido, await s.ReimpostaAsync(token, "una-nuova-password-sicura-1"));
        Assert.NotEmpty(rif);

        await using var t = await Scenario.CreaAsync();
        await t.CodiceAccettatoAsync("anna@esempio.it");
        var secondo = TokenNelTesto().Match(Assert.Single(await t.InviaEmailAsync()).Testo).Groups[1].Value;
        Assert.Equal(EsitoReimpostazione.Riuscita, await t.ReimpostaAsync(secondo, "una-nuova-password-sicura-1"));
        Assert.Equal(EsitoReimpostazione.LinkNonValido, await t.ReimpostaAsync(secondo, "ancora-un-altra-password-22"));
        Assert.True((await t.AccediAsync("anna@esempio.it", "una-nuova-password-sicura-1")).EsitoPositivo);
    }

    [Fact]
    public async Task UnaNuovaRichiestaInvalidaCodiceELinkPrecedenti()
    {
        await using var s = await Scenario.CreaAsync();
        var rif1 = await s.RichiediAsync("anna@esempio.it");
        var codice1 = CodiceNelTesto().Match(Assert.Single(await s.InviaEmailAsync()).Testo).Groups[1].Value;
        var rif2 = await s.RichiediAsync("anna@esempio.it");
        var codice2 = CodiceNelTesto().Match(Assert.Single(await s.InviaEmailAsync()).Testo).Groups[1].Value;

        Assert.Equal(EsitoVerificaCodice.NonValido, await s.VerificaAsync(rif1, codice1));
        Assert.Equal(EsitoVerificaCodice.Accettato, await s.VerificaAsync(rif2, codice2));
        var tokenVecchio = TokenNelTesto().Match(Assert.Single(await s.InviaEmailAsync()).Testo).Groups[1].Value;

        await s.RichiediAsync("anna@esempio.it");
        await s.InviaEmailAsync();

        Assert.False(await s.LinkValidoAsync(tokenVecchio));
        Assert.Equal(EsitoReimpostazione.LinkNonValido, await s.ReimpostaAsync(tokenVecchio, "una-nuova-password-sicura-1"));
    }

    [Fact]
    public async Task OltreTreRichiesteOrarieNonPartonoAltreEmailMaLaRispostaEUguale()
    {
        await using var s = await Scenario.CreaAsync();
        for (var i = 0; i < 3; i++)
        {
            Assert.False(string.IsNullOrEmpty(await s.RichiediAsync("anna@esempio.it")));
        }

        Assert.Equal(3, (await s.InviaEmailAsync()).Count);

        var quarta = await s.RichiediAsync("anna@esempio.it");

        Assert.False(string.IsNullOrEmpty(quarta));
        Assert.Empty(await s.InviaEmailAsync());
        Assert.Equal(StatoRecuperoPassword.Ignorata, await s.StatoUltimaRichiestaAsync());

        // Passata l'ora, si può richiedere di nuovo.
        s.Orologio.Avanza(TimeSpan.FromMinutes(61));
        await s.RichiediAsync("anna@esempio.it");
        Assert.Single(await s.InviaEmailAsync());
    }

    [Fact]
    public async Task IlLimitePerIndirizzoIpValeAncheSuEmailDiverse()
    {
        await using var s = await Scenario.CreaAsync();
        for (var i = 0; i < 10; i++)
        {
            await s.RichiediAsync($"sconosciuto{i}@esempio.it", ip: "203.0.113.9");
        }

        await s.RichiediAsync("anna@esempio.it", ip: "203.0.113.9");

        Assert.Empty(await s.InviaEmailAsync());
        await s.RichiediAsync("anna@esempio.it", ip: "203.0.113.10");
        Assert.Single(await s.InviaEmailAsync());
    }

    [Fact]
    public async Task RiferimentoETokenDiUnTenantNonValgonoSuUnAltro()
    {
        await using var s = await Scenario.CreaAsync();
        var rif = await s.RichiediAsync("anna@esempio.it");
        var codice = CodiceNelTesto().Match(Assert.Single(await s.InviaEmailAsync()).Testo).Groups[1].Value;

        Assert.Equal(EsitoVerificaCodice.NonValido, await s.VerificaAsync(rif, codice, tenantB: true));
        Assert.Equal(EsitoVerificaCodice.Accettato, await s.VerificaAsync(rif, codice));
        var token = TokenNelTesto().Match(Assert.Single(await s.InviaEmailAsync()).Testo).Groups[1].Value;

        Assert.False(await s.LinkValidoAsync(token, tenantB: true));
        Assert.Equal(EsitoReimpostazione.LinkNonValido, await s.ReimpostaAsync(token, "una-nuova-password-sicura-1", tenantB: true));
        Assert.True((await s.AccediAsync("anna@esempio.it", DatiProva.PasswordProva)).EsitoPositivo);
    }

    [Fact]
    public async Task LaStessaEmailInUnAltroTenantNonRiceveNulla()
    {
        await using var s = await Scenario.CreaAsync();
        await DatiProva.CreaUtenzaAsync(s.Fornitore, RuoloUtenza.TenantAdmin, s.B.IdTenant, "anna@esempio.it", annullamento: Ct);

        await s.RichiediAsync("anna@esempio.it");

        Assert.Single(await s.InviaEmailAsync());
        Assert.Empty(s.ServerB.Ricevute);
    }

    [Fact]
    public async Task CodiceETokenNonSonoMaiInChiaroNelDatabase()
    {
        await using var s = await Scenario.CreaAsync();
        var rif = await s.RichiediAsync("anna@esempio.it");
        var codice = CodiceNelTesto().Match(Assert.Single(await s.InviaEmailAsync()).Testo).Groups[1].Value;
        await s.VerificaAsync(rif, codice);
        var token = TokenNelTesto().Match(Assert.Single(await s.InviaEmailAsync()).Testo).Groups[1].Value;
        await s.RichiediAsync("anna@esempio.it");

        // Ricrea le email in coda (non elaborate) per controllare cosa sta nella tabella dell'outbox.
        var codice2Rif = await s.RichiediAsync("cliente@esempio.it");
        Assert.NotEmpty(codice2Rif);
        await using var ambito = s.Fornitore.CreateAsyncScope();
        var contesto = ambito.ServiceProvider.GetRequiredService<ContestoDatabase>();
        var payload = string.Concat(await contesto.MessaggiOutbox.IgnoreQueryFilters().Select(m => m.Payload).ToListAsync(Ct));
        var righe = await contesto.RichiesteRecuperoPassword.IgnoreQueryFilters().AsNoTracking().ToListAsync(Ct);
        Assert.DoesNotContain(token, payload, StringComparison.Ordinal);
        Assert.DoesNotContain(codice, payload, StringComparison.Ordinal);
        Assert.All(righe, r => Assert.DoesNotContain(codice, Convert.ToHexString(r.HashCodice), StringComparison.OrdinalIgnoreCase));
        Assert.All(righe, r => Assert.Equal(32, r.HashRiferimento.Length));
        Assert.DoesNotContain(righe, r => r.HashToken is not null && Convert.ToBase64String(r.HashToken).Contains(token, StringComparison.Ordinal));
        Assert.DoesNotContain(rif, string.Concat(righe.Select(r => Convert.ToBase64String(r.HashRiferimento))), StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnaPasswordNonValidaLasciaIlLinkUtilizzabile()
    {
        await using var s = await Scenario.CreaAsync();
        await s.CodiceAccettatoAsync("anna@esempio.it");
        var token = TokenNelTesto().Match(Assert.Single(await s.InviaEmailAsync()).Testo).Groups[1].Value;

        Assert.Equal(EsitoReimpostazione.PasswordNonValida, await s.ReimpostaAsync(token, "corta"));
        Assert.Equal(EsitoReimpostazione.PasswordNonValida, await s.ReimpostaAsync(token, null));
        Assert.True(await s.LinkValidoAsync(token));
        Assert.Equal(EsitoReimpostazione.Riuscita, await s.ReimpostaAsync(token, "una-nuova-password-sicura-1"));
    }

    [Fact]
    public async Task LaReimpostazioneSbloccaLAccountEInvalidaLeSessioni()
    {
        await using var s = await Scenario.CreaAsync();
        var timbro = await s.TimbroAsync(s.IdAnna);
        await s.BloccaAsync(s.IdAnna);
        Assert.False((await s.AccediAsync("anna@esempio.it", DatiProva.PasswordProva)).EsitoPositivo);
        await s.CodiceAccettatoAsync("anna@esempio.it");
        var token = TokenNelTesto().Match(Assert.Single(await s.InviaEmailAsync()).Testo).Groups[1].Value;

        Assert.Equal(EsitoReimpostazione.Riuscita, await s.ReimpostaAsync(token, "una-nuova-password-sicura-1"));

        Assert.True((await s.AccediAsync("anna@esempio.it", "una-nuova-password-sicura-1")).EsitoPositivo);
        Assert.Null(await s.ConvalidaSessioneAsync(s.IdAnna, timbro));
    }

    [Fact]
    public async Task UnAccountDisabilitatoDopoLInvioDelLinkNonPuoReimpostare()
    {
        await using var s = await Scenario.CreaAsync();
        await s.CodiceAccettatoAsync("anna@esempio.it");
        var token = TokenNelTesto().Match(Assert.Single(await s.InviaEmailAsync()).Testo).Groups[1].Value;
        await s.DisabilitaAsync(s.IdAnna);

        Assert.False(await s.LinkValidoAsync(token));
        Assert.Equal(EsitoReimpostazione.LinkNonValido, await s.ReimpostaAsync(token, "una-nuova-password-sicura-1"));
    }

    [Fact]
    public async Task LoStessoLinkUsatoInParalleloFunzionaUnaSolaVolta()
    {
        await using var s = await Scenario.CreaAsync();
        await s.CodiceAccettatoAsync("anna@esempio.it");
        var token = TokenNelTesto().Match(Assert.Single(await s.InviaEmailAsync()).Testo).Groups[1].Value;

        var esiti = await Task.WhenAll(Enumerable.Range(0, 4).Select(i => s.ReimpostaAsync(token, $"una-nuova-password-sicura-{i}")));

        Assert.Equal(1, esiti.Count(e => e == EsitoReimpostazione.Riuscita));
        Assert.Equal(3, esiti.Count(e => e == EsitoReimpostazione.LinkNonValido));
    }

    [Fact]
    public async Task SeLEmailDelTenantNonEConfiguratalaRispostaRestaUgualeEIlProblemaESulTenant()
    {
        await using var s = await Scenario.CreaAsync(configuraB: false);

        var rif = await s.RichiediAsync("anna@esempio.it", tenantB: true);
        await DatiProva.CreaUtenzaAsync(s.Fornitore, RuoloUtenza.TenantAdmin, s.B.IdTenant, "bruno@esempio.it", annullamento: Ct);
        var rifReale = await s.RichiediAsync("bruno@esempio.it", tenantB: true);

        Assert.False(string.IsNullOrEmpty(rif));
        Assert.False(string.IsNullOrEmpty(rifReale));
        await s.Fornitore.GetRequiredService<ElaboratoreOutbox>().ElaboraLottoAsync(Ct);
        Assert.Contains("ConfigurazioneEmailNonValidaException", await s.UltimoErroreOutboxAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task SenzaChiaveDeiSegretiLaRichiestaNonLanciaENonRivelaNulla()
    {
        await using var s = await Scenario.CreaAsync(conChiave: false);

        var rif = await s.RichiediAsync("anna@esempio.it");

        Assert.False(string.IsNullOrEmpty(rif));
        Assert.Equal(0, await s.ContaOutboxAsync());
    }

    private static readonly Regex _codice = CodiceNelTestoRegex();
    private static readonly Regex _link = LinkNelTestoRegex();

    private static Regex CodiceNelTesto() => _codice;

    private static Regex LinkNelTesto() => _link;

    private static Regex TokenNelTesto() => TokenNelTestoRegex();

    [GeneratedRegex("codice di verifica è (\\d{6})", RegexOptions.CultureInvariant)]
    private static partial Regex CodiceNelTestoRegex();

    [GeneratedRegex("https://[^\\s]+/admin/recupero/reimposta\\?token=[A-Za-z0-9_-]+", RegexOptions.CultureInvariant)]
    private static partial Regex LinkNelTestoRegex();

    [GeneratedRegex("token=([A-Za-z0-9_-]+)", RegexOptions.CultureInvariant)]
    private static partial Regex TokenNelTestoRegex();

    private sealed class Scenario : IAsyncDisposable
    {
        private readonly DatabaseDiProva _database;

        private Scenario(
            DatabaseDiProva database,
            ServiceProvider fornitore,
            OrologioProva orologio,
            ServerSmtpFinto server,
            ServerSmtpFinto serverB,
            DatiProva.TenantCreato a,
            DatiProva.TenantCreato b,
            Guid idAnna)
        {
            _database = database;
            Fornitore = fornitore;
            Orologio = orologio;
            Server = server;
            ServerB = serverB;
            A = a;
            B = b;
            IdAnna = idAnna;
        }

        public ServiceProvider Fornitore { get; }

        public OrologioProva Orologio { get; }

        public ServerSmtpFinto Server { get; }

        public ServerSmtpFinto ServerB { get; }

        public DatiProva.TenantCreato A { get; }

        public DatiProva.TenantCreato B { get; }

        public Guid IdAnna { get; }

        public static async Task<Scenario> CreaAsync(bool conChiave = true, bool configuraB = true)
        {
            var database = await DatabaseDiProva.CreaAsync(Ct);
            var orologio = new OrologioProva();
            var impostazioni = new Dictionary<string, string?>();
            if (conChiave)
            {
                impostazioni["Sicurezza:ChiaveSegreti"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            }

            var fornitore = await ServiziProva.CreaConMigrazioniAsync(database.Connessione, impostazioni, orologio: orologio, annullamento: Ct);
            var a = await DatiProva.CreaTenantAsync(fornitore, "A", "a.esempio.it", annullamento: Ct);
            var b = await DatiProva.CreaTenantAsync(fornitore, "B", "b.esempio.it", annullamento: Ct);
            var server = new ServerSmtpFinto();
            var serverB = new ServerSmtpFinto();
            await DatiProva.ConfiguraSmtpAsync(fornitore, a.IdTenant, server.Porta, Ct);
            if (configuraB)
            {
                await DatiProva.ConfiguraSmtpAsync(fornitore, b.IdTenant, serverB.Porta, Ct);
            }

            var anna = await DatiProva.CreaUtenzaAsync(fornitore, RuoloUtenza.TenantAdmin, a.IdTenant, "anna@esempio.it", annullamento: Ct);
            await DatiProva.CreaUtenzaAsync(fornitore, RuoloUtenza.Utente, a.IdTenant, "cliente@esempio.it", annullamento: Ct);
            await DatiProva.CreaUtenzaAsync(fornitore, RuoloUtenza.TenantAdmin, a.IdTenant, "disabilitato@esempio.it", stato: StatoUtenza.Disabilitata, annullamento: Ct);
            await DatiProva.CreaUtenzaAsync(fornitore, RuoloUtenza.PlatformAdmin, null, "root@esempio.it", annullamento: Ct);
            return new Scenario(database, fornitore, orologio, server, serverB, a, b, anna);
        }

        public async Task<T> ComeAsync<T>(bool tenantB, Func<IServiceProvider, Task<T>> operazione)
        {
            await using var ambito = Fornitore.CreateAsyncScope();
            var tenant = tenantB ? B : A;
            ambito.ServiceProvider.GetRequiredService<ContestoTenant>().Imposta(new DatiTenantRisolto(tenant.IdTenant, tenant.IdSito, tenant.IdDominio, tenant.Host, false));
            return await operazione(ambito.ServiceProvider);
        }

        public async Task<string> RichiediAsync(string? email, string? ip = "198.51.100.7", bool tenantB = false)
        {
            var riferimento = await ComeAsync(tenantB, p => p.GetRequiredService<IServizioRecuperoPassword>().RichiediAsync(email, ip, Ct));
            Orologio.Avanza(TimeSpan.FromSeconds(1));
            return riferimento;
        }

        public Task<EsitoVerificaCodice> VerificaAsync(string? riferimento, string? codice, bool tenantB = false) =>
            ComeAsync(tenantB, p => p.GetRequiredService<IServizioRecuperoPassword>().VerificaCodiceAsync(riferimento, codice, tenantB ? "https://b.esempio.it" : UrlBase, Ct));

        public Task<bool> LinkValidoAsync(string token, bool tenantB = false) =>
            ComeAsync(tenantB, p => p.GetRequiredService<IServizioRecuperoPassword>().LinkValidoAsync(token, Ct));

        public Task<EsitoReimpostazione> ReimpostaAsync(string token, string? password, bool tenantB = false) =>
            ComeAsync(tenantB, p => p.GetRequiredService<IServizioRecuperoPassword>().ReimpostaAsync(token, password, Ct));

        public Task<RisultatoAccesso> AccediAsync(string email, string password) =>
            ComeAsync(false, p => p.GetRequiredService<IServizioAccesso>().AccediAsync(email, password, Ct));

        public Task<IdentitaUtenza?> ConvalidaSessioneAsync(Guid idUtenza, Guid timbro) =>
            ComeAsync(false, p => p.GetRequiredService<IServizioAccesso>().ConvalidaSessioneAsync(idUtenza, timbro, Ct));

        /// <summary>Richiede, legge il codice dall'email e lo conferma; l'email con il link resta da leggere.</summary>
        public async Task<(string Riferimento, string Codice)> CodiceAccettatoAsync(string email)
        {
            var rif = await RichiediAsync(email);
            var codice = CodiceNelTesto().Match(Assert.Single(await InviaEmailAsync()).Testo).Groups[1].Value;
            Assert.Equal(EsitoVerificaCodice.Accettato, await VerificaAsync(rif, codice));
            return (rif, codice);
        }

        /// <summary>Fa lavorare il worker e restituisce le email arrivate al server finto dall'ultima volta.</summary>
        public async Task<IReadOnlyList<EmailRicevuta>> InviaEmailAsync()
        {
            var prima = Server.Ricevute.Count;
            while (await Fornitore.GetRequiredService<ElaboratoreOutbox>().ElaboraLottoAsync(Ct) > 0)
            {
            }

            return [.. Server.Ricevute.Skip(prima)];
        }

        public Task<int> ContaOutboxAsync() =>
            ComeAsync(false, p => p.GetRequiredService<ContestoDatabase>().MessaggiOutbox.IgnoreQueryFilters().CountAsync(Ct));

        public Task<int> ContaAsync(System.Linq.Expressions.Expression<Func<RichiestaRecuperoPassword, bool>> condizione) =>
            ComeAsync(false, p => p.GetRequiredService<ContestoDatabase>().RichiesteRecuperoPassword.IgnoreQueryFilters().CountAsync(condizione, Ct));

        public Task<int> ContaAuditAsync(string azione) =>
            ComeAsync(false, p => p.GetRequiredService<ContestoDatabase>().RegistroAudit.IgnoreQueryFilters().CountAsync(a => a.Azione == azione, Ct));

        public Task<StatoRecuperoPassword> StatoUltimaRichiestaAsync() =>
            ComeAsync(false, p => p.GetRequiredService<ContestoDatabase>().RichiesteRecuperoPassword.IgnoreQueryFilters().OrderByDescending(r => r.CreatoIlUtc).Select(r => r.Stato).FirstAsync(Ct));

        public Task<Guid> TimbroAsync(Guid idUtenza) =>
            ComeAsync(false, p => p.GetRequiredService<ContestoDatabase>().Utenze.IgnoreQueryFilters().Where(u => u.Id == idUtenza).Select(u => u.TimbroSicurezza).SingleAsync(Ct));

        public Task<string?> UltimoErroreOutboxAsync() =>
            ComeAsync(false, p => p.GetRequiredService<ContestoDatabase>().MessaggiOutbox.IgnoreQueryFilters().Where(m => m.UltimoErrore != null).Select(m => m.UltimoErrore).FirstOrDefaultAsync(Ct));

        public Task<bool> BloccaAsync(Guid idUtenza) =>
            ComeAsync(false, async p =>
            {
                await p.GetRequiredService<ContestoDatabase>().Utenze.IgnoreQueryFilters().Where(u => u.Id == idUtenza)
                    .ExecuteUpdateAsync(a => a.SetProperty(u => u.BloccatoFinoAlUtc, Orologio.GetUtcNow().UtcDateTime.AddMinutes(5)).SetProperty(u => u.ContatoreAccessiFalliti, 5), Ct);
                return true;
            });

        public Task<bool> DisabilitaAsync(Guid idUtenza) =>
            ComeAsync(false, async p =>
            {
                await p.GetRequiredService<ContestoDatabase>().Utenze.IgnoreQueryFilters().Where(u => u.Id == idUtenza)
                    .ExecuteUpdateAsync(a => a.SetProperty(u => u.Stato, StatoUtenza.Disabilitata), Ct);
                return true;
            });

        public async ValueTask DisposeAsync()
        {
            await Server.DisposeAsync();
            await ServerB.DisposeAsync();
            await Fornitore.DisposeAsync();
            await _database.DisposeAsync();
        }
    }
}
