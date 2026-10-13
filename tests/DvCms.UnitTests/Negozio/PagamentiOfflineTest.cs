using DvCms.Core.Negozio;
using DvCms.DTO.Negozio;
using DvCms.Models.Negozio;
using Xunit;

namespace DvCms.UnitTests.Negozio;

/// <summary>Regole dei pagamenti manuali (ADR-0023): IBAN, commissione del contrassegno, disponibilità per metodo di consegna, transizioni.</summary>
public class PagamentiOfflineTest
{
    private static decimal D(string valore) => decimal.Parse(valore, System.Globalization.CultureInfo.InvariantCulture);

    [Theory]
    [InlineData("IT60X0542811101000000123456")]
    [InlineData("it60 x054 2811 1010 0000 0123 456")]
    [InlineData("  IT60X0542811101000000123456  ")]
    [InlineData("DE89370400440532013000")]
    [InlineData("GB82WEST12345698765432")]
    [InlineData("FR1420041010050500013M02606")]
    public void UnIbanFormalmenteCorrettoEValido(string iban) => Assert.True(Iban.Valido(iban));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("IT60X0542811101000000123457")]
    [InlineData("IT60X054281110100000012345")]
    [InlineData("IT60X05428111010000001234567")]
    [InlineData("1260X0542811101000000123456")]
    [InlineData("IT60X0542811101000000123456; DROP TABLE")]
    [InlineData("IT60-X054-2811-1010-0000-0123-456")]
    [InlineData("XX")]
    public void UnIbanSbagliatoNonEValido(string? iban) => Assert.False(Iban.Valido(iban));

    [Fact]
    public void LIbanSiNormalizzaEsiFormattaAGruppiDiQuattro()
    {
        Assert.Equal("IT60X0542811101000000123456", Iban.Normalizza(" it60 x054 2811 1010 0000 0123 456 "));
        Assert.Null(Iban.Normalizza("  "));
        Assert.Equal("IT60 X054 2811 1010 0000 0123 456", Iban.Formatta("IT60X0542811101000000123456"));
    }

    [Theory]
    [InlineData("BCITITMM", true)]
    [InlineData("bcit itmm 123", true)]
    [InlineData("BCITITMM12", false)]
    [InlineData("BCIT1TMM", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IlBicHaOttoOUndiciCaratteri(string? bic, bool atteso) => Assert.Equal(atteso, CodiceBic.Valido(bic));

    [Fact]
    public void LaCausaleDelBonificoIdentificaLOrdine() => Assert.Equal("Ordine 1042", CausaleBonifico.Per(1042));

    [Theory]
    [InlineData("5.00", "0", null, "50.00", "5.00")]
    [InlineData("0", "2.5", null, "50.00", "1.25")]
    [InlineData("3.00", "2", null, "100.00", "5.00")]
    [InlineData("3.00", "2", null, "33.33", "3.67")]
    [InlineData("3.00", "2", "100.00", "99.99", "5.00")]
    [InlineData("3.00", "2", "100.00", "100.00", "0.00")]
    [InlineData("3.00", "2", "100.00", "250.00", "0.00")]
    [InlineData("0", "0", null, "80.00", "0.00")]
    public void LaCommissioneEParteFissaPiuPercentualeEGratisDallaSoglia(string fissa, string percentuale, string? gratisSopra, string totale, string atteso)
    {
        var condizioni = new CondizioniContrassegno(true, D(fissa), D(percentuale), gratisSopra is null ? null : D(gratisSopra), null, null);

        Assert.Equal(D(atteso), condizioni.Commissione(D(totale)));
    }

    [Theory]
    [InlineData(true, null, null, "0.01", true)]
    [InlineData(true, "20.00", null, "19.99", false)]
    [InlineData(true, "20.00", null, "20.00", true)]
    [InlineData(true, null, "500.00", "500.00", true)]
    [InlineData(true, null, "500.00", "500.01", false)]
    [InlineData(true, "20.00", "500.00", "100.00", true)]
    [InlineData(false, null, null, "100.00", false)]
    public void IlContrassegnoSiOffreSoloSeAttivoENeiLimiti(bool attivo, string? minimo, string? massimo, string totale, bool atteso)
    {
        var condizioni = new CondizioniContrassegno(attivo, 0m, 0m, null, minimo is null ? null : D(minimo), massimo is null ? null : D(massimo));

        Assert.Equal(atteso, condizioni.Disponibile(D(totale)));
    }

    [Theory]
    [InlineData(TipoMetodoSpedizione.Zona, AreaSpedizione.Italia, true)]
    [InlineData(TipoMetodoSpedizione.Zona, AreaSpedizione.UnioneEuropea, false)]
    [InlineData(TipoMetodoSpedizione.Ritiro, null, false)]
    public void IlContrassegnoVaSoloConLaConsegnaInItalia(TipoMetodoSpedizione tipo, AreaSpedizione? area, bool atteso) =>
        Assert.Equal(atteso, RegoleContrassegno.MetodoAmmesso(tipo, area));

    private static ImpostazioniPagamentoOffline Impostazioni(bool bonifico = true, bool contrassegno = true) => ImpostazioniPagamentoOffline.Spente with
    {
        BonificoAttivo = bonifico,
        ContrassegnoAttivo = contrassegno,
        CommissioneFissa = 4m,
        CommissionePercentuale = 1m,
        CommissioneGratisSopra = 150m,
        OrdineMassimo = 1000m,
    };

    [Fact]
    public void IlPreventivoDelContrassegnoAggiungeLaCommissioneAlTotale()
    {
        var preventivo = PreventivoPagamento.Calcola(Impostazioni(), ModalitaPagamento.Contrassegno, TipoMetodoSpedizione.Zona, AreaSpedizione.Italia, 50m);

        Assert.Equal(new PreventivoPagamento(true, 4.50m, 54.50m), preventivo);
    }

    [Fact]
    public void IlPreventivoDelContrassegnoNonDisponibileSeLaConsegnaNonLoAmmetteOSiSforaIlMassimo()
    {
        var impostazioni = Impostazioni();

        Assert.False(PreventivoPagamento.Calcola(impostazioni, ModalitaPagamento.Contrassegno, TipoMetodoSpedizione.Ritiro, null, 50m).Disponibile);
        Assert.False(PreventivoPagamento.Calcola(impostazioni, ModalitaPagamento.Contrassegno, TipoMetodoSpedizione.Zona, AreaSpedizione.UnioneEuropea, 50m).Disponibile);
        Assert.False(PreventivoPagamento.Calcola(impostazioni, ModalitaPagamento.Contrassegno, TipoMetodoSpedizione.Zona, AreaSpedizione.Italia, 1000.01m).Disponibile);
        Assert.False(PreventivoPagamento.Calcola(Impostazioni(contrassegno: false), ModalitaPagamento.Contrassegno, TipoMetodoSpedizione.Zona, AreaSpedizione.Italia, 50m).Disponibile);
    }

    [Fact]
    public void IlBonificoNonHaCommissioneEIlPagamentoOnlineNonHaCondizioni()
    {
        Assert.Equal(new PreventivoPagamento(true, 0m, 50m), PreventivoPagamento.Calcola(Impostazioni(), ModalitaPagamento.Bonifico, TipoMetodoSpedizione.Ritiro, null, 50m));
        Assert.False(PreventivoPagamento.Calcola(Impostazioni(bonifico: false), ModalitaPagamento.Bonifico, TipoMetodoSpedizione.Zona, AreaSpedizione.Italia, 50m).Disponibile);
        Assert.Equal(new PreventivoPagamento(true, 0m, 50m), PreventivoPagamento.Calcola(ImpostazioniPagamentoOffline.Spente, ModalitaPagamento.Online, TipoMetodoSpedizione.Zona, AreaSpedizione.Italia, 50m));
        Assert.False(PreventivoPagamento.Calcola(Impostazioni(), (ModalitaPagamento)99, TipoMetodoSpedizione.Zona, AreaSpedizione.Italia, 50m).Disponibile);
    }

    [Fact]
    public void LaCommissioneSiScorporaConLAliquotaOrdinariaEBilancia()
    {
        var riga = CalcolatoreTotali.CalcolaCommissione(4.50m);
        var zero = CalcolatoreTotali.CalcolaCommissione(0m);

        Assert.Equal((4.50m, 3.69m, 0.81m), (riga.Lordo, riga.Imponibile, riga.Iva));
        Assert.Equal((0m, 0m, 0m), (zero.Lordo, zero.Imponibile, zero.Iva));
        Assert.Throws<ArgumentOutOfRangeException>(() => CalcolatoreTotali.CalcolaCommissione(-1m));
        Assert.Throws<ArgumentOutOfRangeException>(() => CalcolatoreTotali.CalcolaCommissione(1.005m));
    }

    [Fact]
    public void LeTransizioniDeiPagamentiManualiRispettanoLaModalita()
    {
        // Contrassegno: da spedire → spedito o annullato; la consegna non riuscita (spedito → annullato) vale solo per lui.
        Assert.True(TransizioniOrdine.Consentita(StatoOrdine.DaSpedireContrassegno, StatoOrdine.Spedito, TipoMetodoSpedizione.Zona, ModalitaPagamento.Contrassegno));
        Assert.True(TransizioniOrdine.Consentita(StatoOrdine.DaSpedireContrassegno, StatoOrdine.Annullato, TipoMetodoSpedizione.Zona, ModalitaPagamento.Contrassegno));
        Assert.False(TransizioniOrdine.Consentita(StatoOrdine.DaSpedireContrassegno, StatoOrdine.Spedito, TipoMetodoSpedizione.Zona, ModalitaPagamento.Online));
        Assert.False(TransizioniOrdine.Consentita(StatoOrdine.DaSpedireContrassegno, StatoOrdine.Spedito, TipoMetodoSpedizione.Zona, ModalitaPagamento.Bonifico));
        Assert.False(TransizioniOrdine.Consentita(StatoOrdine.DaSpedireContrassegno, StatoOrdine.Ritirato, TipoMetodoSpedizione.Ritiro, ModalitaPagamento.Contrassegno));
        Assert.False(TransizioniOrdine.Consentita(StatoOrdine.DaSpedireContrassegno, StatoOrdine.Pagato, TipoMetodoSpedizione.Zona, ModalitaPagamento.Contrassegno));
        Assert.True(TransizioniOrdine.Consentita(StatoOrdine.Spedito, StatoOrdine.Annullato, TipoMetodoSpedizione.Zona, ModalitaPagamento.Contrassegno));
        Assert.False(TransizioniOrdine.Consentita(StatoOrdine.Spedito, StatoOrdine.Annullato, TipoMetodoSpedizione.Zona, ModalitaPagamento.Online));
        Assert.False(TransizioniOrdine.Consentita(StatoOrdine.Spedito, StatoOrdine.Annullato, TipoMetodoSpedizione.Zona, ModalitaPagamento.Bonifico));

        // Bonifico: in attesa → pagato o annullato, mai pagamento fallito (nessun provider che rifiuta).
        Assert.True(TransizioniOrdine.Consentita(StatoOrdine.InAttesaDiPagamento, StatoOrdine.Pagato, TipoMetodoSpedizione.Zona, ModalitaPagamento.Bonifico));
        Assert.False(TransizioniOrdine.Consentita(StatoOrdine.InAttesaDiPagamento, StatoOrdine.PagamentoFallito, TipoMetodoSpedizione.Zona, ModalitaPagamento.Bonifico));
        Assert.True(TransizioniOrdine.Consentita(StatoOrdine.InAttesaDiPagamento, StatoOrdine.PagamentoFallito, TipoMetodoSpedizione.Zona, ModalitaPagamento.Online));

        // Il cliente può annullare finché nulla è stato incassato, anche in contrassegno prima della spedizione.
        Assert.True(TransizioniOrdine.AnnullabileDalCliente(StatoOrdine.DaSpedireContrassegno));
        Assert.False(TransizioniOrdine.AnnullabileDalCliente(StatoOrdine.Spedito));
    }
}
