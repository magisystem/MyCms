using DvCms.Application.Accessi;
using DvCms.Domain.Accessi;
using DvCms.Domain.Tenancy;
using DvCms.Infrastructure.Persistenza;
using DvCms.Infrastructure.Popolamento;
using DvCms.IntegrationTests.Supporto;
using DvCms.Web.Comandi;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DvCms.IntegrationTests.Popolamento;

public class PopolamentoTest
{
    private const string PasswordAdmin = "password-platform-123";
    private const string PasswordCliente = "password-cliente-1234";
    private const string HostPrimario = "cms.davidemartelli.dev";

    [Fact]
    public async Task CreaTenantPrimarioSitoDominioGuestEPlatformAdmin()
    {
        await using var ambiente = await Ambiente.CreaAsync("Production");

        var esito = await ambiente.EseguiAsync();

        Assert.Empty(esito.Avvisi);
        var contesto = ambiente.Contesto;
        var tenant = await contesto.Tenant.IgnoreQueryFilters().SingleAsync(TestContext.Current.CancellationToken);
        Assert.True(tenant.Primario);
        Assert.Equal(StatoTenant.Attivo, tenant.Stato);
        var sito = await contesto.Siti.IgnoreQueryFilters().SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(tenant.Id, sito.IdTenant);
        var dominio = await contesto.DominiSito.IgnoreQueryFilters().SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(HostPrimario, dominio.Host);
        Assert.False(dominio.SoloSviluppo);
        Assert.NotNull(dominio.VerificatoIlUtc);
        Assert.Equal(sito.Id, dominio.IdSito);
        Assert.Single(await contesto.ProfiliGuest.IgnoreQueryFilters().ToListAsync(TestContext.Current.CancellationToken));

        var admin = await contesto.Utenze.IgnoreQueryFilters().SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(RuoloUtenza.PlatformAdmin, admin.Ruolo);
        Assert.Null(admin.IdTenant);
        Assert.False(admin.DeveCambiarePassword);
        Assert.NotEqual(PasswordAdmin, admin.HashPassword);
        Assert.True(ambiente.Hash.Verifica(PasswordAdmin, admin.HashPassword));
    }

    [Fact]
    public async Task ScriveUnaVoceDiAuditSenzaSegreti()
    {
        await using var ambiente = await Ambiente.CreaAsync("Production");

        await ambiente.EseguiAsync();

        var voce = await ambiente.Contesto.RegistroAudit.IgnoreQueryFilters().SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(AzioniAudit.Popolamento, voce.Azione);
        Assert.Null(voce.IdUtenzaAttore);
        Assert.DoesNotContain(PasswordAdmin, voce.Dettagli, StringComparison.Ordinal);
        Assert.DoesNotContain("BCrypt", voce.Dettagli, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RilanciatoNonDuplicaNonReimpostaLaPasswordENonScriveAltroAudit()
    {
        await using var ambiente = await Ambiente.CreaAsync("Production");
        await ambiente.EseguiAsync();
        var hashIniziale = await ambiente.Contesto.Utenze.IgnoreQueryFilters().Select(utenza => utenza.HashPassword).SingleAsync(TestContext.Current.CancellationToken);

        var secondo = await ambiente.EseguiAsync(passwordInserita: "un-altra-password-456");

        Assert.Empty(secondo.Creati);
        Assert.Empty(secondo.Avvisi);
        await using var ambito = ambiente.Fabbrica.Services.CreateAsyncScope();
        var contesto = ambito.ServiceProvider.GetRequiredService<ContestoDatabase>();
        Assert.Equal(1, await contesto.Tenant.IgnoreQueryFilters().CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, await contesto.DominiSito.IgnoreQueryFilters().CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, await contesto.RegistroAudit.IgnoreQueryFilters().CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(hashIniziale, await contesto.Utenze.IgnoreQueryFilters().Select(utenza => utenza.HashPassword).SingleAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SenzaPasswordIlPlatformAdminFallisceENonCreaNulla()
    {
        await using var ambiente = await Ambiente.CreaAsync("Production", senzaPasswordAdmin: true);

        var errore = await Assert.ThrowsAsync<InvalidOperationException>(() => ambiente.EseguiAsync());

        Assert.Contains("Seed__PlatformAdmin__Password", errore.Message, StringComparison.Ordinal);
        Assert.Equal(0, await ambiente.Contesto.Tenant.IgnoreQueryFilters().CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(0, await ambiente.Contesto.Utenze.IgnoreQueryFilters().CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task UnaPasswordTroppoCortaVieneRifiutata()
    {
        await using var ambiente = await Ambiente.CreaAsync("Production", passwordAdmin: "corta");

        var errore = await Assert.ThrowsAsync<InvalidOperationException>(() => ambiente.EseguiAsync());

        Assert.DoesNotContain("corta", errore.Message, StringComparison.Ordinal);
        Assert.Equal(0, await ambiente.Contesto.Utenze.IgnoreQueryFilters().CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task LaPasswordInseritaAManoSostituisceQuellaMancanteInConfigurazione()
    {
        await using var ambiente = await Ambiente.CreaAsync("Production", senzaPasswordAdmin: true);

        await ambiente.EseguiAsync(passwordInserita: "inserita-a-mano-123");

        var admin = await ambiente.Contesto.Utenze.IgnoreQueryFilters().SingleAsync(TestContext.Current.CancellationToken);
        Assert.True(ambiente.Hash.Verifica("inserita-a-mano-123", admin.HashPassword));
    }

    [Fact]
    public async Task DatiEsistentiCheDivergonoGeneranoAvvisiENonVengonoModificati()
    {
        await using var ambiente = await Ambiente.CreaAsync("Production");
        await ambiente.EseguiAsync();
        await using var ambienteConNomeDiverso = await Ambiente.CreaAsync("Production", nomeTenant: "Altro nome", database: ambiente.Database);

        var esito = await ambienteConNomeDiverso.EseguiAsync();

        Assert.Empty(esito.Creati);
        Assert.Contains(esito.Avvisi, avviso => avviso.Contains("Altro nome", StringComparison.Ordinal));
        var tenant = await ambiente.Contesto.Tenant.IgnoreQueryFilters().SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal("Primario", tenant.Nome);
    }

    [Fact]
    public async Task UnDominioGiaDiUnAltroTenantNonVieneRiassegnato()
    {
        await using var ambiente = await Ambiente.CreaAsync("Production", migra: true);
        var altro = await DatiProva.CreaTenantAsync(ambiente.Fabbrica.Services, "Altro", HostPrimario, annullamento: TestContext.Current.CancellationToken);

        var esito = await ambiente.EseguiAsync();

        Assert.Contains(esito.Avvisi, avviso => avviso.Contains("altro tenant", StringComparison.Ordinal));
        var dominio = await ambiente.Contesto.DominiSito.IgnoreQueryFilters().SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(altro.IdTenant, dominio.IdTenant);
    }

    [Fact]
    public async Task SeEsisteGiaUnPlatformAdminNonNeCreaUnSecondo()
    {
        await using var ambiente = await Ambiente.CreaAsync("Production", migra: true);
        await DatiProva.CreaUtenzaAsync(ambiente.Fabbrica.Services, RuoloUtenza.PlatformAdmin, null, "altro@esempio.it", annullamento: TestContext.Current.CancellationToken);

        var esito = await ambiente.EseguiAsync();

        Assert.Contains(esito.Avvisi, avviso => avviso.Contains("PlatformAdmin", StringComparison.Ordinal));
        Assert.Equal(1, await ambiente.Contesto.Utenze.IgnoreQueryFilters().CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task InProduzioneLocalhostEITenantDiSviluppoNonVengonoCreati()
    {
        await using var ambiente = await Ambiente.CreaAsync("Production", conDatiSviluppo: true);

        var esito = await ambiente.EseguiAsync();

        var host = await ambiente.Contesto.DominiSito.IgnoreQueryFilters().Select(dominio => dominio.Host).ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal([HostPrimario], host);
        Assert.Equal(1, await ambiente.Contesto.Tenant.IgnoreQueryFilters().CountAsync(TestContext.Current.CancellationToken));
        Assert.Empty(esito.Avvisi);
    }

    [Fact]
    public async Task InSviluppoCreaLocalhostEITenantDiProvaConIlLoroTenantAdmin()
    {
        await using var ambiente = await Ambiente.CreaAsync("Development", conDatiSviluppo: true);

        await ambiente.EseguiAsync();

        var contesto = ambiente.Contesto;
        var domini = await contesto.DominiSito.IgnoreQueryFilters().ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal(["cms.davidemartelli.dev", "cms1.davidemartelli.dev", "cms2.davidemartelli.dev", "localhost"], domini.Select(dominio => dominio.Host).Order().ToArray());
        Assert.True(domini.Single(dominio => dominio.Host == "localhost").SoloSviluppo);
        Assert.Equal(3, await contesto.Tenant.IgnoreQueryFilters().CountAsync(TestContext.Current.CancellationToken));
        Assert.Single(await contesto.Tenant.IgnoreQueryFilters().Where(tenant => tenant.Primario).ToListAsync(TestContext.Current.CancellationToken));
        Assert.Equal(3, await contesto.ProfiliGuest.IgnoreQueryFilters().CountAsync(TestContext.Current.CancellationToken));

        var amministratori = await contesto.Utenze.IgnoreQueryFilters().Where(utenza => utenza.Ruolo == RuoloUtenza.TenantAdmin).ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, amministratori.Count);
        Assert.Equal(2, amministratori.Select(utenza => utenza.IdTenant).Distinct().Count());
        Assert.All(amministratori, utenza => Assert.True(ambiente.Hash.Verifica(PasswordCliente, utenza.HashPassword)));
    }

    [Fact]
    public async Task InSviluppoUnTenantDiProvaSenzaPasswordFallisce()
    {
        await using var ambiente = await Ambiente.CreaAsync("Development", conDatiSviluppo: true, senzaPasswordCliente: true);

        var errore = await Assert.ThrowsAsync<InvalidOperationException>(() => ambiente.EseguiAsync());

        Assert.Contains("Password mancante", errore.Message, StringComparison.Ordinal);
        Assert.Equal(0, await ambiente.Contesto.Tenant.IgnoreQueryFilters().CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task IlPlatformAdminSeminatoAccedeDalDominioDelTenantPrimario()
    {
        await using var ambiente = await Ambiente.CreaAsync("Production");
        await ambiente.EseguiAsync();
        using var client = ambiente.Fabbrica.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false,
            BaseAddress = new Uri("https://localhost"),
        });
        var sessione = new SessioneProva(client, HostPrimario);

        using var accesso = await sessione.AccediAsync("platform@esempio.it", PasswordAdmin);

        Assert.True(SessioneProva.EReindirizzamento(accesso));
        Assert.Equal("/admin", SessioneProva.Destinazione(accesso));
        Assert.NotNull(sessione.CookieAdmin);
    }

    [Fact]
    public async Task IlComandoSeedRestituisceZeroSeRiesceEUnoSeMancaLaPassword()
    {
        await using var buono = await Ambiente.CreaAsync("Production");
        Assert.Equal(0, await ComandoSeed.EseguiAsync(buono.Fabbrica.Services, consentiRichiestaInterattiva: false, TestContext.Current.CancellationToken));

        await using var senza = await Ambiente.CreaAsync("Production", senzaPasswordAdmin: true);
        Assert.Equal(1, await ComandoSeed.EseguiAsync(senza.Fabbrica.Services, consentiRichiestaInterattiva: false, TestContext.Current.CancellationToken));
        Assert.Equal(0, await senza.Contesto.Utenze.IgnoreQueryFilters().CountAsync(TestContext.Current.CancellationToken));
    }

    private sealed class Ambiente : IAsyncDisposable
    {
        private readonly bool _possiedeDatabase;
        private readonly AsyncServiceScope _ambito;

        private Ambiente(DatabaseDiProva database, bool possiedeDatabase, FabbricaApplicazioneConConnessione fabbrica)
        {
            Database = database;
            _possiedeDatabase = possiedeDatabase;
            Fabbrica = fabbrica;
            _ambito = fabbrica.Services.CreateAsyncScope();
        }

        public DatabaseDiProva Database { get; }

        public FabbricaApplicazioneConConnessione Fabbrica { get; }

        public ContestoDatabase Contesto => _ambito.ServiceProvider.GetRequiredService<ContestoDatabase>();

        public IHashPassword Hash => _ambito.ServiceProvider.GetRequiredService<IHashPassword>();

        public static async Task<Ambiente> CreaAsync(
            string ambiente,
            bool senzaPasswordAdmin = false,
            string passwordAdmin = PasswordAdmin,
            string? nomeTenant = null,
            bool conDatiSviluppo = false,
            bool senzaPasswordCliente = false,
            DatabaseDiProva? database = null,
            bool migra = false)
        {
            var possiede = database is null;
            database ??= await DatabaseDiProva.CreaAsync(TestContext.Current.CancellationToken);
            var impostazioni = new Dictionary<string, string?>
            {
                ["Seed:PlatformAdmin:Email"] = "platform@esempio.it",
                ["Seed:PlatformAdmin:NomeVisualizzato"] = "Operatore",
            };
            if (!senzaPasswordAdmin)
            {
                impostazioni["Seed:PlatformAdmin:Password"] = passwordAdmin;
            }

            if (nomeTenant is not null)
            {
                impostazioni["Seed:TenantPrimario:Nome"] = nomeTenant;
            }

            if (conDatiSviluppo && !senzaPasswordCliente)
            {
                impostazioni["Seed:TenantSviluppo:0:TenantAdmin:Password"] = PasswordCliente;
                impostazioni["Seed:TenantSviluppo:1:TenantAdmin:Password"] = PasswordCliente;
            }

            var fabbrica = new FabbricaApplicazioneConConnessione(database.Connessione, ambiente, impostazioni);
            if (possiede || migra)
            {
                var esito = await DvCms.Web.Comandi.ComandoMigra.EseguiAsync(fabbrica.Services, TestContext.Current.CancellationToken);
                Assert.Equal(0, esito);
            }

            return new Ambiente(database, possiede, fabbrica);
        }

        public async Task<EsitoPopolamento> EseguiAsync(string? passwordInserita = null)
        {
            await using var ambito = Fabbrica.Services.CreateAsyncScope();
            var servizio = ambito.ServiceProvider.GetRequiredService<ServizioPopolamento>();
            return await servizio.EseguiAsync(passwordInserita, TestContext.Current.CancellationToken);
        }

        public async ValueTask DisposeAsync()
        {
            await _ambito.DisposeAsync();
            Fabbrica.Dispose();
            if (_possiedeDatabase)
            {
                await Database.DisposeAsync();
            }
        }
    }
}
