using DvCms.Application.Negozio;
using DvCms.Application.Piattaforma;
using DvCms.Application.Tenancy;
using DvCms.Domain.Accessi;
using DvCms.Domain.Negozio;
using DvCms.Infrastructure.Persistenza;
using DvCms.IntegrationTests.Supporto;
using DvCms.IntegrationTests.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DvCms.IntegrationTests.Negozio;

/// <summary>Ordini (F-003): snapshot, totali ricalcolati dal server, indirizzo e area, stati, proprietà e isolamento fra tenant.</summary>
public class ServizioOrdiniTest
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static DatiOrdine Consegna(Guid metodo, decimal atteso, string paese = "IT", string? cap = "20100") =>
        new(metodo, "Mario Rossi", "Via Roma 1", "Scala B", cap, "Milano", paese, atteso);

    [Fact]
    public async Task OrdineCopiaRigheImportiSpedizioneEIndirizzoESvuotaIlCarrello()
    {
        await using var c = await Contesto.CreaAsync();
        var maglietta = await c.CreaProdottoAsync(c.A, "Maglietta", 22m, ("TS-M", 19.90m, "Taglia M"));
        var libro = await c.CreaProdottoAsync(c.A, "Libro", 4m, ("LIB-1", 12.00m, null));
        var corriere = await c.CreaMetodoAsync(c.A, "Corriere Italia", 6.90m, AreaSpedizione.Italia);
        await c.AggiungiAsync(c.Cliente, maglietta[0], 3);
        await c.AggiungiAsync(c.Cliente, libro[0], 1);

        // 59,70 + 12,00 + spedizione 6,90 = 78,60.
        var esito = await c.ComeAsync(c.A, s => s.CreaDaCarrelloAsync(c.Cliente, "it", Consegna(corriere, 78.60m), Ct));

        Assert.True(esito.Riuscito, $"{esito.Codice} {string.Join(',', esito.CampiNonValidi ?? [])}");
        var ordine = (await c.ComeAsync(c.A, s => s.LeggiMioAsync(c.Cliente, esito.Id!.Value, Ct)))!;
        Assert.Equal((1, StatoOrdine.InAttesaDiPagamento, "EUR"), (ordine.Numero, ordine.Stato, ordine.Valuta));
        Assert.Equal((78.60m, 48.93m + 11.54m + 5.66m, 10.77m + 0.46m + 1.24m), (ordine.TotaleLordo, ordine.TotaleImponibile, ordine.TotaleIva));
        Assert.Equal(ordine.TotaleLordo, ordine.TotaleImponibile + ordine.TotaleIva);
        Assert.Equal(["TS-M", "LIB-1"], ordine.Righe.Select(r => r.Sku));
        Assert.Equal((3, 19.90m, 22m, 59.70m, 48.93m, 10.77m), (ordine.Righe[0].Quantita, ordine.Righe[0].PrezzoUnitarioLordo, ordine.Righe[0].AliquotaIva, ordine.Righe[0].Lordo, ordine.Righe[0].Imponibile, ordine.Righe[0].Iva));
        Assert.Equal("Taglia M", ordine.Righe[0].EtichettaVariante);
        Assert.Equal(("Corriere Italia", TipoMetodoSpedizione.Zona, 6.90m, 5.66m, 1.24m), (ordine.NomeSpedizione, ordine.TipoSpedizione, ordine.SpedizioneLorda, ordine.SpedizioneImponibile, ordine.SpedizioneIva));
        Assert.Equal(("Mario Rossi", "Via Roma 1", "Scala B", "20100", "Milano", "IT"), (ordine.Destinatario, ordine.IndirizzoRiga1, ordine.IndirizzoRiga2, ordine.Cap, ordine.Citta, ordine.Paese));
        Assert.Equal("cliente@esempio.it", ordine.EmailAcquirente);
        Assert.Empty((await c.ComeCarrelloAsync(c.A, s => s.LeggiAsync(c.Cliente, "it", Ct))).Righe);
    }

    [Fact]
    public async Task LoSnapshotNonCambiaSeCatalogoESpedizioneCambianoOSpariscono()
    {
        await using var c = await Contesto.CreaAsync();
        var varianti = await c.CreaProdottoAsync(c.A, "Maglietta", 22m, ("TS-M", 19.90m, null));
        var metodo = await c.CreaMetodoAsync(c.A, "Corriere", 5m, AreaSpedizione.Italia);
        await c.AggiungiAsync(c.Cliente, varianti[0], 1);
        var esito = await c.ComeAsync(c.A, s => s.CreaDaCarrelloAsync(c.Cliente, "it", Consegna(metodo, 24.90m), Ct));
        Assert.True(esito.Riuscito);

        await c.CambiaPrezzoAsync(c.A, "TS-M", 99m);
        await c.EliminaMetodoAsync(c.A, metodo);
        await c.TogliVarianteAsync(c.A, "TS-M");
        var ordine = (await c.ComeAsync(c.A, s => s.LeggiMioAsync(c.Cliente, esito.Id!.Value, Ct)))!;

        Assert.Equal(24.90m, ordine.TotaleLordo);
        Assert.Equal((19.90m, "TS-M", "Corriere"), (ordine.Righe.Single().PrezzoUnitarioLordo, ordine.Righe.Single().Sku, ordine.NomeSpedizione));
    }

    [Fact]
    public async Task IlRitiroNonHaIndirizzoNeCostoEUnIndirizzoInviatoSiScarta()
    {
        await using var c = await Contesto.CreaAsync();
        var varianti = await c.CreaProdottoAsync(c.A, "Libro", 4m, ("LIB-1", 12.00m, null));
        var ritiro = await c.CreaRitiroAsync(c.A);
        await c.AggiungiAsync(c.Cliente, varianti[0], 2);

        var esito = await c.ComeAsync(c.A, s => s.CreaDaCarrelloAsync(c.Cliente, "it", Consegna(ritiro, 24.00m, paese: "FR"), Ct));

        Assert.True(esito.Riuscito, $"{esito.Codice} {string.Join(',', esito.CampiNonValidi ?? [])}");
        var ordine = (await c.ComeAsync(c.A, s => s.LeggiMioAsync(c.Cliente, esito.Id!.Value, Ct)))!;
        Assert.Equal((TipoMetodoSpedizione.Ritiro, 0m, 24.00m), (ordine.TipoSpedizione, ordine.SpedizioneLorda, ordine.TotaleLordo));
        Assert.Null(ordine.Destinatario);
        Assert.Null(ordine.Paese);
        Assert.Null(ordine.AreaSpedizione);
    }

    [Theory]
    [InlineData("FR", "20100", "Paese")]
    [InlineData("it", "20100", null)]
    [InlineData("", "20100", "Paese")]
    [InlineData("IT", "", "Cap")]
    [InlineData("IT", "<script>", "Cap")]
    public async Task LIndirizzoDeveStareNellAreaDelMetodo(string paese, string cap, string? campo)
    {
        await using var c = await Contesto.CreaAsync();
        var varianti = await c.CreaProdottoAsync(c.A, "Libro", 4m, ("LIB-1", 10.00m, null));
        var metodo = await c.CreaMetodoAsync(c.A, "Corriere Italia", 5m, AreaSpedizione.Italia);
        await c.AggiungiAsync(c.Cliente, varianti[0], 1);

        var esito = await c.ComeAsync(c.A, s => s.CreaDaCarrelloAsync(c.Cliente, "it", Consegna(metodo, 15m, paese, cap), Ct));

        if (campo is null)
        {
            Assert.True(esito.Riuscito);
            Assert.Equal("IT", (await c.ComeAsync(c.A, s => s.LeggiMioAsync(c.Cliente, esito.Id!.Value, Ct)))!.Paese);
            return;
        }

        Assert.Equal(CodiceEsito.DatiNonValidi, esito.Codice);
        Assert.Contains(campo, esito.CampiNonValidi!);
        Assert.Equal(0, await c.ContaOrdiniAsync());
        Assert.Single((await c.ComeCarrelloAsync(c.A, s => s.LeggiAsync(c.Cliente, "it", Ct))).Righe);
    }

    [Fact]
    public async Task UnMetodoDellUnioneEuropeaAccettaSoloPaesiDelLUnioneEIlTotaleAtteso()
    {
        await using var c = await Contesto.CreaAsync();
        var varianti = await c.CreaProdottoAsync(c.A, "Libro", 4m, ("LIB-1", 10.00m, null));
        var europa = await c.CreaMetodoAsync(c.A, "Corriere UE", 14.50m, AreaSpedizione.UnioneEuropea);
        await c.AggiungiAsync(c.Cliente, varianti[0], 1);

        var italia = await c.ComeAsync(c.A, s => s.CreaDaCarrelloAsync(c.Cliente, "it", Consegna(europa, 24.50m, "IT"), Ct));
        var svizzera = await c.ComeAsync(c.A, s => s.CreaDaCarrelloAsync(c.Cliente, "it", Consegna(europa, 24.50m, "CH"), Ct));
        var francia = await c.ComeAsync(c.A, s => s.CreaDaCarrelloAsync(c.Cliente, "it", Consegna(europa, 24.50m, "FR", "75001"), Ct));

        Assert.Equal(CodiceEsito.DatiNonValidi, italia.Codice);
        Assert.Equal(CodiceEsito.DatiNonValidi, svizzera.Codice);
        Assert.True(francia.Riuscito);
    }

    [Theory]
    [InlineData(0.01)]
    [InlineData(-5)]
    [InlineData(1)]
    public async Task UnTotaleDiversoDaQuelloVistoNonCreaLOrdine(double atteso)
    {
        await using var c = await Contesto.CreaAsync();
        var varianti = await c.CreaProdottoAsync(c.A, "Libro", 4m, ("LIB-1", 10.00m, null));
        var metodo = await c.CreaMetodoAsync(c.A, "Corriere", 5m, AreaSpedizione.Italia);
        await c.AggiungiAsync(c.Cliente, varianti[0], 1);

        var esito = await c.ComeAsync(c.A, s => s.CreaDaCarrelloAsync(c.Cliente, "it", Consegna(metodo, (decimal)atteso), Ct));
        var dopoRincaro = await RincaraEProvaAsync(c, metodo);

        Assert.Equal(CodiceEsito.Conflitto, esito.Codice);
        Assert.Contains("Totale", esito.CampiNonValidi!);
        Assert.Equal(CodiceEsito.Conflitto, dopoRincaro.Codice);
        Assert.Equal(0, await c.ContaOrdiniAsync());
        Assert.Single((await c.ComeCarrelloAsync(c.A, s => s.LeggiAsync(c.Cliente, "it", Ct))).Righe);
    }

    private static async Task<EsitoOperazione> RincaraEProvaAsync(Contesto c, Guid metodo)
    {
        await c.CambiaPrezzoAsync(c.A, "LIB-1", 11.00m);
        return await c.ComeAsync(c.A, s => s.CreaDaCarrelloAsync(c.Cliente, "it", Consegna(metodo, 15m), Ct));
    }

    [Fact]
    public async Task CarrelloVuotoMetodoInesistenteDisattivatoOAltruiEArticoliNonPiuDisponibiliSonoRifiutati()
    {
        await using var c = await Contesto.CreaAsync();
        var varianti = await c.CreaProdottoAsync(c.A, "Libro", 4m, ("LIB-1", 10.00m, "Base"));
        var metodo = await c.CreaMetodoAsync(c.A, "Corriere", 5m, AreaSpedizione.Italia);
        var spento = await c.CreaMetodoAsync(c.A, "Spento", 5m, AreaSpedizione.Italia, attivo: false);
        var diB = await c.CreaMetodoAsync(c.B, "Di B", 5m, AreaSpedizione.Italia);

        var vuoto = await c.ComeAsync(c.A, s => s.CreaDaCarrelloAsync(c.Cliente, "it", Consegna(metodo, 5m), Ct));
        await c.AggiungiAsync(c.Cliente, varianti[0], 1);
        var inesistente = await c.ComeAsync(c.A, s => s.CreaDaCarrelloAsync(c.Cliente, "it", Consegna(Guid.NewGuid(), 15m), Ct));
        var disattivato = await c.ComeAsync(c.A, s => s.CreaDaCarrelloAsync(c.Cliente, "it", Consegna(spento, 15m), Ct));
        var altrui = await c.ComeAsync(c.A, s => s.CreaDaCarrelloAsync(c.Cliente, "it", Consegna(diB, 15m), Ct));
        await c.DisattivaVarianteAsync(c.A, "LIB-1");
        var nonDisponibile = await c.ComeAsync(c.A, s => s.CreaDaCarrelloAsync(c.Cliente, "it", Consegna(metodo, 5m), Ct));

        Assert.Equal(CodiceEsito.DatiNonValidi, vuoto.Codice);
        Assert.Equal(CodiceEsito.NonTrovato, inesistente.Codice);
        Assert.Equal(CodiceEsito.NonTrovato, disattivato.Codice);
        Assert.Equal(CodiceEsito.NonTrovato, altrui.Codice);
        Assert.Equal(CodiceEsito.Conflitto, nonDisponibile.Codice);
        Assert.Equal(0, await c.ContaOrdiniAsync());
    }

    [Fact]
    public async Task INumeriDOrdineSonoProgressiviPerSitoEDueInviiDellaStessaCassaCreanoUnSoloOrdine()
    {
        await using var c = await Contesto.CreaAsync();
        var variantiA = await c.CreaProdottoAsync(c.A, "Libro", 4m, ("LIB-1", 10.00m, null));
        var variantiB = await c.CreaProdottoAsync(c.B, "Libro", 4m, ("LIB-1", 10.00m, null));
        var metodoA = await c.CreaMetodoAsync(c.A, "Corriere", 5m, AreaSpedizione.Italia);
        var metodoB = await c.CreaMetodoAsync(c.B, "Corriere", 5m, AreaSpedizione.Italia);

        var numeri = new List<int>();
        for (var i = 0; i < 3; i++)
        {
            await c.AggiungiAsync(c.Cliente, variantiA[0], 1);
            var esito = await c.ComeAsync(c.A, s => s.CreaDaCarrelloAsync(c.Cliente, "it", Consegna(metodoA, 15m), Ct));
            numeri.Add((await c.ComeAsync(c.A, s => s.LeggiMioAsync(c.Cliente, esito.Id!.Value, Ct)))!.Numero);
        }

        await c.AggiungiAsync(c.ClienteDiB, variantiB[0], 1, c.B);
        var primoDiB = await c.ComeAsync(c.B, s => s.CreaDaCarrelloAsync(c.ClienteDiB, "it", Consegna(metodoB, 15m), Ct));
        await c.AggiungiAsync(c.Cliente, variantiA[0], 1);
        var risposte = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => c.ComeAsync(c.A, s => s.CreaDaCarrelloAsync(c.Cliente, "it", Consegna(metodoA, 15m), Ct))));

        Assert.Equal([1, 2, 3], numeri);
        Assert.Equal(1, (await c.ComeAsync(c.B, s => s.LeggiMioAsync(c.ClienteDiB, primoDiB.Id!.Value, Ct)))!.Numero);
        Assert.Single(risposte, e => e.Riuscito);
        Assert.Equal(4, (await c.ComeAsync(c.A, s => s.ElencaMieiAsync(c.Cliente, Ct))).Count);
        Assert.Equal([4, 3, 2, 1], (await c.ComeAsync(c.A, s => s.ElencaMieiAsync(c.Cliente, Ct))).Select(o => o.Numero));
    }

    [Fact]
    public async Task OgniClienteVedeEAnnullaSoloIPropriOrdini()
    {
        await using var c = await Contesto.CreaAsync();
        var varianti = await c.CreaProdottoAsync(c.A, "Libro", 4m, ("LIB-1", 10.00m, null));
        var metodo = await c.CreaMetodoAsync(c.A, "Corriere", 5m, AreaSpedizione.Italia);
        await c.AggiungiAsync(c.Cliente, varianti[0], 1);
        var mio = await c.ComeAsync(c.A, s => s.CreaDaCarrelloAsync(c.Cliente, "it", Consegna(metodo, 15m), Ct));

        Assert.Null(await c.ComeAsync(c.A, s => s.LeggiMioAsync(c.AltroCliente, mio.Id!.Value, Ct)));
        Assert.Empty(await c.ComeAsync(c.A, s => s.ElencaMieiAsync(c.AltroCliente, Ct)));
        Assert.Equal(CodiceEsito.NonTrovato, (await c.ComeAsync(c.A, s => s.AnnullaMioAsync(c.AltroCliente, mio.Id!.Value, Ct))).Codice);
        Assert.Null(await c.ComeAsync(c.B, s => s.LeggiMioAsync(c.ClienteDiB, mio.Id!.Value, Ct)));
        Assert.Equal(CodiceEsito.NonAutorizzato, (await c.ComeAsync(c.B, s => s.AnnullaMioAsync(c.Cliente, mio.Id!.Value, Ct))).Codice);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => c.ComeAsync(c.B, s => s.ElencaMieiAsync(c.Cliente, Ct)));
        Assert.Equal(StatoOrdine.InAttesaDiPagamento, (await c.ComeAsync(c.A, s => s.LeggiMioAsync(c.Cliente, mio.Id!.Value, Ct)))!.Stato);
    }

    [Fact]
    public async Task IlClienteAnnullaUnOrdineNonPagatoMaNonDueVolteENonUnOrdinePagato()
    {
        await using var c = await Contesto.CreaAsync();
        var varianti = await c.CreaProdottoAsync(c.A, "Libro", 4m, ("LIB-1", 10.00m, null));
        var metodo = await c.CreaMetodoAsync(c.A, "Corriere", 5m, AreaSpedizione.Italia);
        await c.AggiungiAsync(c.Cliente, varianti[0], 1);
        var annullabile = await c.ComeAsync(c.A, s => s.CreaDaCarrelloAsync(c.Cliente, "it", Consegna(metodo, 15m), Ct));
        await c.AggiungiAsync(c.Cliente, varianti[0], 1);
        var pagato = await c.ComeAsync(c.A, s => s.CreaDaCarrelloAsync(c.Cliente, "it", Consegna(metodo, 15m), Ct));
        await c.ImpostaStatoAsync(pagato.Id!.Value, StatoOrdine.Pagato);

        var primo = await c.ComeAsync(c.A, s => s.AnnullaMioAsync(c.Cliente, annullabile.Id!.Value, Ct));
        var secondo = await c.ComeAsync(c.A, s => s.AnnullaMioAsync(c.Cliente, annullabile.Id!.Value, Ct));
        var daPagato = await c.ComeAsync(c.A, s => s.AnnullaMioAsync(c.Cliente, pagato.Id!.Value, Ct));

        Assert.True(primo.Riuscito);
        Assert.Equal(CodiceEsito.NonConsentito, secondo.Codice);
        Assert.Equal(CodiceEsito.NonConsentito, daPagato.Codice);
        Assert.Equal(StatoOrdine.Annullato, (await c.ComeAsync(c.A, s => s.LeggiMioAsync(c.Cliente, annullabile.Id!.Value, Ct)))!.Stato);
        Assert.Equal(StatoOrdine.Pagato, (await c.ComeAsync(c.A, s => s.LeggiMioAsync(c.Cliente, pagato.Id!.Value, Ct)))!.Stato);
    }

    [Fact]
    public async Task SenzaNegozioAttivoOAccountNonValidoNonSiOrdina()
    {
        await using var c = await Contesto.CreaAsync();
        var varianti = await c.CreaProdottoAsync(c.A, "Libro", 4m, ("LIB-1", 10.00m, null));
        var metodo = await c.CreaMetodoAsync(c.A, "Corriere", 5m, AreaSpedizione.Italia);
        await c.AggiungiAsync(c.Cliente, varianti[0], 1);

        var sconosciuto = await c.ComeAsync(c.A, s => s.CreaDaCarrelloAsync(Guid.NewGuid(), "it", Consegna(metodo, 15m), Ct));
        var dellAltroTenant = await c.ComeAsync(c.A, s => s.CreaDaCarrelloAsync(c.ClienteDiB, "it", Consegna(metodo, 15m), Ct));
        await c.DisabilitaAsync(c.Cliente);
        var disabilitato = await c.ComeAsync(c.A, s => s.CreaDaCarrelloAsync(c.Cliente, "it", Consegna(metodo, 15m), Ct));

        Assert.Equal(CodiceEsito.NonAutorizzato, sconosciuto.Codice);
        Assert.Equal(CodiceEsito.NonAutorizzato, dellAltroTenant.Codice);
        Assert.Equal(CodiceEsito.NonAutorizzato, disabilitato.Codice);

        await c.SpegniNegozioAsync(c.A);
        var spento = await c.ComeAsync(c.A, s => s.CreaDaCarrelloAsync(c.AltroCliente, "it", Consegna(metodo, 15m), Ct));
        Assert.Equal(CodiceEsito.NonConsentito, spento.Codice);
        Assert.Equal(0, await c.ContaOrdiniAsync());
    }

    [Fact]
    public async Task LAmministrazioneVedeTuttiGliOrdiniDelSitoMaNonQuelliDiUnAltroTenantNeIClienti()
    {
        await using var c = await Contesto.CreaAsync();
        var variantiA = await c.CreaProdottoAsync(c.A, "Libro", 4m, ("LIB-1", 10.00m, null));
        var variantiB = await c.CreaProdottoAsync(c.B, "Libro", 4m, ("LIB-1", 10.00m, null));
        var metodoA = await c.CreaMetodoAsync(c.A, "Corriere", 5m, AreaSpedizione.Italia);
        var metodoB = await c.CreaMetodoAsync(c.B, "Corriere", 5m, AreaSpedizione.Italia);
        await c.AggiungiAsync(c.Cliente, variantiA[0], 1);
        await c.AggiungiAsync(c.AltroCliente, variantiA[0], 2);
        await c.AggiungiAsync(c.ClienteDiB, variantiB[0], 1, c.B);
        var uno = await c.ComeAsync(c.A, s => s.CreaDaCarrelloAsync(c.Cliente, "it", Consegna(metodoA, 15m), Ct));
        await c.ComeAsync(c.A, s => s.CreaDaCarrelloAsync(c.AltroCliente, "it", Consegna(metodoA, 25m), Ct));
        var diB = await c.ComeAsync(c.B, s => s.CreaDaCarrelloAsync(c.ClienteDiB, "it", Consegna(metodoB, 15m), Ct));

        var elenco = await c.ComeAsync(c.A, s => s.ElencaAsync(c.AdminA, Ct));

        Assert.Equal([2, 1], elenco.Select(o => o.Numero));
        Assert.Equal(["altro@esempio.it", "cliente@esempio.it"], elenco.Select(o => o.EmailAcquirente));
        Assert.NotNull(await c.ComeAsync(c.A, s => s.LeggiAsync(c.AdminA, uno.Id!.Value, Ct)));
        Assert.Null(await c.ComeAsync(c.A, s => s.LeggiAsync(c.AdminA, diB.Id!.Value, Ct)));
        Assert.NotNull(await c.ComeAsync(c.A, s => s.LeggiAsync(c.Platform, uno.Id!.Value, Ct)));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => c.ComeAsync(c.A, s => s.ElencaAsync(c.Cliente, Ct)));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => c.ComeAsync(c.A, s => s.LeggiAsync(c.AdminB, uno.Id!.Value, Ct)));
    }

    [Fact]
    public void LeTransizioniDellOrdineSonoSoloQuelleDelleRegole()
    {
        // Spedito vale solo per la consegna a zona, Ritirato solo per il ritiro; tutto il resto non dipende dal tipo.
        var consentite = new (StatoOrdine Da, StatoOrdine A, TipoMetodoSpedizione? Tipo)[]
        {
            (StatoOrdine.InAttesaDiPagamento, StatoOrdine.Pagato, null),
            (StatoOrdine.InAttesaDiPagamento, StatoOrdine.PagamentoFallito, null),
            (StatoOrdine.InAttesaDiPagamento, StatoOrdine.Annullato, null),
            (StatoOrdine.PagamentoFallito, StatoOrdine.InAttesaDiPagamento, null),
            (StatoOrdine.PagamentoFallito, StatoOrdine.Annullato, null),
            (StatoOrdine.Pagato, StatoOrdine.Spedito, TipoMetodoSpedizione.Zona),
            (StatoOrdine.Pagato, StatoOrdine.Ritirato, TipoMetodoSpedizione.Ritiro),
        };

        foreach (var da in Enum.GetValues<StatoOrdine>())
        {
            foreach (var a in Enum.GetValues<StatoOrdine>())
            {
                foreach (var tipo in Enum.GetValues<TipoMetodoSpedizione>())
                {
                    var atteso = consentite.Any(x => x.Da == da && x.A == a && (x.Tipo is null || x.Tipo == tipo));
                    Assert.Equal(atteso, TransizioniOrdine.Consentita(da, a, tipo));
                }
            }
        }

        Assert.All([StatoOrdine.Spedito, StatoOrdine.Ritirato, StatoOrdine.Annullato], s => Assert.True(TransizioniOrdine.EFinale(s)));
        Assert.All([StatoOrdine.InAttesaDiPagamento, StatoOrdine.PagamentoFallito, StatoOrdine.Pagato], s => Assert.False(TransizioniOrdine.EFinale(s)));
    }

    private sealed class Contesto : IAsyncDisposable
    {
        private Contesto(ScenarioWeb web) => Web = web;

        public ScenarioWeb Web { get; }

        public DatiProva.TenantCreato A => Web.A;

        public DatiProva.TenantCreato B => Web.B;

        public Guid Platform { get; private set; }

        public Guid AdminA { get; private set; }

        public Guid AdminB { get; private set; }

        public Guid Cliente { get; private set; }

        public Guid AltroCliente { get; private set; }

        public Guid ClienteDiB { get; private set; }

        public static async Task<Contesto> CreaAsync()
        {
            var c = new Contesto(await ScenarioWeb.CreaAsync());
            c.Platform = await c.Web.CreaPlatformAdminAsync("root@esempio.it");
            c.AdminA = await c.Web.CreaTenantAdminAsync("anna@esempio.it");
            c.AdminB = await DatiProva.CreaUtenzaAsync(c.Web.Fabbrica.Services, RuoloUtenza.TenantAdmin, c.B.IdTenant, "bruno@esempio.it", annullamento: Ct);
            c.Cliente = await DatiProva.CreaUtenzaAsync(c.Web.Fabbrica.Services, RuoloUtenza.Utente, c.A.IdTenant, "cliente@esempio.it", annullamento: Ct);
            c.AltroCliente = await DatiProva.CreaUtenzaAsync(c.Web.Fabbrica.Services, RuoloUtenza.Utente, c.A.IdTenant, "altro@esempio.it", annullamento: Ct);
            c.ClienteDiB = await DatiProva.CreaUtenzaAsync(c.Web.Fabbrica.Services, RuoloUtenza.Utente, c.B.IdTenant, "cliente@esempio.it", annullamento: Ct);
            foreach (var tenant in new[] { c.A, c.B })
            {
                await using var ambito = c.Web.Fabbrica.Services.CreateAsyncScope();
                var esito = await ambito.ServiceProvider.GetRequiredService<IServizioPiattaforma>().ImpostaCommercioAsync(c.Platform, tenant.IdTenant, tenant.IdSito, true, Ct);
                Assert.True(esito.Riuscito, esito.Codice.ToString());
            }

            return c;
        }

        public Task<T> ComeAsync<T>(DatiProva.TenantCreato tenant, Func<IServizioOrdini, Task<T>> operazione) => InAsync(tenant, operazione);

        public Task<T> ComeCarrelloAsync<T>(DatiProva.TenantCreato tenant, Func<IServizioCarrello, Task<T>> operazione) => InAsync(tenant, operazione);

        private async Task<T> InAsync<TServizio, T>(DatiProva.TenantCreato tenant, Func<TServizio, Task<T>> operazione)
            where TServizio : notnull
        {
            await using var ambito = Web.Fabbrica.Services.CreateAsyncScope();
            ambito.ServiceProvider.GetRequiredService<ContestoTenant>().Imposta(new DatiTenantRisolto(tenant.IdTenant, tenant.IdSito, tenant.IdDominio, tenant.Host, false));
            return await operazione(ambito.ServiceProvider.GetRequiredService<TServizio>());
        }

        private Task<T> InAsync<T>(DatiProva.TenantCreato tenant, Func<IServizioOrdini, Task<T>> operazione) => InAsync<IServizioOrdini, T>(tenant, operazione);

        private Guid Attore(DatiProva.TenantCreato tenant) => tenant.IdTenant == A.IdTenant ? AdminA : AdminB;

        public async Task AggiungiAsync(Guid cliente, Guid idVariante, int quantita, DatiProva.TenantCreato? tenant = null)
        {
            var esito = await InAsync<IServizioCarrello, EsitoOperazione>(tenant ?? A, s => s.AggiungiAsync(cliente, idVariante, quantita, Ct));
            Assert.True(esito.Riuscito, esito.Codice.ToString());
        }

        public async Task<IReadOnlyList<Guid>> CreaProdottoAsync(DatiProva.TenantCreato tenant, string nome, decimal iva, params (string Sku, decimal Prezzo, string? Etichetta)[] varianti)
        {
            var dati = new DatiProdotto(null, null, StatoProdotto.Pubblicato, iva, [new TraduzioneProdottoDati("it", nome, null)],
                [.. varianti.Select(v => new VarianteDati(null, v.Sku, v.Prezzo, null, true, v.Etichetta is null ? null : new Dictionary<string, string> { ["it"] = v.Etichetta }))]);
            var esito = await InAsync<IServizioCatalogo, EsitoOperazione>(tenant, s => s.SalvaAsync(Attore(tenant), dati, Ct));
            Assert.True(esito.Riuscito, $"{esito.Codice} {string.Join(',', esito.CampiNonValidi ?? [])}");
            var dettaglio = (await InAsync<IServizioCatalogo, DettaglioProdotto?>(tenant, s => s.LeggiAsync(Attore(tenant), esito.Id!.Value, Ct)))!;
            return [.. dettaglio.Varianti.Select(v => v.Id)];
        }

        public async Task<Guid> CreaMetodoAsync(DatiProva.TenantCreato tenant, string nome, decimal tariffa, AreaSpedizione area, bool attivo = true)
        {
            var esito = await InAsync<IServizioSpedizioni, EsitoOperazione>(tenant, s => s.SalvaAsync(Attore(tenant), new(null, TipoMetodoSpedizione.Zona, area, nome, tariffa, attivo), Ct));
            Assert.True(esito.Riuscito, esito.Codice.ToString());
            return esito.Id!.Value;
        }

        public async Task<Guid> CreaRitiroAsync(DatiProva.TenantCreato tenant)
        {
            var esito = await InAsync<IServizioSpedizioni, EsitoOperazione>(tenant, s => s.SalvaAsync(Attore(tenant), new(null, TipoMetodoSpedizione.Ritiro, null, "Ritiro in negozio", 0m, true), Ct));
            Assert.True(esito.Riuscito, esito.Codice.ToString());
            return esito.Id!.Value;
        }

        public async Task EliminaMetodoAsync(DatiProva.TenantCreato tenant, Guid id) =>
            Assert.True((await InAsync<IServizioSpedizioni, EsitoOperazione>(tenant, s => s.EliminaAsync(Attore(tenant), id, Ct))).Riuscito);

        public Task CambiaPrezzoAsync(DatiProva.TenantCreato tenant, string sku, decimal prezzo) =>
            ModificaAsync(tenant, sku, d => d with { Varianti = [.. d.Varianti.Select(x => x with { PrezzoLordo = x.Sku == sku ? prezzo : x.PrezzoLordo })] });

        public Task DisattivaVarianteAsync(DatiProva.TenantCreato tenant, string sku) =>
            ModificaAsync(tenant, sku, d => d with { Varianti = [.. d.Varianti.Select(x => x with { Attiva = x.Sku != sku && x.Attiva }), new VarianteDati(null, "ALTRA", 1m, null, true, new Dictionary<string, string> { ["it"] = "Altra" })] });

        public Task TogliVarianteAsync(DatiProva.TenantCreato tenant, string sku) =>
            ModificaAsync(tenant, sku, d => d with { Varianti = [.. d.Varianti.Where(x => x.Sku != sku), new VarianteDati(null, "ALTRA", 1m, null, true, new Dictionary<string, string> { ["it"] = "Altra" })] });

        private async Task ModificaAsync(DatiProva.TenantCreato tenant, string sku, Func<DatiProdotto, DatiProdotto> trasforma)
        {
            await using var ambito = Web.Fabbrica.Services.CreateAsyncScope();
            var idProdotto = await ambito.ServiceProvider.GetRequiredService<ContestoDatabase>().VariantiProdotto.IgnoreQueryFilters()
                .Where(v => v.IdSito == tenant.IdSito && v.Sku == sku).Select(v => v.IdProdotto).SingleAsync(Ct);
            var dettaglio = (await InAsync<IServizioCatalogo, DettaglioProdotto?>(tenant, s => s.LeggiAsync(Attore(tenant), idProdotto, Ct)))!;
            var dati = new DatiProdotto(dettaglio.Id, dettaglio.TokenConcorrenza, dettaglio.Stato, dettaglio.AliquotaIva, dettaglio.Traduzioni,
                [.. dettaglio.Varianti.Select(v => new VarianteDati(v.Id, v.Sku, v.PrezzoLordo, v.Valuta, v.Attiva, v.Etichette))]);
            var modificati = trasforma(dati);
            var esito = await InAsync<IServizioCatalogo, EsitoOperazione>(tenant, s => s.SalvaAsync(Attore(tenant), modificati, Ct));
            Assert.True(esito.Riuscito, $"{esito.Codice} {string.Join(',', esito.CampiNonValidi ?? [])}");
        }

        public async Task ImpostaStatoAsync(Guid idOrdine, StatoOrdine stato)
        {
            await using var ambito = Web.Fabbrica.Services.CreateAsyncScope();
            await ambito.ServiceProvider.GetRequiredService<ContestoDatabase>().Ordini.IgnoreQueryFilters().Where(o => o.Id == idOrdine)
                .ExecuteUpdateAsync(x => x.SetProperty(o => o.Stato, stato), Ct);
        }

        public async Task DisabilitaAsync(Guid idAccount)
        {
            await using var ambito = Web.Fabbrica.Services.CreateAsyncScope();
            await ambito.ServiceProvider.GetRequiredService<ContestoDatabase>().Utenze.IgnoreQueryFilters().Where(u => u.Id == idAccount)
                .ExecuteUpdateAsync(x => x.SetProperty(u => u.Stato, StatoUtenza.Disabilitata), Ct);
        }

        public async Task SpegniNegozioAsync(DatiProva.TenantCreato tenant)
        {
            await using var ambito = Web.Fabbrica.Services.CreateAsyncScope();
            Assert.True((await ambito.ServiceProvider.GetRequiredService<IServizioPiattaforma>().ImpostaCommercioAsync(Platform, tenant.IdTenant, tenant.IdSito, false, Ct)).Riuscito);
        }

        public async Task<int> ContaOrdiniAsync()
        {
            await using var ambito = Web.Fabbrica.Services.CreateAsyncScope();
            return await ambito.ServiceProvider.GetRequiredService<ContestoDatabase>().Ordini.IgnoreQueryFilters().CountAsync(Ct);
        }

        public ValueTask DisposeAsync() => Web.DisposeAsync();
    }
}
