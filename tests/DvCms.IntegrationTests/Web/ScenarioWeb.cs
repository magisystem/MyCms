using DvCms.Domain.Accessi;
using DvCms.Infrastructure.Persistenza;
using DvCms.IntegrationTests.Supporto;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DvCms.IntegrationTests.Web;

/// <summary>Applicazione web con database reale e due tenant (A e B), ciascuno con il proprio host.</summary>
public sealed class ScenarioWeb : IAsyncDisposable
{
    public const string HostA = "a.esempio.it";
    public const string HostB = "b.esempio.it";

    private readonly DatabaseDiProva _database;

    private ScenarioWeb(DatabaseDiProva database, FabbricaApplicazioneConConnessione fabbrica, DatiProva.TenantCreato a, DatiProva.TenantCreato b)
    {
        _database = database;
        Fabbrica = fabbrica;
        A = a;
        B = b;
    }

    public FabbricaApplicazioneConConnessione Fabbrica { get; }

    public DatiProva.TenantCreato A { get; }

    public DatiProva.TenantCreato B { get; }

    public static async Task<ScenarioWeb> CreaAsync(string ambiente = "Development", int richiesteAlMinuto = 1000)
    {
        var database = await DatabaseDiProva.CreaAsync(TestContext.Current.CancellationToken);
        var fabbrica = new FabbricaApplicazioneConConnessione(
            database.Connessione,
            ambiente,
            new Dictionary<string, string?> { ["Sicurezza:Accesso:RichiesteAlMinuto"] = richiesteAlMinuto.ToString(System.Globalization.CultureInfo.InvariantCulture) });
        var esito = await DvCms.Web.Comandi.ComandoMigra.EseguiAsync(fabbrica.Services, TestContext.Current.CancellationToken);
        Assert.Equal(0, esito);

        var a = await DatiProva.CreaTenantAsync(fabbrica.Services, "Tenant A", HostA, annullamento: TestContext.Current.CancellationToken);
        var b = await DatiProva.CreaTenantAsync(fabbrica.Services, "Tenant B", HostB, annullamento: TestContext.Current.CancellationToken);
        return new ScenarioWeb(database, fabbrica, a, b);
    }

    public SessioneProva SessioneA() => Nuova(HostA);

    public SessioneProva SessioneB() => Nuova(HostB);

    public Task<Guid> CreaTenantAdminAsync(string email, bool deveCambiarePassword = false) =>
        DatiProva.CreaUtenzaAsync(
            Fabbrica.Services,
            RuoloUtenza.TenantAdmin,
            A.IdTenant,
            email,
            deveCambiarePassword: deveCambiarePassword,
            annullamento: TestContext.Current.CancellationToken);

    public Task<Guid> CreaPlatformAdminAsync(string email) =>
        DatiProva.CreaUtenzaAsync(
            Fabbrica.Services,
            RuoloUtenza.PlatformAdmin,
            null,
            email,
            annullamento: TestContext.Current.CancellationToken);

    public async Task DisabilitaAsync(Guid idUtenza)
    {
        await using var ambito = Fabbrica.Services.CreateAsyncScope();
        var contesto = ambito.ServiceProvider.GetRequiredService<ContestoDatabase>();
        var utenza = await contesto.Utenze.IgnoreQueryFilters().SingleAsync(candidata => candidata.Id == idUtenza, TestContext.Current.CancellationToken);
        utenza.Stato = StatoUtenza.Disabilitata;
        await contesto.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        Fabbrica.Dispose();
        await _database.DisposeAsync();
    }

    private SessioneProva Nuova(string host)
    {
        var client = Fabbrica.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false,
            BaseAddress = new Uri(Fabbrica.Services.GetRequiredService<Microsoft.AspNetCore.Hosting.IWebHostEnvironment>().EnvironmentName == "Development" ? "http://localhost" : "https://localhost"),
        });
        return new SessioneProva(client, host);
    }
}
