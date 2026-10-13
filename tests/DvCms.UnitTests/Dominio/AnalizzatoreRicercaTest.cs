using DvCms.Models.Negozio;
using Xunit;

namespace DvCms.UnitTests.Dominio;

/// <summary>Analisi del testo per la ricerca nel catalogo (M7.24): termini, accenti, parole vuote, radici e codici articolo.</summary>
public class AnalizzatoreRicercaTest
{
    [Theory]
    [InlineData("Perché", "perche")]
    [InlineData("CAFFÈ", "caffe")]
    [InlineData("Ünïcode", "unicode")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void NormalizzaTogliendoAccentiEMaiuscole(string? testo, string atteso)
    {
        Assert.Equal(atteso, AnalizzatoreRicerca.Normalizza(testo));
    }

    [Theory]
    [InlineData("Maglietta di cotone", "it", new[] { "magliett", "coton" })]
    [InlineData("Magliette", "it", new[] { "magliett" })]
    [InlineData("Scarpe e scarpa", "it", new[] { "scarp" })]
    [InlineData("La camicia", "it", new[] { "camici" })]
    [InlineData("Cotton shirts and dresses", "en", new[] { "cotton", "shirt", "dress" })]
    [InlineData("Berries", "en", new[] { "berry" })]
    [InlineData("Boxes", "en", new[] { "box" })]
    [InlineData("Glass", "en", new[] { "glass" })]
    [InlineData("Maglietta", "de", new[] { "maglietta" })]
    [InlineData("Maglietta", null, new[] { "maglietta" })]
    public void ScomponeEriduceAllaRadiceSecondoLaLingua(string testo, string? lingua, string[] attesi)
    {
        Assert.Equal(attesi, AnalizzatoreRicerca.Termini(testo, lingua));
    }

    [Fact]
    public void ParoleVuoteBrevitaDuplicatiESimboliNonDiventanoTermini()
    {
        Assert.Equal(["libr", "ross"], AnalizzatoreRicerca.Termini("Il libro, un LIBRO! rosso / a 5 -", "it-IT"));
        Assert.Empty(AnalizzatoreRicerca.Termini("il la di", "it"));
        Assert.Empty(AnalizzatoreRicerca.Termini("   ", "it"));
    }

    [Fact]
    public void ISoloNumeriNonSiTroncanoELaRadiceHaUnLimite()
    {
        Assert.Equal(["2026", "12345"], AnalizzatoreRicerca.Termini("2026 12345", "it"));
        var lungo = new string('a', 80);
        Assert.Equal(AnalizzatoreRicerca.LunghezzaMassima, Assert.Single(AnalizzatoreRicerca.Termini(lungo, "de")).Length);
    }

    [Fact]
    public void IlMassimoDeiTerminiLimitaAnchePerLaDescrizione()
    {
        var testo = string.Join(' ', Enumerable.Range(100, 400).Select(n => "parola" + n));

        Assert.Equal(AnalizzatoreRicerca.MassimoTerminiDescrizione, AnalizzatoreRicerca.Termini(testo, "de", AnalizzatoreRicerca.MassimoTerminiDescrizione).Count);
        Assert.Equal(AnalizzatoreRicerca.MassimoTerminiRicerca, AnalizzatoreRicerca.TerminiRicerca(testo, "de").Count);
    }

    [Fact]
    public void UnaRicercaDiSoleParoleVuoteLeTieneComunque()
    {
        Assert.Equal(["il"], AnalizzatoreRicerca.TerminiRicerca("il", "it"));
        Assert.Equal(["libr"], AnalizzatoreRicerca.TerminiRicerca("il libro", "it"));
        Assert.Empty(AnalizzatoreRicerca.TerminiRicerca("a", "it"));
    }

    [Fact]
    public void UnaParolaConIlTrattinoDaAncheLaFormaUnitaEInRicercaValeSoloQuella()
    {
        Assert.Equal(["tshirt", "shirt", "ross"], AnalizzatoreRicerca.Termini("T-shirt rossa", "it"));
        Assert.Equal(["tshirt"], AnalizzatoreRicerca.TerminiRicerca("t-shirt", "it"));
        Assert.Equal(["lamb"], AnalizzatoreRicerca.TerminiRicerca("lam-b", "it"));
        Assert.Equal(["tsm001", "coton"], AnalizzatoreRicerca.TerminiRicerca("TS-M-001 cotone", "it"));
    }

    [Theory]
    [InlineData("TS-M-001", new[] { "ts", "m", "001", "tsm001" })]
    [InlineData("pen_2", new[] { "pen", "2", "pen2" })]
    [InlineData("QUA100", new[] { "qua100" })]
    [InlineData("x", new string[0])]
    [InlineData(null, new string[0])]
    public void IlCodiceArticoloDiventaLeSueParti(string? codice, string[] attesi)
    {
        Assert.Equal(attesi, AnalizzatoreRicerca.TerminiCodice(codice));
    }
}
