using DvCms.Application.Accessi;
using DvCms.Application.Negozio;
using DvCms.Application.Piattaforma;
using DvCms.Domain.Negozio;
using DvCms.IntegrationTests.Supporto;
using Xunit;

namespace DvCms.IntegrationTests.Negozio;

/// <summary>
/// Ordini pagati a bonifico o in contrassegno (ADR-0023): scelta alla cassa, commissione ricalcolata dal server, prenotazione della merce,
/// azioni dell'amministrazione, email, scadenza e isolamento.
/// </summary>
public class OrdiniPagamentoOfflineTest
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class Scenario(ContestoNegozio c, Guid variante, Guid italia, Guid europa, Guid ritiro) : IAsyncDisposable
    {
        public ContestoNegozio C { get; } = c;

        public Guid Variante { get; } = variante;

        public Guid Italia { get; } = italia;

        public Guid Europa { get; } = europa;

        public Guid Ritiro { get; } = ritiro;

        public ValueTask DisposeAsync() => C.DisposeAsync();
    }

    /// <summary>Un prodotto da 20,00 con 5 pezzi in magazzino, spedizioni Italia (5,00), UE (12,00) e ritiro; bonifico e contrassegno attivi.</summary>
    private static async Task<Scenario> PreparaAsync(bool conEmail = false, ImpostazioniPagamentoOffline? offline = null, PayPalFinto? payPal = null)
    {
        var c = await ContestoNegozio.CreaAsync(payPal, conEmail: conEmail);
        var varianti = await c.CreaProdottoAsync(c.A, "Libro", 22m, ("LIB-1", 20.00m, null));
        await c.ImpostaGiacenzaAsync(c.A, varianti[0], 5);
        var italia = await c.CreaMetodoAsync(c.A, "Corriere Italia", 5.00m, AreaSpedizione.Italia);
        var europa = await c.CreaMetodoAsync(c.A, "Corriere UE", 12.00m, AreaSpedizione.UnioneEuropea);
        var ritiro = await c.CreaRitiroAsync(c.A);
        await c.ConfiguraOfflineAsync(c.A, offline);
        return new Scenario(c, varianti[0], italia, europa, ritiro);
    }

    private static DatiOrdine Dati(Guid metodo, decimal atteso, ModalitaPagamento modalita, string paese = "IT") =>
        new(metodo, "Mario Rossi", "Via Roma 1", null, "20100", "Milano", paese, atteso, modalita);

    private static async Task<EsitoOperazione> OrdinaAsync(Scenario s, Guid metodo, decimal atteso, ModalitaPagamento modalita, int quantita = 1, string paese = "IT")
    {
        await s.C.AggiungiAsync(s.C.Cliente, s.Variante, quantita);
        return await s.C.ComeAsync(s.C.A, o => o.CreaDaCarrelloAsync(s.C.Cliente, "it", Dati(metodo, atteso, modalita, paese), Ct));
    }

    private static async Task<Guid> OrdineAsync(Scenario s, ModalitaPagamento modalita, decimal? atteso = null)
    {
        var esito = await OrdinaAsync(s, s.Italia, atteso ?? (modalita == ModalitaPagamento.Contrassegno ? 29.00m : 25.00m), modalita);
        Assert.True(esito.Riuscito, $"{esito.Codice} {string.Join(',', esito.CampiNonValidi ?? [])}");
        return esito.Id!.Value;
    }

    [Fact]
    public async Task UnOrdineConBonificoRestaInAttesaConLaMerceTrattenutaPerIGiorniImpostati()
    {
        await using var s = await PreparaAsync();
        var prima = DateTime.UtcNow;

        var id = await OrdineAsync(s, ModalitaPagamento.Bonifico);

        var ordine = await s.C.OrdineAsync(id);
        Assert.Equal((StatoOrdine.InAttesaDiPagamento, ModalitaPagamento.Bonifico, 25.00m, 0m), (ordine.Stato, ordine.ModalitaPagamento, ordine.TotaleLordo, ordine.CommissioneLorda));
        var prenotazione = Assert.Single(await s.C.PrenotazioniAsync(id));
        Assert.Equal(StatoPrenotazione.Attiva, prenotazione.Stato);
        Assert.InRange(prenotazione.ScadeIlUtc, prima.AddDays(7).AddMinutes(-1), DateTime.UtcNow.AddDays(7).AddMinutes(1));
        Assert.Equal(prenotazione.ScadeIlUtc, ordine.PrenotazioneScadeIlUtc);
        Assert.Equal((5, 1), ((await s.C.GiacenzaAsync(s.Variante)).Disponibile, (await s.C.GiacenzaAsync(s.Variante)).Riservata));
    }

    [Fact]
    public async Task UnOrdineInContrassegnoAggiungeLaCommissioneAiTotaliEResteDaSpedireSenzaScadenza()
    {
        await using var s = await PreparaAsync(offline: ContestoNegozio.ImpostazioniOffline with { CommissioneFissa = 3m, CommissionePercentuale = 2m });

        // 20,00 + 5,00 = 25,00; commissione 3,00 + 2% di 25,00 = 3,50; totale 28,50.
        var id = await OrdineAsync(s, ModalitaPagamento.Contrassegno, 28.50m);

        var vista = (await s.C.ComeAsync(s.C.A, o => o.LeggiMioAsync(s.C.Cliente, id, Ct)))!;
        Assert.Equal((StatoOrdine.DaSpedireContrassegno, ModalitaPagamento.Contrassegno, 28.50m), (vista.Stato, vista.ModalitaPagamento, vista.TotaleLordo));
        Assert.Equal((3.50m, 2.87m, 0.63m), (vista.CommissioneLorda, vista.CommissioneImponibile, vista.CommissioneIva));
        Assert.Equal(vista.TotaleLordo, vista.TotaleImponibile + vista.TotaleIva);
        Assert.Equal(vista.TotaleLordo, vista.Righe.Sum(r => r.Lordo) + vista.SpedizioneLorda + vista.CommissioneLorda);
        Assert.Null(vista.PrenotazioneScadeIlUtc);
        Assert.Equal(PrenotazioneGiacenza.SenzaScadenza, Assert.Single(await s.C.PrenotazioniAsync(id)).ScadeIlUtc);
    }

    [Fact]
    public async Task LaPrenotazioneDelContrassegnoNonScadeMaQuellaDelBonificoSiDopoIGiorni()
    {
        await using var s = await PreparaAsync();
        var bonifico = await OrdineAsync(s, ModalitaPagamento.Bonifico);
        var contrassegno = await OrdineAsync(s, ModalitaPagamento.Contrassegno);

        await s.C.ScadiPrenotazioniAsync(bonifico);
        await s.C.ScadiPrenotazioniAsync(contrassegno);
        var annullati = await s.C.ComeGiacenzeAsync(s.C.A, g => g.RilasciaScaduteAsync(Ct));

        Assert.Equal(1, annullati);
        Assert.Equal(StatoOrdine.Annullato, await s.C.StatoOrdineAsync(bonifico));
        Assert.Equal(StatoOrdine.DaSpedireContrassegno, await s.C.StatoOrdineAsync(contrassegno));
        Assert.Equal(1, (await s.C.GiacenzaAsync(s.Variante)).Riservata);
    }

    [Fact]
    public async Task IlContrassegnoSenzaPrenotazioneScadutaNonSiTocca()
    {
        await using var s = await PreparaAsync();
        var id = await OrdineAsync(s, ModalitaPagamento.Contrassegno);

        Assert.Equal(0, await s.C.ComeGiacenzeAsync(s.C.A, g => g.RilasciaScaduteAsync(Ct)));
        Assert.Equal(StatoOrdine.DaSpedireContrassegno, await s.C.StatoOrdineAsync(id));
    }

    [Fact]
    public async Task ILimitiEIlMetodoDiConsegnaDecidonoSeIlContrassegnoSiOffre()
    {
        await using var s = await PreparaAsync(offline: ContestoNegozio.ImpostazioniOffline with { OrdineMinimo = 20m, OrdineMassimo = 60m });

        // Fuori dall'Italia e per il ritiro: mai. Sopra il massimo (2 libri = 40 + 5 = 45 va, 4 libri = 80 + 5 = 85 no).
        var ue = await OrdinaAsync(s, s.Europa, 36m, ModalitaPagamento.Contrassegno, paese: "FR");
        await s.C.ComeCarrelloAsync(s.C.A, c => c.SvuotaAsync(s.C.Cliente, Ct));
        var ritiro = await OrdinaAsync(s, s.Ritiro, 24m, ModalitaPagamento.Contrassegno);
        await s.C.ComeCarrelloAsync(s.C.A, c => c.SvuotaAsync(s.C.Cliente, Ct));
        var troppo = await OrdinaAsync(s, s.Italia, 93m, ModalitaPagamento.Contrassegno, quantita: 4);
        await s.C.ComeCarrelloAsync(s.C.A, c => c.SvuotaAsync(s.C.Cliente, Ct));
        var giusto = await OrdinaAsync(s, s.Italia, 49m, ModalitaPagamento.Contrassegno, quantita: 2);

        foreach (var rifiutato in new[] { ue, ritiro, troppo })
        {
            Assert.Equal(CodiceEsito.DatiNonValidi, rifiutato.Codice);
            Assert.Contains("ModalitaPagamento", rifiutato.CampiNonValidi!);
        }

        Assert.True(giusto.Riuscito, $"{giusto.Codice} {string.Join(',', giusto.CampiNonValidi ?? [])}");
        Assert.Equal(1, await s.C.ContaOrdiniAsync());
    }

    [Fact]
    public async Task LaCommissioneEGratisDallaSogliaEPuoEssereSoloPercentuale()
    {
        await using var s = await PreparaAsync(offline: ContestoNegozio.ImpostazioniOffline with { CommissioneFissa = 4m, CommissionePercentuale = 1m, CommissioneGratisSopra = 40m });

        // 25,00 → 4,00 + 0,25 = 4,25; con 2 libri 45,00 ≥ soglia → gratis.
        var sotto = await OrdinaAsync(s, s.Italia, 29.25m, ModalitaPagamento.Contrassegno);
        await s.C.ComeCarrelloAsync(s.C.A, c => c.SvuotaAsync(s.C.Cliente, Ct));
        var sopra = await OrdinaAsync(s, s.Italia, 45m, ModalitaPagamento.Contrassegno, quantita: 2);

        Assert.True(sotto.Riuscito && sopra.Riuscito);
        Assert.Equal(4.25m, (await s.C.OrdineAsync(sotto.Id!.Value)).CommissioneLorda);
        Assert.Equal(0m, (await s.C.OrdineAsync(sopra.Id!.Value)).CommissioneLorda);
    }

    [Fact]
    public async Task IlTotaleVistoDalClienteDeveComprendereLaCommissioneAltrimentiConflitto()
    {
        await using var s = await PreparaAsync();

        // Senza la commissione di 4,00 il totale (25,00) non è quello che il server ricalcola (29,00).
        var senza = await OrdinaAsync(s, s.Italia, 25.00m, ModalitaPagamento.Contrassegno);
        var con = await s.C.ComeAsync(s.C.A, o => o.CreaDaCarrelloAsync(s.C.Cliente, "it", Dati(s.Italia, 29.00m, ModalitaPagamento.Contrassegno), Ct));

        Assert.Equal(CodiceEsito.Conflitto, senza.Codice);
        Assert.Contains("Totale", senza.CampiNonValidi!);
        Assert.True(con.Riuscito);
    }

    [Fact]
    public async Task UnPagamentoNonAttivoONonValidoSiRifiutaSenzaCreareOrdini()
    {
        await using var s = await PreparaAsync(offline: ContestoNegozio.ImpostazioniOffline with { BonificoAttivo = false, ContrassegnoAttivo = false });

        var bonifico = await OrdinaAsync(s, s.Italia, 25m, ModalitaPagamento.Bonifico);
        var contrassegno = await s.C.ComeAsync(s.C.A, o => o.CreaDaCarrelloAsync(s.C.Cliente, "it", Dati(s.Italia, 29m, ModalitaPagamento.Contrassegno), Ct));
        var inventato = await s.C.ComeAsync(s.C.A, o => o.CreaDaCarrelloAsync(s.C.Cliente, "it", Dati(s.Italia, 25m, (ModalitaPagamento)42), Ct));
        var online = await s.C.ComeAsync(s.C.A, o => o.CreaDaCarrelloAsync(s.C.Cliente, "it", Dati(s.Italia, 25m, ModalitaPagamento.Online), Ct));

        Assert.All([bonifico, contrassegno, inventato], e => Assert.Equal((CodiceEsito.DatiNonValidi, "ModalitaPagamento"), (e.Codice, e.CampiNonValidi!.Single())));
        Assert.True(online.Riuscito);
        Assert.Equal(1, await s.C.ContaOrdiniAsync());
    }

    [Fact]
    public async Task IlBonificoVaBeneAncheConIlRitiroEUnCartellinoVecchioSenzaModalitaEOnline()
    {
        await using var s = await PreparaAsync();
        await s.C.AggiungiAsync(s.C.Cliente, s.Variante, 1);

        var ritiro = await s.C.ComeAsync(s.C.A, o => o.CreaDaCarrelloAsync(s.C.Cliente, "it", Dati(s.Ritiro, 20m, ModalitaPagamento.Bonifico), Ct));
        Assert.True(ritiro.Riuscito);

        await s.C.AggiungiAsync(s.C.Cliente, s.Variante, 1);
        var vecchio = await s.C.ComeAsync(s.C.A, o => o.CreaDaCarrelloAsync(s.C.Cliente, "it", new DatiOrdine(s.Italia, "Mario Rossi", "Via Roma 1", null, "20100", "Milano", "IT", 25m), Ct));
        Assert.True(vecchio.Riuscito);
        Assert.Equal((ModalitaPagamento.Online, StatoOrdine.InAttesaDiPagamento), ((await s.C.OrdineAsync(vecchio.Id!.Value)).ModalitaPagamento, await s.C.StatoOrdineAsync(vecchio.Id!.Value)));
    }

    [Fact]
    public async Task LAmministratoreSegnaPagatoUnBonificoLaMerceEsceEIlClienteRiceveLaConferma()
    {
        await using var s = await PreparaAsync(conEmail: true);
        var id = await OrdineAsync(s, ModalitaPagamento.Bonifico);
        await s.C.ElaboraEmailAsync();

        var esito = await s.C.ComeAsync(s.C.A, o => o.SegnaPagatoAsync(s.C.AdminA, id, Ct));

        Assert.True(esito.Riuscito, esito.Codice.ToString());
        Assert.Equal(StatoOrdine.Pagato, await s.C.StatoOrdineAsync(id));
        var giacenza = await s.C.GiacenzaAsync(s.Variante);
        Assert.Equal((4, 0), (giacenza.Disponibile, giacenza.Riservata));
        Assert.Equal(StatoPrenotazione.Confermata, Assert.Single(await s.C.PrenotazioniAsync(id)).Stato);
        var email = Assert.Single(await s.C.ElaboraEmailAsync());
        Assert.Equal(["cliente@esempio.it"], email.Destinatari.Select(d => d.Trim('<', '>')));
        Assert.Contains("confermato", email.Oggetto, StringComparison.Ordinal);
        Assert.Single(await s.C.AuditAsync(AzioniAudit.PagamentoOfflineRicevuto));

        // Una seconda volta non fa nulla; poi l'ordine si evade come uno pagato online.
        Assert.Equal(CodiceEsito.NonConsentito, (await s.C.ComeAsync(s.C.A, o => o.SegnaPagatoAsync(s.C.AdminA, id, Ct))).Codice);
        Assert.True((await s.C.ComeAsync(s.C.A, o => o.SegnaEvasoAsync(s.C.AdminA, id, Ct))).Riuscito);
        Assert.Equal(StatoOrdine.Spedito, await s.C.StatoOrdineAsync(id));
    }

    [Fact]
    public async Task SegnaPagatoVaSoloPerIlBonificoInAttesaEDaChiPuo()
    {
        await using var s = await PreparaAsync();
        var bonifico = await OrdineAsync(s, ModalitaPagamento.Bonifico);
        var contrassegno = await OrdineAsync(s, ModalitaPagamento.Contrassegno);
        var online = await OrdineAsync(s, ModalitaPagamento.Online);

        Assert.Equal(CodiceEsito.NonConsentito, (await s.C.ComeAsync(s.C.A, o => o.SegnaPagatoAsync(s.C.AdminA, contrassegno, Ct))).Codice);
        Assert.Equal(CodiceEsito.NonConsentito, (await s.C.ComeAsync(s.C.A, o => o.SegnaPagatoAsync(s.C.AdminA, online, Ct))).Codice);
        Assert.Equal(CodiceEsito.NonAutorizzato, (await s.C.ComeAsync(s.C.A, o => o.SegnaPagatoAsync(s.C.Cliente, bonifico, Ct))).Codice);
        Assert.Equal(CodiceEsito.NonAutorizzato, (await s.C.ComeAsync(s.C.A, o => o.SegnaPagatoAsync(s.C.AdminB, bonifico, Ct))).Codice);
        Assert.Equal(CodiceEsito.NonTrovato, (await s.C.ComeAsync(s.C.A, o => o.SegnaPagatoAsync(s.C.AdminA, Guid.NewGuid(), Ct))).Codice);
        Assert.Equal(StatoOrdine.InAttesaDiPagamento, await s.C.StatoOrdineAsync(bonifico));
        Assert.Equal(StatoOrdine.DaSpedireContrassegno, await s.C.StatoOrdineAsync(contrassegno));
        Assert.Equal(StatoOrdine.InAttesaDiPagamento, await s.C.StatoOrdineAsync(online));
    }

    [Fact]
    public async Task UnBonificoArrivatoDopoLaScadenzaNonRendePagatoUnOrdineAnnullato()
    {
        await using var s = await PreparaAsync();
        var id = await OrdineAsync(s, ModalitaPagamento.Bonifico);
        await s.C.ScadiPrenotazioniAsync(id);
        await s.C.ComeGiacenzeAsync(s.C.A, g => g.RilasciaScaduteAsync(Ct));

        var esito = await s.C.ComeAsync(s.C.A, o => o.SegnaPagatoAsync(s.C.AdminA, id, Ct));

        Assert.Equal(CodiceEsito.NonConsentito, esito.Codice);
        Assert.Equal(StatoOrdine.Annullato, await s.C.StatoOrdineAsync(id));
        Assert.Equal((5, 0), ((await s.C.GiacenzaAsync(s.Variante)).Disponibile, (await s.C.GiacenzaAsync(s.Variante)).Riservata));
    }

    [Fact]
    public async Task LAmministratoreAnnullaUnOrdineManualeNonIncassatoEliberaLaMerce()
    {
        await using var s = await PreparaAsync();
        var bonifico = await OrdineAsync(s, ModalitaPagamento.Bonifico);
        var contrassegno = await OrdineAsync(s, ModalitaPagamento.Contrassegno);
        Assert.Equal(2, (await s.C.GiacenzaAsync(s.Variante)).Riservata);

        var primo = await s.C.ComeAsync(s.C.A, o => o.AnnullaAsync(s.C.AdminA, bonifico, Ct));
        var secondo = await s.C.ComeAsync(s.C.A, o => o.AnnullaAsync(s.C.AdminA, contrassegno, Ct));
        var ancora = await s.C.ComeAsync(s.C.A, o => o.AnnullaAsync(s.C.AdminA, bonifico, Ct));

        Assert.True(primo.Riuscito && secondo.Riuscito);
        Assert.Equal(CodiceEsito.NonConsentito, ancora.Codice);
        Assert.Equal((StatoOrdine.Annullato, StatoOrdine.Annullato), (await s.C.StatoOrdineAsync(bonifico), await s.C.StatoOrdineAsync(contrassegno)));
        Assert.Equal((5, 0), ((await s.C.GiacenzaAsync(s.Variante)).Disponibile, (await s.C.GiacenzaAsync(s.Variante)).Riservata));
        Assert.Equal(2, (await s.C.AuditAsync(AzioniAudit.AnnullamentoOrdineAmministrazione)).Count);
    }

    [Fact]
    public async Task LAnnullamentoDallAmministrazioneNonVaPerOrdiniOnlineEPerChiNonPuo()
    {
        await using var s = await PreparaAsync();
        var online = await OrdineAsync(s, ModalitaPagamento.Online);
        var bonifico = await OrdineAsync(s, ModalitaPagamento.Bonifico);

        Assert.Equal(CodiceEsito.NonConsentito, (await s.C.ComeAsync(s.C.A, o => o.AnnullaAsync(s.C.AdminA, online, Ct))).Codice);
        Assert.Equal(CodiceEsito.NonAutorizzato, (await s.C.ComeAsync(s.C.A, o => o.AnnullaAsync(s.C.Cliente, bonifico, Ct))).Codice);
        Assert.Equal(CodiceEsito.NonAutorizzato, (await s.C.ComeAsync(s.C.A, o => o.AnnullaAsync(s.C.AdminB, bonifico, Ct))).Codice);

        await s.C.ComeAsync(s.C.A, o => o.SegnaPagatoAsync(s.C.AdminA, bonifico, Ct));
        Assert.Equal(CodiceEsito.NonConsentito, (await s.C.ComeAsync(s.C.A, o => o.AnnullaAsync(s.C.AdminA, bonifico, Ct))).Codice);
    }

    [Fact]
    public async Task IlContrassegnoSiSpedisceLaMerceEsceEUnaConsegnaFallitaAnnullaLOrdine()
    {
        await using var s = await PreparaAsync();
        var id = await OrdineAsync(s, ModalitaPagamento.Contrassegno);

        // Prima della spedizione la consegna non può "fallire"; poi spedito: la merce esce dal magazzino.
        Assert.Equal(CodiceEsito.NonConsentito, (await s.C.ComeAsync(s.C.A, o => o.SegnaConsegnaNonRiuscitaAsync(s.C.AdminA, id, Ct))).Codice);
        var spedito = await s.C.ComeAsync(s.C.A, o => o.SegnaEvasoAsync(s.C.AdminA, id, Ct));

        Assert.True(spedito.Riuscito, spedito.Codice.ToString());
        Assert.Equal(StatoOrdine.Spedito, await s.C.StatoOrdineAsync(id));
        Assert.Equal((4, 0), ((await s.C.GiacenzaAsync(s.Variante)).Disponibile, (await s.C.GiacenzaAsync(s.Variante)).Riservata));
        Assert.Equal(StatoPrenotazione.Confermata, Assert.Single(await s.C.PrenotazioniAsync(id)).Stato);
        Assert.Equal(-1, Assert.Single(await s.C.MovimentiAsync(s.Variante), m => m.Motivo == MotivoMovimento.Vendita).Variazione);
        Assert.Equal(CodiceEsito.NonConsentito, (await s.C.ComeAsync(s.C.A, o => o.AnnullaAsync(s.C.AdminA, id, Ct))).Codice);

        var fallita = await s.C.ComeAsync(s.C.A, o => o.SegnaConsegnaNonRiuscitaAsync(s.C.AdminA, id, Ct));
        var ripetuta = await s.C.ComeAsync(s.C.A, o => o.SegnaConsegnaNonRiuscitaAsync(s.C.AdminA, id, Ct));

        Assert.True(fallita.Riuscito);
        Assert.Equal(CodiceEsito.NonConsentito, ripetuta.Codice);
        Assert.Equal(StatoOrdine.Annullato, await s.C.StatoOrdineAsync(id));
        Assert.Single(await s.C.AuditAsync(AzioniAudit.ConsegnaNonRiuscita));
    }

    [Fact]
    public async Task LaConsegnaNonRiuscitaVaSoloPerIlContrassegnoSpeditoEDaChiPuo()
    {
        await using var s = await PreparaAsync();
        var bonifico = await OrdineAsync(s, ModalitaPagamento.Bonifico);
        await s.C.ComeAsync(s.C.A, o => o.SegnaPagatoAsync(s.C.AdminA, bonifico, Ct));
        await s.C.ComeAsync(s.C.A, o => o.SegnaEvasoAsync(s.C.AdminA, bonifico, Ct));
        var contrassegno = await OrdineAsync(s, ModalitaPagamento.Contrassegno);
        await s.C.ComeAsync(s.C.A, o => o.SegnaEvasoAsync(s.C.AdminA, contrassegno, Ct));

        Assert.Equal(CodiceEsito.NonConsentito, (await s.C.ComeAsync(s.C.A, o => o.SegnaConsegnaNonRiuscitaAsync(s.C.AdminA, bonifico, Ct))).Codice);
        Assert.Equal(CodiceEsito.NonAutorizzato, (await s.C.ComeAsync(s.C.A, o => o.SegnaConsegnaNonRiuscitaAsync(s.C.Cliente, contrassegno, Ct))).Codice);
        Assert.Equal(CodiceEsito.NonAutorizzato, (await s.C.ComeAsync(s.C.A, o => o.SegnaConsegnaNonRiuscitaAsync(s.C.AdminB, contrassegno, Ct))).Codice);
        Assert.Equal(StatoOrdine.Spedito, await s.C.StatoOrdineAsync(bonifico));
        Assert.Equal(StatoOrdine.Spedito, await s.C.StatoOrdineAsync(contrassegno));
    }

    [Fact]
    public async Task IlClientePuoAnnullareUnContrassegnoPrimaDellaSpedizioneNonDopo()
    {
        await using var s = await PreparaAsync();
        var prima = await OrdineAsync(s, ModalitaPagamento.Contrassegno);
        var dopo = await OrdineAsync(s, ModalitaPagamento.Contrassegno);
        await s.C.ComeAsync(s.C.A, o => o.SegnaEvasoAsync(s.C.AdminA, dopo, Ct));

        var annullato = await s.C.ComeAsync(s.C.A, o => o.AnnullaMioAsync(s.C.Cliente, prima, Ct));
        var rifiutato = await s.C.ComeAsync(s.C.A, o => o.AnnullaMioAsync(s.C.Cliente, dopo, Ct));

        Assert.True(annullato.Riuscito);
        Assert.Equal(CodiceEsito.NonConsentito, rifiutato.Codice);
        Assert.Equal((4, 0), ((await s.C.GiacenzaAsync(s.Variante)).Disponibile, (await s.C.GiacenzaAsync(s.Variante)).Riservata));
    }

    [Fact]
    public async Task UnOrdineManualeNonPassaDaUnProviderOnline()
    {
        var payPal = new PayPalFinto();
        await using var s = await PreparaAsync(payPal: payPal);
        await s.C.ConfiguraPagamentoAsync(s.C.A);
        var bonifico = await OrdineAsync(s, ModalitaPagamento.Bonifico);
        var contrassegno = await OrdineAsync(s, ModalitaPagamento.Contrassegno);

        var uno = await s.C.ComePagamentiAsync(s.C.A, p => p.AvviaAsync(s.C.Cliente, bonifico, NomiProviderPagamento.PayPal, ContestoNegozio.Link, Ct));
        var due = await s.C.ComePagamentiAsync(s.C.A, p => p.AvviaAsync(s.C.Cliente, contrassegno, NomiProviderPagamento.PayPal, ContestoNegozio.Link, Ct));

        Assert.Equal((RisultatoPagamento.OrdineChiuso, RisultatoPagamento.OrdineChiuso), (uno.Risultato, due.Risultato));
        Assert.Equal(0, payPal.Conta(HttpMethod.Post, "/v2/checkout/orders"));
        Assert.Empty(await s.C.TentativiAsync(bonifico));
    }

    [Fact]
    public async Task IlBonificoInviaAlClienteLeIstruzioniEAlTenantLAvviso()
    {
        await using var s = await PreparaAsync(conEmail: true);
        var id = await OrdineAsync(s, ModalitaPagamento.Bonifico);

        var email = await s.C.ElaboraEmailAsync();

        var cliente = Assert.Single(email, e => e.Oggetto.Contains("bonifico", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(["cliente@esempio.it"], cliente.Destinatari.Select(d => d.Trim('<', '>')));
        foreach (var atteso in new[] { "25,00 EUR", "Negozio Srl", "IT60 X054 2811 1010 0000 0123 456", "BCITITMM", "Banca di Prova", "Causale: Ordine 1", "Indica l'ordine nella causale.", $"https://a.esempio.it/account/ordini/{id:D}" })
        {
            Assert.Contains(atteso, cliente.Testo, StringComparison.Ordinal);
        }

        var avviso = Assert.Single(email, e => e.Oggetto.Contains("Nuovo ordine", StringComparison.Ordinal));
        Assert.Equal(["anna@esempio.it"], avviso.Destinatari.Select(d => d.Trim('<', '>')));
        Assert.Contains("in attesa di bonifico", avviso.Testo, StringComparison.Ordinal);
    }

    [Fact]
    public async Task IlContrassegnoInviaAlClienteLaConfermaConLaCommissioneEAlTenantLAvviso()
    {
        await using var s = await PreparaAsync(conEmail: true);
        await OrdineAsync(s, ModalitaPagamento.Contrassegno);

        var email = await s.C.ElaboraEmailAsync();

        var cliente = Assert.Single(email, e => e.Oggetto.Contains("ricevuto", StringComparison.Ordinal));
        Assert.Contains("Pagherai 29,00 EUR alla consegna", cliente.Testo, StringComparison.Ordinal);
        Assert.Contains("Commissione contrassegno: 4,00 EUR", cliente.Testo, StringComparison.Ordinal);
        Assert.Contains("Tieni pronto il contante.", cliente.Testo, StringComparison.Ordinal);
        Assert.DoesNotContain("IBAN", cliente.Testo, StringComparison.Ordinal);
        Assert.Single(email, e => e.Oggetto.Contains("Nuovo ordine", StringComparison.Ordinal) && e.Testo.Contains("in contrassegno", StringComparison.Ordinal));
    }

    [Fact]
    public async Task UnOrdineOnlineNonInviaNullaAllaCreazioneESenzaCanaleEmailNonSiAccodaNulla()
    {
        await using var s = await PreparaAsync(conEmail: true);
        await OrdineAsync(s, ModalitaPagamento.Online);
        Assert.Equal(0, await s.C.ContaMessaggiAsync());

        await using var senza = await PreparaAsync(conEmail: false);
        await OrdineAsync(senza, ModalitaPagamento.Bonifico);
        Assert.Equal(0, await senza.C.ContaMessaggiAsync());
    }

    [Fact]
    public async Task LAvvisoAlTenantSiPuoSpegnereMaLaConfermaAlClienteResta()
    {
        await using var c = await ContestoNegozio.CreaAsync(conEmail: true, avvisoAmministratori: false);
        var varianti = await c.CreaProdottoAsync(c.A, "Libro", 22m, ("LIB-1", 20.00m, null));
        var italia = await c.CreaMetodoAsync(c.A, "Corriere Italia", 5.00m, AreaSpedizione.Italia);
        await c.ConfiguraOfflineAsync(c.A);
        await c.AggiungiAsync(c.Cliente, varianti[0], 1);

        var esito = await c.ComeAsync(c.A, o => o.CreaDaCarrelloAsync(c.Cliente, "it", Dati(italia, 29m, ModalitaPagamento.Contrassegno), Ct));

        Assert.True(esito.Riuscito);
        var email = Assert.Single(await c.ElaboraEmailAsync());
        Assert.Equal(["cliente@esempio.it"], email.Destinatari.Select(d => d.Trim('<', '>')));
    }

    [Fact]
    public async Task LEsportazioneRiportaPagamentoECommissioneEUnaRigaPerLaCommissione()
    {
        await using var s = await PreparaAsync();
        await OrdineAsync(s, ModalitaPagamento.Contrassegno);

        var ordini = await s.C.ComeEsportazioneAsync(s.C.A, e => e.EsportaAsync(s.C.AdminA, new FiltroEsportazioneOrdini(null, null, null, FormatoEsportazioneOrdini.Ordini), Ct));
        var righe = await s.C.ComeEsportazioneAsync(s.C.A, e => e.EsportaAsync(s.C.AdminA, new FiltroEsportazioneOrdini(null, null, null, FormatoEsportazioneOrdini.Righe), Ct));

        var testoOrdini = System.Text.Encoding.UTF8.GetString(ordini.Contenuto).TrimStart('\uFEFF').Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        var testoRighe = System.Text.Encoding.UTF8.GetString(righe.Contenuto).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(["Pagamento", "CommissioneLorda"], testoOrdini[0].Split(';')[^2..]);
        var campi = testoOrdini[1].Split(';');
        Assert.Equal(("Contrassegno", "4,00"), (campi[^2], campi[^1]));
        Assert.Contains(testoRighe, r => r.Contains(";Commissione;", StringComparison.Ordinal) && r.Contains("Contrassegno", StringComparison.Ordinal) && r.Contains("4,00", StringComparison.Ordinal));
    }

    [Fact]
    public async Task LAnonimizzazioneDelClienteAnnullaAncheIContrassegniDaSpedireELiberaLaMerce()
    {
        await using var s = await PreparaAsync();
        var contrassegno = await OrdineAsync(s, ModalitaPagamento.Contrassegno);
        var bonifico = await OrdineAsync(s, ModalitaPagamento.Bonifico);

        var esito = await s.C.ComeDatiPersonaliAsync(s.C.A, d => d.AnonimizzaAcquirenteDiOrdineAsync(s.C.AdminA, contrassegno, Ct));

        Assert.True(esito.Riuscito, esito.Codice.ToString());
        Assert.Equal((StatoOrdine.Annullato, StatoOrdine.Annullato), (await s.C.StatoOrdineAsync(contrassegno), await s.C.StatoOrdineAsync(bonifico)));
        Assert.Equal((5, 0), ((await s.C.GiacenzaAsync(s.Variante)).Disponibile, (await s.C.GiacenzaAsync(s.Variante)).Riservata));
    }

    [Fact]
    public async Task UnTenantNonVedeNeUsaIPagamentiManualiDiUnAltro()
    {
        await using var s = await PreparaAsync();
        var varianti = await s.C.CreaProdottoAsync(s.C.B, "Libro B", 22m, ("LIB-B", 20.00m, null));
        var italiaB = await s.C.CreaMetodoAsync(s.C.B, "Corriere B", 5.00m, AreaSpedizione.Italia);
        await s.C.AggiungiAsync(s.C.ClienteDiB, varianti[0], 1, s.C.B);

        // B non ha attivato nulla: i pagamenti di A non valgono per lui.
        var bonificoB = await s.C.ComeAsync(s.C.B, o => o.CreaDaCarrelloAsync(s.C.ClienteDiB, "it", Dati(italiaB, 25m, ModalitaPagamento.Bonifico), Ct));
        var contrassegnoB = await s.C.ComeAsync(s.C.B, o => o.CreaDaCarrelloAsync(s.C.ClienteDiB, "it", Dati(italiaB, 29m, ModalitaPagamento.Contrassegno), Ct));

        Assert.Equal(CodiceEsito.DatiNonValidi, bonificoB.Codice);
        Assert.Equal(CodiceEsito.DatiNonValidi, contrassegnoB.Codice);

        // L'amministratore di B non agisce sugli ordini di A.
        var ordineA = await OrdineAsync(s, ModalitaPagamento.Bonifico);
        Assert.Equal(CodiceEsito.NonTrovato, (await s.C.ComeAsync(s.C.B, o => o.SegnaPagatoAsync(s.C.AdminB, ordineA, Ct))).Codice);
        Assert.Equal(StatoOrdine.InAttesaDiPagamento, await s.C.StatoOrdineAsync(ordineA));
    }
}
