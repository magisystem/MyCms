using DvCms.Application.Accessi;
using DvCms.Application.Tenancy;
using DvCms.Domain.Accessi;
using DvCms.Infrastructure.Persistenza;
using DvCms.IntegrationTests.Supporto;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DvCms.IntegrationTests.Accessi;

/// <summary>Accesso, blocco, sessioni e cambio password (B-003) su SQL Server reale.</summary>
public class ServizioAccessoTest
{
    private const string PasswordNuova = "una-nuova-password-sicura-45";

    [Fact]
    public async Task AccessoConCredenzialiGiusteRestituisceLIdentita()
    {
        await using var scenario = await Scenario.CreaAsync();
        var idUtenza = await DatiProva.CreaUtenzaAsync(scenario.Fornitore, RuoloUtenza.TenantAdmin, scenario.A.IdTenant, "anna@esempio.it", annullamento: TestContext.Current.CancellationToken);

        var risultato = await scenario.AccediAsync(scenario.A, "Anna@Esempio.IT", DatiProva.PasswordProva);

        Assert.True(risultato.EsitoPositivo);
        Assert.Equal(idUtenza, risultato.Identita!.IdUtenza);
        Assert.Equal(RuoloUtenza.TenantAdmin, risultato.Identita.Ruolo);
        Assert.Equal(scenario.A.IdTenant, risultato.Identita.IdTenantAccount);
        Assert.Equal("it", risultato.Identita.CodiceLingua);
        Assert.False(risultato.Identita.DeveCambiarePassword);
    }

    [Theory]
    [InlineData("anna@esempio.it", "password-sbagliata-123")]
    [InlineData("nessuno@esempio.it", DatiProva.PasswordProva)]
    [InlineData("non-una-email", DatiProva.PasswordProva)]
    [InlineData("", DatiProva.PasswordProva)]
    [InlineData("anna@esempio.it", "")]
    [InlineData("anna@esempio.it", null)]
    public async Task OgniFallimentoDaLoStessoRisultato(string? email, string? password)
    {
        await using var scenario = await Scenario.CreaAsync();
        await DatiProva.CreaUtenzaAsync(scenario.Fornitore, RuoloUtenza.TenantAdmin, scenario.A.IdTenant, "anna@esempio.it", annullamento: TestContext.Current.CancellationToken);

        var risultato = await scenario.AccediAsync(scenario.A, email, password);

        Assert.False(risultato.EsitoPositivo);
        Assert.Null(risultato.Identita);
    }

    [Fact]
    public async Task PasswordLunghissimaVieneRifiutataSenzaCalcolo()
    {
        await using var scenario = await Scenario.CreaAsync();
        await DatiProva.CreaUtenzaAsync(scenario.Fornitore, RuoloUtenza.TenantAdmin, scenario.A.IdTenant, "anna@esempio.it", annullamento: TestContext.Current.CancellationToken);

        var risultato = await scenario.AccediAsync(scenario.A, "anna@esempio.it", new string('x', 100_000));

        Assert.False(risultato.EsitoPositivo);
    }

    [Fact]
    public async Task TenantAdminNonAccedeDallHostDiUnAltroTenant()
    {
        await using var scenario = await Scenario.CreaAsync();
        await DatiProva.CreaUtenzaAsync(scenario.Fornitore, RuoloUtenza.TenantAdmin, scenario.A.IdTenant, "anna@esempio.it", annullamento: TestContext.Current.CancellationToken);

        var dalSuoTenant = await scenario.AccediAsync(scenario.A, "anna@esempio.it", DatiProva.PasswordProva);
        var dalTenantAltrui = await scenario.AccediAsync(scenario.B, "anna@esempio.it", DatiProva.PasswordProva);

        Assert.True(dalSuoTenant.EsitoPositivo);
        Assert.False(dalTenantAltrui.EsitoPositivo);
    }

    [Fact]
    public async Task LaStessaEmailInDueTenantSonoAccountDistintiConPasswordDistinte()
    {
        await using var scenario = await Scenario.CreaAsync();
        var idA = await DatiProva.CreaUtenzaAsync(scenario.Fornitore, RuoloUtenza.TenantAdmin, scenario.A.IdTenant, "anna@esempio.it", "password-del-tenant-a-1", annullamento: TestContext.Current.CancellationToken);
        var idB = await DatiProva.CreaUtenzaAsync(scenario.Fornitore, RuoloUtenza.TenantAdmin, scenario.B.IdTenant, "anna@esempio.it", "password-del-tenant-b-2", annullamento: TestContext.Current.CancellationToken);

        var inA = await scenario.AccediAsync(scenario.A, "anna@esempio.it", "password-del-tenant-a-1");
        var inB = await scenario.AccediAsync(scenario.B, "anna@esempio.it", "password-del-tenant-b-2");
        var incrociato = await scenario.AccediAsync(scenario.A, "anna@esempio.it", "password-del-tenant-b-2");

        Assert.Equal(idA, inA.Identita!.IdUtenza);
        Assert.Equal(idB, inB.Identita!.IdUtenza);
        Assert.False(incrociato.EsitoPositivo);
    }

    [Fact]
    public async Task PlatformAdminAccedeDaOgniHostEIngressoEInRegistroAudit()
    {
        await using var scenario = await Scenario.CreaAsync();
        var idAdmin = await DatiProva.CreaUtenzaAsync(scenario.Fornitore, RuoloUtenza.PlatformAdmin, null, "root@esempio.it", annullamento: TestContext.Current.CancellationToken);

        var inA = await scenario.AccediAsync(scenario.A, "root@esempio.it", DatiProva.PasswordProva);
        var inB = await scenario.AccediAsync(scenario.B, "root@esempio.it", DatiProva.PasswordProva);

        Assert.True(inA.EsitoPositivo);
        Assert.True(inB.EsitoPositivo);
        Assert.Null(inA.Identita!.IdTenantAccount);

        await using var ambito = scenario.Fornitore.CreateAsyncScope();
        var contesto = ambito.ServiceProvider.GetRequiredService<ContestoDatabase>();
        var voci = await contesto.RegistroAudit.IgnoreQueryFilters()
            .Where(voce => voce.Azione == AzioniAudit.AccessoPlatformAdmin)
            .ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, voci.Count);
        Assert.All(voci, voce => Assert.Equal(idAdmin, voce.IdUtenzaAttore));
        Assert.Equivalent(new Guid?[] { scenario.A.IdTenant, scenario.B.IdTenant }, voci.Select(voce => voce.IdTenant).ToArray());
    }

    [Fact]
    public async Task AccountDisabilitatoNonAccede()
    {
        await using var scenario = await Scenario.CreaAsync();
        await DatiProva.CreaUtenzaAsync(scenario.Fornitore, RuoloUtenza.TenantAdmin, scenario.A.IdTenant, "off@esempio.it", stato: StatoUtenza.Disabilitata, annullamento: TestContext.Current.CancellationToken);

        Assert.False((await scenario.AccediAsync(scenario.A, "off@esempio.it", DatiProva.PasswordProva)).EsitoPositivo);
    }

    [Fact]
    public async Task CinqueTentativiFallitiBloccanoPerCinqueMinuti()
    {
        await using var scenario = await Scenario.CreaAsync();
        await DatiProva.CreaUtenzaAsync(scenario.Fornitore, RuoloUtenza.TenantAdmin, scenario.A.IdTenant, "anna@esempio.it", annullamento: TestContext.Current.CancellationToken);

        for (var tentativo = 0; tentativo < 5; tentativo++)
        {
            Assert.False((await scenario.AccediAsync(scenario.A, "anna@esempio.it", "password-sbagliata-123")).EsitoPositivo);
        }

        // Bloccato: anche la password giusta viene rifiutata.
        Assert.False((await scenario.AccediAsync(scenario.A, "anna@esempio.it", DatiProva.PasswordProva)).EsitoPositivo);
        scenario.Orologio.Avanza(TimeSpan.FromMinutes(4) + TimeSpan.FromSeconds(59));
        Assert.False((await scenario.AccediAsync(scenario.A, "anna@esempio.it", DatiProva.PasswordProva)).EsitoPositivo);

        // Passati i 5 minuti l'accesso torna possibile.
        scenario.Orologio.Avanza(TimeSpan.FromSeconds(2));
        Assert.True((await scenario.AccediAsync(scenario.A, "anna@esempio.it", DatiProva.PasswordProva)).EsitoPositivo);
    }

    [Fact]
    public async Task QuattroTentativiFallitiNonBloccanoEUnAccessoRiuscitoAzzeraIlContatore()
    {
        await using var scenario = await Scenario.CreaAsync();
        await DatiProva.CreaUtenzaAsync(scenario.Fornitore, RuoloUtenza.TenantAdmin, scenario.A.IdTenant, "anna@esempio.it", annullamento: TestContext.Current.CancellationToken);

        for (var tentativo = 0; tentativo < 4; tentativo++)
        {
            await scenario.AccediAsync(scenario.A, "anna@esempio.it", "password-sbagliata-123");
        }

        Assert.True((await scenario.AccediAsync(scenario.A, "anna@esempio.it", DatiProva.PasswordProva)).EsitoPositivo);

        // Il contatore è ripartito da zero: altri 4 fallimenti non bloccano.
        for (var tentativo = 0; tentativo < 4; tentativo++)
        {
            await scenario.AccediAsync(scenario.A, "anna@esempio.it", "password-sbagliata-123");
        }

        Assert.True((await scenario.AccediAsync(scenario.A, "anna@esempio.it", DatiProva.PasswordProva)).EsitoPositivo);
    }

    [Fact]
    public async Task DopoIlBloccoIlContatoreRipartePulito()
    {
        await using var scenario = await Scenario.CreaAsync();
        await DatiProva.CreaUtenzaAsync(scenario.Fornitore, RuoloUtenza.TenantAdmin, scenario.A.IdTenant, "anna@esempio.it", annullamento: TestContext.Current.CancellationToken);
        for (var tentativo = 0; tentativo < 5; tentativo++)
        {
            await scenario.AccediAsync(scenario.A, "anna@esempio.it", "password-sbagliata-123");
        }

        scenario.Orologio.Avanza(TimeSpan.FromMinutes(6));

        // Quattro nuovi fallimenti dopo lo sblocco non bloccano di nuovo.
        for (var tentativo = 0; tentativo < 4; tentativo++)
        {
            await scenario.AccediAsync(scenario.A, "anna@esempio.it", "password-sbagliata-123");
        }

        Assert.True((await scenario.AccediAsync(scenario.A, "anna@esempio.it", DatiProva.PasswordProva)).EsitoPositivo);
    }

    [Fact]
    public async Task HashConCostoInferioreVieneRigenerato()
    {
        await using var scenario = await Scenario.CreaAsync();
        var idUtenza = await DatiProva.CreaUtenzaAsync(scenario.Fornitore, RuoloUtenza.TenantAdmin, scenario.A.IdTenant, "anna@esempio.it", annullamento: TestContext.Current.CancellationToken);
        string hashPrima;
        await using (var ambito = scenario.Fornitore.CreateAsyncScope())
        {
            var contesto = ambito.ServiceProvider.GetRequiredService<ContestoDatabase>();
            var utenza = await contesto.Utenze.IgnoreQueryFilters().SingleAsync(candidata => candidata.Id == idUtenza, TestContext.Current.CancellationToken);
            utenza.HashPassword = BCrypt.Net.BCrypt.EnhancedHashPassword(DatiProva.PasswordProva, BCrypt.Net.HashType.SHA384, 4);
            hashPrima = utenza.HashPassword;
            await contesto.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var scenarioCostoMaggiore = await Scenario.CreaAsync(fattoreLavoro: 5, database: scenario.Database, fornitoreEsistente: false);
        // Lo stesso database, con un fattore di lavoro maggiore: al login l'hash viene rigenerato.
        Assert.True((await scenarioCostoMaggiore.AccediAsync(scenario.A, "anna@esempio.it", DatiProva.PasswordProva)).EsitoPositivo);

        await using var lettura = scenarioCostoMaggiore.Fornitore.CreateAsyncScope();
        var letta = await lettura.ServiceProvider.GetRequiredService<ContestoDatabase>().Utenze.IgnoreQueryFilters()
            .SingleAsync(candidata => candidata.Id == idUtenza, TestContext.Current.CancellationToken);
        Assert.NotEqual(hashPrima, letta.HashPassword);
        Assert.StartsWith("$2a$05$", letta.HashPassword, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SessioneValidaSoloConTimbroEAccountAttivoENelTenantGiusto()
    {
        await using var scenario = await Scenario.CreaAsync();
        var idUtenza = await DatiProva.CreaUtenzaAsync(scenario.Fornitore, RuoloUtenza.TenantAdmin, scenario.A.IdTenant, "anna@esempio.it", annullamento: TestContext.Current.CancellationToken);
        var identita = (await scenario.AccediAsync(scenario.A, "anna@esempio.it", DatiProva.PasswordProva)).Identita!;

        Assert.NotNull(await scenario.ConvalidaAsync(scenario.A, identita.IdUtenza, identita.TimbroSicurezza));
        Assert.Null(await scenario.ConvalidaAsync(scenario.A, identita.IdUtenza, Guid.NewGuid()));
        Assert.Null(await scenario.ConvalidaAsync(scenario.B, identita.IdUtenza, identita.TimbroSicurezza));
        Assert.Null(await scenario.ConvalidaAsync(scenario.A, Guid.NewGuid(), identita.TimbroSicurezza));

        await using (var ambito = scenario.Fornitore.CreateAsyncScope())
        {
            var contesto = ambito.ServiceProvider.GetRequiredService<ContestoDatabase>();
            var utenza = await contesto.Utenze.IgnoreQueryFilters().SingleAsync(candidata => candidata.Id == idUtenza, TestContext.Current.CancellationToken);
            utenza.Stato = StatoUtenza.Disabilitata;
            await contesto.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        Assert.Null(await scenario.ConvalidaAsync(scenario.A, identita.IdUtenza, identita.TimbroSicurezza));
    }

    [Fact]
    public async Task PlatformAdminHaSessioneValidaSuQualunqueHost()
    {
        await using var scenario = await Scenario.CreaAsync();
        await DatiProva.CreaUtenzaAsync(scenario.Fornitore, RuoloUtenza.PlatformAdmin, null, "root@esempio.it", annullamento: TestContext.Current.CancellationToken);
        var identita = (await scenario.AccediAsync(scenario.A, "root@esempio.it", DatiProva.PasswordProva)).Identita!;

        Assert.NotNull(await scenario.ConvalidaAsync(scenario.B, identita.IdUtenza, identita.TimbroSicurezza));
    }

    [Fact]
    public async Task CambioPasswordInvalidaLeSessioniEFunzionaConLaNuovaPassword()
    {
        await using var scenario = await Scenario.CreaAsync();
        await DatiProva.CreaUtenzaAsync(scenario.Fornitore, RuoloUtenza.TenantAdmin, scenario.A.IdTenant, "anna@esempio.it", deveCambiarePassword: true, annullamento: TestContext.Current.CancellationToken);
        var identita = (await scenario.AccediAsync(scenario.A, "anna@esempio.it", DatiProva.PasswordProva)).Identita!;
        Assert.True(identita.DeveCambiarePassword);

        var esito = await scenario.CambiaPasswordAsync(scenario.A, identita.IdUtenza, DatiProva.PasswordProva, PasswordNuova);

        Assert.Equal(EsitoCambioPassword.Riuscito, esito);
        Assert.Null(await scenario.ConvalidaAsync(scenario.A, identita.IdUtenza, identita.TimbroSicurezza));
        Assert.False((await scenario.AccediAsync(scenario.A, "anna@esempio.it", DatiProva.PasswordProva)).EsitoPositivo);
        var nuovo = await scenario.AccediAsync(scenario.A, "anna@esempio.it", PasswordNuova);
        Assert.True(nuovo.EsitoPositivo);
        Assert.False(nuovo.Identita!.DeveCambiarePassword);
    }

    [Theory]
    [InlineData("password-di-prova-123", "corta", EsitoCambioPassword.PasswordNonValida)]
    [InlineData("password-di-prova-123", "password-di-prova-123", EsitoCambioPassword.PasswordNonValida)]
    [InlineData("password-attuale-sbagliata", "una-nuova-password-sicura-45", EsitoCambioPassword.PasswordAttualeErrata)]
    [InlineData("", "una-nuova-password-sicura-45", EsitoCambioPassword.PasswordAttualeErrata)]
    public async Task CambioPasswordRifiutaInputNonValidi(string attuale, string nuova, EsitoCambioPassword atteso)
    {
        await using var scenario = await Scenario.CreaAsync();
        var idUtenza = await DatiProva.CreaUtenzaAsync(scenario.Fornitore, RuoloUtenza.TenantAdmin, scenario.A.IdTenant, "anna@esempio.it", annullamento: TestContext.Current.CancellationToken);

        var esito = await scenario.CambiaPasswordAsync(scenario.A, idUtenza, attuale, nuova);

        Assert.Equal(atteso, esito);
        Assert.True((await scenario.AccediAsync(scenario.A, "anna@esempio.it", DatiProva.PasswordProva)).EsitoPositivo);
    }

    [Fact]
    public async Task CambioPasswordNonConsentitoDaUnAltroTenant()
    {
        await using var scenario = await Scenario.CreaAsync();
        var idUtenza = await DatiProva.CreaUtenzaAsync(scenario.Fornitore, RuoloUtenza.TenantAdmin, scenario.A.IdTenant, "anna@esempio.it", annullamento: TestContext.Current.CancellationToken);

        var esito = await scenario.CambiaPasswordAsync(scenario.B, idUtenza, DatiProva.PasswordProva, PasswordNuova);

        Assert.Equal(EsitoCambioPassword.NonConsentito, esito);
    }

    [Fact]
    public async Task TentativiFallitiSulCambioPasswordContanoPerIlBlocco()
    {
        await using var scenario = await Scenario.CreaAsync();
        var idUtenza = await DatiProva.CreaUtenzaAsync(scenario.Fornitore, RuoloUtenza.TenantAdmin, scenario.A.IdTenant, "anna@esempio.it", annullamento: TestContext.Current.CancellationToken);

        for (var tentativo = 0; tentativo < 5; tentativo++)
        {
            await scenario.CambiaPasswordAsync(scenario.A, idUtenza, "password-attuale-sbagliata", PasswordNuova);
        }

        Assert.False((await scenario.AccediAsync(scenario.A, "anna@esempio.it", DatiProva.PasswordProva)).EsitoPositivo);
    }

    private sealed class Scenario : IAsyncDisposable
    {
        private Scenario(DatabaseDiProva database, ServiceProvider fornitore, OrologioProva orologio, DatiProva.TenantCreato a, DatiProva.TenantCreato b, bool possiedeDatabase)
        {
            Database = database;
            Fornitore = fornitore;
            Orologio = orologio;
            A = a;
            B = b;
            _possiedeDatabase = possiedeDatabase;
        }

        private readonly bool _possiedeDatabase;

        public DatabaseDiProva Database { get; }

        public ServiceProvider Fornitore { get; }

        public OrologioProva Orologio { get; }

        public DatiProva.TenantCreato A { get; }

        public DatiProva.TenantCreato B { get; }

        public static async Task<Scenario> CreaAsync(int fattoreLavoro = 4, DatabaseDiProva? database = null, bool fornitoreEsistente = true)
        {
            _ = fornitoreEsistente;
            var possiede = database is null;
            database ??= await DatabaseDiProva.CreaAsync(TestContext.Current.CancellationToken);
            var orologio = new OrologioProva();
            var fornitore = await ServiziProva.CreaConMigrazioniAsync(
                database.Connessione,
                new Dictionary<string, string?> { ["Sicurezza:Password:FattoreLavoroBcrypt"] = fattoreLavoro.ToString(System.Globalization.CultureInfo.InvariantCulture) },
                orologio: orologio,
                annullamento: TestContext.Current.CancellationToken);

            DatiProva.TenantCreato a;
            DatiProva.TenantCreato b;
            if (possiede)
            {
                a = await DatiProva.CreaTenantAsync(fornitore, "A", "a.esempio.it", annullamento: TestContext.Current.CancellationToken);
                b = await DatiProva.CreaTenantAsync(fornitore, "B", "b.esempio.it", annullamento: TestContext.Current.CancellationToken);
            }
            else
            {
                await using var ambito = fornitore.CreateAsyncScope();
                var contesto = ambito.ServiceProvider.GetRequiredService<ContestoDatabase>();
                var domini = await contesto.DominiSito.IgnoreQueryFilters().AsNoTracking().OrderBy(dominio => dominio.Host).ToListAsync(TestContext.Current.CancellationToken);
                a = new DatiProva.TenantCreato(domini[0].IdTenant, domini[0].IdSito, domini[0].Id, domini[0].Host);
                b = new DatiProva.TenantCreato(domini[1].IdTenant, domini[1].IdSito, domini[1].Id, domini[1].Host);
            }

            return new Scenario(database, fornitore, orologio, a, b, possiede);
        }

        public async Task<RisultatoAccesso> AccediAsync(DatiProva.TenantCreato tenant, string? email, string? password)
        {
            await using var ambito = Ambito(tenant);
            return await ambito.ServiceProvider.GetRequiredService<IServizioAccesso>().AccediAsync(email, password, TestContext.Current.CancellationToken);
        }

        public async Task<IdentitaUtenza?> ConvalidaAsync(DatiProva.TenantCreato tenant, Guid idUtenza, Guid timbro)
        {
            await using var ambito = Ambito(tenant);
            return await ambito.ServiceProvider.GetRequiredService<IServizioAccesso>().ConvalidaSessioneAsync(idUtenza, timbro, TestContext.Current.CancellationToken);
        }

        public async Task<EsitoCambioPassword> CambiaPasswordAsync(DatiProva.TenantCreato tenant, Guid idUtenza, string? attuale, string? nuova)
        {
            await using var ambito = Ambito(tenant);
            return await ambito.ServiceProvider.GetRequiredService<IServizioAccesso>().CambiaPasswordAsync(idUtenza, attuale, nuova, TestContext.Current.CancellationToken);
        }

        public async ValueTask DisposeAsync()
        {
            await Fornitore.DisposeAsync();
            if (_possiedeDatabase)
            {
                await Database.DisposeAsync();
            }
        }

        private AsyncServiceScope Ambito(DatiProva.TenantCreato tenant)
        {
            var ambito = Fornitore.CreateAsyncScope();
            ambito.ServiceProvider.GetRequiredService<ContestoTenant>().Imposta(
                new DatiTenantRisolto(tenant.IdTenant, tenant.IdSito, tenant.IdDominio, tenant.Host, TenantPrimario: false));
            return ambito;
        }
    }
}
