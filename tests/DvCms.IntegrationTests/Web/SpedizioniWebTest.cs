using System.Net;
using DvCms.Contracts.Piattaforma;
using DvCms.Infrastructure.Persistenza;
using DvCms.IntegrationTests.Supporto;
using DvCms.Models.Accessi;
using DvCms.Models.Negozio;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DvCms.IntegrationTests.Web;

/// <summary>Amministrazione dei metodi di spedizione (F-002, ADR-0020) dal modulo web: accesso, validazione, codifica e isolamento.</summary>
public class SpedizioniWebTest
{
    private const string Percorso = "/admin/negozio/spedizioni";
    private const string PercorsoSalva = Percorso + "?handler=Salva";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static List<KeyValuePair<string, string>> Zona(string nome = "Corriere Italia", string tariffa = "6,90", string area = "Italia", string? id = null, bool attivo = true)
    {
        var campi = new List<KeyValuePair<string, string>> { new("Tipo", "Zona"), new("Area", area), new("Nome", nome), new("Tariffa", tariffa), new("Attivo", "true") };
        if (!attivo)
        {
            campi.RemoveAt(campi.Count - 1);
            campi.Add(new("Attivo", "false"));
        }

        if (id is not null)
        {
            campi.Add(new("Id", id));
        }

        return campi;
    }

    [Fact]
    public async Task SenzaNegozioAttivoLaPaginaE404EIlMenuNonLaMostra()
    {
        await using var c = await Contesto.CreaAsync();
        var anna = await c.AccediAsync(c.Web.A, "anna@esempio.it");

        using var pagina = await anna.GetAsync(Percorso);
        using var cruscotto = await anna.GetAsync("/admin");

        Assert.Equal(HttpStatusCode.NotFound, pagina.StatusCode);
        Assert.DoesNotContain(Percorso, await cruscotto.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConIlNegozioAttivoMenuEPaginaCompaionoESiCreaUnMetodoConLaVirgola()
    {
        await using var c = await Contesto.CreaAsync();
        await c.AttivaCommercioAsync(c.Web.A);
        var anna = await c.AccediAsync(c.Web.A, "anna@esempio.it");

        using var cruscotto = await anna.GetAsync("/admin");
        using var risposta = await anna.InviaModuloAsync(Percorso, Zona(nome: "Corriere <b>24h</b>"), PercorsoSalva);
        using var elenco = await anna.GetAsync(Percorso);

        Assert.Contains(Percorso, await cruscotto.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        Assert.True(SessioneProva.EReindirizzamento(risposta), await risposta.Content.ReadAsStringAsync(Ct));
        var metodo = await c.UnicoMetodoAsync();
        Assert.Equal((TipoMetodoSpedizione.Zona, AreaSpedizione.Italia, 6.90m), (metodo.Tipo, metodo.Area!.Value, metodo.TariffaLorda));
        var html = await elenco.Content.ReadAsStringAsync(Ct);
        Assert.Contains("Corriere &lt;b&gt;24h&lt;/b&gt;", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<b>24h</b>", html, StringComparison.Ordinal);
        Assert.Contains("value=\"6,90\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task IlRitiroScartaAreaETariffaInviateDalModulo()
    {
        await using var c = await Contesto.CreaAsync();
        await c.AttivaCommercioAsync(c.Web.A);
        var anna = await c.AccediAsync(c.Web.A, "anna@esempio.it");
        var campi = new List<KeyValuePair<string, string>> { new("Tipo", "Ritiro"), new("Area", "UnioneEuropea"), new("Nome", "Ritiro in negozio"), new("Tariffa", "9,99"), new("Attivo", "true") };

        using var risposta = await anna.InviaModuloAsync(Percorso, campi, PercorsoSalva);

        Assert.True(SessioneProva.EReindirizzamento(risposta), await risposta.Content.ReadAsStringAsync(Ct));
        var metodo = await c.UnicoMetodoAsync();
        Assert.Equal((TipoMetodoSpedizione.Ritiro, (AreaSpedizione?)null, 0m), (metodo.Tipo, metodo.Area, metodo.TariffaLorda));
    }

    [Theory]
    [InlineData("dieci", "Corriere")]
    [InlineData("-5", "Corriere")]
    [InlineData("5,005", "Corriere")]
    [InlineData("5", "")]
    public async Task UnDatoNonValidoNonSalvaENonPerdeIlTestoScritto(string tariffa, string nome)
    {
        await using var c = await Contesto.CreaAsync();
        await c.AttivaCommercioAsync(c.Web.A);
        var anna = await c.AccediAsync(c.Web.A, "anna@esempio.it");

        using var risposta = await anna.InviaModuloAsync(Percorso, Zona(nome: nome, tariffa: tariffa), PercorsoSalva);

        Assert.Equal(HttpStatusCode.OK, risposta.StatusCode);
        var html = await risposta.Content.ReadAsStringAsync(Ct);
        Assert.Contains("role=\"alert\"", html, StringComparison.Ordinal);
        Assert.Contains($"value=\"{tariffa}\"", html, StringComparison.Ordinal);
        Assert.Equal(0, await c.ContaMetodiAsync());
    }

    [Fact]
    public async Task SiModificaEdElimina()
    {
        await using var c = await Contesto.CreaAsync();
        await c.AttivaCommercioAsync(c.Web.A);
        var anna = await c.AccediAsync(c.Web.A, "anna@esempio.it");
        using var creato = await anna.InviaModuloAsync(Percorso, Zona(), PercorsoSalva);
        var id = (await c.UnicoMetodoAsync()).Id.ToString();

        using var modifica = await anna.InviaModuloAsync(Percorso, Zona(nome: "Corriere 48h", tariffa: "4,50", area: "UnioneEuropea", id: id, attivo: false), PercorsoSalva);
        var modificato = await c.UnicoMetodoAsync();
        using var elimina = await anna.InviaModuloAsync(Percorso, [new("id", id)], $"{Percorso}?handler=Elimina&id={id}");

        Assert.True(SessioneProva.EReindirizzamento(modifica));
        Assert.Equal(("Corriere 48h", 4.50m, AreaSpedizione.UnioneEuropea, false), (modificato.Nome, modificato.TariffaLorda, modificato.Area!.Value, modificato.Attivo));
        Assert.True(SessioneProva.EReindirizzamento(elimina));
        Assert.Equal(0, await c.ContaMetodiAsync());
    }

    [Fact]
    public async Task UnMetodoDiUnAltroTenantNonSiVedeNeSiModificaNeSiElimina()
    {
        await using var c = await Contesto.CreaAsync();
        await c.AttivaCommercioAsync(c.Web.A);
        await c.AttivaCommercioAsync(c.Web.B);
        var anna = await c.AccediAsync(c.Web.A, "anna@esempio.it");
        var bruno = await c.AccediAsync(c.Web.B, "bruno@esempio.it");
        using var creato = await bruno.InviaModuloAsync(Percorso, Zona(nome: "Segreto di B"), PercorsoSalva);
        var idDiB = (await c.UnicoMetodoAsync()).Id.ToString();

        using var elencoA = await anna.GetAsync(Percorso);
        using var modificaDaA = await anna.InviaModuloAsync(Percorso, Zona(nome: "Rubato", id: idDiB), PercorsoSalva);
        using var eliminaDaA = await anna.InviaModuloAsync(Percorso, [new("id", idDiB)], $"{Percorso}?handler=Elimina&id={idDiB}");

        Assert.DoesNotContain("Segreto di B", await elencoA.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, modificaDaA.StatusCode);
        Assert.True(SessioneProva.EReindirizzamento(eliminaDaA));
        var metodo = await c.UnicoMetodoAsync();
        Assert.Equal(("Segreto di B", c.Web.B.IdTenant), (metodo.Nome, metodo.IdTenant));
    }

    [Fact]
    public async Task SenzaSessioneOSenzaTokenAntiforgeryNonSiFaNulla()
    {
        await using var c = await Contesto.CreaAsync();
        await c.AttivaCommercioAsync(c.Web.A);
        var anna = await c.AccediAsync(c.Web.A, "anna@esempio.it");
        var anonimo = c.Web.SessioneSu(c.Web.A.Host);

        using var lettura = await anonimo.GetAsync(Percorso);
        using var senzaToken = await anna.InviaModuloAsync(Percorso, Zona(), PercorsoSalva, conToken: false);

        Assert.True(SessioneProva.EReindirizzamento(lettura) || lettura.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden);
        Assert.Equal(HttpStatusCode.BadRequest, senzaToken.StatusCode);
        Assert.Equal(0, await c.ContaMetodiAsync());
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

        public async Task<MetodoSpedizione> UnicoMetodoAsync()
        {
            await using var ambito = Web.Fabbrica.Services.CreateAsyncScope();
            return await ambito.ServiceProvider.GetRequiredService<ContestoDatabase>().MetodiSpedizione.IgnoreQueryFilters().AsNoTracking().SingleAsync(Ct);
        }

        public async Task<int> ContaMetodiAsync()
        {
            await using var ambito = Web.Fabbrica.Services.CreateAsyncScope();
            return await ambito.ServiceProvider.GetRequiredService<ContestoDatabase>().MetodiSpedizione.IgnoreQueryFilters().CountAsync(Ct);
        }

        public ValueTask DisposeAsync() => Web.DisposeAsync();
    }
}
