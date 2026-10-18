using DvCms.Core.Cms;
using DvCms.DTO.Cms;
using Xunit;

namespace DvCms.UnitTests.Cms;

public class ModelliPaginaTest
{
    private static readonly RegistroModelliPagina _registro = new(RegistroModelliPagina.Predefiniti);

    /// <summary>
    /// Impronte delle versioni già rilasciate. Se questo test fallisce, una versione è stata modificata: non si fa.
    /// Per cambiare un modello si aggiunge una NUOVA versione (e la sua impronta qui), lasciando intatta la vecchia.
    /// </summary>
    [Theory]
    [InlineData("testo-semplice", 1, "BF1A7CAEAE3A20E9C41FC77A989284A2B85A8945E22C6A118EA9853D861F94FD")]
    [InlineData("presentazione", 1, "8AAA6B15B624132003681A18AAE6A7D8790AD14947CD5E9AB8D4446467F153B0")]
    [InlineData("pagina-libera", 1, "AAFD0894A5EF93E37DDBA262441E051699C99166F5E7DBA29D27C66BB21FD033")]
    [InlineData("pagina-con-intestazione-fissa", 1, "FC8786F51D964A2C9EE4FCD3BF8204EDDE6DCACA4ED615B642CF0048593F64C0")]
    [InlineData("pagina-con-intestazione-fissa", 2, "07685099867871DF732EF57D436E94603DF7C142F1E7E4FB85B5214EF17E310F")]
    [InlineData("pagina-con-intestazione-fissa", 3, "F0EB5F991E35751F239EA6EFB75CF8A574B7A6378D82EA96F8D11AC98959F0AD")]
    [InlineData("pagina-con-intestazione-fissa", 4, "DAF3B3DDEE615B29B0BE98D2C7C58ABC54C2EB2C2943EE2FE2343C08FA27C8FC")]
    [InlineData("pagina-con-intestazione-fissa", 5, "04F0A362CBBDD4AC1AFEBEA006B4E382EF9148CF7AB7236F25EB96A1434E18F6")]
    [InlineData("pagina-con-intestazione-fissa", 6, "47CD3E129F691E81E5D52CBEC854EFCCDAF1F9A590B8D5B916C9D9F6C4B97350")]
    [InlineData("pagina-con-intestazione-fissa", 7, "D7FE70E605ADF275914E7D7CC0026E0302CD069543D3A2BAA7B80D9A4A0F03DE")]
    [InlineData("pagina-con-intestazione-fissa", 8, "1EB1E703D9F404BDB205D1E7CF51870FD64FB1C9CA5358C317475B53A509D8F0")]
    public void LeVersioniRilasciateSonoImmutabili(string id, int versione, string improntaAttesa)
    {
        Assert.Equal(improntaAttesa, _registro.Trova(id, versione)!.Impronta);
    }

    [Fact]
    public void TutteLeVersioniPredefiniteHannoUnaImprontaFissata()
    {
        var fissate = new HashSet<(string, int)> { ("testo-semplice", 1), ("presentazione", 1), ("pagina-libera", 1), ("pagina-con-intestazione-fissa", 1), ("pagina-con-intestazione-fissa", 2), ("pagina-con-intestazione-fissa", 3), ("pagina-con-intestazione-fissa", 4), ("pagina-con-intestazione-fissa", 5), ("pagina-con-intestazione-fissa", 6), ("pagina-con-intestazione-fissa", 7), ("pagina-con-intestazione-fissa", 8) };

        Assert.All(_registro.Tutti, modello => Assert.Contains((modello.Id, modello.Versione), fissate));
    }

    [Fact]
    public void LImprontaCambiaSeSiTocca()
    {
        var originale = RegistroModelliPagina.ModelloPresentazione;
        var modificato = originale with { Campi = [.. originale.Campi, new CampoModello("Extra", TipoCampoModello.TestoBreve, false, 10)] };
        var altraPolitica = originale with { Politica = new PoliticaEditing(false, 1) };

        Assert.NotEqual(originale.Impronta, modificato.Impronta);
        Assert.NotEqual(originale.Impronta, altraPolitica.Impronta);
    }

    [Fact]
    public void ILRegistroRifiutaDueDefinizioniConLoStessoIdEVersione()
    {
        var doppio = RegistroModelliPagina.Predefiniti.Append(RegistroModelliPagina.ModelloPresentazione with { Campi = [] });

        Assert.Throws<ArgumentException>(() => new RegistroModelliPagina(doppio));
    }

    [Fact]
    public void ILRegistroRichiedeIlModelloPredefinito()
    {
        Assert.Throws<ArgumentException>(() => new RegistroModelliPagina([RegistroModelliPagina.ModelloPresentazione]));
    }

    [Fact]
    public void LeNuovePagineUsanoLUltimaVersioneEIVecchiRestanoDisponibili()
    {
        var v2 = RegistroModelliPagina.ModelloPresentazione with { Versione = 2 };
        var registro = new RegistroModelliPagina([.. RegistroModelliPagina.Predefiniti, v2]);

        Assert.Equal(2, registro.Ultimo("presentazione")!.Versione);
        Assert.Equal(1, registro.Trova("presentazione", 1)!.Versione);
        Assert.Contains(registro.Ultimi, m => m.Id == "presentazione" && m.Versione == 2);
        Assert.DoesNotContain(registro.Ultimi, m => m.Id == "presentazione" && m.Versione == 1);
        Assert.Null(registro.Trova("presentazione", 3));
        Assert.Null(registro.Trova("inesistente", 1));
        Assert.Null(registro.Trova(null, 1));
    }

    [Fact]
    public void IlModelloSempliceNonHaCampiEAmmetteSoloContenuto()
    {
        Assert.Empty(_registro.Valida("testo-semplice", 1, null, "Testo libero"));
        Assert.Equal(["Extra"], _registro.Valida("testo-semplice", 1, new Dictionary<string, string> { ["Extra"] = "x" }, null));
    }

    [Fact]
    public void LaPresentazioneAccettaIPropriCampi()
    {
        var dati = new Dictionary<string, string>
        {
            ["Sottotitolo"] = "Un sottotitolo",
            ["EtichettaPulsante"] = "Scopri",
            ["UrlPulsante"] = "/prodotti",
        };

        Assert.Empty(_registro.Valida("presentazione", 1, dati, "Testo"));
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("JaVaScRiPt:alert(1)")]
    [InlineData("data:text/html,<script>")]
    [InlineData("//evil.example/x")]
    [InlineData("http://non-sicuro.example/x")]
    [InlineData("ftp://x.example/")]
    [InlineData("/con spazio")]
    [InlineData("/con\\backslash")]
    [InlineData("https://utente:pw@host.example/")]
    [InlineData("pagina-relativa")]
    [InlineData("mailto:a@b.it")]
    public void GliUrlPericolosiOAmbiguiVengonoRifiutati(string url)
    {
        var dati = new Dictionary<string, string> { ["EtichettaPulsante"] = "Vai", ["UrlPulsante"] = url };

        Assert.Equal(["UrlPulsante"], _registro.Valida("presentazione", 1, dati, null));
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/chi-siamo")]
    [InlineData("/en/about?x=1#a")]
    [InlineData("https://www.esempio.it/pagina")]
    public void GliUrlDelSitoOHttpsAssolutiSonoAccettati(string url)
    {
        Assert.True(RegistroModelliPagina.UrlConsentito(url));
    }

    [Fact]
    public void EtichettaEUrlDelPulsanteVannoInsiemeOPerNulla()
    {
        var soloEtichetta = new Dictionary<string, string> { ["EtichettaPulsante"] = "Vai" };
        var soloUrl = new Dictionary<string, string> { ["UrlPulsante"] = "/x" };

        Assert.Equal(["UrlPulsante"], _registro.Valida("presentazione", 1, soloEtichetta, null));
        Assert.Equal(["EtichettaPulsante"], _registro.Valida("presentazione", 1, soloUrl, null));
        Assert.Empty(_registro.Valida("presentazione", 1, new Dictionary<string, string>(), null));
    }

    [Fact]
    public void LimitiDiLunghezzaEContenutoSonoApplicati()
    {
        var troppo = new Dictionary<string, string> { ["Sottotitolo"] = new string('s', 201) };

        Assert.Equal(["Sottotitolo"], _registro.Valida("presentazione", 1, troppo, null));
        Assert.Equal(["Contenuto"], _registro.Valida("presentazione", 1, null, new string('c', 20_001)));
        Assert.Empty(_registro.Valida("presentazione", 1, null, new string('c', 20_000)));
    }

    [Fact]
    public void ModelloOVersioneSconosciutiSonoRifiutati()
    {
        Assert.Equal(["Modello"], _registro.Valida("inesistente", 1, null, null));
        Assert.Equal(["Modello"], _registro.Valida("presentazione", 9, null, null));
        Assert.Equal(["Modello"], _registro.Valida(null, 1, null, null));
    }

    [Fact]
    public void UnaPoliticaSenzaContenutoLiberoLoRifiuta()
    {
        var senzaTesto = RegistroModelliPagina.ModelloPresentazione with { Id = "solo-campi", Politica = new PoliticaEditing(false, 0) };
        var registro = new RegistroModelliPagina([.. RegistroModelliPagina.Predefiniti, senzaTesto]);

        Assert.Equal(["Contenuto"], registro.Valida("solo-campi", 1, null, "non ammesso"));
        Assert.Empty(registro.Valida("solo-campi", 1, null, "   "));
    }

    [Fact]
    public void GliErroriNonContengonoIValori()
    {
        var dati = new Dictionary<string, string> { ["UrlPulsante"] = "javascript:segretissimo()", ["EtichettaPulsante"] = "x" };

        Assert.DoesNotContain(_registro.Valida("presentazione", 1, dati, null), errore => errore.Contains("segretissimo", StringComparison.Ordinal));
    }

    [Fact]
    public void NormalizzaTogliSpaziEOmetteIVuoti()
    {
        var normalizzati = RegistroModelliPagina.Normalizza(new Dictionary<string, string> { ["A"] = "  x ", ["B"] = "   ", ["C"] = "" });

        Assert.Equal(new Dictionary<string, string> { ["A"] = "x" }, normalizzati);
        Assert.Empty(RegistroModelliPagina.Normalizza(null));
    }
}
