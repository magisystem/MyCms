using System.Net;
using DvCms.Web.Accessi;
using Xunit;

namespace DvCms.IntegrationTests.Web;

public class ChiaveIndirizzoIpTest
{
    [Fact]
    public void IPv4EInvariato()
    {
        Assert.Equal("203.0.113.7", ChiaveIndirizzoIp.Calcola(IPAddress.Parse("203.0.113.7")));
        Assert.Equal("203.0.113.8", ChiaveIndirizzoIp.Calcola(IPAddress.Parse("203.0.113.8")));
    }

    [Fact]
    public void UnIPv4IncapsulatoInIPv6ValeComeIPv4()
    {
        Assert.Equal("203.0.113.7", ChiaveIndirizzoIp.Calcola(IPAddress.Parse("::ffff:203.0.113.7")));
    }

    [Fact]
    public void GliIndirizziDellaStessaReteSlash64HannoLaStessaChiave()
    {
        var a = ChiaveIndirizzoIp.Calcola(IPAddress.Parse("2001:db8:1:2:aaaa:bbbb:cccc:dddd"));
        var b = ChiaveIndirizzoIp.Calcola(IPAddress.Parse("2001:db8:1:2:1:2:3:4"));
        var altraRete = ChiaveIndirizzoIp.Calcola(IPAddress.Parse("2001:db8:1:3:aaaa:bbbb:cccc:dddd"));

        Assert.Equal("2001:db8:1:2::/64", a);
        Assert.Equal(a, b);
        Assert.NotEqual(a, altraRete);
    }

    [Fact]
    public void UnIndirizzoAssenteHaUnaChiaveComune()
    {
        Assert.Equal(ChiaveIndirizzoIp.Sconosciuto, ChiaveIndirizzoIp.Calcola(null));
    }

    [Fact]
    public void LaChiaveEntraNelCampoDelDatabase()
    {
        // IndirizzoIp ha 45 caratteri: la chiave più lunga possibile deve starci.
        Assert.True(ChiaveIndirizzoIp.Calcola(IPAddress.Parse("ffff:ffff:ffff:ffff:ffff:ffff:ffff:ffff")).Length <= 45);
    }
}
