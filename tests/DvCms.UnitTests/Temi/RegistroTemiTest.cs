using DvCms.Application.Temi;
using Xunit;

namespace DvCms.UnitTests.Temi;

public class RegistroTemiTest
{
    private static readonly RegistroTemi _registro = new(RegistroTemi.Predefiniti);

    /// <summary>
    /// Impronte delle versioni già rilasciate. Se questo test fallisce, una versione è stata modificata: non si fa.
    /// Per cambiare un tema si aggiunge una NUOVA versione (e la sua impronta qui), lasciando intatta la vecchia.
    /// </summary>
    [Theory]
    [InlineData("classico", 1, "3E4195876AB7B70481B2B378D45EFD2DB115A095F47E37A8BD6DEFCF2CE6114A")]
    [InlineData("moderno", 1, "6B008F00FC130247E80DC35D720870C3E8EF1507FB37BC9BB5AF4BB2A0C8A644")]
    public void LeVersioniRilasciateSonoImmutabili(string chiave, int versione, string improntaAttesa)
    {
        Assert.Equal(improntaAttesa, _registro.Trova(chiave, versione)!.Impronta);
    }

    [Fact]
    public void TutteLeVersioniPredefiniteHannoUnaImprontaFissata()
    {
        var fissate = new HashSet<(string, int)> { ("classico", 1), ("moderno", 1) };

        Assert.All(_registro.Tutti, tema => Assert.Contains((tema.Chiave, tema.Versione), fissate));
    }

    [Fact]
    public void LImprontaCambiaSeSiTocca()
    {
        var originale = RegistroTemi.TemaModerno;

        Assert.NotEqual(originale.Impronta, (originale with { Campi = [.. originale.Campi, new CampoTema("Extra", TipoCampoTema.Testo, "", 10)] }).Impronta);
        Assert.NotEqual(originale.Impronta, (originale with { Versione = 2 }).Impronta);
    }

    [Fact]
    public void IlRegistroRifiutaDefinizioniDuplicateNonValideOSenzaTemaPredefinito()
    {
        Assert.Throws<ArgumentException>(() => new RegistroTemi([.. RegistroTemi.Predefiniti, RegistroTemi.TemaModerno]));
        Assert.Throws<ArgumentException>(() => new RegistroTemi([RegistroTemi.TemaModerno]));
        Assert.Throws<ArgumentException>(() => new RegistroTemi([.. RegistroTemi.Predefiniti, new DefinizioneTema("../x", 1, [])]));
        Assert.Throws<ArgumentException>(() => new RegistroTemi([.. RegistroTemi.Predefiniti, new DefinizioneTema("altro", 0, [])]));
        Assert.Throws<ArgumentException>(() => new RegistroTemi([.. RegistroTemi.Predefiniti, new DefinizioneTema("altro", 1, [new CampoTema("Colore", TipoCampoTema.Colore, "rosso")])]));
        Assert.Throws<ArgumentException>(() => new RegistroTemi([.. RegistroTemi.Predefiniti, new DefinizioneTema("altro", 1, [new CampoTema("a-b", TipoCampoTema.Testo, "", 5)])]));
        Assert.Throws<ArgumentException>(() => new RegistroTemi([.. RegistroTemi.Predefiniti, new DefinizioneTema("altro", 1, [new CampoTema("Scelta", TipoCampoTema.Scelta, "")])]));
    }

    [Fact]
    public void LeNuoveScelteUsanoLUltimaVersioneEIVecchiRestanoDisponibili()
    {
        var v2 = RegistroTemi.TemaModerno with { Versione = 2 };
        var registro = new RegistroTemi([.. RegistroTemi.Predefiniti, v2]);

        Assert.Equal(2, registro.Ultimo("moderno")!.Versione);
        Assert.Equal(1, registro.Trova("moderno", 1)!.Versione);
        Assert.Null(registro.Trova("moderno", 3));
        Assert.Null(registro.Trova("inesistente", 1));
        Assert.Null(registro.Trova(null, 1));
        Assert.Contains(registro.Ultimi, t => t.Chiave == "moderno" && t.Versione == 2);
        Assert.DoesNotContain(registro.Ultimi, t => t.Chiave == "moderno" && t.Versione == 1);
    }

    [Fact]
    public void IlTemaPredefinitoEIlClassico()
    {
        Assert.Equal("classico", _registro.Predefinito.Chiave);
    }

    [Theory]
    [InlineData("ColorePrimario", "#1F4E8C", true)]
    [InlineData("ColorePrimario", "#fff", false)]
    [InlineData("ColorePrimario", "rosso", false)]
    [InlineData("ColorePrimario", "#1f4e8c;background:url(x)", false)]
    [InlineData("ColorePrimario", "#gggggg", false)]
    [InlineData("Larghezza", "Larga", true)]
    [InlineData("Larghezza", "Enorme", false)]
    [InlineData("TestoPiePagina", "Tutti i diritti riservati", true)]
    [InlineData("TestoPiePagina", "riga\nsu due", false)]
    public void IValoriSonoValidatiControLoSchemaDelTema(string campo, string valore, bool valido)
    {
        var errori = _registro.Valida("moderno", 1, new Dictionary<string, string> { [campo] = valore });

        Assert.Equal(valido ? [] : [campo], errori);
    }

    [Fact]
    public void ITestiTroppoLunghiEICampiNonPrevistiSonoRifiutati()
    {
        Assert.Equal(["TestoPiePagina"], _registro.Valida("classico", 1, new Dictionary<string, string> { ["TestoPiePagina"] = new string('x', 201) }));
        Assert.Equal(["ColoreIntestazione"], _registro.Valida("classico", 1, new Dictionary<string, string> { ["ColoreIntestazione"] = "#000000" }));
        Assert.Equal(["Tema"], _registro.Valida("sconosciuto", 1, null));
        Assert.Equal(["Tema"], _registro.Valida("classico", 9, null));
        Assert.Empty(_registro.Valida("classico", 1, null));
    }

    [Fact]
    public void GliErroriNonContengonoIValori()
    {
        var errori = _registro.Valida("classico", 1, new Dictionary<string, string> { ["ColorePrimario"] = "segretissimo", ["Altro<script>"] = "x" });

        Assert.DoesNotContain(errori, errore => errore.Contains("segretissimo", StringComparison.Ordinal));
    }

    [Fact]
    public void LaNormalizzazioneOmetteVuotiEPredefinitiETieneIColoriInMinuscolo()
    {
        var normalizzati = _registro.Normalizza(
            "moderno",
            1,
            new Dictionary<string, string>
            {
                ["ColorePrimario"] = " #AA00BB ",
                ["ColoreIntestazione"] = "#111827",
                ["Larghezza"] = "",
                ["TestoPiePagina"] = "  Piè  ",
            });

        Assert.Equal(new Dictionary<string, string> { ["ColorePrimario"] = "#aa00bb", ["TestoPiePagina"] = "Piè" }, normalizzati);
    }

    [Fact]
    public void GliEffettiviUnisconoPredefinitiESalvatiIgnorandoQuelliNonPiuValidi()
    {
        var effettivi = RegistroTemi.Effettivi(
            RegistroTemi.TemaModerno,
            new Dictionary<string, string> { ["ColorePrimario"] = "#aa00bb", ["Larghezza"] = "Enorme", ["Sparito"] = "x" });

        Assert.Equal("#aa00bb", effettivi["ColorePrimario"]);
        Assert.Equal("Stretta", effettivi["Larghezza"]);
        Assert.Equal("#111827", effettivi["ColoreIntestazione"]);
        Assert.False(effettivi.ContainsKey("Sparito"));
    }

    [Fact]
    public void LeVariabiliCssContengonoSoloColoriValidati()
    {
        var effettivi = new Dictionary<string, string>
        {
            ["ColorePrimario"] = "#AA00BB",
            ["ColoreIntestazione"] = "#000000;} body{display:none",
            ["Larghezza"] = "Larga",
            ["TestoPiePagina"] = "x",
        };

        var css = RegistroTemi.VariabiliCss(RegistroTemi.TemaModerno, effettivi);

        Assert.Equal("--tema-colore-primario:#aa00bb;", css);
    }
}
