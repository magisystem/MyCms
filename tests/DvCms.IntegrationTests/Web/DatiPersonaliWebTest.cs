using System.Net;
using DvCms.Application.Cms;
using DvCms.Application.Tenancy;
using DvCms.Infrastructure.Cache;
using DvCms.IntegrationTests.Negozio;
using DvCms.IntegrationTests.Supporto;
using DvCms.Models.Accessi;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DvCms.IntegrationTests.Web;

/// <summary>Pagina «I miei dati» dell'area cliente (A7): esportazione e cancellazione dell'account.</summary>
public class DatiPersonaliWebTest
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<SessioneProva> ClienteAsync(ContestoNegozio c)
    {
        var sessione = c.Web.SessioneSu(c.A.Host);
        using var accesso = await sessione.InviaModuloAsync("/account/accesso", [new("Email", "cliente@esempio.it"), new("Password", DatiProva.PasswordProva)]);
        Assert.True(SessioneProva.EReindirizzamento(accesso));
        return sessione;
    }

    [Fact]
    public async Task LaPaginaDatiSiVedeSoloDaAutenticatiEScaricaUnJsonConLaMiaEmail()
    {
        await using var c = await ContestoNegozio.CreaAsync();
        var cliente = await ClienteAsync(c);

        using var pagina = await cliente.GetAsync("/account/dati");
        using var file = await cliente.GetAsync("/account/dati?handler=Esporta");
        using var anonimo = await c.Web.SessioneSu(c.A.Host).GetAsync("/account/dati");

        Assert.Equal(HttpStatusCode.OK, pagina.StatusCode);
        Assert.Equal(HttpStatusCode.OK, file.StatusCode);
        Assert.Equal("application/json", file.Content.Headers.ContentType?.MediaType);
        Assert.Contains("cliente@esempio.it", await file.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        Assert.True(SessioneProva.EReindirizzamento(anonimo));
    }

    [Fact]
    public async Task EliminareLAccountVuolePasswordEConfermaEChiudeLaSessione()
    {
        await using var c = await ContestoNegozio.CreaAsync();
        var cliente = await ClienteAsync(c);

        using var sbagliata = await cliente.InviaModuloAsync("/account/dati", [new("Password", "no"), new("Confermo", "true")], "/account/dati?handler=Elimina");
        using var senzaConferma = await cliente.InviaModuloAsync("/account/dati", [new("Password", DatiProva.PasswordProva)], "/account/dati?handler=Elimina");
        Assert.Equal(StatoUtenza.Attiva, (await c.UtenzaAsync(c.Cliente)).Stato);
        using var giusta = await cliente.InviaModuloAsync("/account/dati", [new("Password", DatiProva.PasswordProva), new("Confermo", "true")], "/account/dati?handler=Elimina");
        using var dopo = await cliente.GetAsync("/account/carrello");
        using var riaccesso = await c.Web.SessioneSu(c.A.Host).InviaModuloAsync("/account/accesso", [new("Email", "cliente@esempio.it"), new("Password", DatiProva.PasswordProva)]);

        Assert.Contains("La password non è corretta.", WebUtility.HtmlDecode(await sbagliata.Content.ReadAsStringAsync(Ct)), StringComparison.Ordinal);
        Assert.Contains("Spunta la casella", WebUtility.HtmlDecode(await senzaConferma.Content.ReadAsStringAsync(Ct)), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, giusta.StatusCode);
        Assert.Contains("Account eliminato", await giusta.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        Assert.Equal(StatoUtenza.Anonimizzata, (await c.UtenzaAsync(c.Cliente)).Stato);
        Assert.True(SessioneProva.EReindirizzamento(dopo));
        Assert.False(SessioneProva.EReindirizzamento(riaccesso));
    }

    [Fact]
    public async Task IlPiePaginaCollegaPrivacyEResiSoloSeIlTenantLeHaPubblicate()
    {
        await using var c = await ContestoNegozio.CreaAsync();
        var cliente = await ClienteAsync(c);
        var prima = await (await cliente.GetAsync("/account")).Content.ReadAsStringAsync(Ct);

        await PubblicaAsync(c, "privacy");
        var conPrivacy = await (await cliente.GetAsync("/account")).Content.ReadAsStringAsync(Ct);
        var vetrina = await (await cliente.GetAsync("/account/carrello")).Content.ReadAsStringAsync(Ct);

        Assert.DoesNotContain("Informativa sulla privacy", prima, StringComparison.Ordinal);
        Assert.Contains("href=\"/privacy\"", conPrivacy, StringComparison.Ordinal);
        Assert.DoesNotContain("Resi e rimborsi", conPrivacy, StringComparison.Ordinal);
        Assert.Contains("href=\"/privacy\"", vetrina, StringComparison.Ordinal);
    }

    private static async Task PubblicaAsync(ContestoNegozio c, string slug)
    {
        await using var ambito = c.Web.Fabbrica.Services.CreateAsyncScope();
        ambito.ServiceProvider.GetRequiredService<ContestoTenant>().Imposta(new DatiTenantRisolto(c.A.IdTenant, c.A.IdSito, c.A.IdDominio, c.A.Host, false));
        var pagine = ambito.ServiceProvider.GetRequiredService<IServizioPagine>();
        var creata = await pagine.CreaAsync(c.AdminA, new DatiNuovaPagina(slug, "Privacy", "x", "it", false), Ct);
        var dettaglio = (await pagine.LeggiAsync(c.AdminA, creata.Id!.Value, Ct))!;
        Assert.True((await pagine.PubblicaAsync(c.AdminA, creata.Id.Value, dettaglio.TokenConcorrenza, Ct)).Riuscito);
    }

    [Fact]
    public async Task UnAmministratoreCheCompraNonPuoEliminareIlProprioAccountDaQui()
    {
        await using var c = await ContestoNegozio.CreaAsync();
        var anna = c.Web.SessioneSu(c.A.Host);
        await anna.AccediAsync("anna@esempio.it", DatiProva.PasswordProva);
        using var entra = await anna.InviaModuloAsync("/admin", [], "/admin/negozio/acquista");

        using var esito = await anna.InviaModuloAsync("/account/dati", [new("Password", DatiProva.PasswordProva), new("Confermo", "true")], "/account/dati?handler=Elimina");

        Assert.Contains("Questo account non si può eliminare da qui.", WebUtility.HtmlDecode(await esito.Content.ReadAsStringAsync(Ct)), StringComparison.Ordinal);
        Assert.Equal(StatoUtenza.Attiva, (await c.UtenzaAsync(c.AdminA)).Stato);
    }
}
