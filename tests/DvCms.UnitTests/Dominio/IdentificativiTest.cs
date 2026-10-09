using DvCms.Domain;
using Xunit;

namespace DvCms.UnitTests.Dominio;

public class IdentificativiTest
{
    [Fact]
    public void NuovoRestituisceUnGuidVersione7()
    {
        var id = Identificativi.Nuovo(TimeProvider.System);

        Assert.Equal(7, id.Version);
    }

    [Fact]
    public void IdentificativiSonoOrdinatiNelTempo()
    {
        var orologio = new OrologioFisso(new DateTimeOffset(2026, 10, 9, 12, 0, 0, TimeSpan.Zero));
        var primo = Identificativi.Nuovo(orologio);

        orologio.Avanza(TimeSpan.FromSeconds(5));
        var secondo = Identificativi.Nuovo(orologio);

        Assert.True(primo.CompareTo(secondo) < 0 || string.CompareOrdinal(primo.ToString(), secondo.ToString()) < 0);
        Assert.NotEqual(primo, secondo);
    }

    [Fact]
    public void NuovoRifiutaOrologioNullo()
    {
        Assert.Throws<ArgumentNullException>(() => Identificativi.Nuovo(null!));
    }

    private sealed class OrologioFisso : TimeProvider
    {
        private DateTimeOffset _adesso;

        public OrologioFisso(DateTimeOffset adesso)
        {
            _adesso = adesso;
        }

        public void Avanza(TimeSpan intervallo) => _adesso += intervallo;

        public override DateTimeOffset GetUtcNow() => _adesso;
    }
}
