using System.Net;
using DvCms.Contracts.Piattaforma;
using DvCms.Data;
using DvCms.IntegrationTests.Supporto;
using DvCms.Models.Accessi;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DvCms.IntegrationTests.Web;

/// <summary>Amministrazione del catalogo (F-001, F-006) dal modulo web: accesso, validazione, codifica e isolamento.</summary>
public class NegozioWebTest
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static List<KeyValuePair<string, string>> Campi(string nome = "Maglietta", string sku = "TSHIRT", string prezzo = "19,90", string stato = "Pubblicato", string? idVariante = null)
    {
        var campi = new List<KeyValuePair<string, string>>
        {
            new("Modulo.Stato", stato),
            new("Modulo.Aliquota", "22"),
            new("Modulo.Traduzioni[0].Codice", "it"),
            new("Modulo.Traduzioni[0].Nome", nome),
            new("Modulo.Traduzioni[0].Descrizione", "Cotone"),
            new("Modulo.Varianti[0].Sku", sku),
            new("Modulo.Varianti[0].Prezzo", prezzo),
            new("Modulo.Varianti[0].Attiva", "true"),
            new("Modulo.Varianti[0].Attiva", "false"),
        };
        if (idVariante is not null)
        {
            campi.Add(new("Modulo.Varianti[0].Id", idVariante));
        }

        return campi;
    }

    [Fact]
    public async Task SenzaNegozioAttivoLeSchermateSono404EIlMenuNonMostraIlNegozio()
    {
        await using var c = await Contesto.CreaAsync();
        var anna = await c.AccediAsync(c.Web.A, "anna@esempio.it");

        using var elenco = await anna.GetAsync("/admin/negozio/prodotti");
        using var nuovo = await anna.GetAsync("/admin/negozio/prodotti/nuovo");
        using var cruscotto = await anna.GetAsync("/admin");

        Assert.Equal(HttpStatusCode.NotFound, elenco.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, nuovo.StatusCode);
        Assert.DoesNotContain("/admin/negozio/prodotti", await cruscotto.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task IlPlatformAdminAttivaIlNegozioDalDettaglioDelTenantEIlMenuCompare()
    {
        await using var c = await Contesto.CreaAsync();
        var root = await c.AccediAsync(c.Web.A, "root@esempio.it");
        var anna = await c.AccediAsync(c.Web.A, "anna@esempio.it");

        using var attiva = await root.InviaModuloAsync(
            $"/admin/platform/tenant/{c.Web.A.IdTenant}",
            [new("idSito", c.Web.A.IdSito.ToString()), new("attivo", "true")],
            $"/admin/platform/tenant/{c.Web.A.IdTenant}?handler=Commercio");
        using var cruscotto = await anna.GetAsync("/admin");
        using var comeTenantAdmin = await anna.InviaModuloAsync(
            "/admin",
            [new("idSito", c.Web.A.IdSito.ToString()), new("attivo", "false")],
            $"/admin/platform/tenant/{c.Web.A.IdTenant}?handler=Commercio");

        Assert.True(SessioneProva.EReindirizzamento(attiva));
        Assert.Contains("/admin/negozio/prodotti", await cruscotto.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        Assert.NotEqual(HttpStatusCode.OK, comeTenantAdmin.StatusCode);
        Assert.True(await c.CommercioAttivoAsync(c.Web.A));
    }

    [Fact]
    public async Task SiCreaUnProdottoDalModuloConPrezzoConLaVirgolaELoSiVedeInElencoConTestoCodificato()
    {
        await using var c = await Contesto.CreaAsync();
        await c.AttivaCommercioAsync(c.Web.A);
        var anna = await c.AccediAsync(c.Web.A, "anna@esempio.it");

        using var risposta = await anna.InviaModuloAsync("/admin/negozio/prodotti/nuovo", Campi(nome: "Maglietta <b>rossa</b>"));

        Assert.True(SessioneProva.EReindirizzamento(risposta), await risposta.Content.ReadAsStringAsync(Ct));
        Assert.Matches("^/admin/negozio/prodotti/[0-9a-f-]{36}$", SessioneProva.Destinazione(risposta));
        var variante = await c.UnicaVarianteAsync();
        Assert.Equal((19.90m, "TSHIRT", "EUR"), (variante.PrezzoLordo, variante.Sku, variante.Valuta));

        using var elenco = await anna.GetAsync("/admin/negozio/prodotti");
        var html = await elenco.Content.ReadAsStringAsync(Ct);
        Assert.Contains("Maglietta &lt;b&gt;rossa&lt;/b&gt;", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<b>rossa</b>", html, StringComparison.Ordinal);
        Assert.Contains("19,90 EUR", html, StringComparison.Ordinal);

        using var modifica = await anna.GetAsync(SessioneProva.Destinazione(risposta)!);
        var htmlModifica = await modifica.Content.ReadAsStringAsync(Ct);
        Assert.Contains("value=\"TSHIRT\"", htmlModifica, StringComparison.Ordinal);
        Assert.Contains("value=\"19.90\"", htmlModifica, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnPrezzoNonNumericoOUnDatoNonValidoNonSalvaENonPerdeIlTestoScritto()
    {
        await using var c = await Contesto.CreaAsync();
        await c.AttivaCommercioAsync(c.Web.A);
        var anna = await c.AccediAsync(c.Web.A, "anna@esempio.it");

        using var prezzo = await anna.InviaModuloAsync("/admin/negozio/prodotti/nuovo", Campi(nome: "Da non perdere", prezzo: "dieci"));
        using var codice = await anna.InviaModuloAsync("/admin/negozio/prodotti/nuovo", Campi(nome: "Da non perdere", sku: "con spazi"));

        foreach (var risposta in new[] { prezzo, codice })
        {
            Assert.Equal(HttpStatusCode.OK, risposta.StatusCode);
            var html = await risposta.Content.ReadAsStringAsync(Ct);
            Assert.Contains("role=\"alert\"", html, StringComparison.Ordinal);
            Assert.Contains("Da non perdere", html, StringComparison.Ordinal);
        }

        Assert.Contains("Prezzo", await prezzo.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        Assert.Equal(0, await c.ContaProdottiAsync());
    }

    [Fact]
    public async Task UnProdottoDiUnAltroTenantNonSiVedeNeSiModifica()
    {
        await using var c = await Contesto.CreaAsync();
        await c.AttivaCommercioAsync(c.Web.A);
        await c.AttivaCommercioAsync(c.Web.B);
        var anna = await c.AccediAsync(c.Web.A, "anna@esempio.it");
        var bruno = await c.AccediAsync(c.Web.B, "bruno@esempio.it");
        using var creato = await bruno.InviaModuloAsync("/admin/negozio/prodotti/nuovo", Campi(nome: "Segreto di B", sku: "SEGRETO-B"));
        var percorsoB = SessioneProva.Destinazione(creato)!;

        using var elencoA = await anna.GetAsync("/admin/negozio/prodotti");
        using var dettaglioDaA = await anna.GetAsync(percorsoB);
        using var modificaDaA = await anna.InviaModuloAsync("/admin/negozio/prodotti/nuovo", Campi(nome: "Rubato"), percorsoB);

        Assert.DoesNotContain("Segreto di B", await elencoA.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NotFound, dettaglioDaA.StatusCode);
        Assert.Equal(HttpStatusCode.OK, modificaDaA.StatusCode);
        Assert.Contains("role=\"alert\"", await modificaDaA.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        Assert.Equal("Segreto di B", await c.NomeProdottoAsync(c.Web.B));
    }

    private sealed class Contesto : IAsyncDisposable
    {
        private Contesto(ScenarioWeb web) => Web = web;

        public ScenarioWeb Web { get; }

        public static async Task<Contesto> CreaAsync()
        {
            var web = await ScenarioWeb.CreaAsync();
            await web.CreaTenantAdminAsync("anna@esempio.it");
            await DatiProva.CreaUtenzaAsync(web.Fabbrica.Services, RuoloUtenza.TenantAdmin, web.B.IdTenant, "bruno@esempio.it", annullamento: Ct);
            await web.CreaPlatformAdminAsync("root@esempio.it");
            return new Contesto(web);
        }

        public async Task<SessioneProva> AccediAsync(DatiProva.TenantCreato tenant, string email)
        {
            var sessione = Web.SessioneSu(tenant.Host);
            await sessione.AccediAsync(email, DatiProva.PasswordProva);
            return sessione;
        }

        public async Task AttivaCommercioAsync(DatiProva.TenantCreato tenant)
        {
            await using var ambito = Web.Fabbrica.Services.CreateAsyncScope();
            var db = ambito.ServiceProvider.GetRequiredService<ContestoDatabase>();
            var admin = await db.Utenze.IgnoreQueryFilters().Where(u => u.Email == "root@esempio.it").Select(u => u.Id).SingleAsync(Ct);
            var esito = await ambito.ServiceProvider.GetRequiredService<IServizioPiattaforma>().ImpostaCommercioAsync(admin, tenant.IdTenant, tenant.IdSito, true, Ct);
            Assert.True(esito.Riuscito, esito.Codice.ToString());
        }

        public async Task<bool> CommercioAttivoAsync(DatiProva.TenantCreato tenant)
        {
            await using var ambito = Web.Fabbrica.Services.CreateAsyncScope();
            return await ambito.ServiceProvider.GetRequiredService<ContestoDatabase>().Siti.IgnoreQueryFilters().Where(s => s.Id == tenant.IdSito).Select(s => s.CommercioAttivo).SingleAsync(Ct);
        }

        public async Task<DvCms.Models.Negozio.VarianteProdotto> UnicaVarianteAsync()
        {
            await using var ambito = Web.Fabbrica.Services.CreateAsyncScope();
            return await ambito.ServiceProvider.GetRequiredService<ContestoDatabase>().VariantiProdotto.IgnoreQueryFilters().AsNoTracking().SingleAsync(Ct);
        }

        public async Task<int> ContaProdottiAsync()
        {
            await using var ambito = Web.Fabbrica.Services.CreateAsyncScope();
            return await ambito.ServiceProvider.GetRequiredService<ContestoDatabase>().Prodotti.IgnoreQueryFilters().CountAsync(Ct);
        }

        public async Task<string?> NomeProdottoAsync(DatiProva.TenantCreato tenant)
        {
            await using var ambito = Web.Fabbrica.Services.CreateAsyncScope();
            return await ambito.ServiceProvider.GetRequiredService<ContestoDatabase>().TraduzioniProdotto.IgnoreQueryFilters().Where(t => t.IdTenant == tenant.IdTenant).Select(t => t.Nome).SingleOrDefaultAsync(Ct);
        }

        public ValueTask DisposeAsync() => Web.DisposeAsync();
    }
}
