using DvCms.DTO.Email;
using DvCms.Emails;
using Xunit;

namespace DvCms.UnitTests.Email;

/// <summary>Catalogo e modelli di fabbrica (ADR-0025): completezza, validità e regole sui modelli critici.</summary>
public class ModelliEmailTest
{
    public static TheoryData<string, string> CodiciELingue()
    {
        var dati = new TheoryData<string, string>();
        foreach (var definizione in CatalogoModelliEmail.Definizioni)
        {
            foreach (var lingua in CatalogoModelliEmail.LingueDiFabbrica)
            {
                dati.Add(definizione.Codice, lingua);
            }
        }

        return dati;
    }

    [Fact]
    public void OgniCodiceDelCatalogoHaIlModelloDiFabbricaInItalianoEInInglese()
    {
        foreach (var definizione in CatalogoModelliEmail.Definizioni)
        {
            Assert.NotNull(CatalogoModelliEmail.Fabbrica(definizione.Codice, "it"));
            Assert.NotNull(CatalogoModelliEmail.Fabbrica(definizione.Codice, "en"));
            Assert.Null(CatalogoModelliEmail.Fabbrica(definizione.Codice, "fr"));
        }
    }

    [Fact]
    public void LEsempioDefinisceTutteLeVariabiliDelModello()
    {
        foreach (var definizione in CatalogoModelliEmail.Definizioni)
        {
            var esempio = CatalogoModelliEmail.Esempio(definizione.Codice);
            foreach (var variabile in definizione.Variabili)
            {
                Assert.True(esempio.ContainsKey(variabile.Nome), $"{definizione.Codice}: manca l'esempio di {variabile.Nome}");
            }
        }
    }

    [Theory]
    [MemberData(nameof(CodiciELingue))]
    public async Task IModelliDiFabbricaSonoValidiESiRendono(string codice, string lingua)
    {
        var definizione = CatalogoModelliEmail.Trova(codice)!;

        var anteprima = await ValidatoreModelloEmail.RendiAsync(definizione, CatalogoModelliEmail.Fabbrica(codice, lingua)!);

        Assert.True(anteprima.Validazione.Valido, string.Join(" | ", anteprima.Validazione.Errori));
        Assert.NotEmpty(anteprima.Oggetto);
        Assert.NotEmpty(anteprima.CorpoHtml);
        Assert.NotEmpty(anteprima.CorpoTesto);
    }

    [Fact]
    public void ISoliModelliConCodiceOLinkSonoCritici()
    {
        var critici = CatalogoModelliEmail.Definizioni.Where(d => d.Critico).Select(d => d.Codice).Order(StringComparer.Ordinal).ToList();

        Assert.Equal(
            new[]
            {
                CodiciModelloEmail.ConfermaRegistrazione,
                CodiciModelloEmail.InvitoAmministratore,
                CodiciModelloEmail.RecuperoCodice,
                CodiciModelloEmail.RecuperoLink,
            }.Order(StringComparer.Ordinal),
            critici);
    }

    [Fact]
    public async Task IlTestoDiFabbricaItalianoDelCodiceRiprendeLaFraseDiSempre()
    {
        var definizione = CatalogoModelliEmail.Trova(CodiciModelloEmail.RecuperoCodice)!;

        var anteprima = await ValidatoreModelloEmail.RendiAsync(definizione, CatalogoModelliEmail.Fabbrica(CodiciModelloEmail.RecuperoCodice, "it")!);

        Assert.Equal(
            "Il tuo codice di verifica è 483920. Vale 15 minuti. Se non hai chiesto tu di reimpostare la password, ignora questo messaggio.",
            anteprima.CorpoTesto);
    }

    private static ContenutoModelloEmail Link(string oggetto = "Invito", string html = "<p>{{ link }}</p>", string testo = "{{ link }}") => new(oggetto, html, testo);

    [Fact]
    public async Task UnModelloCriticoSenzaIlLinkNelloHtmlONelTestoNonEValido()
    {
        var definizione = CatalogoModelliEmail.Trova(CodiciModelloEmail.InvitoAmministratore)!;

        var senzaHtml = await ValidatoreModelloEmail.RendiAsync(definizione, Link(html: "<p>Benvenuto</p>"));
        var senzaTesto = await ValidatoreModelloEmail.RendiAsync(definizione, Link(testo: "Benvenuto"));
        var completo = await ValidatoreModelloEmail.RendiAsync(definizione, Link());

        Assert.Contains(senzaHtml.Validazione.Errori, e => e.Contains("Nel corpo HTML manca {{ link }}", StringComparison.Ordinal));
        Assert.Contains(senzaTesto.Validazione.Errori, e => e.Contains("Nel corpo testo manca {{ link }}", StringComparison.Ordinal));
        Assert.True(completo.Validazione.Valido);
    }

    [Fact]
    public async Task UnLinkDentroUnaCondizioneFalsaNonContaComePresente()
    {
        var definizione = CatalogoModelliEmail.Trova(CodiciModelloEmail.RecuperoCodice)!;

        var esito = await ValidatoreModelloEmail.RendiAsync(definizione, new ContenutoModelloEmail("Codice", "{% if false %}{{ codice }}{% endif %}", "{{ codice }}"));

        Assert.False(esito.Validazione.Valido);
    }

    [Fact]
    public async Task UnaVariabileSconosciutaESegnalata()
    {
        var definizione = CatalogoModelliEmail.Trova(CodiciModelloEmail.RecuperoNotifica)!;

        var esito = await ValidatoreModelloEmail.RendiAsync(definizione, new ContenutoModelloEmail("Avviso {{ nome }}", "<p>{{ password }}</p>", "{{ nome }}"));

        Assert.Contains(esito.Validazione.Errori, e => e.Contains("«password»", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("<img src=x onerror=alert(1)>")]
    [InlineData("<a href=\"javascript:alert(1)\">x</a>")]
    [InlineData("<iframe src=\"https://x\"></iframe>")]
    [InlineData("<form action=\"https://x\"></form>")]
    [InlineData("<style>p{}</style>")]
    public async Task GliElementiAttiviNellHtmlSonoRifiutati(string html)
    {
        var definizione = CatalogoModelliEmail.Trova(CodiciModelloEmail.RecuperoNotifica)!;

        var esito = await ValidatoreModelloEmail.RendiAsync(definizione, new ContenutoModelloEmail("Avviso", html, "Avviso"));

        Assert.False(esito.Validazione.Valido);
        Assert.Contains(esito.Validazione.Errori, e => e.Contains("non sono ammessi", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SintassiErrataVuotiELunghezzeVengonoSegnalati()
    {
        var definizione = CatalogoModelliEmail.Trova(CodiciModelloEmail.RecuperoNotifica)!;

        var sintassi = await ValidatoreModelloEmail.RendiAsync(definizione, new ContenutoModelloEmail("Avviso", "{% if %}", "ok"));
        var vuoti = await ValidatoreModelloEmail.RendiAsync(definizione, new ContenutoModelloEmail(" ", string.Empty, "ok"));
        var lungo = await ValidatoreModelloEmail.RendiAsync(definizione, new ContenutoModelloEmail(new string('a', 201), "<p>x</p>", "ok"));

        Assert.False(sintassi.Validazione.Valido);
        Assert.Contains(vuoti.Validazione.Errori, e => e.StartsWith("Oggetto", StringComparison.Ordinal));
        Assert.Contains(vuoti.Validazione.Errori, e => e.StartsWith("Corpo HTML", StringComparison.Ordinal));
        Assert.Contains(lungo.Validazione.Errori, e => e.Contains("supera", StringComparison.Ordinal));
    }

    [Fact]
    public async Task UnCicloSenzaFineNelModelloEUnErroreDiValidazioneNonUnBlocco()
    {
        var definizione = CatalogoModelliEmail.Trova(CodiciModelloEmail.RecuperoNotifica)!;

        var esito = await ValidatoreModelloEmail.RendiAsync(definizione, new ContenutoModelloEmail("Avviso", "{% for i in (1..1000000) %}x{% endfor %}", "ok"));

        Assert.False(esito.Validazione.Valido);
    }

    [Fact]
    public async Task LAnteprimaCodificaIValoriNellHtml()
    {
        var definizione = CatalogoModelliEmail.Trova(CodiciModelloEmail.RecuperoNotifica)!;

        var esito = await ValidatoreModelloEmail.RendiAsync(definizione, new ContenutoModelloEmail("Avviso", "<p>{{ nome }}</p>", "{{ nome }}"));

        Assert.True(esito.Validazione.Valido);
        Assert.Contains("Mario Rossi", esito.CorpoHtml, StringComparison.Ordinal);
    }
}
