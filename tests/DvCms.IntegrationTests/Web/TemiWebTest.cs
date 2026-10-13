using System.Net;
using DvCms.Contracts.Cms;
using DvCms.Contracts.Temi;
using DvCms.Core.Tenancy;
using DvCms.DTO.Cms;
using DvCms.DTO.Piattaforma;
using DvCms.DTO.Temi;
using DvCms.DTO.Tenancy;
using DvCms.Infrastructure.Persistenza;
using DvCms.IntegrationTests.Supporto;
using DvCms.Models.Accessi;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DvCms.IntegrationTests.Web;

/// <summary>Registro dei temi e due temi di prova (D-001, D-002): stesso contenuto, aspetto diverso, nessun percorso dal tenant.</summary>
public class TemiWebTest
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task SenzaConfigurazioneSiUsaIlTemaClassicoConIlNomeDelSito()
    {
        await using var c = await Contesto.CreaAsync();
        await c.CreaPaginaPubblicataAsync(c.Web.A, "chi-siamo", "Chi siamo", "Testo uno.");

        var html = await (await c.Web.SessioneA().GetAsync("/chi-siamo")).Content.ReadAsStringAsync(Ct);

        Assert.Contains("<body class=\"tema-classico\"", html, StringComparison.Ordinal);
        Assert.Contains("/css/temi/classico-v1.css?v=", html, StringComparison.Ordinal);
        Assert.Contains("--tema-colore-primario:#1f4e8c;", html, StringComparison.Ordinal);
        Assert.Contains("class=\"tema-nome-sito\"", html, StringComparison.Ordinal);
        Assert.Contains("<h1>Chi siamo</h1>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("tema-moderno", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LaStessaPaginaConDueTemiHaLoStessoContenutoEAspettoDiverso()
    {
        await using var c = await Contesto.CreaAsync();
        await c.CreaPaginaPubblicataAsync(c.Web.A, "chi-siamo", "Chi siamo", "Primo paragrafo.\n\nSecondo.");
        var classico = await (await c.Web.SessioneA().GetAsync("/chi-siamo")).Content.ReadAsStringAsync(Ct);

        var esito = await c.SalvaTemaAsync(c.Web.A, "moderno", new Dictionary<string, string> { ["ColorePrimario"] = "#AA00BB", ["Larghezza"] = "Larga", ["TestoPiePagina"] = "Piè <b>di pagina</b>" });
        var moderno = await (await c.Web.SessioneA().GetAsync("/chi-siamo")).Content.ReadAsStringAsync(Ct);

        Assert.True(esito.Riuscito, esito.Codice.ToString());
        Assert.Contains("<body class=\"tema-moderno\"", moderno, StringComparison.Ordinal);
        Assert.Contains("/css/temi/moderno-v1.css?v=", moderno, StringComparison.Ordinal);
        Assert.Contains("--tema-colore-primario:#aa00bb;", moderno, StringComparison.Ordinal);

        // Le pagine libere non usano i componenti del builder: nessuna skin (che vale solo per i documenti del builder).
        Assert.DoesNotContain("-componenti.css", moderno, StringComparison.Ordinal);
        Assert.Contains("--tema-colore-intestazione:#111827;", moderno, StringComparison.Ordinal);
        Assert.Contains("<main class=\"larga\">", moderno, StringComparison.Ordinal);
        Assert.Contains("Pi&#xE8; &lt;b&gt;di pagina&lt;/b&gt;", moderno, StringComparison.Ordinal);
        Assert.DoesNotContain("<b>di pagina</b>", moderno, StringComparison.Ordinal);
        Assert.DoesNotContain("classico", moderno, StringComparison.Ordinal);
        Assert.NotEqual(classico, moderno);

        // Il contenuto è lo stesso e vive una volta sola: cambia solo la presentazione.
        foreach (var html in new[] { classico, moderno })
        {
            Assert.Contains("<h1>Chi siamo</h1>", html, StringComparison.Ordinal);
            Assert.Contains("Primo paragrafo.", html, StringComparison.Ordinal);
            Assert.Contains("Secondo.", html, StringComparison.Ordinal);
        }

        Assert.Equal(1, await c.ContaAsync(contesto => contesto.RevisioniPagina.IgnoreQueryFilters().CountAsync(Ct)));
    }

    [Fact]
    public async Task IlTemaDiUnSitoNonCambiaQuelloDegliAltri()
    {
        await using var c = await Contesto.CreaAsync();
        await c.CreaPaginaPubblicataAsync(c.Web.A, "pagina", "Pagina", "x");
        await c.CreaPaginaPubblicataAsync(c.Web.B, "pagina", "Pagina", "x");

        await c.SalvaTemaAsync(c.Web.A, "moderno", new Dictionary<string, string>());

        Assert.Contains("tema-moderno", await (await c.Web.SessioneA().GetAsync("/pagina")).Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        Assert.Contains("tema-classico", await (await c.Web.SessioneB().GetAsync("/pagina")).Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("moderno", "ColorePrimario", "#fff")]
    [InlineData("moderno", "ColorePrimario", "#1f4e8c;background:url(//evil)")]
    [InlineData("moderno", "Larghezza", "Enorme")]
    [InlineData("classico", "ColoreIntestazione", "#000000")]
    [InlineData("classico", "TestoPiePagina", "a\nb")]
    public async Task ImpostazioniNonValideSonoRifiutateENonSalvate(string tema, string campo, string valore)
    {
        await using var c = await Contesto.CreaAsync();

        var esito = await c.SalvaTemaAsync(c.Web.A, tema, new Dictionary<string, string> { [campo] = valore });

        Assert.Equal(CodiceEsito.DatiNonValidi, esito.Codice);
        Assert.Equal([campo], esito.CampiNonValidi);
        Assert.Equal(0, await c.ContaAsync(contesto => contesto.ImpostazioniTemaSito.IgnoreQueryFilters().CountAsync(Ct)));
    }

    [Theory]
    [InlineData("../../Pages/Admin/Index")]
    [InlineData("Temi/_moderno_v1")]
    [InlineData("MODERNO")]
    [InlineData("")]
    [InlineData(null)]
    public async Task SoloLeChiaviDelRegistroSonoAmmesse(string? chiave)
    {
        await using var c = await Contesto.CreaAsync();

        var esito = await c.SalvaTemaAsync(c.Web.A, chiave, new Dictionary<string, string>());

        Assert.Equal(CodiceEsito.DatiNonValidi, esito.Codice);
        Assert.Equal(["Tema"], esito.CampiNonValidi);
    }

    [Fact]
    public async Task UnTemaSalvatoCheNonEPiuNelRegistroRipiegaSulPredefinitoSenzaRompereIlSito()
    {
        await using var c = await Contesto.CreaAsync();
        await c.CreaPaginaPubblicataAsync(c.Web.A, "pagina", "Pagina", "x");
        await c.SalvaTemaAsync(c.Web.A, "moderno", new Dictionary<string, string>());
        await c.ScriviDirettamenteAsync(c.Web.A.IdSito, "tema-rimosso", 1, "{\"ColorePrimario\":\"#123456\"}");

        using var risposta = await c.Web.SessioneA().GetAsync("/pagina");
        var html = await risposta.Content.ReadAsStringAsync(Ct);

        Assert.Equal(HttpStatusCode.OK, risposta.StatusCode);
        Assert.Contains("tema-classico", html, StringComparison.Ordinal);
        Assert.DoesNotContain("123456", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnValoreSalvatoNonPiuValidoSiIgnoraSenzaEmetterloNelloStile()
    {
        await using var c = await Contesto.CreaAsync();
        await c.CreaPaginaPubblicataAsync(c.Web.A, "pagina", "Pagina", "x");
        await c.ScriviDirettamenteAsync(c.Web.A.IdSito, "moderno", 1, "{\"ColorePrimario\":\"red;background:url(x)\",\"Larghezza\":\"Larga\"}");

        var html = await (await c.Web.SessioneA().GetAsync("/pagina")).Content.ReadAsStringAsync(Ct);

        Assert.DoesNotContain("url(x)", html, StringComparison.Ordinal);
        Assert.Contains("--tema-colore-primario:#0f766e;", html, StringComparison.Ordinal);
        Assert.Contains("<main class=\"larga\">", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RestandoSulloStessoTemaLaVersioneConfiguratanonCambiaDaSola()
    {
        await using var c = await Contesto.CreaAsync();
        await c.ScriviDirettamenteAsync(c.Web.A.IdSito, "moderno", 1, "{}");
        var letta = await c.LeggiTemaAsync(c.Web.A);

        var esito = await c.SalvaTemaAsync(c.Web.A, "moderno", new Dictionary<string, string> { ["TestoPiePagina"] = "Ciao" }, letta.TokenConcorrenza);

        Assert.True(esito.Riuscito, esito.Codice.ToString());
        var dopo = await c.LeggiTemaAsync(c.Web.A);
        Assert.Equal(1, dopo.Versione);
        Assert.Equal("Ciao", dopo.Valori["TestoPiePagina"]);
    }

    [Fact]
    public async Task ConUnTokenVecchioLaModificaVieneRespinta()
    {
        await using var c = await Contesto.CreaAsync();
        await c.SalvaTemaAsync(c.Web.A, "moderno", new Dictionary<string, string>());
        var vecchio = (await c.LeggiTemaAsync(c.Web.A)).TokenConcorrenza;
        await c.SalvaTemaAsync(c.Web.A, "moderno", new Dictionary<string, string> { ["TestoPiePagina"] = "Seconda" }, vecchio);

        var conflitto = await c.SalvaTemaAsync(c.Web.A, "moderno", new Dictionary<string, string> { ["TestoPiePagina"] = "Terza" }, vecchio);
        var senzaToken = await c.SalvaTemaAsync(c.Web.A, "moderno", new Dictionary<string, string> { ["TestoPiePagina"] = "Quarta" }, null);

        Assert.Equal(CodiceEsito.Conflitto, conflitto.Codice);
        Assert.Equal(CodiceEsito.Conflitto, senzaToken.Codice);
        Assert.Equal("Seconda", (await c.LeggiTemaAsync(c.Web.A)).Valori["TestoPiePagina"]);
    }

    [Fact]
    public async Task SoloUnAmministratoreDelTenantPuoLeggereESalvareIlTema()
    {
        await using var c = await Contesto.CreaAsync();
        await using var ambito = c.Web.Fabbrica.Services.CreateAsyncScope();
        ambito.ServiceProvider.GetRequiredService<ContestoTenant>().Imposta(new DatiTenantRisolto(c.Web.A.IdTenant, c.Web.A.IdSito, c.Web.A.IdDominio, c.Web.A.Host, false));
        var servizio = ambito.ServiceProvider.GetRequiredService<IServizioTemi>();

        var salva = await servizio.SalvaAsync(c.AdminB, "moderno", new Dictionary<string, string>(), null, Ct);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => servizio.LeggiAsync(c.AdminB, Ct));

        Assert.Equal(CodiceEsito.NonAutorizzato, salva.Codice);
        Assert.Equal(0, await c.ContaAsync(contesto => contesto.ImpostazioniTemaSito.IgnoreQueryFilters().CountAsync(Ct)));
    }

    [Fact]
    public async Task LaPaginaAdminSalvaIlTemaELoMostraNellAnteprima()
    {
        await using var c = await Contesto.CreaAsync();
        var sessione = c.Web.SessioneA();
        await sessione.AccediAsync("anna@esempio.it", DatiProva.PasswordProva);
        var id = await c.CreaPaginaAsync(c.Web.A, "bozza", "Bozza", "x");

        using var modulo = await sessione.GetAsync("/admin/tema?tema=moderno");
        var html = await modulo.Content.ReadAsStringAsync(Ct);
        Assert.Contains("name=\"t_ColoreIntestazione\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("name=\"t_TestoPiePagina\" value=\"x\"", html, StringComparison.Ordinal);

        using var salva = await sessione.InviaModuloAsync(
            "/admin/tema?tema=moderno",
            [new("t_ColorePrimario", "#aa00bb"), new("t_ColoreIntestazione", "#111827"), new("t_Larghezza", "Stretta"), new("t_TestoPiePagina", "")],
            "/admin/tema?tema=moderno");
        Assert.True(SessioneProva.EReindirizzamento(salva), await salva.Content.ReadAsStringAsync(Ct));
        using var dopo = await sessione.GetAsync("/admin/tema");
        Assert.Contains("value=\"#aa00bb\"", await dopo.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);

        using var anteprima = await sessione.GetAsync($"/admin/pagine/{id}/anteprima");
        Assert.Contains("tema-moderno", await anteprima.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);

        using var invalido = await sessione.InviaModuloAsync("/admin/tema?tema=moderno", [new("t_ColorePrimario", "rosso")], "/admin/tema?tema=moderno");
        Assert.Equal(HttpStatusCode.OK, invalido.StatusCode);
        Assert.Contains("is-invalid", await invalido.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task SenzaSessioneLaPaginaDelTemaRimandaAllAccessoEUnTenantAdminNonVedeIlTemaDiAltri()
    {
        await using var c = await Contesto.CreaAsync();

        using var anonimo = await c.Web.SessioneA().GetAsync("/admin/tema");

        Assert.True(SessioneProva.EReindirizzamento(anonimo));
        Assert.StartsWith("/admin/accesso", SessioneProva.Destinazione(anonimo), StringComparison.Ordinal);
    }

    private sealed class Contesto : IAsyncDisposable
    {
        private Contesto(ScenarioWeb web, Guid adminA, Guid adminB)
        {
            Web = web;
            AdminA = adminA;
            AdminB = adminB;
        }

        public ScenarioWeb Web { get; }

        public Guid AdminA { get; }

        public Guid AdminB { get; }

        public static async Task<Contesto> CreaAsync()
        {
            var web = await ScenarioWeb.CreaAsync();
            var adminA = await web.CreaTenantAdminAsync("anna@esempio.it");
            var adminB = await DatiProva.CreaUtenzaAsync(web.Fabbrica.Services, RuoloUtenza.TenantAdmin, web.B.IdTenant, "bruno@esempio.it", annullamento: Ct);
            return new Contesto(web, adminA, adminB);
        }

        public Task<EsitoOperazione> SalvaTemaAsync(DatiProva.TenantCreato tenant, string? chiave, Dictionary<string, string> valori, string? token = "") =>
            ComeAsync(tenant, async (temi, attore) =>
            {
                var tokenEffettivo = token == string.Empty ? (await temi.LeggiAsync(attore, Ct)).TokenConcorrenza : token;
                return await temi.SalvaAsync(attore, chiave, valori, tokenEffettivo, Ct);
            });

        public Task<ConfigurazioneTema> LeggiTemaAsync(DatiProva.TenantCreato tenant) =>
            ComeAsync(tenant, (temi, attore) => temi.LeggiAsync(attore, Ct));

        public async Task<Guid> CreaPaginaAsync(DatiProva.TenantCreato tenant, string slug, string titolo, string contenuto)
        {
            await using var ambito = Web.Fabbrica.Services.CreateAsyncScope();
            ambito.ServiceProvider.GetRequiredService<ContestoTenant>().Imposta(new DatiTenantRisolto(tenant.IdTenant, tenant.IdSito, tenant.IdDominio, tenant.Host, false));
            var attore = tenant.IdTenant == Web.A.IdTenant ? AdminA : AdminB;
            var esito = await ambito.ServiceProvider.GetRequiredService<IServizioPagine>().CreaAsync(attore, new DatiNuovaPagina(slug, titolo, contenuto, "it", false), Ct);
            Assert.True(esito.Riuscito, esito.Codice.ToString());
            return esito.Id!.Value;
        }

        public async Task CreaPaginaPubblicataAsync(DatiProva.TenantCreato tenant, string slug, string titolo, string contenuto)
        {
            var id = await CreaPaginaAsync(tenant, slug, titolo, contenuto);
            await using var ambito = Web.Fabbrica.Services.CreateAsyncScope();
            ambito.ServiceProvider.GetRequiredService<ContestoTenant>().Imposta(new DatiTenantRisolto(tenant.IdTenant, tenant.IdSito, tenant.IdDominio, tenant.Host, false));
            var attore = tenant.IdTenant == Web.A.IdTenant ? AdminA : AdminB;
            var pagine = ambito.ServiceProvider.GetRequiredService<IServizioPagine>();
            var dettaglio = (await pagine.LeggiAsync(attore, id, Ct))!;
            Assert.True((await pagine.PubblicaAsync(attore, id, dettaglio.TokenConcorrenza, Ct)).Riuscito);
        }

        /// <summary>Scrive una riga senza passare dal servizio: simula dati vecchi o alterati (tema tolto dal registro, valori non più validi).</summary>
        public async Task ScriviDirettamenteAsync(Guid idSito, string chiave, int versione, string json)
        {
            await using var ambito = Web.Fabbrica.Services.CreateAsyncScope();
            var contesto = ambito.ServiceProvider.GetRequiredService<ContestoDatabase>();
            var riga = await contesto.ImpostazioniTemaSito.IgnoreQueryFilters().SingleOrDefaultAsync(r => r.IdSito == idSito, Ct);
            if (riga is null)
            {
                var sito = await contesto.Siti.IgnoreQueryFilters().SingleAsync(s => s.Id == idSito, Ct);
                riga = new DvCms.Models.Temi.ImpostazioniTemaSito { Id = Guid.NewGuid(), IdTenant = sito.IdTenant, IdSito = idSito };
                contesto.ImpostazioniTemaSito.Add(riga);
            }

            riga.ChiaveTema = chiave;
            riga.VersioneTema = versione;
            riga.ImpostazioniJson = json;
            riga.AggiornatoIlUtc = DateTime.UtcNow;
            await contesto.SaveChangesAsync(Ct);
        }

        public async Task<T> ContaAsync<T>(Func<ContestoDatabase, Task<T>> lettura)
        {
            await using var ambito = Web.Fabbrica.Services.CreateAsyncScope();
            return await lettura(ambito.ServiceProvider.GetRequiredService<ContestoDatabase>());
        }

        public ValueTask DisposeAsync() => Web.DisposeAsync();

        private async Task<T> ComeAsync<T>(DatiProva.TenantCreato tenant, Func<IServizioTemi, Guid, Task<T>> operazione)
        {
            await using var ambito = Web.Fabbrica.Services.CreateAsyncScope();
            ambito.ServiceProvider.GetRequiredService<ContestoTenant>().Imposta(new DatiTenantRisolto(tenant.IdTenant, tenant.IdSito, tenant.IdDominio, tenant.Host, false));
            var attore = tenant.IdTenant == Web.A.IdTenant ? AdminA : AdminB;
            return await operazione(ambito.ServiceProvider.GetRequiredService<IServizioTemi>(), attore);
        }
    }
}
