using DvCms.Core.Builder;
using DvCms.DTO.Builder;
using Xunit;

namespace DvCms.UnitTests.Builder;

/// <summary>Schema del documento CMS, registro dei componenti e serializzazione canonica (E-002).</summary>
public sealed class DocumentoCmsTest
{
    private static readonly RegistroComponenti _registro = new(RegistroComponenti.Predefiniti);
    private static readonly string _risorsa = "0a1b2c3d-0000-4000-8000-00000000abcd";

    internal static NodoCms Nodo(string id, string tipo, Dictionary<string, string>? proprieta = null, Dictionary<string, string>? stili = null, params NodoCms[] figli) =>
        new(id, tipo, proprieta ?? [], stili ?? [], figli);

    internal static DocumentoCms Esempio() => new(1,
    [
        Nodo("sezione-1", "layout.sezione", null, new() { ["sfondo"] = "chiaro" },
            Nodo("titolo-1", "contenuto.titolo", new() { ["testo"] = "Benvenuti", ["livello"] = "1" }, new() { ["allineamento"] = "centro" }),
            Nodo("testo-1", "contenuto.testo", new() { ["testo"] = "Prima riga\nSeconda riga" }),
            Nodo("colonne-1", "layout.colonne", null, null,
                Nodo("col-1", "layout.colonna", null, null, Nodo("img-1", "contenuto.immagine", new() { ["idRisorsa"] = _risorsa, ["alt"] = "Una foto" })),
                Nodo("col-2", "layout.colonna", null, null, Nodo("btn-1", "contenuto.pulsante", new() { ["etichetta"] = "Scopri", ["url"] = "/prodotti" })))),
    ]);

    [Fact]
    public void IlDocumentoDiEsempioEValido() => Assert.Empty(_registro.Valida(Esempio()));

    [Fact]
    public void LeDefinizioniDeiComponentiHannoUnImprontaFissata() =>
        Assert.Equal("6F8048E79CD0C8C2D73B87F08E810A616777AA017655CD62BA6429D576843180", _registro.Impronta);

    [Fact]
    public void IlRegistroRifiutaDefinizioniDuplicateOFigliSconosciuti()
    {
        var uno = RegistroComponenti.Predefiniti[0];
        Assert.Throws<ArgumentException>(() => new RegistroComponenti([uno, uno]));
        Assert.Throws<ArgumentException>(() => new RegistroComponenti([uno with { FigliAmmessi = ["non.esiste"] }]));
    }

    [Fact]
    public void LaSerializzazioneECanonicaEReversibile()
    {
        var json = SerializzatoreDocumento.Scrivi(Esempio());

        Assert.True(SerializzatoreDocumento.TentaLeggi(json, out var letto, out var errori));
        Assert.Empty(errori);
        Assert.Equal(json, SerializzatoreDocumento.Scrivi(letto));
        Assert.StartsWith("{\"versioneSchema\":1,\"nodi\":[{\"id\":\"sezione-1\",\"tipo\":\"layout.sezione\",\"proprieta\":{},\"stili\":{\"sfondo\":\"chiaro\"},\"figli\":[", json, StringComparison.Ordinal);
    }

    [Fact]
    public void LOrdineDelleChiaviNonCambiaLaStringa()
    {
        var a = Nodo("t", "contenuto.titolo", new() { ["testo"] = "x", ["livello"] = "3" });
        var b = Nodo("t", "contenuto.titolo", new() { ["livello"] = "3", ["testo"] = "x" });

        Assert.Equal(SerializzatoreDocumento.Scrivi(new DocumentoCms(1, [a])), SerializzatoreDocumento.Scrivi(new DocumentoCms(1, [b])));
    }

    [Theory]
    [InlineData("")]
    [InlineData("non json")]
    [InlineData("[]")]
    [InlineData("{\"nodi\":[]}")]
    [InlineData("{\"versioneSchema\":1}")]
    [InlineData("{\"versioneSchema\":1,\"nodi\":[],\"extra\":1}")]
    [InlineData("{\"versioneSchema\":1,\"nodi\":[{\"id\":\"a\"}]}")]
    [InlineData("{\"versioneSchema\":1,\"nodi\":[{\"id\":\"a\",\"tipo\":\"contenuto.testo\",\"proprieta\":{\"testo\":5}}]}")]
    [InlineData("{\"versioneSchema\":1,\"nodi\":[{\"id\":\"a\",\"tipo\":\"contenuto.testo\",\"proprieta\":{\"testo\":\"x\",\"testo\":\"y\"}}]}")]
    [InlineData("{\"versioneSchema\":1,\"nodi\":[{\"id\":\"a\",\"tipo\":\"contenuto.testo\",\"onclick\":\"x\"}]}")]
    [InlineData("{\"versioneSchema\":\"1\",\"nodi\":[]}")]
    public void LaLetturaRifiutaJsonNonConformi(string json)
    {
        Assert.False(SerializzatoreDocumento.TentaLeggi(json, out _, out var errori));
        Assert.NotEmpty(errori);
    }

    [Fact]
    public void LaLetturaRifiutaAnnidamentiEsagerati()
    {
        var json = "{\"versioneSchema\":1,\"nodi\":[" + string.Concat(Enumerable.Repeat("{\"id\":\"a\",\"tipo\":\"layout.colonna\",\"figli\":[", 40)) + new string(']', 1) + "]}";

        Assert.False(SerializzatoreDocumento.TentaLeggi(json, out _, out _));
    }

    [Fact]
    public void LaNormalizzazioneToglieDefaultEVuoti()
    {
        var documento = new DocumentoCms(1, [Nodo("t", "contenuto.titolo", new() { ["testo"] = "x", ["livello"] = "2" }, new() { ["allineamento"] = "sinistra" })]);

        var normalizzato = _registro.Normalizza(documento);

        Assert.Equal(["testo"], normalizzato.Nodi[0].Proprieta.Keys);
        Assert.Empty(normalizzato.Nodi[0].Stili);
    }

    public static TheoryData<string, Func<DocumentoCms>> DocumentiNonValidi() => new()
    {
        { "TipoSconosciuto", () => new(1, [Nodo("a", "contenuto.script")]) },
        { "TipoSconosciuto", () => new(1, [Nodo("a", "LAYOUT.SEZIONE")]) },
        { "NodoDuplicato", () => new(1, [Nodo("a", "layout.sezione"), Nodo("a", "layout.sezione")]) },
        { "IdNonValido", () => new(1, [Nodo("1a", "layout.sezione")]) },
        { "IdNonValido", () => new(1, [Nodo("a b", "layout.sezione")]) },
        { "IdNonValido", () => new(1, [Nodo("a\"onload=\"x", "layout.sezione")]) },
        { "IdNonValido", () => new(1, [Nodo(new string('a', 65), "layout.sezione")]) },
        { "VersioneSchemaNonSupportata", () => new(2, []) },
        { "ProprietaSconosciuta", () => new(1, [Nodo("a", "contenuto.testo", new() { ["testo"] = "x", ["onclick"] = "x" })]) },
        { "StileSconosciuta", () => new(1, [Nodo("a", "contenuto.testo", new() { ["testo"] = "x" }, new() { ["colore"] = "red" })]) },
        { "StileNonValida", () => new(1, [Nodo("a", "contenuto.testo", new() { ["testo"] = "x" }, new() { ["allineamento"] = "destra; background:url(x)" })]) },
        { "ProprietaMancante", () => new(1, [Nodo("a", "contenuto.titolo")]) },
        { "ProprietaNonValida", () => new(1, [Nodo("a", "contenuto.titolo", new() { ["testo"] = new string('x', 201) })]) },
        { "ProprietaNonValida", () => new(1, [Nodo("a", "contenuto.titolo", new() { ["testo"] = "a\nb" })]) },
        { "ProprietaNonValida", () => new(1, [Nodo("a", "contenuto.testo", new() { ["testo"] = "a\u0000b" })]) },
        { "ProprietaNonValida", () => new(1, [Nodo("a", "contenuto.titolo", new() { ["testo"] = "x", ["livello"] = "7" })]) },
        { "ProprietaNonValida", () => new(1, [Nodo("a", "contenuto.pulsante", new() { ["etichetta"] = "x", ["url"] = "javascript:alert(1)" })]) },
        { "ProprietaNonValida", () => new(1, [Nodo("a", "contenuto.pulsante", new() { ["etichetta"] = "x", ["url"] = "JaVaScRiPt:alert(1)" })]) },
        { "ProprietaNonValida", () => new(1, [Nodo("a", "contenuto.pulsante", new() { ["etichetta"] = "x", ["url"] = "data:text/html;base64,AAAA" })]) },
        { "ProprietaNonValida", () => new(1, [Nodo("a", "contenuto.pulsante", new() { ["etichetta"] = "x", ["url"] = "//cattivo.example/" })]) },
        { "ProprietaNonValida", () => new(1, [Nodo("a", "contenuto.pulsante", new() { ["etichetta"] = "x", ["url"] = "http://non-sicuro.example/" })]) },
        { "ProprietaNonValida", () => new(1, [Nodo("a", "contenuto.immagine", new() { ["idRisorsa"] = "../../etc/passwd", ["alt"] = "x" })]) },
        { "ProprietaNonValida", () => new(1, [Nodo("a", "contenuto.immagine", new() { ["idRisorsa"] = _risorsa.ToUpperInvariant(), ["alt"] = "x" })]) },
        { "ProprietaNonValida", () => new(1, [Nodo("a", "contenuto.immagine", new() { ["idRisorsa"] = _risorsa, ["alt"] = "x", ["decorativa"] = "forse" })]) },
        { "AltMancante", () => new(1, [Nodo("a", "contenuto.immagine", new() { ["idRisorsa"] = _risorsa })]) },
        { "FiglioNonAmmesso", () => new(1, [Nodo("a", "layout.sezione", null, null, Nodo("b", "layout.sezione"))]) },
        { "FiglioNonAmmesso", () => new(1, [Nodo("a", "layout.sezione", null, null, Nodo("b", "layout.colonna"))]) },
        { "FigliNonAmmessi", () => new(1, [Nodo("a", "contenuto.testo", new() { ["testo"] = "x" }, null, Nodo("b", "contenuto.testo", new() { ["testo"] = "y" }))]) },
        { "FigliInsufficienti", () => new(1, [Nodo("a", "layout.sezione", null, null, Nodo("c", "layout.colonne", null, null, Nodo("d", "layout.colonna")))]) },
        { "TroppiFigli", () => new(1, [Nodo("a", "layout.sezione", null, null, Nodo("c", "layout.colonne", null, null, Nodo("d1", "layout.colonna"), Nodo("d2", "layout.colonna"), Nodo("d3", "layout.colonna"), Nodo("d4", "layout.colonna")))]) },
        { "TroppiFigli", () => new(1, [Nodo("a", "layout.sezione", null, null, [.. Enumerable.Range(0, 51).Select(i => Nodo($"t{i}", "contenuto.testo", new() { ["testo"] = "x" }))])]) },
    };

    [Theory]
    [MemberData(nameof(DocumentiNonValidi))]
    public void UnDocumentoManomessoEDichiaratoNonValido(string codiceAtteso, Func<DocumentoCms> crea)
    {
        var errori = _registro.Valida(crea());

        Assert.Contains(errori, e => e.Codice == codiceAtteso);
    }

    [Fact]
    public void LaProfonditaMassimaEFattaRispettare()
    {
        // sezione > colonne > colonna > ... non supera 4 livelli nel catalogo; si forza con un registro di prova ricorsivo.
        var ricorsivo = new RegistroComponenti([new DefinizioneComponente("x", 1, [], [], ["x"], FigliMassimi: 1)]);
        NodoCms Catena(int livelli) => Nodo($"n{livelli}", "x", null, null, livelli > 1 ? [Catena(livelli - 1)] : []);

        Assert.Empty(ricorsivo.Valida(new DocumentoCms(1, [Catena(RegistroComponenti.ProfonditaMassima)])));
        Assert.Contains(ricorsivo.Valida(new DocumentoCms(1, [Catena(RegistroComponenti.ProfonditaMassima + 1)])), e => e.Codice == "ProfonditaSuperata");
    }

    [Fact]
    public void IlNumeroDiNodiELaDimensioneSonoLimitati()
    {
        var troppi = new DocumentoCms(1, [.. Enumerable.Range(0, RegistroComponenti.NodiMassimi + 1).Select(i => Nodo($"s{i}", "layout.sezione"))]);
        var grande = new DocumentoCms(1, [.. Enumerable.Range(0, 60).Select(i => Nodo($"t{i}", "contenuto.testo", new() { ["testo"] = new string('x', 5000) }))]);

        Assert.Contains(_registro.Valida(troppi), e => e.Codice == "TroppiNodi");
        Assert.Contains(_registro.Valida(grande), e => e.Codice == "DocumentoTroppoGrande");
    }

    [Fact]
    public void GliErroriNonContengonoMaiIValoriInviati()
    {
        var segreto = "SEGRETO-" + new string('z', 40);
        var errori = _registro.Valida(new DocumentoCms(1, [Nodo("a", "contenuto.pulsante", new() { ["etichetta"] = "x", ["url"] = "javascript:" + segreto })]));

        Assert.NotEmpty(errori);
        Assert.DoesNotContain(errori, e => (e.Codice + e.IdNodo + e.Campo).Contains("SEGRETO", StringComparison.Ordinal));
    }

    [Fact]
    public void GliErroriSonoLimitati()
    {
        var errori = _registro.Valida(new DocumentoCms(1, [.. Enumerable.Range(0, 200).Select(i => Nodo($"a{i}", "tipo.inesistente"))]));

        Assert.True(errori.Count <= RegistroComponenti.ErroriMassimi);
    }

    [Fact]
    public void UnTestoConMarcatoreHtmlEUnTestoValidoPercheIlRendererLoCodifica()
    {
        var documento = new DocumentoCms(1, [Nodo("a", "contenuto.testo", new() { ["testo"] = "<script>alert(1)</script> & \"virgolette\"" })]);

        Assert.Empty(_registro.Valida(documento));
    }

    [Fact]
    public void IComponentiAvanzatiEdIlAnimazioniSonoValidi()
    {
        var documento = new DocumentoCms(1,
        [
            Nodo("s", "layout.sezione", null, new() { ["animazione"] = "sale", ["ritardoAnimazione"] = "300" },
                Nodo("h", "contenuto.hero", new() { ["titolo"] = "Benvenuti", ["sottotitolo"] = "Sotto", ["etichettaPulsante"] = "Vai", ["urlPulsante"] = "/x", ["idRisorsa"] = _risorsa }, new() { ["altezza"] = "alta", ["animazione"] = "dissolvenza" }),
                Nodo("c", "contenuto.cta", new() { ["titolo"] = "Contattaci", ["etichetta"] = "Scrivici", ["url"] = "https://esempio.it/contatti" }, new() { ["sfondo"] = "primario" }),
                Nodo("f", "contenuto.fisarmonica", null, null,
                    Nodo("v1", "contenuto.voce", new() { ["domanda"] = "Come?", ["risposta"] = "Così.\n\nOppure così." }),
                    Nodo("v2", "contenuto.voce", new() { ["domanda"] = "Quando?", ["risposta"] = "Presto." }))),
        ]);

        Assert.Empty(_registro.Valida(documento));
    }

    [Fact]
    public void LaGrigliaDiCaratteristicheELeTestimonianzeSonoValide()
    {
        var documento = new DocumentoCms(1,
        [
            Nodo("s", "layout.sezione", null, null,
                Nodo("g", "contenuto.caratteristiche", null, new() { ["colonne"] = "2", ["animazione"] = "sale" },
                    Nodo("c1", "contenuto.caratteristica", new() { ["titolo"] = "Veloce", ["descrizione"] = "Parte subito." }),
                    Nodo("c2", "contenuto.caratteristica", new() { ["titolo"] = "Sicuro" })),
                Nodo("t", "contenuto.testimonianze", null, null,
                    Nodo("q", "contenuto.testimonianza", new() { ["citazione"] = "Ottimo lavoro.\n\nLo consiglio.", ["autore"] = "Ada", ["ruolo"] = "CTO" }))),
        ]);

        Assert.Empty(_registro.Valida(documento));
    }

    [Fact]
    public void LaGrigliaDiProdottiDelNegozioEValidaNellaSezioneEFoglia()
    {
        var documento = new DocumentoCms(1,
        [
            Nodo("s", "layout.sezione", null, null,
                Nodo("p", "negozio.griglia-prodotti", new() { ["quanti"] = "9" }, new() { ["colonne"] = "2", ["animazione"] = "sale" })),
            Nodo("s2", "layout.sezione", null, null, Nodo("p2", "negozio.griglia-prodotti")),
        ]);

        Assert.Empty(_registro.Valida(documento));
    }

    [Fact]
    public void LaSchedaProdottoVuoleUnIdentificativoValido()
    {
        var id = Guid.NewGuid().ToString("D");
        var valido = new DocumentoCms(1, [Nodo("s", "layout.sezione", null, null, Nodo("p", "negozio.scheda-prodotto", new() { ["idProdotto"] = id }, new() { ["animazione"] = "sale" }))]);

        Assert.Empty(_registro.Valida(valido));
    }

    [Fact]
    public void LaGrigliaDiCategoriaVuoleUnIdentificativoValido()
    {
        var valido = new DocumentoCms(1, [Nodo("s", "layout.sezione", null, null, Nodo("g", "negozio.griglia-categoria", new() { ["idCategoria"] = Guid.NewGuid().ToString("D"), ["quanti"] = "12" }, new() { ["colonne"] = "2" }))]);

        Assert.Empty(_registro.Valida(valido));
    }

    public static TheoryData<string, Func<DocumentoCms>> AvanzatiNonValidi() => new()
    {
        { "ProprietaMancante", () => new(1, [Nodo("s", "layout.sezione", null, null, Nodo("g", "negozio.griglia-categoria"))]) },
        { "ProprietaNonValida", () => new(1, [Nodo("s", "layout.sezione", null, null, Nodo("g", "negozio.griglia-categoria", new() { ["idCategoria"] = "x" }))]) },
        { "ProprietaMancante", () => new(1, [Nodo("s", "layout.sezione", null, null, Nodo("p", "negozio.scheda-prodotto"))]) },
        { "ProprietaNonValida", () => new(1, [Nodo("s", "layout.sezione", null, null, Nodo("p", "negozio.scheda-prodotto", new() { ["idProdotto"] = "non-un-guid" }))]) },
        { "ProprietaNonValida", () => new(1, [Nodo("s", "layout.sezione", null, null, Nodo("p", "negozio.scheda-prodotto", new() { ["idProdotto"] = Guid.Empty.ToString("D") }))]) },
        { "FiglioNonAmmesso", () => new(1, [Nodo("s", "layout.sezione", null, null, Nodo("k", "layout.colonne", null, null, Nodo("c1", "layout.colonna", null, null, Nodo("p", "negozio.scheda-prodotto", new() { ["idProdotto"] = Guid.NewGuid().ToString("D") })), Nodo("c2", "layout.colonna")))]) },
        { "ProprietaNonValida", () => new(1, [Nodo("s", "layout.sezione", null, null, Nodo("p", "negozio.griglia-prodotti", new() { ["quanti"] = "100" }))]) },
        { "StileNonValida", () => new(1, [Nodo("s", "layout.sezione", null, null, Nodo("p", "negozio.griglia-prodotti", null, new() { ["colonne"] = "7" }))]) },
        { "FiglioNonAmmesso", () => new(1, [Nodo("s", "layout.sezione", null, null, Nodo("k", "layout.colonne", null, null, Nodo("c1", "layout.colonna", null, null, Nodo("p", "negozio.griglia-prodotti")), Nodo("c2", "layout.colonna")))]) },
        { "FigliNonAmmessi", () => new(1, [Nodo("s", "layout.sezione", null, null, Nodo("p", "negozio.griglia-prodotti", null, null, Nodo("t", "contenuto.titolo", new() { ["testo"] = "x" })))]) },
        { "FigliInsufficienti", () => new(1, [Nodo("g", "contenuto.caratteristiche")]) },
        { "FigliInsufficienti", () => new(1, [Nodo("t", "contenuto.testimonianze")]) },
        { "TroppiFigli", () => new(1, [Nodo("g", "contenuto.caratteristiche", null, null, [.. Enumerable.Range(0, 13).Select(i => Nodo($"c{i}", "contenuto.caratteristica", new() { ["titolo"] = "x" }))])]) },
        { "TroppiFigli", () => new(1, [Nodo("t", "contenuto.testimonianze", null, null, [.. Enumerable.Range(0, 7).Select(i => Nodo($"q{i}", "contenuto.testimonianza", new() { ["citazione"] = "c", ["autore"] = "a" }))])]) },
        { "StileNonValida", () => new(1, [Nodo("g", "contenuto.caratteristiche", null, new() { ["colonne"] = "9" }, Nodo("c", "contenuto.caratteristica", new() { ["titolo"] = "x" }))]) },
        { "ProprietaMancante", () => new(1, [Nodo("t", "contenuto.testimonianze", null, null, Nodo("q", "contenuto.testimonianza", new() { ["citazione"] = "c" }))]) },
        { "ProprietaMancante", () => new(1, [Nodo("g", "contenuto.caratteristiche", null, null, Nodo("c", "contenuto.caratteristica"))]) },
        { "FiglioNonAmmesso", () => new(1, [Nodo("g", "contenuto.caratteristiche", null, null, Nodo("q", "contenuto.testimonianza", new() { ["citazione"] = "c", ["autore"] = "a" }))]) },
        { "FiglioNonAmmesso", () => new(1, [Nodo("s", "layout.sezione", null, null, Nodo("c", "contenuto.caratteristica", new() { ["titolo"] = "x" }))]) },
        { "FiglioNonAmmesso", () => new(1, [Nodo("s", "layout.sezione", null, null, Nodo("k", "layout.colonne", null, null, Nodo("c1", "layout.colonna", null, null, Nodo("g", "contenuto.caratteristiche", null, null, Nodo("c", "contenuto.caratteristica", new() { ["titolo"] = "x" }))), Nodo("c2", "layout.colonna")))]) },
        { "StileNonValida", () => new(1, [Nodo("t", "contenuto.titolo", new() { ["testo"] = "x" }, new() { ["animazione"] = "rotazione" })]) },
        { "StileNonValida", () => new(1, [Nodo("t", "contenuto.titolo", new() { ["testo"] = "x" }, new() { ["ritardoAnimazione"] = "-1" })]) },
        { "StileNonValida", () => new(1, [Nodo("t", "contenuto.titolo", new() { ["testo"] = "x" }, new() { ["animazione"] = "sale\" onload=\"x" })]) },
        { "StileSconosciuta", () => new(1, [Nodo("c", "layout.colonna", null, new() { ["animazione"] = "sale" })]) },
        { "PulsanteIncompleto", () => new(1, [Nodo("h", "contenuto.hero", new() { ["titolo"] = "x", ["etichettaPulsante"] = "Vai" })]) },
        { "PulsanteIncompleto", () => new(1, [Nodo("h", "contenuto.hero", new() { ["titolo"] = "x", ["urlPulsante"] = "/x" })]) },
        { "ProprietaNonValida", () => new(1, [Nodo("h", "contenuto.hero", new() { ["titolo"] = "x", ["etichettaPulsante"] = "Vai", ["urlPulsante"] = "javascript:alert(1)" })]) },
        { "ProprietaMancante", () => new(1, [Nodo("c", "contenuto.cta", new() { ["titolo"] = "x", ["etichetta"] = "Vai" })]) },
        { "FigliInsufficienti", () => new(1, [Nodo("f", "contenuto.fisarmonica")]) },
        { "FiglioNonAmmesso", () => new(1, [Nodo("f", "contenuto.fisarmonica", null, null, Nodo("t", "contenuto.testo", new() { ["testo"] = "x" }))]) },
        { "FiglioNonAmmesso", () => new(1, [Nodo("s", "layout.sezione", null, null, Nodo("v", "contenuto.voce", new() { ["domanda"] = "d", ["risposta"] = "r" }))]) },
        { "TroppiFigli", () => new(1, [Nodo("f", "contenuto.fisarmonica", null, null, [.. Enumerable.Range(0, 21).Select(i => Nodo($"v{i}", "contenuto.voce", new() { ["domanda"] = "d", ["risposta"] = "r" }))])]) },
        { "FiglioNonAmmesso", () => new(1, [Nodo("s", "layout.sezione", null, null, Nodo("c", "layout.colonne", null, null, Nodo("c1", "layout.colonna", null, null, Nodo("h", "contenuto.hero", new() { ["titolo"] = "x" })), Nodo("c2", "layout.colonna")))]) },
    };

    [Theory]
    [MemberData(nameof(AvanzatiNonValidi))]
    public void UnComponenteAvanzatoManomessoEDichiaratoNonValido(string codiceAtteso, Func<DocumentoCms> crea) =>
        Assert.Contains(_registro.Valida(crea()), e => e.Codice == codiceAtteso);

    [Fact]
    public void IlTestoFormattatoValidoSiMemorizzaInFormaCanonica()
    {
        var documento = new DocumentoCms(1, [Nodo("s", "layout.sezione", null, null, Nodo("r", "contenuto.testo-ricco", new() { ["testo"] = "  **grassetto** e *corsivo*  \n\n\n- uno\r\n- due  " }))]);

        Assert.Empty(_registro.Valida(documento));
        var normalizzato = _registro.Normalizza(documento);
        Assert.Equal("**grassetto** e *corsivo*\n\n- uno\n- due", normalizzato.Nodi[0].Figli[0].Proprieta["testo"]);
        Assert.Equal(SerializzatoreDocumento.Scrivi(normalizzato), SerializzatoreDocumento.Scrivi(_registro.Normalizza(normalizzato)));
    }

    [Theory]
    [InlineData("**aperto")]
    [InlineData("[clic](javascript:alert(1))")]
    [InlineData("[clic](http://non-sicuro.example)")]
    [InlineData("<script>alert(1)</script> ]")]
    [InlineData("")]
    public void IlTestoFormattatoNonValidoEDichiaratoTale(string markup)
    {
        var documento = new DocumentoCms(1, [Nodo("r", "contenuto.testo-ricco", new() { ["testo"] = markup })]);

        Assert.Contains(_registro.Valida(documento), e => e.Codice is "ProprietaNonValida" or "ProprietaMancante");
    }

    [Fact]
    public void UnTestoConMarcatoriHtmlSenzaFormattazioneEUnTestoValido()
    {
        // Non c'è HTML da ripulire: i caratteri < e > sono testo e verranno codificati in uscita.
        var documento = new DocumentoCms(1, [Nodo("r", "contenuto.testo-ricco", new() { ["testo"] = "<img src=x onerror=alert(1)> & \"ok\"" })]);

        Assert.Empty(_registro.Valida(documento));
    }
}
