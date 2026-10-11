using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using DvCms.Application.Negozio;
using DvCms.Domain.Negozio;
using DvCms.IntegrationTests.Negozio;
using DvCms.IntegrationTests.Supporto;
using Xunit;

namespace DvCms.IntegrationTests.Web;

/// <summary>Pagamento dal sito e webhook di PayPal (F-004): pagine del cliente, endpoint pubblico firmato, vista amministrativa.</summary>
public class PagamentiWebTest
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<(ContestoNegozio C, PayPalFinto Finto, Guid Ordine)> PreparaAsync()
    {
        var finto = new PayPalFinto();
        var c = await ContestoNegozio.CreaAsync(payPal: finto);
        var varianti = await c.CreaProdottoAsync(c.A, "Libro <b>raro</b>", 4m, ("LIB-1", 10.00m, null));
        var ritiro = await c.CreaRitiroAsync(c.A);
        await c.ImpostaGiacenzaAsync(c.A, varianti[0], 5);
        await c.ConfiguraPagamentoAsync(c.A);
        await c.AggiungiAsync(c.Cliente, varianti[0], 1);
        var esito = await c.ComeAsync(c.A, s => s.CreaDaCarrelloAsync(c.Cliente, "it", new DatiOrdine(ritiro, null, null, null, null, null, null, 10m), Ct));
        Assert.True(esito.Riuscito, esito.Codice.ToString());
        return (c, finto, esito.Id!.Value);
    }

    private static async Task<SessioneProva> ClienteAsync(ContestoNegozio c, string email = "cliente@esempio.it", bool tenantB = false)
    {
        var sessione = c.Web.SessioneSu(tenantB ? c.B.Host : c.A.Host);
        using var accesso = await sessione.InviaModuloAsync("/account/accesso", [new("Email", email), new("Password", DatiProva.PasswordProva)]);
        Assert.True(SessioneProva.EReindirizzamento(accesso), email);
        return sessione;
    }

    private static async Task<HttpResponseMessage> WebhookAsync(SessioneProva sessione, string corpo, string firma = PayPalFinto.FirmaValida, bool conIntestazioni = true)
    {
        using var richiesta = new HttpRequestMessage(HttpMethod.Post, "/webhooks/paypal")
        {
            Content = new StringContent(corpo, Encoding.UTF8, new MediaTypeHeaderValue("application/json")),
        };
        if (conIntestazioni)
        {
            richiesta.Headers.Add("PAYPAL-AUTH-ALGO", "SHA256withRSA");
            richiesta.Headers.Add("PAYPAL-CERT-URL", "https://api.paypal.com/cert");
            richiesta.Headers.Add("PAYPAL-TRANSMISSION-ID", "TRX-1");
            richiesta.Headers.Add("PAYPAL-TRANSMISSION-SIG", firma);
            richiesta.Headers.Add("PAYPAL-TRANSMISSION-TIME", "2026-10-11T10:00:00Z");
        }

        return await sessione.InviaAsync(richiesta);
    }

    private static string Evento(string id, string idOrdineProvider) => new JsonObject
    {
        ["id"] = id,
        ["event_type"] = "CHECKOUT.ORDER.APPROVED",
        ["resource"] = new JsonObject { ["id"] = idOrdineProvider },
    }.ToJsonString();

    [Fact]
    public async Task IlClienteSceglieDiPagareVaSuPayPalETornaConLOrdinePagato()
    {
        var (c, finto, ordine) = await PreparaAsync();
        await using var _ = c;
        var cliente = await ClienteAsync(c);
        var pagina = $"/account/ordini/{ordine}";

        using var prima = await cliente.GetAsync(pagina);
        using var paga = await cliente.InviaModuloAsync(pagina, [], $"{pagina}?handler=Paga");
        finto.Approva("ORD-0001");
        using var ritorno = await cliente.GetAsync($"{pagina}/pagamento?token=ORD-0001&PayerID=XYZ");
        using var dopo = await cliente.GetAsync(pagina);

        var htmlPrima = WebUtility.HtmlDecode(await prima.Content.ReadAsStringAsync(Ct));
        Assert.Contains("Paga con PayPal", htmlPrima, StringComparison.Ordinal);
        var htmlPaga = await paga.Content.ReadAsStringAsync(Ct);
        Assert.Equal(HttpStatusCode.OK, paga.StatusCode);
        Assert.Contains("href=\"https://www.sandbox.paypal.com/checkoutnow?token=ORD-0001\"", htmlPaga, StringComparison.Ordinal);
        Assert.True(SessioneProva.EReindirizzamento(ritorno));
        Assert.Equal(pagina, SessioneProva.Destinazione(ritorno));
        var htmlDopo = WebUtility.HtmlDecode(await dopo.Content.ReadAsStringAsync(Ct));
        Assert.Contains("Pagamento ricevuto", htmlDopo, StringComparison.Ordinal);
        Assert.Contains("Pagato", htmlDopo, StringComparison.Ordinal);
        Assert.DoesNotContain("Paga con PayPal", htmlDopo, StringComparison.Ordinal);
        Assert.DoesNotContain("Annulla l'ordine", htmlDopo, StringComparison.Ordinal);
        Assert.Equal(StatoOrdine.Pagato, await c.StatoOrdineAsync(ordine));
    }

    [Fact]
    public async Task IlRitornoSenzaAverApprovatoNonPagaELOrdineResta()
    {
        var (c, _, ordine) = await PreparaAsync();
        await using var _ = c;
        var cliente = await ClienteAsync(c);
        var pagina = $"/account/ordini/{ordine}";
        using var paga = await cliente.InviaModuloAsync(pagina, [], $"{pagina}?handler=Paga");

        using var ritorno = await cliente.GetAsync($"{pagina}/pagamento?token=ORD-0001");
        using var dopo = await cliente.GetAsync(pagina);

        Assert.True(SessioneProva.EReindirizzamento(ritorno));
        var testo = TestoDecodificato(await dopo.Content.ReadAsStringAsync(Ct));
        Assert.Contains("non è stato approvato", testo, StringComparison.Ordinal);
        Assert.Contains("Paga con PayPal", testo, StringComparison.Ordinal);
        Assert.Equal(StatoOrdine.InAttesaDiPagamento, await c.StatoOrdineAsync(ordine));
    }

    private static string TestoDecodificato(string html) => WebUtility.HtmlDecode(html);

    [Fact]
    public async Task UnTokenInventatoOUnOrdineAltruiDanno404ESenzaSessioneSiVaAllAccesso()
    {
        var (c, _, ordine) = await PreparaAsync();
        await using var _ = c;
        var cliente = await ClienteAsync(c);
        var altro = await ClienteAsync(c, "altro@esempio.it");
        var anonimo = c.Web.SessioneSu(c.A.Host);
        var pagina = $"/account/ordini/{ordine}";
        using var avvio = await cliente.InviaModuloAsync(pagina, [], $"{pagina}?handler=Paga");

        using var tokenFalso = await cliente.GetAsync($"{pagina}/pagamento?token=ORD-9999");
        using var senzaToken = await cliente.GetAsync($"{pagina}/pagamento");
        using var daAltro = await altro.GetAsync($"{pagina}/pagamento?token=ORD-0001");
        using var pagaDaAltro = await altro.InviaModuloAsync("/account", [], $"{pagina}?handler=Paga");
        using var daAnonimo = await anonimo.GetAsync($"{pagina}/pagamento?token=ORD-0001");

        Assert.Equal(HttpStatusCode.NotFound, tokenFalso.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, senzaToken.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, daAltro.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, pagaDaAltro.StatusCode);
        Assert.True(SessioneProva.EReindirizzamento(daAnonimo));
        Assert.StartsWith("/account/accesso", SessioneProva.Destinazione(daAnonimo), StringComparison.Ordinal);
    }

    [Fact]
    public async Task IlWebhookFirmatoPagaLOrdineEUnaRipetizioneNonCambiaNulla()
    {
        var (c, finto, ordine) = await PreparaAsync();
        await using var _ = c;
        var cliente = await ClienteAsync(c);
        await cliente.InviaModuloAsync($"/account/ordini/{ordine}", [], $"/account/ordini/{ordine}?handler=Paga");
        finto.Approva("ORD-0001");
        var anonimo = c.Web.SessioneSu(c.A.Host);

        using var prima = await WebhookAsync(anonimo, Evento("WH-1", "ORD-0001"));
        using var seconda = await WebhookAsync(anonimo, Evento("WH-1", "ORD-0001"));

        Assert.Equal(HttpStatusCode.OK, prima.StatusCode);
        Assert.Equal(HttpStatusCode.OK, seconda.StatusCode);
        Assert.Equal(string.Empty, await prima.Content.ReadAsStringAsync(Ct));
        Assert.Equal(StatoOrdine.Pagato, await c.StatoOrdineAsync(ordine));
        Assert.Equal(1, finto.Conta(HttpMethod.Post, "/v2/checkout/orders/ORD-0001/capture"));
    }

    [Fact]
    public async Task IlWebhookSenzaFirmaOConFirmaSbagliataEQuelloMalformatoNonHannoEffetto()
    {
        var (c, finto, ordine) = await PreparaAsync();
        await using var _ = c;
        var cliente = await ClienteAsync(c);
        await cliente.InviaModuloAsync($"/account/ordini/{ordine}", [], $"/account/ordini/{ordine}?handler=Paga");
        finto.Approva("ORD-0001");
        var anonimo = c.Web.SessioneSu(c.A.Host);

        using var senzaIntestazioni = await WebhookAsync(anonimo, Evento("WH-1", "ORD-0001"), conIntestazioni: false);
        using var firmaSbagliata = await WebhookAsync(anonimo, Evento("WH-1", "ORD-0001"), firma: "falsa");
        using var senzaId = await WebhookAsync(anonimo, "{\"event_type\":\"CHECKOUT.ORDER.APPROVED\"}");
        using var enorme = await WebhookAsync(anonimo, new string('x', (256 * 1024) + 10));
        using var lettura = await anonimo.GetAsync("/webhooks/paypal");

        Assert.Equal(HttpStatusCode.Unauthorized, senzaIntestazioni.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, firmaSbagliata.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, senzaId.StatusCode);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, enorme.StatusCode);
        Assert.NotEqual(HttpStatusCode.OK, lettura.StatusCode);
        Assert.Equal(StatoOrdine.InAttesaDiPagamento, await c.StatoOrdineAsync(ordine));
        Assert.Equal(0, finto.Conta(HttpMethod.Post, "/v2/checkout/orders/ORD-0001/capture"));
    }

    [Fact]
    public async Task UnSitoSenzaCredenzialiRispondeNonTrovatoAlWebhook()
    {
        var (c, _, _) = await PreparaAsync();
        await using var _ = c;
        var sitoB = c.Web.SessioneSu(c.B.Host);

        using var risposta = await WebhookAsync(sitoB, Evento("WH-1", "ORD-0001"));

        Assert.Equal(HttpStatusCode.NotFound, risposta.StatusCode);
    }

    [Fact]
    public async Task LAmministrazioneVedeIPagamentiESegnaLOrdineComeRitirato()
    {
        var (c, finto, ordine) = await PreparaAsync();
        await using var _ = c;
        var cliente = await ClienteAsync(c);
        var anna = c.Web.SessioneSu(c.A.Host);
        await anna.AccediAsync("anna@esempio.it", DatiProva.PasswordProva);
        var adminPagina = $"/admin/negozio/ordini/{ordine}";
        using var primaDelPagamento = await anna.GetAsync(adminPagina);
        await cliente.InviaModuloAsync($"/account/ordini/{ordine}", [], $"/account/ordini/{ordine}?handler=Paga");
        finto.Approva("ORD-0001");
        using var _1 = await cliente.GetAsync($"/account/ordini/{ordine}/pagamento?token=ORD-0001");

        using var dopo = await anna.GetAsync(adminPagina);
        using var evaso = await anna.InviaModuloAsync(adminPagina, [], $"{adminPagina}?handler=Evaso");
        using var finale = await anna.GetAsync(adminPagina);
        using var clienteProva = await cliente.InviaModuloAsync("/account", [], $"{adminPagina}?handler=Evaso");

        Assert.DoesNotContain("Segna come ritirato", await primaDelPagamento.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        var testoDopo = WebUtility.HtmlDecode(await dopo.Content.ReadAsStringAsync(Ct));
        Assert.Contains("Incassato", testoDopo, StringComparison.Ordinal);
        Assert.Contains("ORD-0001", testoDopo, StringComparison.Ordinal);
        Assert.Contains("Segna come ritirato", testoDopo, StringComparison.Ordinal);
        Assert.True(SessioneProva.EReindirizzamento(evaso));
        Assert.Equal(StatoOrdine.Ritirato, await c.StatoOrdineAsync(ordine));
        Assert.DoesNotContain("Segna come ritirato", await finale.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        Assert.NotEqual(HttpStatusCode.OK, clienteProva.StatusCode);
    }
}
