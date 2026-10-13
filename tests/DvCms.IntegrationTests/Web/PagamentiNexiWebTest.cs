using System.Net;
using System.Net.Http.Headers;
using System.Text;
using DvCms.Application.Negozio;
using DvCms.IntegrationTests.Negozio;
using DvCms.IntegrationTests.Supporto;
using DvCms.Models.Negozio;
using Xunit;

namespace DvCms.IntegrationTests.Web;

/// <summary>Pagamento con Nexi dal sito (ADR-0022): scelta del provider alla cassa, ritorno del cliente, endpoint pubblico delle notifiche e pagina Platform.</summary>
public class PagamentiNexiWebTest
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<(ContestoNegozio C, NexiFinto Nexi, Guid Ordine)> PreparaAsync(bool conPayPal)
    {
        var nexi = new NexiFinto();
        var c = await ContestoNegozio.CreaAsync(payPal: new PayPalFinto(), nexi: nexi);
        var varianti = await c.CreaProdottoAsync(c.A, "Libro", 4m, ("LIB-1", 10.00m, null));
        var ritiro = await c.CreaRitiroAsync(c.A);
        await c.ImpostaGiacenzaAsync(c.A, varianti[0], 5);
        await c.ConfiguraNexiAsync(c.A);
        if (conPayPal)
        {
            await c.ConfiguraPagamentoAsync(c.A);
        }

        await c.AggiungiAsync(c.Cliente, varianti[0], 1);
        var esito = await c.ComeAsync(c.A, s => s.CreaDaCarrelloAsync(c.Cliente, "it", new DatiOrdine(ritiro, null, null, null, null, null, null, 10m), Ct));
        Assert.True(esito.Riuscito, esito.Codice.ToString());
        return (c, nexi, esito.Id!.Value);
    }

    private static async Task<SessioneProva> ClienteAsync(ContestoNegozio c)
    {
        var sessione = c.Web.SessioneSu(c.A.Host);
        using var accesso = await sessione.InviaModuloAsync("/account/accesso", [new("Email", "cliente@esempio.it"), new("Password", DatiProva.PasswordProva)]);
        Assert.True(SessioneProva.EReindirizzamento(accesso));
        return sessione;
    }

    private static async Task<HttpResponseMessage> NotificaAsync(SessioneProva sessione, string corpo)
    {
        using var richiesta = new HttpRequestMessage(HttpMethod.Post, "/webhooks/nexi")
        {
            Content = new StringContent(corpo, Encoding.UTF8, new MediaTypeHeaderValue("application/json")),
        };
        return await sessione.InviaAsync(richiesta);
    }

    [Fact]
    public async Task AllaCassaIlClienteVedeUnPulsantePerProviderEPagaConNexi()
    {
        var (c, nexi, ordine) = await PreparaAsync(conPayPal: true);
        await using var _ = c;
        var cliente = await ClienteAsync(c);
        var pagina = $"/account/ordini/{ordine}";

        using var prima = await cliente.GetAsync(pagina);
        using var paga = await cliente.InviaModuloAsync(pagina, [new("provider", "Nexi")], $"{pagina}?handler=Paga");
        var idProvider = (await c.TentativiAsync(ordine)).Single().IdProvider;
        nexi.Paga(idProvider);
        using var ritorno = await cliente.GetAsync($"{pagina}/pagamento?token={idProvider}");
        using var dopo = await cliente.GetAsync(pagina);

        var htmlPrima = WebUtility.HtmlDecode(await prima.Content.ReadAsStringAsync(Ct));
        Assert.Contains("Paga con PayPal", htmlPrima, StringComparison.Ordinal);
        Assert.Contains("Paga con carta (Nexi)", htmlPrima, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, paga.StatusCode);
        Assert.Contains($"href=\"https://xpaysandbox.nexigroup.com/hpp/nexi/{idProvider}?operationId=1\"", await paga.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        Assert.True(SessioneProva.EReindirizzamento(ritorno));
        Assert.Equal(pagina, SessioneProva.Destinazione(ritorno));
        Assert.Contains("Pagamento ricevuto", WebUtility.HtmlDecode(await dopo.Content.ReadAsStringAsync(Ct)), StringComparison.Ordinal);
        Assert.Equal(StatoOrdine.Pagato, await c.StatoOrdineAsync(ordine));
    }

    [Fact]
    public async Task ConSoloNexiCeUnSoloPulsanteESenzaProviderUnAvviso()
    {
        var (c, _, ordine) = await PreparaAsync(conPayPal: false);
        await using var _ = c;
        var cliente = await ClienteAsync(c);
        var pagina = $"/account/ordini/{ordine}";

        var solo = WebUtility.HtmlDecode(await (await cliente.GetAsync(pagina)).Content.ReadAsStringAsync(Ct));

        Assert.Contains("Paga con carta (Nexi)", solo, StringComparison.Ordinal);
        Assert.DoesNotContain("Paga con PayPal", solo, StringComparison.Ordinal);

        // Il modulo senza scelta (versione precedente) vale PayPal, qui non configurato: nessun pagamento parte.
        using var senzaScelta = await cliente.InviaModuloAsync(pagina, [], $"{pagina}?handler=Paga");
        Assert.True(SessioneProva.EReindirizzamento(senzaScelta));
        Assert.Empty(await c.TentativiAsync(ordine));
    }

    [Fact]
    public async Task LaNotificaDiNexiPagaLOrdineEUnTokenSbagliatoDa401()
    {
        var (c, nexi, ordine) = await PreparaAsync(conPayPal: false);
        await using var _ = c;
        var cliente = await ClienteAsync(c);
        await cliente.InviaModuloAsync($"/account/ordini/{ordine}", [new("provider", "Nexi")], $"/account/ordini/{ordine}?handler=Paga");
        var idProvider = (await c.TentativiAsync(ordine)).Single().IdProvider;
        nexi.Paga(idProvider);
        var anonimo = c.Web.SessioneSu(c.A.Host);

        using var tokenSbagliato = await NotificaAsync(anonimo, nexi.Notifica(idProvider, token: "falso"));
        Assert.Equal(StatoOrdine.InAttesaDiPagamento, await c.StatoOrdineAsync(ordine));
        using var valida = await NotificaAsync(anonimo, nexi.Notifica(idProvider, idEvento: "EVT-W1"));
        using var ripetuta = await NotificaAsync(anonimo, nexi.Notifica(idProvider, idEvento: "EVT-W1"));
        using var malformata = await NotificaAsync(anonimo, "non json");
        using var enorme = await NotificaAsync(anonimo, new string('x', (64 * 1024) + 10));
        using var lettura = await anonimo.GetAsync("/webhooks/nexi");

        Assert.Equal(HttpStatusCode.Unauthorized, tokenSbagliato.StatusCode);
        Assert.Equal(HttpStatusCode.OK, valida.StatusCode);
        Assert.Equal(HttpStatusCode.OK, ripetuta.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, malformata.StatusCode);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, enorme.StatusCode);
        Assert.NotEqual(HttpStatusCode.OK, lettura.StatusCode);
        Assert.Equal(StatoOrdine.Pagato, await c.StatoOrdineAsync(ordine));
        Assert.DoesNotContain(nexi.Token(idProvider), await valida.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task IlDettaglioAmministrativoIndicaNexiENonOffreIlRimborsoDalPannello()
    {
        var (c, nexi, ordine) = await PreparaAsync(conPayPal: false);
        await using var _ = c;
        var cliente = await ClienteAsync(c);
        await cliente.InviaModuloAsync($"/account/ordini/{ordine}", [new("provider", "Nexi")], $"/account/ordini/{ordine}?handler=Paga");
        var idProvider = (await c.TentativiAsync(ordine)).Single().IdProvider;
        nexi.Paga(idProvider);
        await cliente.GetAsync($"/account/ordini/{ordine}/pagamento?token={idProvider}");
        var admin = c.Web.SessioneSu(c.A.Host);
        using var accesso = await admin.AccediAsync("anna@esempio.it", DatiProva.PasswordProva);

        var html = WebUtility.HtmlDecode(await (await admin.GetAsync($"/admin/negozio/ordini/{ordine}")).Content.ReadAsStringAsync(Ct));

        Assert.Contains($"Nexi · {idProvider}", html, StringComparison.Ordinal);
        Assert.Contains("usa il back office Nexi", html, StringComparison.Ordinal);
        Assert.DoesNotContain("handler=Rimborsa\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("name=\"motivo\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LaPaginaPlatformSalvaLaChiaveNexiSenzaMostrarlaEMostraLaVerificaDiEsercizio()
    {
        var (c, _, _) = await PreparaAsync(conPayPal: false);
        await using var _ = c;
        var platform = c.Web.SessioneSu(c.B.Host);
        using var accesso = await platform.AccediAsync("root@esempio.it", DatiProva.PasswordProva);
        var pagina = $"/admin/platform/tenant/{c.A.IdTenant}/pagamenti?idSito={c.A.IdSito}";

        using var salva = await platform.InviaModuloAsync(pagina, [new("ambiente", "Sandbox"), new("chiaveApi", "CHIAVE-NEXI-SEGRETA")], $"{pagina}&handler=SalvaNexi");
        var html = WebUtility.HtmlDecode(await (await platform.GetAsync(pagina)).Content.ReadAsStringAsync(Ct));

        Assert.True(SessioneProva.EReindirizzamento(salva));
        Assert.Contains("Nexi XPay", html, StringComparison.Ordinal);
        Assert.Contains("/webhooks/nexi", html, StringComparison.Ordinal);
        Assert.Contains("Impostata: lascia vuoto per non cambiarla", html, StringComparison.Ordinal);
        Assert.DoesNotContain("CHIAVE-NEXI-SEGRETA", html, StringComparison.Ordinal);
        Assert.Contains("Credenziali Nexi XPay", html, StringComparison.Ordinal);
    }
}
