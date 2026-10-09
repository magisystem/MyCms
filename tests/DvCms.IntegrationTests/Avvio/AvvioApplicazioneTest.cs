using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace DvCms.IntegrationTests.Avvio;

/// <summary>
/// Prova di avvio (smoke test): l'applicazione web parte e risponde sulla pagina iniziale.
/// </summary>
public class AvvioApplicazioneTest : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _fabbrica;

    public AvvioApplicazioneTest(WebApplicationFactory<Program> fabbrica)
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
}
