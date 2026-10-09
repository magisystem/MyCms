using System.Net;
using DvCms.IntegrationTests.Supporto;
using Xunit;

namespace DvCms.IntegrationTests.Avvio;

/// <summary>
/// Prova di avvio (smoke test): l'applicazione web parte e risponde sulla pagina iniziale.
/// </summary>
public class AvvioApplicazioneTest : IClassFixture<FabbricaApplicazioneSenzaDatabase>
{
    private readonly FabbricaApplicazioneSenzaDatabase _fabbrica;

    public AvvioApplicazioneTest(FabbricaApplicazioneSenzaDatabase fabbrica)
    {
        _fabbrica = fabbrica;
    }

    [Fact]
    public async Task PaginaInizialeRispondeConSuccesso()
    {
        using var client = _fabbrica.CreateClient();

        using var risposta = await client.GetAsync("/", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, risposta.StatusCode);
    }

    [Fact]
    public void AvvioFallisceSenzaStringaDiConnessione()
    {
        using var fabbrica = new FabbricaApplicazioneConConnessione(string.Empty);

        var eccezione = Assert.ThrowsAny<Exception>(() => fabbrica.CreateClient());

        Assert.Contains("ConnectionStrings__Predefinita", eccezione.ToString(), StringComparison.Ordinal);
    }
}
