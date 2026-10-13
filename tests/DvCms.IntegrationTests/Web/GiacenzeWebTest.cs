using System.Net;
using DvCms.Infrastructure.Persistenza;
using DvCms.IntegrationTests.Negozio;
using DvCms.IntegrationTests.Supporto;
using DvCms.Models.Accessi;
using DvCms.Models.Negozio;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DvCms.IntegrationTests.Web;

/// <summary>Giacenze dal modulo web (F-005): amministrazione della quantità e messaggi della cassa quando la merce non basta.</summary>
public class GiacenzeWebTest
{
    private const string Percorso = "/admin/negozio/giacenze";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task LaPaginaLaVedeSoloChiAmministraIlSitoConIlNegozioAttivo()
    {
        await using var c = await ContestoNegozio.CreaAsync();
        await c.CreaProdottoAsync(c.A, "Libro <b>raro</b>", 4m, ("LIB-1", 10.00m, null));
        var anna = c.Web.SessioneSu(c.A.Host);
        await anna.AccediAsync("anna@esempio.it", DatiProva.PasswordProva);
        var anonimo = c.Web.SessioneSu(c.A.Host);

        using var pagina = await anna.GetAsync(Percorso);
        using var cruscotto = await anna.GetAsync("/admin");
        using var senza = await anonimo.GetAsync(Percorso);

        var html = await pagina.Content.ReadAsStringAsync(Ct);
        Assert.Equal(HttpStatusCode.OK, pagina.StatusCode);
        Assert.Contains("Libro &lt;b&gt;raro&lt;/b&gt;", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<b>raro</b>", html, StringComparison.Ordinal);
        Assert.Contains(Percorso, await cruscotto.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        Assert.True(SessioneProva.EReindirizzamento(senza));
    }

    [Fact]
    public async Task SiImpostaLaQuantitaSiRifiutaUnValoreNonValidoESiSmetteDiGestire()
    {
        await using var c = await ContestoNegozio.CreaAsync();
        var varianti = await c.CreaProdottoAsync(c.A, "Libro", 4m, ("LIB-1", 10.00m, null));
        var anna = c.Web.SessioneSu(c.A.Host);
        await anna.AccediAsync("anna@esempio.it", DatiProva.PasswordProva);

        using var ok = await anna.InviaModuloAsync(Percorso, [new("idVariante", varianti[0].ToString()), new("disponibile", "12")], $"{Percorso}?handler=Imposta");
        using var testo = await anna.InviaModuloAsync(Percorso, [new("idVariante", varianti[0].ToString()), new("disponibile", "molti")], $"{Percorso}?handler=Imposta");
        using var negativa = await anna.InviaModuloAsync(Percorso, [new("idVariante", varianti[0].ToString()), new("disponibile", "-3")], $"{Percorso}?handler=Imposta");

        Assert.True(SessioneProva.EReindirizzamento(ok));
        Assert.Equal(12, (await c.GiacenzaAsync(varianti[0])).Disponibile);
        foreach (var risposta in new[] { testo, negativa })
        {
            Assert.Equal(HttpStatusCode.OK, risposta.StatusCode);
            Assert.Contains("role=\"alert\"", await risposta.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        }

        Assert.Equal(12, (await c.GiacenzaAsync(varianti[0])).Disponibile);
        using var spegni = await anna.InviaModuloAsync(Percorso, [new("idVariante", varianti[0].ToString())], $"{Percorso}?handler=NonGestire");
        Assert.True(SessioneProva.EReindirizzamento(spegni));
        await using var ambito = c.Web.Fabbrica.Services.CreateAsyncScope();
        Assert.Equal(0, await ambito.ServiceProvider.GetRequiredService<ContestoDatabase>().GiacenzeVarianti.IgnoreQueryFilters().CountAsync(Ct));
    }

    [Fact]
    public async Task UnaVarianteDiUnAltroTenantNonSiImpostaDallAmministrazioneDiUnAltro()
    {
        await using var c = await ContestoNegozio.CreaAsync();
        var variantiB = await c.CreaProdottoAsync(c.B, "Libro", 4m, ("LIB-1", 10.00m, null));
        var anna = c.Web.SessioneSu(c.A.Host);
        await anna.AccediAsync("anna@esempio.it", DatiProva.PasswordProva);

        using var risposta = await anna.InviaModuloAsync(Percorso, [new("idVariante", variantiB[0].ToString()), new("disponibile", "99")], $"{Percorso}?handler=Imposta");

        Assert.Equal(HttpStatusCode.OK, risposta.StatusCode);
        await using var ambito = c.Web.Fabbrica.Services.CreateAsyncScope();
        Assert.Equal(0, await ambito.ServiceProvider.GetRequiredService<ContestoDatabase>().GiacenzeVarianti.IgnoreQueryFilters().CountAsync(Ct));
    }

    [Fact]
    public async Task LaCassaDiceQualiArticoliNonBastanoENonCreaOrdini()
    {
        await using var c = await ContestoNegozio.CreaAsync();
        var varianti = await c.CreaProdottoAsync(c.A, "Libro", 4m, ("LIB-1", 10.00m, null));
        var ritiro = await c.CreaRitiroAsync(c.A);
        await c.ImpostaGiacenzaAsync(c.A, varianti[0], 1);
        var cliente = c.Web.SessioneSu(c.A.Host);
        using var accesso = await cliente.InviaModuloAsync("/account/accesso", [new("Email", "cliente@esempio.it"), new("Password", DatiProva.PasswordProva)]);
        Assert.True(SessioneProva.EReindirizzamento(accesso));
        using var aggiunta = await cliente.InviaModuloAsync(
            "/account",
            [new("idVariante", varianti[0].ToString()), new("quantita", "2")],
            "/account/carrello?handler=Aggiungi");
        Assert.True(SessioneProva.EReindirizzamento(aggiunta));

        using var risposta = await cliente.InviaModuloAsync(
            "/account/checkout",
            [new("Modulo.IdMetodo", ritiro.ToString()), new($"Modulo.Atteso[{ritiro}]", "20.00")]);

        Assert.Equal(HttpStatusCode.OK, risposta.StatusCode);
        var testo = WebUtility.HtmlDecode(await risposta.Content.ReadAsStringAsync(Ct));
        Assert.Contains("role=\"alert\"", testo, StringComparison.Ordinal);
        Assert.Contains("LIB-1", testo, StringComparison.Ordinal);
        Assert.Equal(0, await c.ContaOrdiniAsync());
    }

    [Fact]
    public async Task IlCarrelloAvvisaEImpedisceLaCassaSeLaMerceNonBasta()
    {
        await using var c = await ContestoNegozio.CreaAsync();
        var varianti = await c.CreaProdottoAsync(c.A, "Libro", 4m, ("LIB-1", 10.00m, null));
        await c.ImpostaGiacenzaAsync(c.A, varianti[0], 2);
        var cliente = c.Web.SessioneSu(c.A.Host);
        using var accesso = await cliente.InviaModuloAsync("/account/accesso", [new("Email", "cliente@esempio.it"), new("Password", DatiProva.PasswordProva)]);
        Assert.True(SessioneProva.EReindirizzamento(accesso));
        await c.AggiungiAsync(c.Cliente, varianti[0], 1);
        using var abbastanza = await cliente.GetAsync("/account/carrello");
        await c.AggiungiAsync(c.Cliente, varianti[0], 4);

        using var troppo = await cliente.GetAsync("/account/carrello");

        var bene = WebUtility.HtmlDecode(await abbastanza.Content.ReadAsStringAsync(Ct));
        var male = WebUtility.HtmlDecode(await troppo.Content.ReadAsStringAsync(Ct));
        Assert.Contains("/account/checkout", bene, StringComparison.Ordinal);
        Assert.DoesNotContain("Disponibili solo", bene, StringComparison.Ordinal);
        Assert.Contains("Disponibili solo", male, StringComparison.Ordinal);
        Assert.DoesNotContain("/account/checkout", male, StringComparison.Ordinal);
    }
}
