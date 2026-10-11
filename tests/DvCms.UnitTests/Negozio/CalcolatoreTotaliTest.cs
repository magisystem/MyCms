using DvCms.Application.Negozio;
using DvCms.Domain.Negozio;
using Xunit;

namespace DvCms.UnitTests.Negozio;

/// <summary>Regole di calcolo dei totali (F-002, ADR-0020): IVA scorporata per riga, arrotondamento al centesimo, nessuna deriva.</summary>
public class CalcolatoreTotaliTest
{
    [Theory]
    [InlineData("19.90", 1, "22", "19.90", "16.31", "3.59")]
    [InlineData("10.00", 3, "22", "30.00", "24.59", "5.41")]
    [InlineData("5.20", 1, "4", "5.20", "5.00", "0.20")]
    [InlineData("11.00", 1, "10", "11.00", "10.00", "1.00")]
    [InlineData("12.34", 2, "0", "24.68", "24.68", "0.00")]
    [InlineData("0.01", 1, "22", "0.01", "0.01", "0.00")]
    [InlineData("1000000.00", 99, "22", "99000000.00", "81147540.98", "17852459.02")]
    public void LoScorporoDellIvaVieneArrotondatoAlCentesimoEBilancia(string prezzo, int quantita, string aliquota, string lordo, string imponibile, string iva)
    {
        var riga = CalcolatoreTotali.CalcolaRiga(D(prezzo), quantita, D(aliquota));

        Assert.Equal((D(lordo), D(imponibile), D(iva)), (riga.Lordo, riga.Imponibile, riga.Iva));
    }

    [Fact]
    public void PerOgniPrezzoAliquotaEQuantitaImponibileEIvaFannoIlLordoELImponibileEVicinoAlValoreEsatto()
    {
        foreach (var aliquota in AliquoteIva.Ammesse)
        {
            for (var centesimi = 1; centesimi <= 20_000; centesimi += 7)
            {
                var prezzo = centesimi / 100m;
                foreach (var quantita in new[] { 1, 2, 3, 7, 99 })
                {
                    var riga = CalcolatoreTotali.CalcolaRiga(prezzo, quantita, aliquota);
                    var esatto = riga.Lordo * 100m / (100m + aliquota);

                    Assert.Equal(riga.Lordo, riga.Imponibile + riga.Iva);
                    Assert.True(riga.Iva >= 0 && riga.Imponibile > 0 || riga.Lordo == 0.01m);
                    Assert.True(Math.Abs(riga.Imponibile - esatto) <= 0.005m, $"{prezzo} x {quantita} @ {aliquota}%");
                    Assert.Equal(riga.Imponibile, decimal.Round(riga.Imponibile, 2));
                }
            }
        }
    }

    [Fact]
    public void ITotaliSonoLaSommaDelleRigheENonUnNuovoScorporoSulTotale()
    {
        // I totali sono la somma delle righe (ognuna già arrotondata al centesimo), non un nuovo scorporo sul totale lordo.
        var righe = new[]
        {
            CalcolatoreTotali.CalcolaRiga(1.00m, 1, 22m),
            CalcolatoreTotali.CalcolaRiga(1.00m, 1, 22m),
            CalcolatoreTotali.CalcolaRiga(0.60m, 1, 22m),
            CalcolatoreTotali.CalcolaRiga(0.60m, 1, 22m),
        };

        var totali = CalcolatoreTotali.Somma(righe);

        Assert.Equal(righe.Sum(r => r.Imponibile), totali.Imponibile);
        Assert.Equal(righe.Sum(r => r.Iva), totali.Iva);
        Assert.Equal(totali.Lordo, totali.Imponibile + totali.Iva);
        Assert.Equal(3.20m, totali.Lordo);
        Assert.Equal([0.82m, 0.82m, 0.49m, 0.49m], totali.Righe.Select(r => r.Imponibile));
    }

    [Theory]
    [InlineData("0", 1, "22")]
    [InlineData("-1", 1, "22")]
    [InlineData("10.005", 1, "22")]
    [InlineData("10", 0, "22")]
    [InlineData("10", 100, "22")]
    [InlineData("10", 1, "21")]
    public void ValoriNonValidiSonoRifiutati(string prezzo, int quantita, string aliquota) =>
        Assert.ThrowsAny<ArgumentOutOfRangeException>(() => CalcolatoreTotali.CalcolaRiga(D(prezzo), quantita, D(aliquota)));

    [Fact]
    public void UnaListaVuotaDaTotaleZero()
    {
        var totali = CalcolatoreTotali.Somma([]);

        Assert.Equal((0m, 0m, 0m), (totali.Lordo, totali.Imponibile, totali.Iva));
        Assert.Empty(totali.Righe);
    }

    private static decimal D(string testo) => decimal.Parse(testo, System.Globalization.CultureInfo.InvariantCulture);
}
