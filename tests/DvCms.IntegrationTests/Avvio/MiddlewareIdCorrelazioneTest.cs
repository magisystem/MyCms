using DvCms.IntegrationTests.Supporto;
using DvCms.Web.Middleware;
using Xunit;

namespace DvCms.IntegrationTests.Avvio;

public class MiddlewareIdCorrelazioneTest : IClassFixture<FabbricaApplicazioneSenzaDatabase>
{
    private readonly FabbricaApplicazioneSenzaDatabase _fabbrica;

    public MiddlewareIdCorrelazioneTest(FabbricaApplicazioneSenzaDatabase fabbrica)
    {
        _fabbrica = fabbrica;
    }

    [Fact]
    public async Task GeneraIdentificatoreQuandoManca()
    {
        using var client = _fabbrica.CreateClient();

        using var risposta = await client.GetAsync("/", TestContext.Current.CancellationToken);

        var valori = risposta.Headers.GetValues(MiddlewareIdCorrelazione.NomeIntestazione).ToList();
        Assert.Single(valori);
        Assert.Matches("^[0-9a-f]{32}$", valori[0]);
    }

    [Fact]
    public async Task RestituisceIdentificatoreRicevutoSeValido()
    {
        using var client = _fabbrica.CreateClient();
        using var richiesta = new HttpRequestMessage(HttpMethod.Get, "/");
        richiesta.Headers.Add(MiddlewareIdCorrelazione.NomeIntestazione, "richiesta-123_ABC");

        using var risposta = await client.SendAsync(richiesta, TestContext.Current.CancellationToken);

        Assert.Equal(
            "richiesta-123_ABC",
            Assert.Single(risposta.Headers.GetValues(MiddlewareIdCorrelazione.NomeIntestazione)));
    }

    [Theory]
    [InlineData("valore con spazi")]
    [InlineData("riga1;riga2")]
    [InlineData("<script>x</script>")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public async Task SostituisceIdentificatoreNonSicuro(string ricevuto)
    {
        using var client = _fabbrica.CreateClient();
        using var richiesta = new HttpRequestMessage(HttpMethod.Get, "/");
        richiesta.Headers.TryAddWithoutValidation(MiddlewareIdCorrelazione.NomeIntestazione, ricevuto);

        using var risposta = await client.SendAsync(richiesta, TestContext.Current.CancellationToken);

        var restituito = Assert.Single(risposta.Headers.GetValues(MiddlewareIdCorrelazione.NomeIntestazione));
        Assert.NotEqual(ricevuto, restituito);
        Assert.Matches("^[0-9a-f]{32}$", restituito);
    }
}
