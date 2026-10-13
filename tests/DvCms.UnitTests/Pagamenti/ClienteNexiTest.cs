using System.Globalization;
using System.Net;
using System.Text.Json;
using DvCms.Contracts.Negozio;
using DvCms.DTO.Negozio;
using DvCms.Infrastructure.Pagamenti;
using DvCms.Models.Negozio;
using Xunit;

namespace DvCms.UnitTests.Pagamenti;

/// <summary>Client Nexi XPay (ADR-0022): identificativo ordine, importi, lettura dell'esito dalle operazioni e risposte anomale del gateway.</summary>
public class ClienteNexiTest
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly CredenzialiNexi _credenziali = new(AmbientePagamento.Sandbox, "CHIAVE-SEGRETA");

    private static RichiestaOrdineProvider Richiesta(decimal importo = 10m, string valuta = "EUR") =>
        new(Guid.NewGuid(), importo, valuta, "rif", "Ordine #1", new Uri("https://a.esempio.it/r"), new Uri("https://a.esempio.it/c"), 1, new Uri("https://a.esempio.it/n"));

    private static JsonElement Json(string testo) => JsonDocument.Parse(testo).RootElement;

    [Theory]
    [InlineData(1)]
    [InlineData(123456)]
    [InlineData(int.MaxValue)]
    public void LIdentificativoOrdineRispettaLimitiECaratteriDiNexi(int numero)
    {
        var id = ClienteNexi.NuovoIdOrdine(numero, Guid.NewGuid());

        Assert.InRange(id.Length, 5, ClienteNexi.LunghezzaMassimaIdOrdine);
        Assert.Matches("^NX[0-9]+-[0-9a-f]{10}$", id);
        Assert.NotEqual(id, ClienteNexi.NuovoIdOrdine(numero, Guid.NewGuid()));
    }

    [Theory]
    [InlineData("10.00", "1000")]
    [InlineData("0.01", "1")]
    [InlineData("78.60", "7860")]
    [InlineData("19.995", "2000")]
    public void LImportoVaInCentesimiSenzaPerdite(string importo, string atteso)
    {
        Assert.Equal(atteso, ClienteNexi.ImportoInCentesimi(decimal.Parse(importo, CultureInfo.InvariantCulture)));
    }

    [Fact]
    public void UnIncassoEseguitoDiventaCompletatoConImportoInEuro()
    {
        var esito = ClienteNexi.Interpreta(Json("""
            {"orderStatus":{"order":{"orderId":"X"}},"operations":[
              {"operationId":"1","operationType":"AUTHORIZATION","operationResult":"AUTHORIZED","operationAmount":"1000","operationCurrency":"EUR"},
              {"operationId":"2","operationType":"CAPTURE","operationResult":"EXECUTED","operationAmount":"1000","operationCurrency":"EUR"}]}
            """));

        Assert.Equal((StatoCatturaProvider.Completata, "2", 10m, "EUR"), (esito.Stato, esito.IdCattura, esito.Importo, esito.Valuta));
    }

    [Fact]
    public void UnIncassoSeguitoDaUnRimborsoRestaComunqueCompletato()
    {
        var esito = ClienteNexi.Interpreta(Json("""
            {"operations":[
              {"operationId":"2","operationType":"CAPTURE","operationResult":"EXECUTED","operationAmount":"1000","operationCurrency":"EUR"},
              {"operationId":"3","operationType":"REFUND","operationResult":"REFUNDED","operationAmount":"1000","operationCurrency":"EUR"}]}
            """));

        Assert.Equal(StatoCatturaProvider.Completata, esito.Stato);
    }

    [Theory]
    [InlineData("""{"operations":[{"operationType":"AUTHORIZATION","operationResult":"PENDING"}]}""", StatoCatturaProvider.InSospeso)]
    [InlineData("""{"operations":[{"operationType":"AUTHORIZATION","operationResult":"AUTHORIZED","operationAmount":"1000"}]}""", StatoCatturaProvider.InSospeso)]
    [InlineData("""{"operations":[{"operationType":"AUTHORIZATION","operationResult":"DECLINED"}]}""", StatoCatturaProvider.Rifiutata)]
    [InlineData("""{"operations":[{"operationType":"AUTHORIZATION","operationResult":"DENIED_BY_RISK"}]}""", StatoCatturaProvider.Rifiutata)]
    [InlineData("""{"operations":[{"operationType":"AUTHORIZATION","operationResult":"THREEDS_FAILED"}]}""", StatoCatturaProvider.Rifiutata)]
    [InlineData("""{"operations":[{"operationType":"AUTHORIZATION","operationResult":"CANCELED"}]}""", StatoCatturaProvider.NonApprovata)]
    [InlineData("""{"operations":[{"operationType":"AUTHORIZATION","operationResult":"THREEDS_VALIDATED"}]}""", StatoCatturaProvider.NonApprovata)]
    [InlineData("""{"operations":[]}""", StatoCatturaProvider.NonApprovata)]
    [InlineData("""{}""", StatoCatturaProvider.NonApprovata)]
    [InlineData("""[]""", StatoCatturaProvider.NonApprovata)]
    public void LeOperazioniSiTraduconoNellEsitoDelPagamento(string json, StatoCatturaProvider atteso)
    {
        Assert.Equal(atteso, ClienteNexi.Interpreta(Json(json)).Stato);
    }

    [Fact]
    public void UnRifiutoSeguitoDaUnIncassoSulloStessoOrdineEPagato()
    {
        var esito = ClienteNexi.Interpreta(Json("""
            {"operations":[
              {"operationType":"AUTHORIZATION","operationResult":"DECLINED"},
              {"operationId":"7","operationType":"CAPTURE","operationResult":"EXECUTED","operationAmount":"500","operationCurrency":"EUR"}]}
            """));

        Assert.Equal((StatoCatturaProvider.Completata, 5m), (esito.Stato, esito.Importo));
    }

    [Fact]
    public void ISoloRimborsiEseguitiConImportoEIdentificativoValidiSiLeggonoDalleOperazioni()
    {
        var rimborsi = ClienteNexi.Rimborsi(Json("""
            {"operations":[
              {"operationId":"2","operationType":"CAPTURE","operationResult":"EXECUTED","operationAmount":"1000","operationCurrency":"EUR"},
              {"operationId":"3","operationType":"REFUND","operationResult":"REFUNDED","operationAmount":"400","operationCurrency":"EUR"},
              {"operationId":"4","operationType":"REFUND","operationResult":"PENDING","operationAmount":"100","operationCurrency":"EUR"},
              {"operationId":"5","operationType":"REFUND","operationResult":"REFUNDED","operationAmount":"0","operationCurrency":"EUR"},
              {"operationType":"REFUND","operationResult":"REFUNDED","operationAmount":"100","operationCurrency":"EUR"},
              {"operationId":"7","operationType":"REFUND","operationResult":"REFUNDED","operationAmount":"abc","operationCurrency":"EUR"}]}
            """));

        var unico = Assert.Single(rimborsi);
        Assert.Equal(("3", 4m, "EUR"), (unico.IdRimborso, unico.Importo, unico.Valuta));
        Assert.Empty(ClienteNexi.Rimborsi(Json("{}")));
    }

    [Fact]
    public async Task ImportoOValutaNonValidiNonPartonoMentreUnaPaginaNonFidataERifiutata()
    {
        var cliente = new ClienteNexi(new FabbricaFinta(new GestoreFinto(_ => new HttpResponseMessage(HttpStatusCode.OK))));

        await Assert.ThrowsAsync<ArgumentException>(() => cliente.CreaPagamentoAsync(_credenziali, "NX1-abcdef0123", Richiesta(importo: 0m), Ct));
        await Assert.ThrowsAsync<ArgumentException>(() => cliente.CreaPagamentoAsync(_credenziali, "NX1-abcdef0123", Richiesta(valuta: "USD"), Ct));
        await Assert.ThrowsAsync<ArgumentException>(() => cliente.CreaPagamentoAsync(_credenziali, "ordine con spazi!", Richiesta(), Ct));
        await Assert.ThrowsAsync<ArgumentException>(() => cliente.CreaPagamentoAsync(_credenziali, new string('A', 28), Richiesta(), Ct));

        foreach (var pagina in new[] { "http://xpay.nexigroup.com/x", "https://127.0.0.1/x", "https://user:pw@xpay.nexigroup.com/x", "https://xpay.nexigroup.com:8443/x", "https://localhost/x", "javascript:alert(1)", "https://pay.altro.example/x", "https://nexigroup.com.evil.example/x" })
        {
            var finto = new GestoreFinto(_ => Risposta(HttpStatusCode.OK, $$"""{"hostedPage":"{{pagina}}","securityToken":"t"}"""));
            await Assert.ThrowsAsync<ProviderPagamentoException>(() => new ClienteNexi(new FabbricaFinta(finto)).CreaPagamentoAsync(_credenziali, "NX1-abcdef0123", Richiesta(), Ct));
        }
    }

    [Fact]
    public async Task UnaRispostaDiErroreRiportaSoloCodiceHttpEDelGatewayMaiCorpoNeChiave()
    {
        var finto = new GestoreFinto(_ => Risposta(HttpStatusCode.BadRequest, """{"errors":[{"code":"GW0001","description":"CHIAVE-SEGRETA nel corpo"}]}"""));
        var cliente = new ClienteNexi(new FabbricaFinta(finto));

        var errore = await Assert.ThrowsAsync<ProviderPagamentoException>(() => cliente.CreaPagamentoAsync(_credenziali, "NX1-abcdef0123", Richiesta(), Ct));

        Assert.Contains("HTTP 400", errore.Message, StringComparison.Ordinal);
        Assert.Contains("GW0001", errore.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("CHIAVE-SEGRETA", errore.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("nel corpo", errore.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("CHIAVE-SEGRETA", _credenziali.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task LaLetturaDiUnOrdineSconosciutoENonApprovataEUnGuastoELancia()
    {
        var sconosciuto = new ClienteNexi(new FabbricaFinta(new GestoreFinto(_ => Risposta(HttpStatusCode.NotFound, "{}"))));
        var guasto = new ClienteNexi(new FabbricaFinta(new GestoreFinto(_ => Risposta(HttpStatusCode.InternalServerError, "<html>errore</html>"))));
        var irraggiungibile = new ClienteNexi(new FabbricaFinta(new GestoreFinto(_ => throw new HttpRequestException("rete"))));

        Assert.Equal(StatoCatturaProvider.NonApprovata, (await sconosciuto.LeggiOrdineAsync(_credenziali, "NX1-abcdef0123", Ct)).Stato);
        await Assert.ThrowsAsync<ProviderPagamentoException>(() => guasto.LeggiOrdineAsync(_credenziali, "NX1-abcdef0123", Ct));
        await Assert.ThrowsAsync<ProviderPagamentoException>(() => irraggiungibile.LeggiOrdineAsync(_credenziali, "NX1-abcdef0123", Ct));
    }

    [Fact]
    public async Task LaRichiestaVaAlGatewayGiustoPerAmbienteConChiaveECorrelazione()
    {
        var finto = new GestoreFinto(_ => Risposta(HttpStatusCode.OK, """{"hostedPage":"https://xpaysandbox.nexigroup.com/hpp/nexi/x","securityToken":"tok"}"""));
        var cliente = new ClienteNexi(new FabbricaFinta(finto));

        var creato = await cliente.CreaPagamentoAsync(_credenziali, "NX1-abcdef0123", Richiesta(), Ct);
        await cliente.CreaPagamentoAsync(new CredenzialiNexi(AmbientePagamento.Live, "K2"), "NX1-abcdef0124", Richiesta(), Ct);

        Assert.Equal("tok", creato.TokenSicurezza);
        Assert.Equal("https://xpaysandbox.nexigroup.com/api/phoenix-0.0/psp/api/v1/orders/hpp", finto.Indirizzi[0]);
        Assert.Equal("https://xpay.nexigroup.com/api/phoenix-0.0/psp/api/v1/orders/hpp", finto.Indirizzi[1]);
        Assert.Equal(["CHIAVE-SEGRETA", "K2"], finto.Chiavi);
        Assert.All(finto.Correlazioni, c => Assert.True(Guid.TryParse(c, out _)));
        Assert.DoesNotContain("tok", creato.ToString(), StringComparison.Ordinal);
    }

    private static HttpResponseMessage Risposta(HttpStatusCode stato, string corpo) => new(stato) { Content = new StringContent(corpo) };

    private sealed class GestoreFinto : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _risposta;

        public GestoreFinto(Func<HttpRequestMessage, HttpResponseMessage> risposta) => _risposta = risposta;

        public List<string> Indirizzi { get; } = [];

        public List<string?> Chiavi { get; } = [];

        public List<string?> Correlazioni { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Indirizzi.Add(request.RequestUri!.AbsoluteUri);
            Chiavi.Add(request.Headers.TryGetValues("X-API-KEY", out var k) ? k.First() : null);
            Correlazioni.Add(request.Headers.TryGetValues("Correlation-Id", out var c) ? c.First() : null);
            return Task.FromResult(_risposta(request));
        }
    }

    private sealed class FabbricaFinta : IHttpClientFactory
    {
        private readonly HttpMessageHandler _gestore;

        public FabbricaFinta(HttpMessageHandler gestore) => _gestore = gestore;

        public HttpClient CreateClient(string name) => new(_gestore, disposeHandler: false);
    }
}
