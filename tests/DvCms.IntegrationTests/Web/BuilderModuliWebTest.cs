using System.Net;
using System.Security.Cryptography;
using DvCms.Contracts.Moduli;
using DvCms.Core.Tenancy;
using DvCms.DTO.Moduli;
using DvCms.DTO.Tenancy;
using DvCms.IntegrationTests.Supporto;
using DvCms.Models.Moduli;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DvCms.IntegrationTests.Web;

/// <summary>Componente <c>contenuto.modulo</c> del page builder (ADR-0027): scelta del modulo, difese del server e rendering pubblico.</summary>
public partial class BuilderWebTest
{
    private static async Task<Guid> CreaModuloDiContattoAsync(Contesto c, DatiProva.TenantCreato tenant, Guid attore, DatiModulo dati)
    {
        await using var ambito = c.Web.Fabbrica.Services.CreateAsyncScope();
        ambito.ServiceProvider.GetRequiredService<ContestoTenant>().Imposta(new DatiTenantRisolto(tenant.IdTenant, tenant.IdSito, tenant.IdDominio, tenant.Host, false));
        var esito = await ambito.ServiceProvider.GetRequiredService<IServizioModuli>().CreaAsync(attore, dati, Ct);
        Assert.True(esito.Riuscito, string.Join(";", esito.Errori));
        return esito.Id!.Value;
    }

    [Fact]
    public async Task IlModuloDiContattoVuoleUnModuloDelSitoENonArchiviatoELaPaginaMostraSoloQuelloPubblicato()
    {
        await using var c = await Contesto.CreaAsync(new Dictionary<string, string?>
        {
            ["Altcha:Abilitato"] = "true",
            ["Altcha:HmacSignatureSecret"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
            ["Sicurezza:ChiaveSegreti"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
        });
        var (id, percorso) = await c.CreaPaginaAsync("pagina-con-intestazione-fissa", "contatti", slug: "contatti");
        var attivo = await CreaModuloDiContattoAsync(c, c.Web.A, c.AdminA, ModuloPubblicoWebTest.Dati());
        var bozza = await CreaModuloDiContattoAsync(c, c.Web.A, c.AdminA, ModuloPubblicoWebTest.Dati(StatoModulo.Bozza, nome: "In bozza"));
        var archiviato = await CreaModuloDiContattoAsync(c, c.Web.A, c.AdminA, ModuloPubblicoWebTest.Dati(StatoModulo.Archiviato, nome: "Vecchio"));
        var altrui = await CreaModuloDiContattoAsync(c, c.Web.B, await c.IdAdminBAsync(), ModuloPubblicoWebTest.Dati());
        var token = (await c.StatoAsync(id)).GetProperty("token").GetString()!;

        using var inesistente = await c.SalvaAsync(id, token, Progetto(ConCorpo(Iniziale(), Nodo("m", "contenuto.modulo", new() { ["idModulo"] = Guid.NewGuid().ToString("D") }))));
        using var diAltri = await c.SalvaAsync(id, token, Progetto(ConCorpo(Iniziale(), Nodo("m", "contenuto.modulo", new() { ["idModulo"] = altrui.ToString("D") }))));
        using var conArchiviato = await c.SalvaAsync(id, token, Progetto(ConCorpo(Iniziale(), Nodo("m", "contenuto.modulo", new() { ["idModulo"] = archiviato.ToString("D") }))));
        using var senzaId = await c.SalvaAsync(id, token, Progetto(ConCorpo(Iniziale(), Nodo("m", "contenuto.modulo"))));
        using var buono = await c.SalvaAsync(id, token, Progetto(ConCorpo(Iniziale(), Nodo("m", "contenuto.modulo", new() { ["idModulo"] = attivo.ToString("D") }))));
        await c.PubblicaAsync(percorso);
        using var vista = await c.Web.SessioneA().GetAsync("/contatti");
        var html = await vista.Content.ReadAsStringAsync(Ct);
        var nuovoToken = (await c.StatoAsync(id)).GetProperty("token").GetString()!;
        using var conBozza = await c.SalvaAsync(id, nuovoToken, Progetto(ConCorpo(Iniziale(), Nodo("m", "contenuto.modulo", new() { ["idModulo"] = bozza.ToString("D") }))));
        await c.PubblicaAsync(percorso);
        using var vistaBozza = await c.Web.SessioneA().GetAsync("/contatti");

        foreach (var rifiutato in new[] { inesistente, diAltri, conArchiviato })
        {
            Assert.NotEqual(HttpStatusCode.OK, rifiutato.StatusCode);
            Assert.Contains((await Json(rifiutato)).GetProperty("errori").EnumerateArray(), e => e.GetProperty("codice").GetString() == "ModuloNonDisponibile");
        }

        Assert.NotEqual(HttpStatusCode.OK, senzaId.StatusCode);
        Assert.Equal(HttpStatusCode.OK, buono.StatusCode);
        Assert.Contains($"action=\"/moduli/{attivo:D}\"", html, StringComparison.Ordinal);
        Assert.Contains("name=\"campo_c1\"", html, StringComparison.Ordinal);
        Assert.Contains("name=\"lingua\" value=\"it\"", html, StringComparison.Ordinal);
        Assert.Contains("<altcha-widget", html, StringComparison.Ordinal);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(html, "/lib/altcha/altcha.min.js"));
        Assert.Equal(HttpStatusCode.OK, conBozza.StatusCode);
        var senzaForm = await vistaBozza.Content.ReadAsStringAsync(Ct);
        Assert.DoesNotContain("dv-modulo", senzaForm.Replace("dv-modulo-", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.DoesNotContain("campo_c1", senzaForm, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LaPaginaDelBuilderOffreISoliModuliNonArchiviatiDelSito()
    {
        await using var c = await Contesto.CreaAsync();
        var (id, _) = await c.CreaPaginaAsync("pagina-con-intestazione-fissa", "contatti", slug: "contatti");
        var attivo = await CreaModuloDiContattoAsync(c, c.Web.A, c.AdminA, ModuloPubblicoWebTest.Dati());
        await CreaModuloDiContattoAsync(c, c.Web.A, c.AdminA, ModuloPubblicoWebTest.Dati(StatoModulo.Archiviato, nome: "Vecchio"));
        var altrui = await CreaModuloDiContattoAsync(c, c.Web.B, await c.IdAdminBAsync(), ModuloPubblicoWebTest.Dati(nome: "Di Bruno"));

        using var pagina = await c.Sessione.GetAsync($"/admin/pagine/{id}/builder");
        var html = WebUtility.HtmlDecode(await pagina.Content.ReadAsStringAsync(Ct));

        Assert.Equal(HttpStatusCode.OK, pagina.StatusCode);
        Assert.Contains($"\"id\":\"{attivo:D}\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Vecchio", html, StringComparison.Ordinal);
        Assert.DoesNotContain(altrui.ToString("D"), html, StringComparison.Ordinal);
        Assert.DoesNotContain("Di Bruno", html, StringComparison.Ordinal);
    }
}
