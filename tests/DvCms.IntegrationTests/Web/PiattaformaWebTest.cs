using System.Net;
using System.Text.RegularExpressions;
using DvCms.Data;
using DvCms.Infrastructure.Persistenza;
using DvCms.IntegrationTests.Supporto;
using DvCms.Models.Accessi;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DvCms.IntegrationTests.Web;

/// <summary>Area "Platform" (B-006) e creazione dei TenantAdmin (B-007) attraverso l'applicazione web.</summary>
public partial class PiattaformaWebTest
{
    private const string Elenco = "/admin/platform/tenant";

    [Fact]
    public async Task SenzaSessioneLAreaPlatformRimandaAllAccesso()
    {
        await using var scenario = await ScenarioWeb.CreaAsync();

        using var risposta = await scenario.SessioneA().GetAsync(Elenco);

        Assert.True(SessioneProva.EReindirizzamento(risposta));
        Assert.StartsWith("/admin/accesso", SessioneProva.Destinazione(risposta), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/admin/platform/tenant")]
    [InlineData("/admin/platform/tenant/nuovo")]
    public async Task UnTenantAdminRiceve403SulleSchermateDellAreaPlatform(string percorso)
    {
        await using var scenario = await ScenarioWeb.CreaAsync();
        await scenario.CreaTenantAdminAsync("anna@esempio.it");
        var sessione = scenario.SessioneA();
        await sessione.AccediAsync("anna@esempio.it", DatiProva.PasswordProva);

        using var risposta = await sessione.GetAsync(percorso);

        Assert.Equal(HttpStatusCode.Forbidden, risposta.StatusCode);
    }

    [Fact]
    public async Task UnTenantAdminRiceve403AncheSulDettaglioDelProprioTenant()
    {
        await using var scenario = await ScenarioWeb.CreaAsync();
        await scenario.CreaTenantAdminAsync("anna@esempio.it");
        var sessione = scenario.SessioneA();
        await sessione.AccediAsync("anna@esempio.it", DatiProva.PasswordProva);

        using var risposta = await sessione.GetAsync($"{Elenco}/{scenario.A.IdTenant}");

        Assert.Equal(HttpStatusCode.Forbidden, risposta.StatusCode);
    }

    [Fact]
    public async Task IlMenuPlatformEVisibileSoloAlPlatformAdmin()
    {
        await using var scenario = await ScenarioWeb.CreaAsync();
        await scenario.CreaTenantAdminAsync("anna@esempio.it");
        await scenario.CreaPlatformAdminAsync("platform@esempio.it");
        var tenantAdmin = scenario.SessioneA();
        await tenantAdmin.AccediAsync("anna@esempio.it", DatiProva.PasswordProva);
        var platform = scenario.SessioneB();
        await platform.AccediAsync("platform@esempio.it", DatiProva.PasswordProva);

        using var paginaTenantAdmin = await tenantAdmin.GetAsync("/admin");
        using var paginaPlatform = await platform.GetAsync("/admin");

        Assert.DoesNotContain("/admin/platform", await paginaTenantAdmin.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);
        Assert.Contains("/admin/platform/tenant", await paginaPlatform.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);
    }

    [Fact]
    public async Task IlPlatformAdminVedeITenantEPuoAprireIlDettaglio()
    {
        await using var scenario = await ScenarioWeb.CreaAsync();
        await scenario.CreaPlatformAdminAsync("platform@esempio.it");
        var sessione = scenario.SessioneB();
        await sessione.AccediAsync("platform@esempio.it", DatiProva.PasswordProva);

        using var elenco = await sessione.GetAsync(Elenco);
        using var dettaglio = await sessione.GetAsync($"{Elenco}/{scenario.A.IdTenant}");
        using var inesistente = await sessione.GetAsync($"{Elenco}/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.OK, elenco.StatusCode);
        var htmlElenco = await elenco.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("Tenant A", htmlElenco, StringComparison.Ordinal);
        Assert.Contains("Tenant B", htmlElenco, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, dettaglio.StatusCode);
        var htmlDettaglio = await dettaglio.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains(ScenarioWeb.HostA, htmlDettaglio, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NotFound, inesistente.StatusCode);
    }

    [Fact]
    public async Task IlPlatformAdminCreaUnTenantDalModulo()
    {
        await using var scenario = await ScenarioWeb.CreaAsync();
        await scenario.CreaPlatformAdminAsync("platform@esempio.it");
        var sessione = scenario.SessioneB();
        await sessione.AccediAsync("platform@esempio.it", DatiProva.PasswordProva);

        using var risposta = await sessione.InviaModuloAsync(
            $"{Elenco}/nuovo",
            [
                new("Nome", "Cliente Nuovo"),
                new("NomeSito", "Sito Nuovo"),
                new("FusoOrario", "Europe/Rome"),
                new("Host", "nuovo.esempio.it"),
                new("DominioVerificato", "true"),
            ]);

        Assert.True(SessioneProva.EReindirizzamento(risposta));
        Assert.StartsWith(Elenco + "/", SessioneProva.Destinazione(risposta), StringComparison.Ordinal);
        await using var ambito = scenario.Fabbrica.Services.CreateAsyncScope();
        var contesto = ambito.ServiceProvider.GetRequiredService<ContestoDatabase>();
        Assert.True(await contesto.DominiSito.IgnoreQueryFilters().AnyAsync(dominio => dominio.Host == "nuovo.esempio.it" && dominio.VerificatoIlUtc != null, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task UnModuloConErroreMostraIlMessaggioENonCreaNulla()
    {
        await using var scenario = await ScenarioWeb.CreaAsync();
        await scenario.CreaPlatformAdminAsync("platform@esempio.it");
        var sessione = scenario.SessioneB();
        await sessione.AccediAsync("platform@esempio.it", DatiProva.PasswordProva);

        using var risposta = await sessione.InviaModuloAsync(
            $"{Elenco}/nuovo",
            [new("Nome", "Cliente"), new("NomeSito", "Sito"), new("Host", ScenarioWeb.HostA), new("DominioVerificato", "true")]);

        Assert.Equal(HttpStatusCode.OK, risposta.StatusCode);
        Assert.Contains("alert-danger", await risposta.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnTenantAdminCheForgiaLeRichiesteVieneRespintoEntroIlPortale()
    {
        await using var scenario = await ScenarioWeb.CreaAsync();
        var idAdmin = await scenario.CreaTenantAdminAsync("anna@esempio.it");
        var sessione = scenario.SessioneA();
        await sessione.AccediAsync("anna@esempio.it", DatiProva.PasswordProva);
        var dettaglio = $"{Elenco}/{scenario.A.IdTenant}";

        // Il token antiforgery è valido (viene dal cruscotto): è l'autorizzazione a negare, non il token.
        using var creaAmministratore = await sessione.InviaModuloAsync(
            "/admin",
            [new("email", "complice@esempio.it"), new("nomeVisualizzato", "Complice")],
            $"{dettaglio}?handler=NuovoAmministratore");
        using var creaTenant = await sessione.InviaModuloAsync(
            "/admin",
            [new("Nome", "Abusivo"), new("NomeSito", "Sito"), new("Host", "abusivo.esempio.it"), new("DominioVerificato", "true")],
            $"{Elenco}/nuovo");
        using var sospendi = await sessione.InviaModuloAsync("/admin", [], $"{Elenco}/{scenario.B.IdTenant}?handler=Sospendi");
        using var staccaIlRuolo = await sessione.InviaModuloAsync(
            "/admin",
            [new("idUtenza", idAdmin.ToString())],
            $"{dettaglio}?handler=Promuovi");

        Assert.Equal(HttpStatusCode.Forbidden, creaAmministratore.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, creaTenant.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, sospendi.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, staccaIlRuolo.StatusCode);
        await using var ambito = scenario.Fabbrica.Services.CreateAsyncScope();
        var contesto = ambito.ServiceProvider.GetRequiredService<ContestoDatabase>();
        Assert.Equal(2, await contesto.Tenant.IgnoreQueryFilters().CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, await contesto.Utenze.IgnoreQueryFilters().CountAsync(TestContext.Current.CancellationToken));
        Assert.False(await contesto.Utenze.IgnoreQueryFilters().AnyAsync(u => u.Ruolo == RuoloUtenza.PlatformAdmin, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task UnaScritturaSenzaTokenAntiforgeryVieneRifiutataAnchePerIlPlatformAdmin()
    {
        await using var scenario = await ScenarioWeb.CreaAsync();
        await scenario.CreaPlatformAdminAsync("platform@esempio.it");
        var sessione = scenario.SessioneB();
        await sessione.AccediAsync("platform@esempio.it", DatiProva.PasswordProva);

        using var risposta = await sessione.InviaModuloAsync(
            "/admin",
            [],
            $"{Elenco}/{scenario.A.IdTenant}?handler=Sospendi",
            conToken: false);

        Assert.Equal(HttpStatusCode.BadRequest, risposta.StatusCode);
    }

    [Fact]
    public async Task IlTenantAdminCreatoHaUnaPasswordTemporaneaMostrataUnaSolaVoltaECambioObbligatorio()
    {
        await using var scenario = await ScenarioWeb.CreaAsync();
        await scenario.CreaPlatformAdminAsync("platform@esempio.it");
        var platform = scenario.SessioneB();
        await platform.AccediAsync("platform@esempio.it", DatiProva.PasswordProva);
        var dettaglio = $"{Elenco}/{scenario.A.IdTenant}";

        using var creazione = await platform.InviaModuloAsync(
            dettaglio,
            [new("email", "nuova@esempio.it"), new("nomeVisualizzato", "Nuova Amministratrice")],
            $"{dettaglio}?handler=NuovoAmministratore");

        Assert.Equal(HttpStatusCode.OK, creazione.StatusCode);
        Assert.Contains("no-store", string.Join(',', creazione.Headers.CacheControl?.ToString() ?? string.Empty), StringComparison.OrdinalIgnoreCase);
        var html = await creazione.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var password = PasswordMostrata().Match(html).Groups[1].Value;
        Assert.Equal(20, password.Length);

        // Una nuova visita non la mostra più.
        using var successiva = await platform.GetAsync(dettaglio);
        Assert.DoesNotContain(password, await successiva.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);

        // Il nuovo amministratore accede dall'host del proprio tenant ma deve prima cambiare la password.
        var nuovo = scenario.SessioneA();
        using var accesso = await nuovo.AccediAsync("nuova@esempio.it", password);
        Assert.True(SessioneProva.EReindirizzamento(accesso));
        using var cruscotto = await nuovo.GetAsync("/admin");
        Assert.Equal("/admin/password", SessioneProva.Destinazione(cruscotto));
        using var areaPlatform = await nuovo.GetAsync(Elenco);
        Assert.NotEqual(HttpStatusCode.OK, areaPlatform.StatusCode);
    }

    [Fact]
    public async Task LaSchermataTestiEPerIlSoloPlatformAdminEPermetteDiSalvare()
    {
        await using var scenario = await ScenarioWeb.CreaAsync();
        await scenario.CreaTenantAdminAsync("anna@esempio.it");
        await scenario.CreaPlatformAdminAsync("platform@esempio.it");
        var tenantAdmin = scenario.SessioneA();
        await tenantAdmin.AccediAsync("anna@esempio.it", DatiProva.PasswordProva);
        var platform = scenario.SessioneB();
        await platform.AccediAsync("platform@esempio.it", DatiProva.PasswordProva);

        using var negata = await tenantAdmin.GetAsync("/admin/platform/testi");
        using var negataInScrittura = await tenantAdmin.InviaModuloAsync("/admin", [new("idTesto", Guid.NewGuid().ToString()), new("testo", "x")], "/admin/platform/testi?handler=Modifica");
        using var elenco = await platform.GetAsync("/admin/platform/testi?Lingua=en&DaTradurre=true");

        Assert.Equal(HttpStatusCode.Forbidden, negata.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, negataInScrittura.StatusCode);
        Assert.Equal(HttpStatusCode.OK, elenco.StatusCode);
    }

    [Fact]
    public async Task IlPlatformAdminGestisceLeLingueDelSitoEUnTenantAdminNo()
    {
        await using var scenario = await ScenarioWeb.CreaAsync();
        await scenario.CreaPlatformAdminAsync("platform@esempio.it");
        await scenario.CreaTenantAdminAsync("anna@esempio.it");
        var platform = scenario.SessioneB();
        await platform.AccediAsync("platform@esempio.it", DatiProva.PasswordProva);
        var anna = scenario.SessioneA();
        await anna.AccediAsync("anna@esempio.it", DatiProva.PasswordProva);
        var dettaglio = $"{Elenco}/{scenario.B.IdTenant}";

        // Il tenant B ha già it (predefinita) ed en: si disabilita en e lo si riabilita dal modulo.
        using var disabilita = await platform.InviaModuloAsync(dettaglio, [new("idSito", scenario.B.IdSito.ToString()), new("lingua", "en")], $"{dettaglio}?handler=DisabilitaLingua");
        Assert.True(SessioneProva.EReindirizzamento(disabilita));
        var senza = await (await platform.GetAsync(dettaglio)).Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.DoesNotContain("(en)</span>", senza, StringComparison.Ordinal);
        Assert.Contains("<option value=\"en\">", senza, StringComparison.Ordinal);

        using var abilita = await platform.InviaModuloAsync(dettaglio, [new("idSito", scenario.B.IdSito.ToString()), new("lingua", "en")], $"{dettaglio}?handler=AbilitaLingua");
        var con = await (await platform.GetAsync(dettaglio)).Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("Lingua abilitata", con, StringComparison.Ordinal);
        Assert.Contains("(en)", con, StringComparison.Ordinal);

        foreach (var handler in new[] { "AbilitaLingua", "DisabilitaLingua", "LinguaPredefinita" })
        {
            using var negata = await anna.InviaModuloAsync("/admin", [new("idSito", scenario.A.IdSito.ToString()), new("lingua", "en")], $"{Elenco}/{scenario.A.IdTenant}?handler={handler}");
            Assert.Equal(HttpStatusCode.Forbidden, negata.StatusCode);
        }
    }

    [GeneratedRegex("id=\"password-temporanea\".*?<code[^>]*>([^<]+)</code>", RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex PasswordMostrata();
}
