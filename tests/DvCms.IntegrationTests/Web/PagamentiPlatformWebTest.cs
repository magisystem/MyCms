using System.Net;
using System.Security.Cryptography;
using DvCms.Application.Negozio;
using DvCms.Infrastructure.Persistenza;
using DvCms.IntegrationTests.Supporto;
using DvCms.Models.Negozio;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DvCms.IntegrationTests.Web;

/// <summary>Credenziali PayPal per sito nel menu Platform (ADR-0020): sola scrittura dei segreti, cifratura, permessi, isolamento.</summary>
public class PagamentiPlatformWebTest
{
    private const string Segreto = "SEGRETO-DEL-CLIENT-PAYPAL";
    private const string Webhook = "WEBHOOK-ID-RISERVATO";

    private static readonly Dictionary<string, string?> _conChiave = new() { ["Sicurezza:ChiaveSegreti"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Pagina(ScenarioWeb scenario) => $"/admin/platform/tenant/{scenario.A.IdTenant}/pagamenti?idSito={scenario.A.IdSito}";

    private static List<KeyValuePair<string, string>> Campi(string client = "client-sandbox-1", string segreto = Segreto, string webhook = Webhook, string ambiente = "Sandbox") =>
        [new("ambiente", ambiente), new("idClient", client), new("segretoClient", segreto), new("idWebhook", webhook)];

    [Fact]
    public async Task UnTenantAdminNonVedeNeModificaLeCredenzialiDiPagamento()
    {
        await using var scenario = await ScenarioWeb.CreaAsync(impostazioni: _conChiave);
        await scenario.CreaTenantAdminAsync("anna@esempio.it");
        var anna = scenario.SessioneA();
        await anna.AccediAsync("anna@esempio.it", DatiProva.PasswordProva);

        using var lettura = await anna.GetAsync(Pagina(scenario));
        using var salva = await anna.InviaModuloAsync("/admin", Campi(), $"{Pagina(scenario)}&handler=Salva");

        Assert.Equal(HttpStatusCode.Forbidden, lettura.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, salva.StatusCode);
        await using var ambito = scenario.Fabbrica.Services.CreateAsyncScope();
        Assert.Empty(await ambito.ServiceProvider.GetRequiredService<ContestoDatabase>().ImpostazioniPagamentoSiti.IgnoreQueryFilters().ToListAsync(Ct));
    }

    [Fact]
    public async Task IlPlatformAdminSalvaEISegretiSonoCifratiENonTornanoMaNellaPagina()
    {
        await using var scenario = await ScenarioWeb.CreaAsync(impostazioni: _conChiave);
        await scenario.CreaPlatformAdminAsync("platform@esempio.it");
        var sessione = scenario.SessioneB();
        await sessione.AccediAsync("platform@esempio.it", DatiProva.PasswordProva);

        using var salva = await sessione.InviaModuloAsync(Pagina(scenario), Campi(), $"{Pagina(scenario)}&handler=Salva");
        using var dopo = await sessione.GetAsync(Pagina(scenario));

        Assert.True(SessioneProva.EReindirizzamento(salva), await salva.Content.ReadAsStringAsync(Ct));
        var html = await dopo.Content.ReadAsStringAsync(Ct);
        Assert.Contains("client-sandbox-1", html, StringComparison.Ordinal);
        Assert.DoesNotContain(Segreto, html, StringComparison.Ordinal);
        Assert.DoesNotContain(Webhook, html, StringComparison.Ordinal);
        Assert.Contains("lascia vuoto per non cambiarlo", html, StringComparison.Ordinal);
        await using var ambito = scenario.Fabbrica.Services.CreateAsyncScope();
        var riga = await ambito.ServiceProvider.GetRequiredService<ContestoDatabase>().ImpostazioniPagamentoSiti.IgnoreQueryFilters().SingleAsync(Ct);
        Assert.DoesNotContain(Segreto, riga.SegretiProtetti + riga.IdClient, StringComparison.Ordinal);
        Assert.DoesNotContain(Webhook, riga.SegretiProtetti, StringComparison.Ordinal);
        var credenziali = await ambito.ServiceProvider.GetRequiredService<IArchivioCredenzialiPagamento>().OttieniAsync(scenario.A.IdTenant, scenario.A.IdSito, Ct);
        Assert.Equal((AmbientePagamento.Sandbox, "client-sandbox-1", Segreto, Webhook), (credenziali!.Ambiente, credenziali.IdClient, credenziali.SegretoClient, credenziali.IdWebhook));
    }

    [Fact]
    public async Task UnSegretoLasciatoVuotoRestaQuelloSalvatoEUnoMancanteNonSiSalva()
    {
        await using var scenario = await ScenarioWeb.CreaAsync(impostazioni: _conChiave);
        await scenario.CreaPlatformAdminAsync("platform@esempio.it");
        var sessione = scenario.SessioneB();
        await sessione.AccediAsync("platform@esempio.it", DatiProva.PasswordProva);
        using var prima = await sessione.InviaModuloAsync(Pagina(scenario), Campi(), $"{Pagina(scenario)}&handler=Salva");

        using var cambioClient = await sessione.InviaModuloAsync(Pagina(scenario), Campi(client: "client-nuovo", segreto: string.Empty, webhook: string.Empty, ambiente: "Live"), $"{Pagina(scenario)}&handler=Salva");
        using var clientVuoto = await sessione.InviaModuloAsync(Pagina(scenario), Campi(client: string.Empty, segreto: string.Empty, webhook: string.Empty), $"{Pagina(scenario)}&handler=Salva");

        Assert.True(SessioneProva.EReindirizzamento(cambioClient));
        Assert.Equal(HttpStatusCode.OK, clientVuoto.StatusCode);
        Assert.Contains("is-invalid", await clientVuoto.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        await using var ambito = scenario.Fabbrica.Services.CreateAsyncScope();
        var credenziali = await ambito.ServiceProvider.GetRequiredService<IArchivioCredenzialiPagamento>().OttieniAsync(scenario.A.IdTenant, scenario.A.IdSito, Ct);
        Assert.Equal((AmbientePagamento.Live, "client-nuovo", Segreto, Webhook), (credenziali!.Ambiente, credenziali.IdClient, credenziali.SegretoClient, credenziali.IdWebhook));
    }

    [Fact]
    public async Task SenzaSegretoONoNonSiSalvaEUnSitoDiUnAltroTenantNonSiApre()
    {
        await using var scenario = await ScenarioWeb.CreaAsync(impostazioni: _conChiave);
        await scenario.CreaPlatformAdminAsync("platform@esempio.it");
        var sessione = scenario.SessioneB();
        await sessione.AccediAsync("platform@esempio.it", DatiProva.PasswordProva);

        using var senzaSegreto = await sessione.InviaModuloAsync(Pagina(scenario), Campi(segreto: string.Empty), $"{Pagina(scenario)}&handler=Salva");
        using var sitoAltrui = await sessione.GetAsync($"/admin/platform/tenant/{scenario.A.IdTenant}/pagamenti?idSito={scenario.B.IdSito}");
        using var tenantInesistente = await sessione.GetAsync($"/admin/platform/tenant/{Guid.NewGuid()}/pagamenti?idSito={scenario.A.IdSito}");

        Assert.Equal(HttpStatusCode.OK, senzaSegreto.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, sitoAltrui.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, tenantInesistente.StatusCode);
        await using var ambito = scenario.Fabbrica.Services.CreateAsyncScope();
        Assert.Null(await ambito.ServiceProvider.GetRequiredService<IArchivioCredenzialiPagamento>().OttieniAsync(scenario.A.IdTenant, scenario.A.IdSito, Ct));
    }

    [Fact]
    public async Task ConLaChiaveDeiSegretiMancanteNonSiSalvaNulla()
    {
        await using var scenario = await ScenarioWeb.CreaAsync();
        await scenario.CreaPlatformAdminAsync("platform@esempio.it");
        var sessione = scenario.SessioneB();
        await sessione.AccediAsync("platform@esempio.it", DatiProva.PasswordProva);

        using var risposta = await sessione.InviaModuloAsync(Pagina(scenario), Campi(), $"{Pagina(scenario)}&handler=Salva");

        Assert.True(SessioneProva.EReindirizzamento(risposta));
        await using var ambito = scenario.Fabbrica.Services.CreateAsyncScope();
        Assert.Empty(await ambito.ServiceProvider.GetRequiredService<ContestoDatabase>().ImpostazioniPagamentoSiti.IgnoreQueryFilters().ToListAsync(Ct));
    }
}
