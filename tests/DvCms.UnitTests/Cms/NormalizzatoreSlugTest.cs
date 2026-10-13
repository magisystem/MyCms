using DvCms.Extender;
using DvCms.Models.Cms;
using Xunit;

namespace DvCms.UnitTests.Cms;

public class NormalizzatoreSlugTest
{
    [Theory]
    [InlineData("chi-siamo", "chi-siamo")]
    [InlineData("  Chi-Siamo ", "chi-siamo")]
    [InlineData("pagina2", "pagina2")]
    [InlineData("2026", "2026")]
    public void AccettaESlugValidiNormalizzandoli(string ingresso, string atteso)
    {
        Assert.True(NormalizzatoreSlug.TentaNormalizza(ingresso, out var slug));
        Assert.Equal(atteso, slug);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("-iniziale")]
    [InlineData("finale-")]
    [InlineData("doppio--trattino")]
    [InlineData("con spazio")]
    [InlineData("con/barra")]
    [InlineData("file.html")]
    [InlineData("città")]
    [InlineData("under_score")]
    [InlineData("a%20b")]
    [InlineData("..")]
    public void RifiutaSlugNonValidi(string? ingresso)
    {
        Assert.False(NormalizzatoreSlug.TentaNormalizza(ingresso, out var slug));
        Assert.Equal(string.Empty, slug);
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("Admin")]
    [InlineData("lib")]
    [InlineData("errore")]
    [InlineData("webhooks")]
    [InlineData("negozio")]
    [InlineData("media")]
    public void RifiutaGliSlugRiservati(string ingresso)
    {
        Assert.False(NormalizzatoreSlug.TentaNormalizza(ingresso, out _));
    }

    [Fact]
    public void RifiutaUnoSlugTroppoLungoEAccettaIlLimite()
    {
        Assert.True(NormalizzatoreSlug.TentaNormalizza(new string('a', NormalizzatoreSlug.LunghezzaMassima), out _));
        Assert.False(NormalizzatoreSlug.TentaNormalizza(new string('a', NormalizzatoreSlug.LunghezzaMassima + 1), out _));
    }

    [Theory]
    [InlineData("Chi siamo", "chi-siamo")]
    [InlineData("  La nostra città! ", "la-nostra-citta")]
    [InlineData("Caffè & Co. — 2026", "caffe-co-2026")]
    [InlineData("---", "")]
    [InlineData(null, "")]
    public void PropostaDaTitolo(string? titolo, string atteso)
    {
        Assert.Equal(atteso, NormalizzatoreSlug.DaTitolo(titolo));
    }
}
