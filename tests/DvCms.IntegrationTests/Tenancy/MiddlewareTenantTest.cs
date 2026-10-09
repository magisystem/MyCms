using System.Net;
using DvCms.Domain.Tenancy;
using DvCms.IntegrationTests.Supporto;
using Xunit;

namespace DvCms.IntegrationTests.Tenancy;

/// <summary>
/// Il tenant dipende solo dall'host dell'URL (B-002): host sconosciuti 404, tenant sospesi 503,
/// nessun valore del client lo sostituisce, intestazioni inoltrate accettate solo da proxy configurati.
/// </summary>
public class MiddlewareTenantTest
{
    [Fact]
    public async Task HostRegistratoRispondeEHostSconosciutoDa404SenzaCorpo()
    {
        await using var database = await DatabaseDiProva.CreaAsync(TestContext.Current.CancellationToken);
        using var fabbrica = new FabbricaApplicazioneConConnessione(database.Connessione);
        await Migra(fabbrica);
        await DatiProva.CreaTenantAsync(fabbrica.Services, "Uno", "uno.esempio.it", annullamento: TestContext.Current.CancellationToken);

        using var registrato = await Richiedi(fabbrica, "uno.esempio.it");
        using var sconosciuto = await Richiedi(fabbrica, "sconosciuto.esempio.it");

        Assert.Equal(HttpStatusCode.OK, registrato.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, sconosciuto.StatusCode);
        Assert.Equal(string.Empty, await sconosciuto.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task TenantSospesoRispondeServizioNonDisponibile()
    {
        await using var database = await DatabaseDiProva.CreaAsync(TestContext.Current.CancellationToken);
        using var fabbrica = new FabbricaApplicazioneConConnessione(database.Connessione);
        await Migra(fabbrica);
        await DatiProva.CreaTenantAsync(fabbrica.Services, "Sospeso", "sospeso.esempio.it", statoTenant: StatoTenant.Sospeso, annullamento: TestContext.Current.CancellationToken);

        using var risposta = await Richiedi(fabbrica, "sospeso.esempio.it");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, risposta.StatusCode);
    }

    [Fact]
    public async Task ValoriDelClientNonSostituisconoIlTenantDellHost()
    {
        await using var database = await DatabaseDiProva.CreaAsync(TestContext.Current.CancellationToken);
        using var fabbrica = new FabbricaApplicazioneConConnessione(database.Connessione, registraRisoluzioni: true);
        await Migra(fabbrica);
        var a = await DatiProva.CreaTenantAsync(fabbrica.Services, "A", "a.esempio.it", annullamento: TestContext.Current.CancellationToken);
        var b = await DatiProva.CreaTenantAsync(fabbrica.Services, "B", "b.esempio.it", annullamento: TestContext.Current.CancellationToken);

        using var client = fabbrica.CreateClient();
        client.DefaultRequestHeaders.Host = "a.esempio.it";
        client.DefaultRequestHeaders.Add("X-Tenant-Id", b.IdTenant.ToString());
        client.DefaultRequestHeaders.Add("X-Forwarded-Host", "b.esempio.it");
        client.DefaultRequestHeaders.Add("X-Original-Host", "b.esempio.it");
        using var risposta = await client.GetAsync($"/admin/accesso?idTenant={b.IdTenant}&host=b.esempio.it", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, risposta.StatusCode);
        Assert.Equal(["a.esempio.it"], fabbrica.Risoluzioni.Host);
        Assert.NotEqual(a.IdTenant, b.IdTenant);
    }

    [Fact]
    public async Task IntestazioniInoltrateSonoIgnorateSenzaProxyConfigurati()
    {
        await using var database = await DatabaseDiProva.CreaAsync(TestContext.Current.CancellationToken);
        using var fabbrica = new FabbricaApplicazioneConConnessione(
            database.Connessione,
            indirizzoRemoto: System.Net.IPAddress.Parse("10.1.2.3"),
            registraRisoluzioni: true);
        await Migra(fabbrica);
        await DatiProva.CreaTenantAsync(fabbrica.Services, "A", "a.esempio.it", annullamento: TestContext.Current.CancellationToken);

        using var client = fabbrica.CreateClient();
        client.DefaultRequestHeaders.Host = "a.esempio.it";
        client.DefaultRequestHeaders.Add("X-Forwarded-Host", "b.esempio.it");
        using var risposta = await client.GetAsync("/admin/accesso", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, risposta.StatusCode);
        Assert.Equal(["a.esempio.it"], fabbrica.Risoluzioni.Host);
    }

    [Theory]
    [InlineData("10.1.2.3", "b.esempio.it")]
    [InlineData("10.9.9.9", "a.esempio.it")]
    public async Task IntestazioniInoltrateSonoAccettateSoloDaProxyConfigurati(string indirizzoRemoto, string hostAtteso)
    {
        await using var database = await DatabaseDiProva.CreaAsync(TestContext.Current.CancellationToken);
        var impostazioni = new Dictionary<string, string?> { ["ProxyAttendibili:Indirizzi:0"] = "10.1.2.3" };
        using var fabbrica = new FabbricaApplicazioneConConnessione(
            database.Connessione,
            impostazioni: impostazioni,
            indirizzoRemoto: System.Net.IPAddress.Parse(indirizzoRemoto),
            registraRisoluzioni: true);
        await Migra(fabbrica);
        await DatiProva.CreaTenantAsync(fabbrica.Services, "A", "a.esempio.it", annullamento: TestContext.Current.CancellationToken);
        await DatiProva.CreaTenantAsync(fabbrica.Services, "B", "b.esempio.it", annullamento: TestContext.Current.CancellationToken);

        using var client = fabbrica.CreateClient();
        client.DefaultRequestHeaders.Host = "a.esempio.it";
        client.DefaultRequestHeaders.Add("X-Forwarded-Host", "b.esempio.it");
        using var risposta = await client.GetAsync("/admin/accesso", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, risposta.StatusCode);
        Assert.Equal([hostAtteso], fabbrica.Risoluzioni.Host);
    }

    [Fact]
    public async Task ConfigurazioneProxyNonValidaFaFallireLAvvio()
    {
        using var fabbrica = new FabbricaApplicazioneConConnessione(
            FabbricaApplicazioneConConnessione_ConnessioneQualsiasi,
            impostazioni: new Dictionary<string, string?> { ["ProxyAttendibili:Indirizzi:0"] = "non-un-indirizzo" });

        await Task.Yield();
        var eccezione = Assert.ThrowsAny<Exception>(() => fabbrica.CreateClient());
        Assert.Contains("non-un-indirizzo", eccezione.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Development", HttpStatusCode.OK)]
    [InlineData("Production", HttpStatusCode.NotFound)]
    public async Task LocalhostRisolveIlTenantDiGestioneSoloInSviluppo(string ambiente, HttpStatusCode atteso)
    {
        await using var database = await DatabaseDiProva.CreaAsync(TestContext.Current.CancellationToken);
        using var fabbrica = new FabbricaApplicazioneConConnessione(database.Connessione, ambiente);
        await Migra(fabbrica);
        await DatiProva.CreaTenantAsync(fabbrica.Services, "Gestione", "localhost", primario: true, soloSviluppo: true, annullamento: TestContext.Current.CancellationToken);

        // Il client di test usa l'host "localhost" per impostazione predefinita; la porta non conta.
        using var client = fabbrica.CreateClient();
        client.DefaultRequestHeaders.Host = "localhost:5173";
        using var risposta = await client.GetAsync("/admin/accesso", TestContext.Current.CancellationToken);

        Assert.Equal(atteso, risposta.StatusCode);
    }

    private const string FabbricaApplicazioneConConnessione_ConnessioneQualsiasi =
        "Server=localhost;Database=DvCmsNonUsato;Integrated Security=false;TrustServerCertificate=True";

    private static async Task Migra(FabbricaApplicazioneConConnessione fabbrica)
    {
        var esito = await DvCms.Web.Comandi.ComandoMigra.EseguiAsync(fabbrica.Services, TestContext.Current.CancellationToken);
        Assert.Equal(0, esito);
    }

    private static async Task<HttpResponseMessage> Richiedi(FabbricaApplicazioneConConnessione fabbrica, string host)
    {
        using var client = fabbrica.CreateClient();
        client.DefaultRequestHeaders.Host = host;
        return await client.GetAsync("/admin/accesso", TestContext.Current.CancellationToken);
    }
}
