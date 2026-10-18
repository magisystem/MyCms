using DvCms.Core.Moduli;
using DvCms.DTO.Moduli;
using Xunit;

namespace DvCms.UnitTests.Moduli;

/// <summary>Lettura dei valori inviati contro la definizione corrente (ADR-0027): nessun over-posting, regole per tipo, istantanea autodescrittiva.</summary>
public class ElaboratoreInvioModuloTest
{
    private static Dictionary<string, string> Testi(string it, string? en = null) =>
        en is null ? new() { ["it"] = it } : new() { ["it"] = it, ["en"] = en };

    private static CampoModulo Campo(string id, TipoCampoModulo tipo, bool obbligatorio = false, int? lunghezza = null, params string[] opzioni) =>
        new(id, tipo, obbligatorio, lunghezza ?? CampoModulo.LunghezzaPredefinita(tipo), Testi("Etichetta " + id, "Label " + id), new Dictionary<string, string>(), [.. opzioni.Select(o => (IReadOnlyDictionary<string, string>)Testi(o, o + " EN"))]);

    private static DefinizioneModulo Definizione(params CampoModulo[] campi) =>
        new(campi, Testi("Invia"), Testi("Grazie"), null, null);

    private static EsitoElaborazioneInvio Elabora(DefinizioneModulo definizione, params (string Nome, string[] Valori)[] ricevuti) =>
        ElaboratoreInvioModulo.Elabora(definizione, "it", "it", ricevuti.ToDictionary(r => r.Nome, r => r.Valori, StringComparer.Ordinal));

    [Fact]
    public void UnInvioValidoDiventaUnIstantaneaAutodescrittivaNellaLinguaDellInvio()
    {
        var definizione = Definizione(Campo("c1", TipoCampoModulo.TestoBreve, true), Campo("c2", TipoCampoModulo.Email, true), Campo("c3", TipoCampoModulo.SceltaSingola, false, null, "Uno", "Due"));

        var esito = ElaboratoreInvioModulo.Elabora(
            definizione,
            "en",
            "it",
            new Dictionary<string, string[]> { ["campo_c1"] = ["  Anna  "], ["campo_c2"] = ["ANNA@Esempio.it"], ["campo_c3"] = ["o2"] });

        Assert.True(esito.Valido);
        Assert.Equal(["c1", "c2", "c3"], esito.Contenuto!.Select(v => v.Id));
        Assert.Equal("Label c1", esito.Contenuto![0].Etichetta);
        Assert.Equal(["Anna"], esito.Contenuto![0].Valori);
        Assert.Equal(["anna@esempio.it"], esito.Contenuto![1].Valori);
        Assert.Equal(["Due EN"], esito.Contenuto![2].Valori);
    }

    [Fact]
    public void ICampiNonPrevistiDalModuloSiIgnoranoENonSiSalvanoMai()
    {
        var definizione = Definizione(Campo("c1", TipoCampoModulo.TestoBreve));

        var esito = Elabora(definizione, ("campo_c1", ["ciao"]), ("campo_c9", ["intruso"]), ("Ruolo", ["TenantAdmin"]), ("IdTenant", [Guid.NewGuid().ToString()]));

        Assert.True(esito.Valido);
        Assert.Single(esito.Contenuto!);
        Assert.DoesNotContain(esito.ValoriRicevuti.Keys, k => k != "c1");
    }

    [Fact]
    public void UnCampoObbligatorioVuotoOMancanteEUnErrore()
    {
        var definizione = Definizione(Campo("c1", TipoCampoModulo.TestoBreve, true), Campo("c2", TipoCampoModulo.SceltaMultipla, true, null, "A"), Campo("c3", TipoCampoModulo.Consenso, true));

        var esito = Elabora(definizione, ("campo_c1", ["   "]));

        Assert.False(esito.Valido);
        Assert.Equal(ErroreCampoModulo.Obbligatorio, esito.Errori["c1"]);
        Assert.Equal(ErroreCampoModulo.Obbligatorio, esito.Errori["c2"]);
        Assert.Equal(ErroreCampoModulo.Obbligatorio, esito.Errori["c3"]);
    }

    [Fact]
    public void UnCampoFacoltativoVuotoNonEntraNelContenuto()
    {
        var esito = Elabora(Definizione(Campo("c1", TipoCampoModulo.TestoBreve), Campo("c2", TipoCampoModulo.TestoBreve)), ("campo_c1", ["x"]), ("campo_c2", [""]));

        Assert.True(esito.Valido);
        Assert.Equal(["c1"], esito.Contenuto!.Select(v => v.Id));
    }

    [Theory]
    [InlineData(TipoCampoModulo.Email, "non-una-email")]
    [InlineData(TipoCampoModulo.Email, "a@b@c.it")]
    [InlineData(TipoCampoModulo.Telefono, "abc")]
    [InlineData(TipoCampoModulo.Telefono, "12")]
    [InlineData(TipoCampoModulo.Numero, "12a")]
    [InlineData(TipoCampoModulo.Numero, "1.2.3")]
    [InlineData(TipoCampoModulo.Data, "31/12/2026")]
    [InlineData(TipoCampoModulo.Data, "2026-02-30")]
    [InlineData(TipoCampoModulo.Data, "1700-01-01")]
    public void ValoriDallaFormaSbagliataSonoRifiutatiPerTipo(TipoCampoModulo tipo, string valore)
    {
        var esito = Elabora(Definizione(Campo("c1", tipo)), ("campo_c1", [valore]));

        Assert.Equal(ErroreCampoModulo.NonValido, esito.Errori["c1"]);
    }

    [Theory]
    [InlineData(TipoCampoModulo.Telefono, "+39 02 1234-5678", "+39 02 1234-5678")]
    [InlineData(TipoCampoModulo.Numero, "12,5", "12.5")]
    [InlineData(TipoCampoModulo.Numero, "-3", "-3")]
    [InlineData(TipoCampoModulo.Data, "2026-10-18", "2026-10-18")]
    public void ValoriValidiSiNormalizzano(TipoCampoModulo tipo, string valore, string atteso)
    {
        var esito = Elabora(Definizione(Campo("c1", tipo)), ("campo_c1", [valore]));

        Assert.Equal([atteso], esito.Contenuto![0].Valori);
    }

    [Fact]
    public void LaLunghezzaMassimaDelCampoSiRispetta()
    {
        var esito = Elabora(Definizione(Campo("c1", TipoCampoModulo.TestoBreve, false, 5)), ("campo_c1", ["123456"]));

        Assert.Equal(ErroreCampoModulo.TroppoLungo, esito.Errori["c1"]);
        Assert.True(Elabora(Definizione(Campo("c1", TipoCampoModulo.TestoBreve, false, 5)), ("campo_c1", ["12345"])).Valido);
    }

    [Fact]
    public void ICaratteriDiControlloSonoRifiutatiMaIlTestoLungoAmmetteLeRighe()
    {
        var breve = Elabora(Definizione(Campo("c1", TipoCampoModulo.TestoBreve)), ("campo_c1", ["riga\nuno"]));
        var controllo = Elabora(Definizione(Campo("c1", TipoCampoModulo.TestoLungo)), ("campo_c1", ["a\u0000b"]));
        var lungo = Elabora(Definizione(Campo("c1", TipoCampoModulo.TestoLungo)), ("campo_c1", ["riga uno\r\nriga due"]));

        Assert.Equal(ErroreCampoModulo.NonValido, breve.Errori["c1"]);
        Assert.Equal(ErroreCampoModulo.NonValido, controllo.Errori["c1"]);
        Assert.Equal(["riga uno\nriga due"], lungo.Contenuto![0].Valori);
    }

    [Fact]
    public void UnCampoDiTestoNonAccettaPiuValori()
    {
        var esito = Elabora(Definizione(Campo("c1", TipoCampoModulo.TestoBreve)), ("campo_c1", ["a", "b"]));

        Assert.Equal(ErroreCampoModulo.NonValido, esito.Errori["c1"]);
    }

    [Fact]
    public void LeScelteSiFannoSoloTraLeOpzioniDefinite()
    {
        var definizione = Definizione(Campo("c1", TipoCampoModulo.SceltaSingola, false, null, "A", "B"), Campo("c2", TipoCampoModulo.SceltaMultipla, false, null, "X", "Y", "Z"));

        Assert.Equal(ErroreCampoModulo.NonValido, Elabora(definizione, ("campo_c1", ["o3"])).Errori["c1"]);
        Assert.Equal(ErroreCampoModulo.NonValido, Elabora(definizione, ("campo_c1", ["o0"])).Errori["c1"]);
        Assert.Equal(ErroreCampoModulo.NonValido, Elabora(definizione, ("campo_c1", ["o01"])).Errori["c1"]);
        Assert.Equal(ErroreCampoModulo.NonValido, Elabora(definizione, ("campo_c1", ["A"])).Errori["c1"]);
        Assert.Equal(ErroreCampoModulo.NonValido, Elabora(definizione, ("campo_c1", ["o1", "o2"])).Errori["c1"]);
        var multipla = Elabora(definizione, ("campo_c2", ["o1", "o3", "o1"]));
        Assert.Equal(["X", "Z"], multipla.Contenuto![0].Valori);
    }

    [Fact]
    public void IlConsensoSiDaSoloConIlValoreSi()
    {
        var definizione = Definizione(Campo("c1", TipoCampoModulo.Consenso, true));

        Assert.Equal(["si"], Elabora(definizione, ("campo_c1", ["si"])).Contenuto![0].Valori);
        Assert.Equal(ErroreCampoModulo.NonValido, Elabora(definizione, ("campo_c1", ["on"])).Errori["c1"]);
        Assert.Equal(ErroreCampoModulo.NonValido, Elabora(definizione, ("campo_c1", ["si", "si"])).Errori["c1"]);
        Assert.Equal(ErroreCampoModulo.Obbligatorio, Elabora(definizione).Errori["c1"]);
    }

    [Fact]
    public void LaSommaDeiCaratteriRicevutiHaUnTetto()
    {
        var campi = Enumerable.Range(1, 5).Select(i => Campo("c" + i, TipoCampoModulo.TestoLungo, false, 5000)).ToArray();
        var ricevuti = campi.Select(c => ("campo_" + c.Id, new[] { new string('a', 5000) })).ToArray();

        var esito = Elabora(Definizione(campi), ricevuti);

        Assert.False(esito.Valido);
        Assert.Equal(ErroreCampoModulo.TroppoLungo, esito.Errori["c1"]);
    }

    [Fact]
    public void IValoriRicevutiSiRestituisconoPerRimostrareLaForm()
    {
        var esito = Elabora(Definizione(Campo("c1", TipoCampoModulo.Email, true)), ("campo_c1", ["sbagliata"]));

        Assert.Equal(["sbagliata"], esito.ValoriRicevuti["c1"]);
    }

    [Fact]
    public void IlContenutoSiSerializzaESiRileggeSenzaPerdite()
    {
        var contenuto = new List<ValoreInviato> { new("c1", "Nome", TipoCampoModulo.TestoBreve, ["Anna <b>"]), new("c2", "Scelte", TipoCampoModulo.SceltaMultipla, ["A", "B"]) };

        var riletto = SerializzatoreModulo.LeggiContenuto(SerializzatoreModulo.ScriviContenuto(contenuto));

        Assert.Equal(contenuto.Select(c => (c.Id, c.Etichetta, c.Tipo, string.Join('|', c.Valori))), riletto.Select(c => (c.Id, c.Etichetta, c.Tipo, string.Join('|', c.Valori))));
        Assert.Empty(SerializzatoreModulo.LeggiContenuto("non json"));
    }
}
