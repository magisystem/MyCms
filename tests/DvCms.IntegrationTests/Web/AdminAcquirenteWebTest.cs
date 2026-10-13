using System.Net;
using DvCms.Application.Negozio;
using DvCms.Infrastructure.Persistenza;
using DvCms.IntegrationTests.Negozio;
using DvCms.IntegrationTests.Supporto;
using DvCms.Models.Accessi;
using DvCms.Models.Negozio;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DvCms.IntegrationTests.Web;

/// <summary>Amministratori che comprano (ADR-0011): sessione cliente ottenuta da quella admin, solo capacità di acquirente.</summary>
public class AdminAcquirenteWebTest
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<SessioneProva> AdminAsync(ContestoNegozio c, string email, bool tenantB = false)
    {
        var sessione = c.Web.SessioneSu(tenantB ? c.B.Host : c.A.Host);
        await sessione.AccediAsync(email, DatiProva.PasswordProva);
        return sessione;
    }

    [Fact]
    public async Task UnTenantAdminEntraComeClienteEOrdinaConIlSuoAccountSenzaPoteriDiAmministrazione()
    {
        await using var c = await ContestoNegozio.CreaAsync();
        var varianti = await c.CreaProdottoAsync(c.A, "Libro", 4m, ("LIB-1", 10m, null));
        var ritiro = await c.CreaRitiroAsync(c.A);
        var anna = await AdminAsync(c, "anna@esempio.it");

        using var prima = await anna.GetAsync("/account/carrello");
        using var entra = await anna.InviaModuloAsync("/admin", [], "/admin/negozio/acquista");
        using var carrello = await anna.GetAsync("/account/carrello");
        using var aggiunta = await anna.InviaModuloAsync("/account", [new("idVariante", varianti[0].ToString()), new("quantita", "2")], "/account/carrello?handler=Aggiungi");
        using var cassa = await anna.InviaModuloAsync(
            "/account/checkout",
            [new("Modulo.IdMetodo", ritiro.ToString()), new($"Modulo.Atteso[{ritiro}]", "20.00")]);

        Assert.True(SessioneProva.EReindirizzamento(prima));
        Assert.True(SessioneProva.EReindirizzamento(entra));
        Assert.Equal("/negozio", SessioneProva.Destinazione(entra));
        Assert.NotNull(anna.CookieCliente);
        Assert.Equal(HttpStatusCode.OK, carrello.StatusCode);
        Assert.True(SessioneProva.EReindirizzamento(aggiunta));
        Assert.True(SessioneProva.EReindirizzamento(cassa), WebUtility.HtmlDecode(await cassa.Content.ReadAsStringAsync(Ct)));
        await using var ambito = c.Web.Fabbrica.Services.CreateAsyncScope();
        var ordine = await ambito.ServiceProvider.GetRequiredService<ContestoDatabase>().Ordini.IgnoreQueryFilters().SingleAsync(Ct);
        Assert.Equal(("anna@esempio.it", 20m, c.A.IdTenant), (ordine.EmailAcquirente, ordine.TotaleLordo, ordine.IdTenant));
    }

    [Fact]
    public async Task LaSessioneClienteDiUnAmministratoreNonApreLAmministrazione()
    {
        await using var c = await ContestoNegozio.CreaAsync();
        var anna = await AdminAsync(c, "anna@esempio.it");
        using var entra = await anna.InviaModuloAsync("/admin", [], "/admin/negozio/acquista");
        var soloCliente = c.Web.SessioneSu(c.A.Host);
        soloCliente.ImpostaCookie(".DvCms.Cliente", anna.CookieCliente!);

        using var admin = await soloCliente.GetAsync("/admin");
        using var piattaforma = await soloCliente.GetAsync("/admin/platform/tenant");
        using var account = await soloCliente.GetAsync("/account");

        Assert.StartsWith("/admin/accesso", SessioneProva.Destinazione(admin), StringComparison.Ordinal);
        Assert.NotEqual(HttpStatusCode.OK, piattaforma.StatusCode);
        Assert.Equal(HttpStatusCode.OK, account.StatusCode);
    }

    [Fact]
    public async Task IlPlatformAdminPuoComprareInQualunqueTenantEUnUtenteNonUsaLEndpoint()
    {
        await using var c = await ContestoNegozio.CreaAsync();
        var root = await AdminAsync(c, "root@esempio.it", tenantB: true);
        var cliente = c.Web.SessioneSu(c.A.Host);
        using var accesso = await cliente.InviaModuloAsync("/account/accesso", [new("Email", "cliente@esempio.it"), new("Password", DatiProva.PasswordProva)]);
        Assert.True(SessioneProva.EReindirizzamento(accesso));

        using var entra = await root.InviaModuloAsync("/admin", [], "/admin/negozio/acquista");
        using var carrello = await root.GetAsync("/account/carrello");
        using var daCliente = await cliente.InviaModuloAsync("/account", [], "/admin/negozio/acquista");
        using var anonimo = await c.Web.SessioneSu(c.A.Host).GetAsync("/admin/negozio/acquista");

        Assert.Equal("/negozio", SessioneProva.Destinazione(entra));
        Assert.Equal(HttpStatusCode.OK, carrello.StatusCode);
        Assert.NotEqual(HttpStatusCode.OK, daCliente.StatusCode);
        Assert.True(SessioneProva.EReindirizzamento(anonimo));
        Assert.StartsWith("/admin/accesso", SessioneProva.Destinazione(anonimo), StringComparison.Ordinal);
    }

    [Fact]
    public async Task SenzaNegozioAttivoNonSiEntraEUnAccountDisabilitatoPerdeLaSessioneCliente()
    {
        await using var c = await ContestoNegozio.CreaAsync();
        var anna = await AdminAsync(c, "anna@esempio.it");
        using var entra = await anna.InviaModuloAsync("/admin", [], "/admin/negozio/acquista");
        Assert.Equal(HttpStatusCode.OK, (await anna.GetAsync("/account/carrello")).StatusCode);

        await c.DisabilitaAccountAsync(c.AdminA);
        using var dopo = await anna.GetAsync("/account/carrello");

        Assert.True(SessioneProva.EReindirizzamento(dopo));
        Assert.StartsWith("/account/accesso", SessioneProva.Destinazione(dopo), StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnAdminDiUnAltroTenantNonOttieneUnaSessioneClienteSuQuestoSito()
    {
        await using var c = await ContestoNegozio.CreaAsync();
        var bruno = await AdminAsync(c, "bruno@esempio.it", tenantB: true);
        using var suB = await bruno.InviaModuloAsync("/admin", [], "/admin/negozio/acquista");
        var suA = c.Web.SessioneSu(c.A.Host);
        suA.ImpostaCookie(".DvCms.Cliente", bruno.CookieCliente!);

        using var carrello = await suA.GetAsync("/account/carrello");

        Assert.Equal("/negozio", SessioneProva.Destinazione(suB));
        Assert.True(SessioneProva.EReindirizzamento(carrello));
        Assert.StartsWith("/account/accesso", SessioneProva.Destinazione(carrello), StringComparison.Ordinal);
    }
}
