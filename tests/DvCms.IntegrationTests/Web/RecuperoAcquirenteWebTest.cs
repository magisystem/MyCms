using System.Net;
using System.Text.RegularExpressions;
using DvCms.IntegrationTests.Negozio;
using DvCms.IntegrationTests.Supporto;
using Xunit;

namespace DvCms.IntegrationTests.Web;

/// <summary>Recupero password dell'acquirente (F-000): stesso flusso dell'amministrazione, ma si parte e si torna all'accesso dell'area cliente.</summary>
public partial class RecuperoAcquirenteWebTest
{
    private const string Nuova = "una-nuova-password-sicura-1";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task LAcquirenteRecuperaLaPasswordEOraTornaAllAccessoDellAreaCliente()
    {
        await using var c = await ContestoNegozio.CreaAsync(conEmail: true);
        var anonimo = c.Web.SessioneSu(c.A.Host);

        using var accesso = await anonimo.GetAsync("/account/accesso");
        using var richiesta = await anonimo.InviaModuloAsync("/admin/recupero?da=account", [new("Email", "cliente@esempio.it"), new("Da", "account")], "/admin/recupero?da=account");
        var codice = CodiceNelTesto().Match(Assert.Single(await c.ElaboraEmailAsync()).Testo).Groups[1].Value;
        using var paginaCodice = await anonimo.GetAsync("/admin/recupero/codice?da=account");
        using var verifica = await anonimo.InviaModuloAsync("/admin/recupero/codice?da=account", [new("Codice", codice)], "/admin/recupero/codice?da=account");
        var link = Assert.Single(await c.ElaboraEmailAsync()).Testo;
        var indirizzo = LinkNelTesto().Match(link).Groups[1].Value;
        var percorso = new Uri(indirizzo).PathAndQuery;
        using var modulo = await anonimo.GetAsync(percorso);
        var token = TokenNelTesto().Match(indirizzo).Groups[1].Value;
        using var fatto = await anonimo.InviaModuloAsync(percorso, [new("Token", token), new("NuovaPassword", Nuova), new("ConfermaPassword", Nuova), new("Da", "account")]);
        using var nuovoAccesso = await c.Web.SessioneSu(c.A.Host).InviaModuloAsync("/account/accesso", [new("Email", "cliente@esempio.it"), new("Password", Nuova)]);
        using var vecchioAccesso = await c.Web.SessioneSu(c.A.Host).InviaModuloAsync("/account/accesso", [new("Email", "cliente@esempio.it"), new("Password", DatiProva.PasswordProva)]);

        Assert.Contains("/admin/recupero?da=account", await accesso.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        Assert.Equal("/admin/recupero/codice?da=account", SessioneProva.Destinazione(richiesta));
        Assert.Contains("href=\"/account/accesso\"", await paginaCodice.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, verifica.StatusCode);
        Assert.EndsWith("&da=account", indirizzo, StringComparison.Ordinal);
        Assert.Contains("href=\"/account/accesso\"", await modulo.Content.ReadAsStringAsync(Ct) + await fatto.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        Assert.Contains("Password cambiata", WebUtility.HtmlDecode(await fatto.Content.ReadAsStringAsync(Ct)), StringComparison.Ordinal);
        Assert.True(SessioneProva.EReindirizzamento(nuovoAccesso));
        Assert.False(SessioneProva.EReindirizzamento(vecchioAccesso));
    }

    [Fact]
    public async Task UnValoreDiProvenienzaStranoNonCambiaDoveSiTorna()
    {
        await using var c = await ContestoNegozio.CreaAsync();
        var anonimo = c.Web.SessioneSu(c.A.Host);

        using var pagina = await anonimo.GetAsync("/admin/recupero?da=https://sito-ostile.esempio.it/");

        var html = await pagina.Content.ReadAsStringAsync(Ct);
        Assert.Contains("href=\"/admin/accesso\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("sito-ostile", html, StringComparison.Ordinal);
    }

    [GeneratedRegex("codice di verifica è (\\d{6})", RegexOptions.CultureInvariant)]
    private static partial Regex CodiceNelTesto();

    [GeneratedRegex("(https?://[^\\s]+/admin/recupero/reimposta\\?token=[A-Za-z0-9_-]+&da=account)", RegexOptions.CultureInvariant)]
    private static partial Regex LinkNelTesto();

    [GeneratedRegex("token=([A-Za-z0-9_-]+)", RegexOptions.CultureInvariant)]
    private static partial Regex TokenNelTesto();
}
