using DvCms.Emails;
using Xunit;

namespace DvCms.UnitTests.Email;

/// <summary>Motore dei modelli email (ADR-0025): sostituzione dei valori, codifica HTML, limiti e rifiuto di tutto ciò che somiglia a codice.</summary>
public class MotoreModelliEmailTest
{
    private static readonly IReadOnlyDictionary<string, object?> _nessuna = new Dictionary<string, object?>();

    private static Dictionary<string, object?> Valori(params (string Nome, object? Valore)[] coppie) =>
        coppie.ToDictionary(c => c.Nome, c => c.Valore, StringComparer.Ordinal);

    [Fact]
    public async Task SostituisceLeVariabili()
    {
        var reso = await MotoreModelliEmail.RenderizzaAsync("Ciao {{ nome }}, il codice è {{ codice }}.", Valori(("nome", "Anna"), ("codice", 123456)), html: false);

        Assert.Equal("Ciao Anna, il codice è 123456.", reso);
    }

    [Fact]
    public async Task NelHtmlICaratteriSpecialiVengonoCodificati()
    {
        var reso = await MotoreModelliEmail.RenderizzaAsync("<p>{{ nome }}</p>", Valori(("nome", "<script>alert(1)</script> & \"x\"")), html: true);

        Assert.DoesNotContain("<script>", reso, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;", reso, StringComparison.Ordinal);
        Assert.Contains("&amp;", reso, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NelTestoICaratteriNonVengonoCodificati()
    {
        var reso = await MotoreModelliEmail.RenderizzaAsync("{{ nome }}", Valori(("nome", "Rossi & Figli <srl>")), html: false);

        Assert.Equal("Rossi & Figli <srl>", reso);
    }

    [Fact]
    public async Task LeggeElenchiEDizionariAnnidati()
    {
        var righe = new List<object?>
        {
            new Dictionary<string, object?> { ["nome"] = "Penna", ["quantita"] = 2 },
            new Dictionary<string, object?> { ["nome"] = "Quaderno", ["quantita"] = 1 },
        };

        var reso = await MotoreModelliEmail.RenderizzaAsync("{% for r in righe %}{{ r.nome }} x{{ r.quantita }};{% endfor %}", Valori(("righe", righe)), html: false);

        Assert.Equal("Penna x2;Quaderno x1;", reso);
    }

    [Fact]
    public async Task LeCondizioniUsanoISiNoEIValoriVuoti()
    {
        const string Modello = "{% if ritiro %}ritiro{% else %}consegna{% endif %}|{% if sconto %}sconto {{ sconto }}{% endif %}";

        Assert.Equal("ritiro|", await MotoreModelliEmail.RenderizzaAsync(Modello, Valori(("ritiro", true), ("sconto", null)), html: false));
        Assert.Equal("consegna|sconto 5", await MotoreModelliEmail.RenderizzaAsync(Modello, Valori(("ritiro", false), ("sconto", "5")), html: false));
    }

    [Fact]
    public async Task ITestiSuPiuRigheDiventanoAcapoNelHtmlSenzaPerdereLaCodifica()
    {
        var reso = await MotoreModelliEmail.RenderizzaAsync("{{ istruzioni | escape | newline_to_br | raw }}", Valori(("istruzioni", "Riga <1>\nRiga 2")), html: true);

        Assert.Contains("Riga &lt;1&gt;", reso, StringComparison.Ordinal);
        Assert.Contains("<br", reso, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RaccogliLeVariabiliNonFornite()
    {
        var indefinite = new List<string>();

        var reso = await MotoreModelliEmail.RenderizzaAsync("{{ nome }} {{ sconosciuta }}", Valori(("nome", "Anna")), html: false, indefinite);

        Assert.Equal("Anna ", reso);
        Assert.Equal(["sconosciuta"], indefinite);
    }

    [Fact]
    public async Task UnCicloSenzaFineSiFerma()
    {
        var eccezione = await Assert.ThrowsAsync<InvalidOperationException>(
            () => MotoreModelliEmail.RenderizzaAsync("{% for i in (1..1000000) %}{{ i }}{% endfor %}", _nessuna, html: false));

        Assert.Contains("non si è potuto rendere", eccezione.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NonSiPossonoIncludereFile()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => MotoreModelliEmail.RenderizzaAsync("{% include 'segreto' %}", _nessuna, html: false));
    }

    [Fact]
    public async Task NonSiLeggonoMembriDiOggettiDotNet()
    {
        // I valori ammessi sono solo testi, numeri, sì/no, elenchi e dizionari: un oggetto qualunque è rifiutato prima di arrivare al modello.
        await Assert.ThrowsAsync<ArgumentException>(() => MotoreModelliEmail.RenderizzaAsync("{{ x.GetType }}", Valori(("x", new object())), html: false));
    }

    [Fact]
    public async Task UnaSintassiErrataEUnErrore()
    {
        Assert.NotNull(MotoreModelliEmail.Analizza("{% if %}"));
        Assert.NotNull(MotoreModelliEmail.Analizza("{{ nome "));
        Assert.Null(MotoreModelliEmail.Analizza("Ciao {{ nome }}"));
        await Assert.ThrowsAsync<FormatException>(() => MotoreModelliEmail.RenderizzaAsync("{% for %}", _nessuna, html: false));
    }

    [Fact]
    public async Task LOggettoDiventaUnaRigaSola()
    {
        var reso = await MotoreModelliEmail.RenderizzaOggettoAsync("  Ordine {{ numero }}\n  confermato  ", Valori(("numero", 7)));

        Assert.Equal("Ordine 7 confermato", reso);
    }
}
