using System.Net;
using DvCms.Application.Negozio;
using DvCms.IntegrationTests.Negozio;
using DvCms.IntegrationTests.Supporto;
using DvCms.Models.Negozio;
using Xunit;

namespace DvCms.IntegrationTests.Web;

/// <summary>Rimborsi dal pannello del tenant (A5): modulo, importi con la virgola, messaggi, permessi e vista del cliente.</summary>
public class RimborsiWebTest
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<(ContestoNegozio C, PayPalFinto Finto, Guid Ordine)> OrdinePagatoAsync()
    {
        var finto = new PayPalFinto();
        var c = await ContestoNegozio.CreaAsync(payPal: finto);
        var varianti = await c.CreaProdottoAsync(c.A, "Libro", 4m, ("LIB-1", 10m, null));
        var ritiro = await c.CreaRitiroAsync(c.A);
        await c.ConfiguraPagamentoAsync(c.A);
        await c.AggiungiAsync(c.Cliente, varianti[0], 1);
        var ordine = (await c.ComeAsync(c.A, s => s.CreaDaCarrelloAsync(c.Cliente, "it", new DatiOrdine(ritiro, null, null, null, null, null, null, 10m), Ct))).Id!.Value;
        await c.ComePagamentiAsync(c.A, s => s.AvviaAsync(c.Cliente, ordine, DvCms.Application.Negozio.NomiProviderPagamento.PayPal, ContestoNegozio.Link, Ct));
        var idProvider = (await c.TentativiAsync(ordine)).Single().IdProvider;
        finto.Approva(idProvider);
        await c.ComePagamentiAsync(c.A, s => s.CompletaAsync(c.Cliente, ordine, idProvider, Ct));
        return (c, finto, ordine);
    }

    [Fact]
    public async Task LAmministrazioneRimborsaUnaParteELaVedeInElencoEIlClienteVedeLImportoRimborsato()
    {
        var (c, finto, ordine) = await OrdinePagatoAsync();
        await using var _ = c;
        var anna = c.Web.SessioneSu(c.A.Host);
        await anna.AccediAsync("anna@esempio.it", DatiProva.PasswordProva);
        var pagina = $"/admin/negozio/ordini/{ordine}";

        using var prima = await anna.GetAsync(pagina);
        using var rimborsa = await anna.InviaModuloAsync(pagina, [new("importo", "4,00"), new("motivo", "Un pezzo danneggiato")], $"{pagina}?handler=Rimborsa");
        using var dopo = await anna.GetAsync(pagina);
        var cliente = c.Web.SessioneSu(c.A.Host);
        using var accesso = await cliente.InviaModuloAsync("/account/accesso", [new("Email", "cliente@esempio.it"), new("Password", DatiProva.PasswordProva)]);
        using var vistaCliente = await cliente.GetAsync($"/account/ordini/{ordine}");

        Assert.Contains("name=\"importo\"", await prima.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        Assert.True(SessioneProva.EReindirizzamento(rimborsa));
        var testo = WebUtility.HtmlDecode(await dopo.Content.ReadAsStringAsync(Ct));
        Assert.Contains("Rimborso eseguito", testo, StringComparison.Ordinal);
        Assert.Contains("4,00 EUR — Completato", testo, StringComparison.Ordinal);
        Assert.Contains("Un pezzo danneggiato", testo, StringComparison.Ordinal);
        Assert.Contains("6,00 EUR", testo, StringComparison.Ordinal);
        Assert.Contains("Rimborsato: <strong>4,00 EUR</strong>", WebUtility.HtmlDecode(await vistaCliente.Content.ReadAsStringAsync(Ct)), StringComparison.Ordinal);
        Assert.Equal(1, finto.Conta(HttpMethod.Post, "/v2/payments/captures/CAP-ORD-0001/refund"));
    }

    [Fact]
    public async Task UnImportoNonValidoODaUnClienteNonRimborsaNulla()
    {
        var (c, finto, ordine) = await OrdinePagatoAsync();
        await using var _ = c;
        var anna = c.Web.SessioneSu(c.A.Host);
        await anna.AccediAsync("anna@esempio.it", DatiProva.PasswordProva);
        var pagina = $"/admin/negozio/ordini/{ordine}";
        var cliente = c.Web.SessioneSu(c.A.Host);
        using var accesso = await cliente.InviaModuloAsync("/account/accesso", [new("Email", "cliente@esempio.it"), new("Password", DatiProva.PasswordProva)]);

        using var testo = await anna.InviaModuloAsync(pagina, [new("importo", "tanto")], $"{pagina}?handler=Rimborsa");
        using var troppo = await anna.InviaModuloAsync(pagina, [new("importo", "10,01")], $"{pagina}?handler=Rimborsa");
        using var dopo = await anna.GetAsync(pagina);
        using var daCliente = await cliente.InviaModuloAsync("/account", [new("importo", "1")], $"{pagina}?handler=Rimborsa");

        Assert.True(SessioneProva.EReindirizzamento(testo));
        Assert.True(SessioneProva.EReindirizzamento(troppo));
        Assert.Contains("Importo o motivo non validi", WebUtility.HtmlDecode(await dopo.Content.ReadAsStringAsync(Ct)), StringComparison.Ordinal);
        Assert.NotEqual(HttpStatusCode.OK, daCliente.StatusCode);
        Assert.Equal(0, finto.Conta(HttpMethod.Post, "/v2/payments/captures/CAP-ORD-0001/refund"));
    }
}
