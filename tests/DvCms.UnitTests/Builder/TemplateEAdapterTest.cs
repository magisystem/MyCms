using DvCms.Application.Builder;
using Xunit;
using static DvCms.UnitTests.Builder.DocumentoCmsTest;

namespace DvCms.UnitTests.Builder;

/// <summary>Policy dei template lato server (E-004) e adapter GrapesJS ↔ documento (E-003).</summary>
public sealed class TemplateEAdapterTest
{
    private static readonly RegistroComponenti _registro = new(RegistroComponenti.Predefiniti);
    private static readonly RegistroTemplateBuilder _template = new(RegistroTemplateBuilder.Predefiniti);

    private static DocumentoCms Iniziale(string id = RegistroTemplateBuilder.IntestazioneFissa) => _template.Trova(id, 1)!.DocumentoIniziale();

    private static NodoCms Con(NodoCms nodo, Dictionary<string, string>? proprieta = null, Dictionary<string, string>? stili = null, IReadOnlyList<NodoCms>? figli = null, string? tipo = null) =>
        nodo with
        {
            Tipo = tipo ?? nodo.Tipo,
            Proprieta = proprieta ?? new Dictionary<string, string>(nodo.Proprieta),
            Stili = stili ?? new Dictionary<string, string>(nodo.Stili),
            Figli = figli ?? nodo.Figli,
        };

    private static DocumentoCms Sostituisci(DocumentoCms documento, string id, Func<NodoCms, NodoCms> modifica)
    {
        NodoCms Visita(NodoCms nodo) => nodo.Id == id ? modifica(nodo) : nodo with { Figli = [.. nodo.Figli.Select(Visita)] };
        return documento with { Nodi = [.. documento.Nodi.Select(Visita)] };
    }

    [Fact]
    public void LeImprontiDeiTemplateRilasciatiSonoFissate()
    {
        Assert.Equal("7311A05FA8822C268A01034509A4FB38F4A713DC6E3F7A7DC12F931B3DC445E2", RegistroTemplateBuilder.TemplatePaginaLibera.Impronta);
        Assert.Equal("25C07D3EB1BF71104F6D410014450418F8B1B14C84EABCB3154AF10BE83E3738", RegistroTemplateBuilder.TemplateIntestazioneFissa.Impronta);
        Assert.Equal("9BB90F19375FF34AF5456E7FF1CC92E0BCD285BB2ADC1E14C0FA37A70C1A0C78", RegistroTemplateBuilder.TemplateIntestazioneFissaV2.Impronta);
        Assert.Equal("78557BFD3AFDA3B795763D472917AFAB165FEC0666F0868BBE417B6B6AA49841", RegistroTemplateBuilder.TemplateIntestazioneFissaV3.Impronta);
        Assert.Equal("97ACBC0D4A3A3EA943C8FE56CA2C8D8C5F30447AA8A4CAE178AF323BF1F35D27", RegistroTemplateBuilder.TemplateIntestazioneFissaV4.Impronta);
    }

    [Fact]
    public void IlRegistroRifiutaTemplateDuplicati() =>
        Assert.Throws<ArgumentException>(() => new RegistroTemplateBuilder([RegistroTemplateBuilder.TemplatePaginaLibera, RegistroTemplateBuilder.TemplatePaginaLibera]));

    [Fact]
    public void LaVersione2AmmetteIComponentiAvanzatiNelCorpoELaVersione1No()
    {
        var v1 = RegistroTemplateBuilder.TemplateIntestazioneFissa;
        var v2 = RegistroTemplateBuilder.TemplateIntestazioneFissaV2;
        var conHero = Sostituisci(Iniziale(), "corpo", corpo => Con(corpo, figli: [Nodo("h", "contenuto.hero", new() { ["titolo"] = "x" })]));
        var conFisarmonica = Sostituisci(Iniziale(), "corpo", corpo => Con(corpo, figli: [Nodo("f", "contenuto.fisarmonica", null, null, Nodo("v", "contenuto.voce", new() { ["domanda"] = "d", ["risposta"] = "r" }))]));

        Assert.Contains(ValidatoreTemplate.Valida(v1, conHero), e => e.Codice == "TipoNonAmmessoNelloSlot");
        Assert.Empty(ValidatoreTemplate.Valida(v2, conHero));
        Assert.Empty(ValidatoreTemplate.Valida(v2, conFisarmonica));
        Assert.Empty(_registro.Valida(conFisarmonica));
        // Un documento valido per la v1 lo è anche per la v2 (presupposto del passaggio di versione).
        Assert.Empty(ValidatoreTemplate.Valida(v2, Iniziale()));
    }

    [Fact]
    public void LaVersione3AmmetteIlTestoFormattatoELeVersioniPrecedentiNo()
    {
        var conTestoRicco = Sostituisci(Iniziale(), "corpo", corpo => Con(corpo, figli: [Nodo("r", "contenuto.testo-ricco", new() { ["testo"] = "Un **testo** formattato" })]));

        Assert.Contains(ValidatoreTemplate.Valida(RegistroTemplateBuilder.TemplateIntestazioneFissa, conTestoRicco), e => e.Codice == "TipoNonAmmessoNelloSlot");
        Assert.Contains(ValidatoreTemplate.Valida(RegistroTemplateBuilder.TemplateIntestazioneFissaV2, conTestoRicco), e => e.Codice == "TipoNonAmmessoNelloSlot");
        Assert.Empty(ValidatoreTemplate.Valida(RegistroTemplateBuilder.TemplateIntestazioneFissaV3, conTestoRicco));
        Assert.Empty(_registro.Valida(conTestoRicco));
        Assert.Empty(ValidatoreTemplate.Valida(RegistroTemplateBuilder.TemplateIntestazioneFissaV3, Iniziale()));
        Assert.Same(RegistroTemplateBuilder.TemplateIntestazioneFissaV4, _template.Ultimo(RegistroTemplateBuilder.IntestazioneFissa));
    }

    [Fact]
    public void LaVersione4AmmetteGrigliaETestimonianzeELeVersioniPrecedentiNo()
    {
        var caratteristiche = Nodo("g", "contenuto.caratteristiche", new(), null, Nodo("c", "contenuto.caratteristica", new() { ["titolo"] = "Veloce" }));
        var testimonianze = Nodo("t", "contenuto.testimonianze", new(), null, Nodo("q", "contenuto.testimonianza", new() { ["citazione"] = "Ottimo", ["autore"] = "Ada" }));
        var documento = Sostituisci(Iniziale(), "corpo", corpo => Con(corpo, figli: [caratteristiche, testimonianze]));

        foreach (var precedente in new[] { RegistroTemplateBuilder.TemplateIntestazioneFissa, RegistroTemplateBuilder.TemplateIntestazioneFissaV2, RegistroTemplateBuilder.TemplateIntestazioneFissaV3 })
        {
            Assert.Contains(ValidatoreTemplate.Valida(precedente, documento), e => e.Codice == "TipoNonAmmessoNelloSlot");
        }

        Assert.Empty(ValidatoreTemplate.Valida(RegistroTemplateBuilder.TemplateIntestazioneFissaV4, documento));
        Assert.Empty(_registro.Valida(documento));
        Assert.Empty(ValidatoreTemplate.Valida(RegistroTemplateBuilder.TemplateIntestazioneFissaV4, Iniziale()));
    }

    [Theory]
    [InlineData(RegistroTemplateBuilder.PaginaLibera)]
    [InlineData(RegistroTemplateBuilder.IntestazioneFissa)]
    public void IlDocumentoInizialeDiOgniTemplateEValidoEConforme(string id)
    {
        var template = _template.Trova(id, 1)!;
        var iniziale = template.DocumentoIniziale();

        Assert.Empty(_registro.Valida(iniziale));
        Assert.Empty(ValidatoreTemplate.Valida(template, iniziale));
    }

    [Fact]
    public void NelloSlotSiPossonoAggiungereSpostareEdEliminareContenutiAmmessi()
    {
        var template = RegistroTemplateBuilder.TemplateIntestazioneFissa;
        var documento = Sostituisci(Iniziale(), "corpo", corpo => Con(corpo, figli:
        [
            Nodo("nuovo-titolo", "contenuto.titolo", new() { ["testo"] = "Sezione" }),
            Nodo("nuovo-testo", "contenuto.testo", new() { ["testo"] = "Testo" }),
            Nodo("nuovo-btn", "contenuto.pulsante", new() { ["etichetta"] = "Vai", ["url"] = "/x" }),
        ]));

        Assert.Empty(_registro.Valida(documento));
        Assert.Empty(ValidatoreTemplate.Valida(template, documento));
        var vuoto = Sostituisci(Iniziale(), "corpo", corpo => Con(corpo, figli: []));
        Assert.Empty(ValidatoreTemplate.Valida(template, vuoto));
    }

    [Fact]
    public void IlTestoDelTitoloDellIntestazioneEModificabileMaIlRestoNo()
    {
        var template = RegistroTemplateBuilder.TemplateIntestazioneFissa;

        var testo = Sostituisci(Iniziale(), "intestazione-titolo", n => Con(n, new() { ["testo"] = "Nuovo titolo", ["livello"] = "1" }));
        var livello = Sostituisci(Iniziale(), "intestazione-titolo", n => Con(n, new() { ["testo"] = "Titolo della pagina", ["livello"] = "3" }));
        var allineamento = Sostituisci(Iniziale(), "intestazione-titolo", n => Con(n, stili: new() { ["allineamento"] = "destra" }));

        Assert.Empty(ValidatoreTemplate.Valida(template, testo));
        Assert.Contains(ValidatoreTemplate.Valida(template, livello), e => e is { Codice: "NodoBloccatoModificato", IdNodo: "intestazione-titolo", Campo: "proprieta.livello" });
        Assert.Contains(ValidatoreTemplate.Valida(template, allineamento), e => e is { Codice: "NodoBloccatoModificato", Campo: "stili.allineamento" });
    }

    public static TheoryData<string, string, Func<DocumentoCms, DocumentoCms>> Manomissioni() => new()
    {
        { "NodoBloccatoMancante", "avviso", d => d with { Nodi = [.. d.Nodi.Where(n => n.Id != "avviso")] } },
        { "NodoBloccatoMancante", "intestazione", d => d with { Nodi = [.. d.Nodi.Where(n => n.Id != "intestazione")] } },
        { "NodoBloccatoMancante", "avviso-testo", d => Sostituisci(d, "avviso", n => Con(n, figli: [])) },
        { "NodoBloccatoModificato", "avviso-testo", d => Sostituisci(d, "avviso-testo", n => Con(n, new() { ["testo"] = "Avviso cambiato" })) },
        { "NodoBloccatoModificato", "avviso-testo", d => Sostituisci(d, "avviso-testo", n => Con(n, tipo: "contenuto.titolo")) },
        { "NodoBloccatoModificato", "avviso", d => Sostituisci(d, "avviso", n => Con(n, stili: new() { ["sfondo"] = "scuro" })) },
        { "NodoBloccatoModificato", "intestazione", d => Sostituisci(d, "intestazione", n => Con(n, new() { ["x"] = "y" })) },
        { "NodoBloccatoSpostato", "", d => d with { Nodi = [d.Nodi[2], d.Nodi[0], d.Nodi[1]] } },
        { "NodoFuoriSlot", "extra", d => d with { Nodi = [.. d.Nodi, Nodo("extra", "layout.sezione")] } },
        { "NodoFuoriSlot", "intruso", d => Sostituisci(d, "avviso", n => Con(n, figli: [.. n.Figli, Nodo("intruso", "contenuto.testo", new() { ["testo"] = "x" })])) },
        { "NodoFuoriSlot", "intruso2", d => Sostituisci(d, "intestazione", n => Con(n, figli: [.. n.Figli, Nodo("intruso2", "contenuto.testo", new() { ["testo"] = "x" })])) },
        { "TipoNonAmmessoNelloSlot", "sez", d => Sostituisci(d, "corpo", n => Con(n, figli: [.. n.Figli, Nodo("sez", "layout.sezione")])) },
        { "TipoNonAmmessoNelloSlot", "col", d => Sostituisci(d, "corpo", n => Con(n, figli: [.. n.Figli, Nodo("col", "layout.colonna")])) },
        { "TroppiFigliNelloSlot", "corpo", d => Sostituisci(d, "corpo", n => Con(n, figli: [.. Enumerable.Range(0, 31).Select(i => Nodo($"t{i}", "contenuto.testo", new() { ["testo"] = "x" }))])) },
        { "NodoBloccatoMancante", "corpo", d => d with { Nodi = [d.Nodi[0], Nodo("corpo2", "layout.sezione"), d.Nodi[2]] } },
    };

    [Theory]
    [MemberData(nameof(Manomissioni))]
    public void UnPayloadManomessoNonPuoAggirareIlTemplate(string codice, string idNodo, Func<DocumentoCms, DocumentoCms> manomette)
    {
        var template = RegistroTemplateBuilder.TemplateIntestazioneFissa;

        var errori = ValidatoreTemplate.Valida(template, manomette(Iniziale()));

        Assert.Contains(errori, e => e.Codice == codice && (idNodo.Length == 0 || e.IdNodo == idNodo));
    }

    [Fact]
    public void LaPaginaLiberaAccettaSoloSezioniInRadice()
    {
        var template = RegistroTemplateBuilder.TemplatePaginaLibera;
        var valido = new DocumentoCms(1, [Nodo("s1", "layout.sezione"), Nodo("s2", "layout.sezione", null, null, Nodo("t", "contenuto.titolo", new() { ["testo"] = "x" }))]);
        var testoInRadice = new DocumentoCms(1, [Nodo("t", "contenuto.testo", new() { ["testo"] = "x" })]);
        var troppe = new DocumentoCms(1, [.. Enumerable.Range(0, 21).Select(i => Nodo($"s{i}", "layout.sezione"))]);

        Assert.Empty(ValidatoreTemplate.Valida(template, valido));
        Assert.Contains(ValidatoreTemplate.Valida(template, testoInRadice), e => e.Codice == "TipoNonAmmessoNelloSlot");
        Assert.Contains(ValidatoreTemplate.Valida(template, troppe), e => e.Codice == "TroppiFigliNelloSlot");
    }

    private static DocumentoCms Normalizzato(DocumentoCms d) => _registro.Normalizza(d);

    [Fact]
    public void DocumentoProgettoDocumentoELaStessaCosa()
    {
        var documento = Normalizzato(Esempio());

        var progetto = AdapterGrapesJs.AProgetto(documento);
        Assert.True(AdapterGrapesJs.TentaDaProgetto(progetto, _registro, out var ritorno, out var errori));

        Assert.Empty(errori);
        Assert.Equal(SerializzatoreDocumento.Scrivi(documento), SerializzatoreDocumento.Scrivi(Normalizzato(ritorno)));
        Assert.Equal(progetto, AdapterGrapesJs.AProgetto(Normalizzato(ritorno)));
    }

    [Theory]
    [InlineData(RegistroTemplateBuilder.PaginaLibera)]
    [InlineData(RegistroTemplateBuilder.IntestazioneFissa)]
    public void IlRoundTripDeiDocumentiInizialiDeiTemplateENonLossy(string id)
    {
        var documento = Normalizzato(Iniziale(id));

        Assert.True(AdapterGrapesJs.TentaDaProgetto(AdapterGrapesJs.AProgetto(documento), _registro, out var ritorno, out _));
        Assert.Equal(SerializzatoreDocumento.Scrivi(documento), SerializzatoreDocumento.Scrivi(Normalizzato(ritorno)));
    }

    [Fact]
    public void IValoriDiDefaultEsplicitiNonCambianoIlDocumentoCanonico()
    {
        var conDefault = Esempio() with { };
        var progetto = AdapterGrapesJs.AProgetto(conDefault).Replace("\"data-p-livello\":\"1\"", "\"data-p-livello\":\"1\",\"data-s-allineamento\":\"centro\"", StringComparison.Ordinal);

        Assert.True(AdapterGrapesJs.TentaDaProgetto(progetto, _registro, out var documento, out _));
        Assert.Equal(SerializzatoreDocumento.Scrivi(Normalizzato(Esempio())), SerializzatoreDocumento.Scrivi(Normalizzato(documento)));
    }

    [Fact]
    public void ISoliMetadatiEditorialiSonoIgnoratiSenzaErrore()
    {
        var progetto = """
            {"assets":[],"styles":[],"symbols":[],"dataSources":[],"pages":[{"id":"x","frames":[{"id":"f","component":{"type":"wrapper","attributes":{"id":"iw"},"stylable":["background","background-color"],"head":{"type":"head"},"docEl":{"tagName":"html"},"components":[
              {"type":"contenuto.testo","name":"Il mio testo","removable":false,"draggable":true,"tagName":"p","classes":[],"style":{},"attributes":{"id":"t1","data-p-testo":"Ciao\r\nmondo"}}
            ]}}]}]}
            """;

        Assert.True(AdapterGrapesJs.TentaDaProgetto(progetto, _registro, out var documento, out var errori));

        Assert.Empty(errori);
        Assert.Equal("Ciao\nmondo", documento.Nodi[0].Proprieta["testo"]);
    }

    [Theory]
    [InlineData("\"head\":{\"type\":\"head\",\"components\":[{\"type\":\"script\"}]}")]
    [InlineData("\"head\":\"x\"")]
    [InlineData("\"docEl\":{\"tagName\":\"html\",\"attributes\":{\"onload\":\"x\"}}")]
    [InlineData("\"docEl\":{\"tagName\":\"body\"}")]
    [InlineData("\"script\":\"alert(1)\"")]
    public void IMetadatiDelContenitoreSonoAccettatiSoloNellaFormaNota(string aggiunta)
    {
        var progetto = "{\"pages\":[{\"frames\":[{\"component\":{\"type\":\"wrapper\"," + aggiunta + ",\"components\":[]}}]}]}";

        Assert.False(AdapterGrapesJs.TentaDaProgetto(progetto, _registro, out _, out var errori));
        Assert.NotEmpty(errori);
    }

    [Theory]
    [InlineData(RegistroTemplateBuilder.PaginaLibera)]
    [InlineData(RegistroTemplateBuilder.IntestazioneFissa)]
    public void IDocumentiInizialiDeiTemplateSonoGiaInFormaCanonica(string id)
    {
        // Il confronto con il documento inviato avviene dopo la normalizzazione: un template con valori di default espliciti non combacerebbe mai.
        var iniziale = Iniziale(id);

        Assert.Equal(SerializzatoreDocumento.Scrivi(iniziale), SerializzatoreDocumento.Scrivi(_registro.Normalizza(_template.Trova(id, 1)!.DocumentoIniziale())));
    }

    public static TheoryData<string, string> ProgettiNonRappresentabili() => new()
    {
        { "StiliCssNonSupportati", """{"assets":[],"styles":[{"selectors":["#a"],"style":{"color":"red"}}],"symbols":[],"dataSources":[],"pages":[{"frames":[{"component":{"type":"wrapper","components":[]}}]}]}""" },
        { "ElementoNonSupportato", """{"assets":[{"src":"x.png"}],"styles":[],"pages":[{"frames":[{"component":{"type":"wrapper","components":[]}}]}]}""" },
        { "PagineNonSupportate", """{"pages":[{"frames":[{"component":{"type":"wrapper","components":[]}}]},{"frames":[{"component":{"type":"wrapper","components":[]}}]}]}""" },
        { "PagineNonSupportate", """{"pages":[]}""" },
        { "CampoNonSupportato", """{"pages":[{"frames":[{"component":{"type":"wrapper","components":[]}}]}],"extra":1}""" },
        { "TipoNonSupportato", """{"pages":[{"frames":[{"component":{"type":"wrapper","components":[{"type":"text","components":[{"type":"textnode","content":"x"}]}]}}]}]}""" },
        { "TipoNonSupportato", """{"pages":[{"frames":[{"component":{"type":"wrapper","components":[{"type":"script","content":"alert(1)"}]}}]}]}""" },
        { "TipoNonSupportato", """{"pages":[{"frames":[{"component":{"type":"wrapper","components":[{"tagName":"iframe","attributes":{"src":"https://cattivo.example"}}]}}]}]}""" },
        { "ClassiNonSupportate", """{"pages":[{"frames":[{"component":{"type":"wrapper","components":[{"type":"layout.sezione","classes":["rosso"]}]}}]}]}""" },
        { "StileInLineaNonSupportato", """{"pages":[{"frames":[{"component":{"type":"wrapper","components":[{"type":"layout.sezione","style":{"color":"red"}}]}}]}]}""" },
        { "AttributoNonSupportato", """{"pages":[{"frames":[{"component":{"type":"wrapper","components":[{"type":"layout.sezione","attributes":{"onclick":"alert(1)"}}]}}]}]}""" },
        { "AttributoNonSupportato", """{"pages":[{"frames":[{"component":{"type":"wrapper","components":[{"type":"contenuto.testo","attributes":{"data-p-sconosciuta":"x"}}]}}]}]}""" },
        { "AttributoNonSupportato", """{"pages":[{"frames":[{"component":{"type":"wrapper","components":[{"type":"layout.sezione","attributes":{"data-p-testo":"x"}}]}}]}]}""" },
        { "AttributoNonValido", """{"pages":[{"frames":[{"component":{"type":"wrapper","components":[{"type":"layout.sezione","attributes":{"id":5}}]}}]}]}""" },
        { "CampoNonSupportato", """{"pages":[{"frames":[{"component":{"type":"wrapper","components":[{"type":"layout.sezione","content":"<img src=x onerror=alert(1)>"}]}}]}]}""" },
        { "CampoNonSupportato", """{"pages":[{"frames":[{"component":{"type":"wrapper","components":[{"type":"layout.sezione","script":"alert(1)"}]}}]}]}""" },
        { "ComponenteNonValido", """{"pages":[{"frames":[{"component":{"type":"wrapper","components":["testo"]}}]}]}""" },
        { "AttributoNonSupportato", """{"pages":[{"frames":[{"component":{"type":"wrapper","attributes":{"onload":"x"},"components":[]}}]}]}""" },
        { "ProgettoNonValido", "" },
        { "JsonNonValido", "{non json" },
    };

    [Theory]
    [MemberData(nameof(ProgettiNonRappresentabili))]
    public void CioCheNonSiRappresentaEUnErroreEsplicitoMaiUnoScartoSilenzioso(string codice, string progetto)
    {
        Assert.False(AdapterGrapesJs.TentaDaProgetto(progetto, _registro, out _, out var errori));

        Assert.Contains(errori, e => e.Codice == codice);
    }

    [Fact]
    public void UnProgettoTroppoGrandeOTroppoAnnidatoEDichiaratoNonValido()
    {
        var grande = "{\"pages\":[],\"x\":\"" + new string('a', AdapterGrapesJs.ByteMassimiProgetto) + "\"}";
        var annidato = "{\"pages\":" + new string('[', 100) + new string(']', 100) + "}";

        Assert.False(AdapterGrapesJs.TentaDaProgetto(grande, _registro, out _, out var e1));
        Assert.False(AdapterGrapesJs.TentaDaProgetto(annidato, _registro, out _, out var e2));
        Assert.Contains(e1, e => e.Codice == "ProgettoTroppoGrande");
        Assert.Contains(e2, e => e.Codice == "JsonNonValido");
    }

    [Fact]
    public void GliIdMancantiSonoDeterministiciDalPercorso()
    {
        var progetto = """{"pages":[{"frames":[{"component":{"type":"wrapper","components":[{"type":"layout.sezione","components":[{"type":"contenuto.testo","attributes":{"data-p-testo":"a"}}]},{"type":"layout.sezione"}]}}]}]}""";

        Assert.True(AdapterGrapesJs.TentaDaProgetto(progetto, _registro, out var primo, out _));
        Assert.True(AdapterGrapesJs.TentaDaProgetto(progetto, _registro, out var secondo, out _));

        Assert.Equal(SerializzatoreDocumento.Scrivi(primo), SerializzatoreDocumento.Scrivi(secondo));
        Assert.Equal(["auto-n-0", "auto-n-0-0", "auto-n-1"], primo.Visita().Select(n => n.Id));
        Assert.Empty(_registro.Valida(primo));
    }

    [Fact]
    public void IlProgettoInviatoNonEMaiMemorizzatoMaRigenerato()
    {
        // L'adapter scarta i metadati editoriali: ciò che si salva è AProgetto(documento), non l'input.
        var progetto = """{"pages":[{"frames":[{"component":{"type":"wrapper","components":[{"type":"layout.sezione","name":"<img src=x onerror=alert(1)>","attributes":{"id":"s1"}}]}}]}]}""";

        Assert.True(AdapterGrapesJs.TentaDaProgetto(progetto, _registro, out var documento, out _));
        var rigenerato = AdapterGrapesJs.AProgetto(documento);

        Assert.DoesNotContain("onerror", rigenerato, StringComparison.Ordinal);
        Assert.DoesNotContain("<img", rigenerato, StringComparison.Ordinal);
    }

    [Fact]
    public void INomiDegliAttributiSonoInKebabCase()
    {
        Assert.Equal("data-p-id-risorsa", AdapterGrapesJs.NomeAttributoProprieta("idRisorsa"));
        Assert.Equal("data-s-allineamento", AdapterGrapesJs.NomeAttributoStile("allineamento"));
    }
}
