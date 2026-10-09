using System.Net;
using System.Text.RegularExpressions;
using DvCms.IntegrationTests.Supporto;
using Xunit;

namespace DvCms.IntegrationTests.Layout;

/// <summary>
/// Tema dell'admin (ADR-0013): asset di Tabler e Tabler Icons serviti dal sito, pagine demo non pubbliche,
/// nessuna risorsa esterna a runtime.
/// </summary>
public partial class RisorseStaticheETemaTest : IClassFixture<FabbricaApplicazioneSenzaDatabase>
{
    private readonly FabbricaApplicazioneSenzaDatabase _fabbrica;

    public RisorseStaticheETemaTest(FabbricaApplicazioneSenzaDatabase fabbrica)
    {
        _fabbrica = fabbrica;
    }

    [Theory]
    [InlineData("/lib/tabler/dashboard/dist/css/tabler.min.css", "text/css")]
    [InlineData("/lib/tabler/dashboard/dist/js/tabler.min.js", "text/javascript")]
    [InlineData("/lib/tabler-icons/webfont/tabler-icons.min.css", "text/css")]
    [InlineData("/lib/tabler-icons/webfont/fonts/tabler-icons.woff2", "font/woff2")]
    public async Task AssetDelTemaSonoServiti(string percorso, string tipoAtteso)
    {
        using var client = _fabbrica.CreateClient();

        using var risposta = await client.GetAsync(percorso, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, risposta.StatusCode);
        Assert.Contains(tipoAtteso, risposta.Content.Headers.ContentType?.MediaType ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("/lib/tabler/dashboard/index.html")]
    [InlineData("/lib/tabler/dashboard/layout-vertical.html")]
    [InlineData("/lib/tabler/dashboard/sign-in.html")]
    [InlineData("/lib/tabler/documentation.url")]
    [InlineData("/lib/tabler-icons/webfont/tabler-icons.html")]
    [InlineData("/lib/tabler-icons/tabler-icons.png.pdf")]
    [InlineData("/LIB/tabler/dashboard/index.html")]
    public async Task PagineDemoEFormatiNonConsentitiRispondono404(string percorso)
    {
        using var client = _fabbrica.CreateClient();

        using var risposta = await client.GetAsync(percorso, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, risposta.StatusCode);
    }

    [Fact]
    public async Task LayoutAdminUsaSoloRisorseLocaliEDichiaraLaLingua()
    {
        using var client = _fabbrica.CreateClient();

        using var risposta = await client.GetAsync("/admin", TestContext.Current.CancellationToken);
        var html = await risposta.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, risposta.StatusCode);
        Assert.Contains("<html lang=\"it\"", html, StringComparison.Ordinal);
        Assert.Contains("/lib/tabler/dashboard/dist/css/tabler.min.css", html, StringComparison.Ordinal);
        Assert.Contains("/lib/tabler-icons/webfont/tabler-icons.min.css", html, StringComparison.Ordinal);
        Assert.Contains("navbar-vertical", html, StringComparison.Ordinal);
        Assert.Contains("Cruscotto", html, StringComparison.Ordinal);
        Assert.DoesNotMatch(RiferimentoEsterno(), html);
    }

    [GeneratedRegex(@"(?:src|href)\s*=\s*[""']\s*(?:https?:)?//", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RiferimentoEsterno();
}
