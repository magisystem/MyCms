using DvCms.Extender;
using DvCms.Models.Tenancy;
using Xunit;

namespace DvCms.UnitTests.Tenancy;

public class NormalizzatoreHostTest
{
    [Theory]
    [InlineData("cms.davidemartelli.dev", "cms.davidemartelli.dev")]
    [InlineData("CMS.DavideMartelli.DEV", "cms.davidemartelli.dev")]
    [InlineData("cms1.davidemartelli.dev:5000", "cms1.davidemartelli.dev")]
    [InlineData("  cms.davidemartelli.dev.  ", "cms.davidemartelli.dev")]
    [InlineData("localhost", "localhost")]
    [InlineData("LOCALHOST:5123", "localhost")]
    [InlineData("a-b.example.com", "a-b.example.com")]
    [InlineData("città.example.com", "xn--citt-3na.example.com")]
    public void NormalizzaHostValidi(string ingresso, string atteso)
    {
        Assert.True(NormalizzatoreHost.TentaNormalizza(ingresso, out var normalizzato));
        Assert.Equal(atteso, normalizzato);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(".")]
    [InlineData("-esempio.com")]
    [InlineData("esempio-.com")]
    [InlineData("esem pio.com")]
    [InlineData("esempio..com")]
    [InlineData("esempio.com:abc")]
    [InlineData("esempio.com:80:80")]
    [InlineData("[::1]")]
    [InlineData("::1")]
    [InlineData("192.168.1.10")]
    [InlineData("exa_mple.com")]
    [InlineData("a/b.example.com")]
    [InlineData("https://esempio.com")]
    [InlineData("esempio.com\r\nX: y")]
    public void RifiutaHostNonValidi(string? ingresso)
    {
        Assert.False(NormalizzatoreHost.TentaNormalizza(ingresso, out var normalizzato));
        Assert.Equal(string.Empty, normalizzato);
    }

    [Fact]
    public void RifiutaEtichettaTroppoLunga()
    {
        Assert.False(NormalizzatoreHost.TentaNormalizza(new string('a', 64) + ".com", out _));
        Assert.True(NormalizzatoreHost.TentaNormalizza(new string('a', 63) + ".com", out _));
    }

    [Fact]
    public void RifiutaHostTroppoLungo()
    {
        var host = string.Join('.', Enumerable.Repeat(new string('a', 60), 5));

        Assert.False(NormalizzatoreHost.TentaNormalizza(host, out _));
    }

    [Fact]
    public void MessaggioNonContieneCaratteriDiControllo()
    {
        Assert.Equal("a?b", NormalizzatoreHost.PerMessaggio("a\nb"));
    }
}
