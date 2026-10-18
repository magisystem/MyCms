using DvCms.Core.Moduli;
using DvCms.DTO.Moduli;
using DvCms.Models.Moduli;
using Xunit;

namespace DvCms.UnitTests.Moduli;

/// <summary>Regole sui moduli di contatto (ADR-0027): limiti di struttura sempre, completezza solo per un modulo pubblicato.</summary>
public class ValidatoreModuloTest
{
    private static readonly IReadOnlyList<LinguaModulo> _lingue = [new("it", "Italiano", true), new("en", "English", false)];

    private static Dictionary<string, string> It(string testo) => new Dictionary<string, string> { ["it"] = testo };

    private static CampoModulo Campo(string id = "c1", TipoCampoModulo tipo = TipoCampoModulo.TestoBreve, string etichetta = "Nome", bool obbligatorio = true, int? lunghezza = null) =>
        new(id, tipo, obbligatorio, lunghezza ?? CampoModulo.LunghezzaPredefinita(tipo), It(etichetta), new Dictionary<string, string>(), []);

    private static DatiModulo Dati(
        StatoModulo stato = StatoModulo.Attivo,
        IReadOnlyList<CampoModulo>? campi = null,
        string nome = "Contatti",
        ImpostazioniModulo? impostazioni = null,
        string? percorso = null,
        string? informativa = null,
        string conferma = "Grazie!",
        string pulsante = "Invia") =>
        new(
            nome,
            stato,
            new DefinizioneModulo(campi ?? [Campo()], It(pulsante), It(conferma), percorso, informativa),
            impostazioni ?? ImpostazioniModulo.Predefinite);

    [Fact]
    public void UnModuloCompletoEValido() => Assert.Empty(ValidatoreModulo.Valida(Dati(), _lingue));

    [Fact]
    public void UnaBozzaPuoEssereIncompletaMaNonSenzaNome()
    {
        var bozza = ValidatoreModulo.Valida(Dati(StatoModulo.Bozza, campi: [], conferma: string.Empty, pulsante: string.Empty), _lingue);
        var senzaNome = ValidatoreModulo.Valida(Dati(StatoModulo.Bozza, nome: "  "), _lingue);

        Assert.Empty(bozza);
        Assert.Contains(senzaNome, e => e.Contains("nome del modulo", StringComparison.Ordinal));
    }

    [Fact]
    public void UnModuloPubblicatoDeveEssereCompleto()
    {
        var errori = ValidatoreModulo.Valida(Dati(campi: [], conferma: string.Empty, pulsante: string.Empty, impostazioni: new ImpostazioniModulo([], false, false, 365)), _lingue);

        Assert.Contains(errori, e => e.Contains("almeno un campo", StringComparison.Ordinal));
        Assert.Contains(errori, e => e.Contains("pulsante", StringComparison.Ordinal));
        Assert.Contains(errori, e => e.Contains("conferma", StringComparison.Ordinal));
        Assert.Contains(errori, e => e.Contains("chi riceve", StringComparison.Ordinal));
    }

    [Fact]
    public void IlLimiteDeiCampiESempreApplicato()
    {
        var troppi = Enumerable.Range(1, 21).Select(n => Campo($"c{n}")).ToList();

        Assert.Contains(ValidatoreModulo.Valida(Dati(StatoModulo.Bozza, troppi), _lingue), e => e.Contains("al massimo 20", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("")]
    [InlineData("campo1")]
    [InlineData("C1")]
    [InlineData("c1; drop")]
    public void GliIdentificativiDeiCampiHannoUnaFormaFissa(string id)
    {
        Assert.Contains(ValidatoreModulo.Valida(Dati(campi: [Campo(id)]), _lingue), e => e.Contains("identificativo", StringComparison.Ordinal));
    }

    [Fact]
    public void GliIdentificativiNonSiRipetono()
    {
        Assert.Contains(ValidatoreModulo.Valida(Dati(campi: [Campo("c1"), Campo("c1", etichetta: "Cognome")]), _lingue), e => e.Contains("ripetuto", StringComparison.Ordinal));
    }

    [Fact]
    public void LEtichettaNellaLinguaPredefinitaEObbligatoriaELeAltreLingueDevonoEssereDelSito()
    {
        var senza = Campo() with { Etichette = new Dictionary<string, string> { ["en"] = "Name" } };
        var sconosciuta = Campo() with { Etichette = new Dictionary<string, string> { ["it"] = "Nome", ["fr"] = "Nom" } };

        Assert.Contains(ValidatoreModulo.Valida(Dati(campi: [senza]), _lingue), e => e.Contains("obbligatorio nella lingua predefinita", StringComparison.Ordinal));
        Assert.Contains(ValidatoreModulo.Valida(Dati(campi: [sconosciuta]), _lingue), e => e.Contains("«fr»", StringComparison.Ordinal));
    }

    [Fact]
    public void LeLunghezzeRispettanoIlTipo()
    {
        Assert.Contains(ValidatoreModulo.Valida(Dati(campi: [Campo(lunghezza: 201)]), _lingue), e => e.Contains("fra 1 e 200", StringComparison.Ordinal));
        Assert.Contains(ValidatoreModulo.Valida(Dati(campi: [Campo(tipo: TipoCampoModulo.TestoLungo, lunghezza: 5001)]), _lingue), e => e.Contains("fra 1 e 5000", StringComparison.Ordinal));
        Assert.Contains(ValidatoreModulo.Valida(Dati(campi: [Campo(tipo: TipoCampoModulo.Email, lunghezza: 500)]), _lingue), e => e.Contains("fissa", StringComparison.Ordinal));
        Assert.Empty(ValidatoreModulo.Valida(Dati(campi: [Campo(tipo: TipoCampoModulo.TestoLungo, lunghezza: 5000)]), _lingue));
    }

    [Fact]
    public void LeScelteHannoDaDueATrentaOpzioniEGliAltriCampiNessuna()
    {
        Dictionary<string, string> Opz(string testo) => It(testo);
        var scelta = Campo(tipo: TipoCampoModulo.SceltaSingola) with { Opzioni = [Opz("A"), Opz("B")] };
        var una = scelta with { Opzioni = [Opz("A")] };
        var trentuno = scelta with { Opzioni = [.. Enumerable.Range(1, 31).Select(n => Opz("O" + n))] };
        var senzaTesto = scelta with { Opzioni = [Opz("A"), Opz(" ")] };
        var suTesto = Campo() with { Opzioni = [Opz("A"), Opz("B")] };

        Assert.Empty(ValidatoreModulo.Valida(Dati(campi: [scelta]), _lingue));
        Assert.Contains(ValidatoreModulo.Valida(Dati(campi: [una]), _lingue), e => e.Contains("da 2 a 30", StringComparison.Ordinal));
        Assert.Contains(ValidatoreModulo.Valida(Dati(campi: [trentuno]), _lingue), e => e.Contains("da 2 a 30", StringComparison.Ordinal));
        Assert.Contains(ValidatoreModulo.Valida(Dati(campi: [senzaTesto]), _lingue), e => e.Contains("opzione 2", StringComparison.Ordinal));
        Assert.Contains(ValidatoreModulo.Valida(Dati(campi: [suTesto]), _lingue), e => e.Contains("solo i campi a scelta", StringComparison.Ordinal));
    }

    [Fact]
    public void ConUnConsensoServeIlLinkAllInformativa()
    {
        var consenso = Campo(tipo: TipoCampoModulo.Consenso, etichetta: "Accetto il trattamento dei dati");

        Assert.Contains(ValidatoreModulo.Valida(Dati(campi: [consenso]), _lingue), e => e.Contains("informativa", StringComparison.Ordinal));
        Assert.Empty(ValidatoreModulo.Valida(Dati(campi: [consenso], informativa: "https://www.esempio.it/privacy"), _lingue));
        Assert.Empty(ValidatoreModulo.Valida(Dati(campi: [consenso], informativa: "/privacy"), _lingue));
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("http://www.esempio.it/privacy")]
    [InlineData("//evil.example/privacy")]
    [InlineData("https://www.esempio.it/con spazio")]
    public void LInformativaEHttpsOUnPercorsoDelSito(string informativa)
    {
        Assert.Contains(ValidatoreModulo.Valida(Dati(informativa: informativa), _lingue), e => e.Contains("informativa", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("/grazie", true)]
    [InlineData("/it/grazie-mille", true)]
    [InlineData("https://evil.example", false)]
    [InlineData("//evil.example", false)]
    [InlineData("grazie", false)]
    [InlineData("/../segreto", false)]
    [InlineData("/grazie?x=1", false)]
    [InlineData("/a b", false)]
    [InlineData("javascript:alert(1)", false)]
    public void LaPaginaDopoLInvioEUnPercorsoInterno(string percorso, bool valido)
    {
        var errori = ValidatoreModulo.Valida(Dati(percorso: percorso, conferma: string.Empty), _lingue);

        Assert.Equal(valido, errori.Count == 0);
    }

    [Fact]
    public void IDestinatariSonoIndirizziValidiDistintiEAlPiuCinque()
    {
        ImpostazioniModulo Con(params string[] indirizzi) => new(indirizzi, false, false, 365);

        Assert.Empty(ValidatoreModulo.Valida(Dati(impostazioni: Con("a@esempio.it", "b@esempio.it")), _lingue));
        Assert.Contains(ValidatoreModulo.Valida(Dati(impostazioni: Con("non-una-email")), _lingue), e => e.Contains("non è valido", StringComparison.Ordinal));
        Assert.Contains(ValidatoreModulo.Valida(Dati(impostazioni: Con("a@esempio.it", "A@Esempio.it")), _lingue), e => e.Contains("ripetuto", StringComparison.Ordinal));
        Assert.Contains(ValidatoreModulo.Valida(Dati(impostazioni: Con("a@e.it", "b@e.it", "c@e.it", "d@e.it", "e@e.it", "f@e.it")), _lingue), e => e.Contains("al massimo 5", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(29, false)]
    [InlineData(30, true)]
    [InlineData(1825, true)]
    [InlineData(1826, false)]
    public void LaConservazioneStaNeiLimiti(int giorni, bool valida)
    {
        var errori = ValidatoreModulo.Valida(Dati(impostazioni: new ImpostazioniModulo([], true, false, giorni)), _lingue);

        Assert.Equal(valida, errori.Count == 0);
    }

    [Fact]
    public void LaRispostaAutomaticaRichiedeUnCampoEmail()
    {
        var senza = ValidatoreModulo.Valida(Dati(impostazioni: new ImpostazioniModulo([], true, true, 365)), _lingue);
        var con = ValidatoreModulo.Valida(Dati(campi: [Campo(), Campo("c2", TipoCampoModulo.Email, "Email")], impostazioni: new ImpostazioniModulo([], true, true, 365)), _lingue);

        Assert.Contains(senza, e => e.Contains("campo email", StringComparison.Ordinal));
        Assert.Empty(con);
    }

    [Fact]
    public void IlJsonDiUnModuloSiRileggeUguale()
    {
        var definizione = Dati(campi: [Campo(), Campo("c2", TipoCampoModulo.SceltaMultipla, "Interessi") with { Opzioni = [It("A"), It("B")] }]).Definizione;
        var impostazioni = new ImpostazioniModulo(["a@esempio.it"], false, true, 90);

        var riletta = SerializzatoreModulo.LeggiDefinizione(SerializzatoreModulo.Scrivi(definizione), "it");
        var impostazioniRiletta = SerializzatoreModulo.LeggiImpostazioni(SerializzatoreModulo.Scrivi(impostazioni));

        Assert.Equal(2, riletta.Campi.Count);
        Assert.Equal(TipoCampoModulo.SceltaMultipla, riletta.Campi[1].Tipo);
        Assert.Equal("B", riletta.Campi[1].Opzioni[1]["it"]);
        Assert.Equal("Grazie!", riletta.Conferma["it"]);
        Assert.Equal(["a@esempio.it"], impostazioniRiletta.Destinatari);
        Assert.True(impostazioniRiletta.RispostaAutomatica);
        Assert.Equal(90, impostazioniRiletta.GiorniConservazione);
    }

    [Fact]
    public void UnJsonGuastoNonFaFallireLaLettura()
    {
        Assert.Empty(SerializzatoreModulo.LeggiDefinizione("{non json", "it").Campi);
        Assert.Equal(ImpostazioniModulo.Predefinite, SerializzatoreModulo.LeggiImpostazioni("[[["));
    }

    [Fact]
    public void ITestiRipieganoSullaLinguaPredefinita()
    {
        var testi = new Dictionary<string, string> { ["it"] = "Ciao", ["en"] = "Hello" };

        Assert.Equal("Hello", TestiModulo.Per(testi, "en", "it"));
        Assert.Equal("Ciao", TestiModulo.Per(testi, "de", "it"));
        Assert.Equal("Ciao", TestiModulo.Per(new Dictionary<string, string> { ["it"] = "Ciao", ["en"] = " " }, "en", "it"));
        Assert.Equal(string.Empty, TestiModulo.Per(new Dictionary<string, string>(), "en", "it"));
    }
}
