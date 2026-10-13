using DvCms.Data;
using DvCms.DTO.Negozio;
using DvCms.DTO.Piattaforma;
using DvCms.IntegrationTests.Supporto;
using DvCms.Models.Accessi;
using DvCms.Models.Negozio;
using DvCms.Services.Manutenzione;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DvCms.IntegrationTests.Negozio;

/// <summary>Giacenza e prenotazioni (F-005): prenotazione atomica, scadenza, annullamento, conferma, permessi e isolamento fra tenant.</summary>
public class ServizioGiacenzeTest
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static DatiOrdine Ritiro(Guid metodo, decimal atteso) => new(metodo, null, null, null, null, null, null, atteso);

    private static async Task<Guid> OrdinaAsync(ContestoNegozio c, Guid cliente, Guid variante, int quantita, Guid ritiro, decimal atteso, DatiProva.TenantCreato? tenant = null)
    {
        await c.AggiungiAsync(cliente, variante, quantita, tenant);
        var esito = await c.ComeAsync(tenant ?? c.A, s => s.CreaDaCarrelloAsync(cliente, "it", Ritiro(ritiro, atteso), Ct));
        Assert.True(esito.Riuscito, $"{esito.Codice} {string.Join(',', esito.CampiNonValidi ?? [])}");
        return esito.Id!.Value;
    }

    [Fact]
    public async Task SenzaGiacenzaGestitaSiVendeSenzaLimiteEConGiacenzaSiPrenotaIlNecessario()
    {
        await using var c = await ContestoNegozio.CreaAsync();
        var varianti = await c.CreaProdottoAsync(c.A, "Libro", 4m, ("LIB-1", 10.00m, null));
        var ritiro = await c.CreaRitiroAsync(c.A);

        var libero = await OrdinaAsync(c, c.Cliente, varianti[0], 50, ritiro, 500m);
        await c.ImpostaGiacenzaAsync(c.A, varianti[0], 5);
        var ordine = await OrdinaAsync(c, c.Cliente, varianti[0], 3, ritiro, 30m);

        Assert.Empty(await c.PrenotazioniAsync(libero));
        var giacenza = await c.GiacenzaAsync(varianti[0]);
        Assert.Equal((5, 3), (giacenza.Disponibile, giacenza.Riservata));
        var prenotazione = (await c.PrenotazioniAsync(ordine)).Single();
        Assert.Equal((3, StatoPrenotazione.Attiva), (prenotazione.Quantita, prenotazione.Stato));
        Assert.InRange(prenotazione.ScadeIlUtc, DateTime.UtcNow.AddMinutes(14), DateTime.UtcNow.AddMinutes(16));
        var vista = (await c.ComeAsync(c.A, s => s.LeggiMioAsync(c.Cliente, ordine, Ct)))!;
        Assert.Equal(prenotazione.ScadeIlUtc, vista.PrenotazioneScadeIlUtc);
        Assert.Null((await c.ComeAsync(c.A, s => s.LeggiMioAsync(c.Cliente, libero, Ct)))!.PrenotazioneScadeIlUtc);
    }

    [Fact]
    public async Task UnaQuantitaOltreLaDisponibilitaNonCreaLOrdineENonToccaIlCarrello()
    {
        await using var c = await ContestoNegozio.CreaAsync();
        var varianti = await c.CreaProdottoAsync(c.A, "Libro", 4m, ("LIB-1", 10.00m, "Base"), ("LIB-2", 5.00m, "Extra"));
        var ritiro = await c.CreaRitiroAsync(c.A);
        await c.ImpostaGiacenzaAsync(c.A, varianti[0], 2);
        await c.ImpostaGiacenzaAsync(c.A, varianti[1], 10);
        await c.AggiungiAsync(c.Cliente, varianti[0], 3);
        await c.AggiungiAsync(c.Cliente, varianti[1], 1);

        var esito = await c.ComeAsync(c.A, s => s.CreaDaCarrelloAsync(c.Cliente, "it", Ritiro(ritiro, 35m), Ct));

        Assert.Equal(CodiceEsito.Conflitto, esito.Codice);
        Assert.Equal(["Giacenza:LIB-1"], esito.CampiNonValidi);
        Assert.Equal(0, await c.ContaOrdiniAsync());
        Assert.Equal((2, 0), ((await c.GiacenzaAsync(varianti[0])).Disponibile, (await c.GiacenzaAsync(varianti[0])).Riservata));
        Assert.Equal(0, (await c.GiacenzaAsync(varianti[1])).Riservata);
        Assert.Equal(2, (await c.ComeCarrelloAsync(c.A, s => s.LeggiAsync(c.Cliente, "it", Ct))).Righe.Count);
    }

    [Fact]
    public async Task MoltiAcquirentiInsiemeNonVendonoMaiPiuDiQuantoCe()
    {
        await using var c = await ContestoNegozio.CreaAsync();
        var varianti = await c.CreaProdottoAsync(c.A, "Edizione limitata", 22m, ("LIM-1", 10.00m, null));
        var ritiro = await c.CreaRitiroAsync(c.A);
        await c.ImpostaGiacenzaAsync(c.A, varianti[0], 5);
        var clienti = new List<Guid>();
        for (var i = 0; i < 15; i++)
        {
            var id = await DatiProva.CreaUtenzaAsync(c.Web.Fabbrica.Services, RuoloUtenza.Utente, c.A.IdTenant, $"c{i}@esempio.it", annullamento: Ct);
            await c.AggiungiAsync(id, varianti[0], 1);
            clienti.Add(id);
        }

        var esiti = await Task.WhenAll(clienti.Select(id => c.ComeAsync(c.A, s => s.CreaDaCarrelloAsync(id, "it", Ritiro(ritiro, 10m), Ct))));

        Assert.Equal(5, esiti.Count(e => e.Riuscito));
        Assert.All(esiti.Where(e => !e.Riuscito), e => Assert.Equal(CodiceEsito.Conflitto, e.Codice));
        var giacenza = await c.GiacenzaAsync(varianti[0]);
        Assert.Equal((5, 5), (giacenza.Disponibile, giacenza.Riservata));
        Assert.Equal(5, await c.ContaOrdiniAsync());
    }

    [Fact]
    public async Task LAnnullamentoDelClienteRestituisceLaMerceEUnSecondoAnnullamentoNonLaRestituisceDueVolte()
    {
        await using var c = await ContestoNegozio.CreaAsync();
        var varianti = await c.CreaProdottoAsync(c.A, "Libro", 4m, ("LIB-1", 10.00m, null));
        var ritiro = await c.CreaRitiroAsync(c.A);
        await c.ImpostaGiacenzaAsync(c.A, varianti[0], 4);
        var ordine = await OrdinaAsync(c, c.Cliente, varianti[0], 4, ritiro, 40m);

        var primo = await c.ComeAsync(c.A, s => s.AnnullaMioAsync(c.Cliente, ordine, Ct));
        var secondo = await c.ComeAsync(c.A, s => s.AnnullaMioAsync(c.Cliente, ordine, Ct));
        var rilascio = await c.ComeGiacenzeAsync(c.A, s => s.RilasciaOrdineAsync(ordine, Ct));

        Assert.True(primo.Riuscito);
        Assert.Equal(CodiceEsito.NonConsentito, secondo.Codice);
        Assert.True(rilascio.Riuscito);
        Assert.Equal((4, 0), ((await c.GiacenzaAsync(varianti[0])).Disponibile, (await c.GiacenzaAsync(varianti[0])).Riservata));
        Assert.Equal(StatoPrenotazione.Rilasciata, (await c.PrenotazioniAsync(ordine)).Single().Stato);
        // Ora la merce si può comprare di nuovo.
        Assert.NotEqual(Guid.Empty, await OrdinaAsync(c, c.AltroCliente, varianti[0], 4, ritiro, 40m));
    }

    [Fact]
    public async Task UnaPrenotazioneScadutaLiberaLaMerceEAnnullaLOrdineNonPagato()
    {
        await using var c = await ContestoNegozio.CreaAsync();
        var varianti = await c.CreaProdottoAsync(c.A, "Libro", 4m, ("LIB-1", 10.00m, null));
        var ritiro = await c.CreaRitiroAsync(c.A);
        await c.ImpostaGiacenzaAsync(c.A, varianti[0], 3);
        var scaduto = await OrdinaAsync(c, c.Cliente, varianti[0], 3, ritiro, 30m);
        var altro = await c.ComeAsync(c.A, s => s.LeggiMioAsync(c.Cliente, scaduto, Ct));
        Assert.Equal(StatoOrdine.InAttesaDiPagamento, altro!.Stato);

        // Un altro cliente, finché la prenotazione vale, non può comprare quella merce...
        await c.AggiungiAsync(c.AltroCliente, varianti[0], 1);
        var bloccato = await c.ComeAsync(c.A, s => s.CreaDaCarrelloAsync(c.AltroCliente, "it", Ritiro(ritiro, 10m), Ct));
        Assert.Equal(CodiceEsito.Conflitto, bloccato.Codice);

        // ...dopo la scadenza sì: la creazione stessa fa scadere le prenotazioni vecchie.
        await c.ScadiPrenotazioniAsync(scaduto);
        var riuscito = await c.ComeAsync(c.A, s => s.CreaDaCarrelloAsync(c.AltroCliente, "it", Ritiro(ritiro, 10m), Ct));

        Assert.True(riuscito.Riuscito, riuscito.Codice.ToString());
        Assert.Equal(StatoOrdine.Annullato, await c.StatoOrdineAsync(scaduto));
        Assert.Equal(StatoPrenotazione.Rilasciata, (await c.PrenotazioniAsync(scaduto)).Single().Stato);
        var giacenza = await c.GiacenzaAsync(varianti[0]);
        Assert.Equal((3, 1), (giacenza.Disponibile, giacenza.Riservata));
    }

    [Fact]
    public async Task LaScadenzaNonToccaUnOrdineGiaPagatoELaManutenzioneFaScadereTutto()
    {
        await using var c = await ContestoNegozio.CreaAsync();
        var varianti = await c.CreaProdottoAsync(c.A, "Libro", 4m, ("LIB-1", 10.00m, null));
        var varianteB = await c.CreaProdottoAsync(c.B, "Libro", 4m, ("LIB-1", 10.00m, null));
        var ritiro = await c.CreaRitiroAsync(c.A);
        var ritiroB = await c.CreaRitiroAsync(c.B);
        await c.ImpostaGiacenzaAsync(c.A, varianti[0], 5);
        await c.ImpostaGiacenzaAsync(c.B, varianteB[0], 5);
        var pagato = await OrdinaAsync(c, c.Cliente, varianti[0], 1, ritiro, 10m);
        var inAttesa = await OrdinaAsync(c, c.AltroCliente, varianti[0], 2, ritiro, 20m);
        var diB = await OrdinaAsync(c, c.ClienteDiB, varianteB[0], 1, ritiroB, 10m, c.B);
        await c.ImpostaStatoAsync(pagato, StatoOrdine.Pagato);
        foreach (var ordine in new[] { pagato, inAttesa, diB })
        {
            await c.ScadiPrenotazioniAsync(ordine);
        }

        var annullati = await c.ComeGiacenzeAsync(c.A, s => s.RilasciaScaduteAsync(Ct));

        Assert.Equal(2, annullati);
        Assert.Equal(StatoOrdine.Pagato, await c.StatoOrdineAsync(pagato));
        Assert.Equal(StatoPrenotazione.Attiva, (await c.PrenotazioniAsync(pagato)).Single().Stato);
        Assert.Equal(StatoOrdine.Annullato, await c.StatoOrdineAsync(inAttesa));
        Assert.Equal(StatoOrdine.Annullato, await c.StatoOrdineAsync(diB));
        Assert.Equal((5, 1), ((await c.GiacenzaAsync(varianti[0])).Disponibile, (await c.GiacenzaAsync(varianti[0])).Riservata));
        Assert.Equal(0, (await c.GiacenzaAsync(varianteB[0])).Riservata);
        Assert.Equal(0, await c.ComeGiacenzeAsync(c.A, s => s.RilasciaScaduteAsync(Ct)));
    }

    [Fact]
    public async Task ConfermareUnOrdineFaUscireLaMerceELasciaUnMovimentoEUnaPrenotazioneRilasciataNonSiConferma()
    {
        await using var c = await ContestoNegozio.CreaAsync();
        var varianti = await c.CreaProdottoAsync(c.A, "Libro", 4m, ("LIB-1", 10.00m, null));
        var ritiro = await c.CreaRitiroAsync(c.A);
        await c.ImpostaGiacenzaAsync(c.A, varianti[0], 10);
        var venduto = await OrdinaAsync(c, c.Cliente, varianti[0], 4, ritiro, 40m);
        var perso = await OrdinaAsync(c, c.AltroCliente, varianti[0], 2, ritiro, 20m);
        await c.ComeGiacenzeAsync(c.A, s => s.RilasciaOrdineAsync(perso, Ct));

        var conferma = await c.ComeGiacenzeAsync(c.A, s => s.ConfermaOrdineAsync(venduto, Ct));
        var ripetuta = await c.ComeGiacenzeAsync(c.A, s => s.ConfermaOrdineAsync(venduto, Ct));
        var rilasciato = await c.ComeGiacenzeAsync(c.A, s => s.ConfermaOrdineAsync(perso, Ct));
        var diAltroTenant = await c.ComeGiacenzeAsync(c.B, s => s.ConfermaOrdineAsync(venduto, Ct));

        Assert.True(conferma.Riuscito);
        Assert.True(ripetuta.Riuscito);
        Assert.Equal(CodiceEsito.Conflitto, rilasciato.Codice);
        Assert.Equal(CodiceEsito.NonTrovato, diAltroTenant.Codice);
        var giacenza = await c.GiacenzaAsync(varianti[0]);
        Assert.Equal((6, 0), (giacenza.Disponibile, giacenza.Riservata));
        var movimenti = await c.MovimentiAsync(varianti[0]);
        Assert.Equal([MotivoMovimento.Impostazione, MotivoMovimento.Vendita], movimenti.Select(m => m.Motivo));
        Assert.Equal((-4, 6, venduto), (movimenti[1].Variazione, movimenti[1].DisponibileDopo, movimenti[1].IdOrdine!.Value));
    }

    [Fact]
    public async Task LAmministrazioneImpostaLaQuantitaMaNonSottoLaPartePrenotataENonSmetteDiGestireMerceTrattenuta()
    {
        await using var c = await ContestoNegozio.CreaAsync();
        var varianti = await c.CreaProdottoAsync(c.A, "Libro", 4m, ("LIB-1", 10.00m, null));
        var ritiro = await c.CreaRitiroAsync(c.A);
        await c.ImpostaGiacenzaAsync(c.A, varianti[0], 10);
        var ordine = await OrdinaAsync(c, c.Cliente, varianti[0], 4, ritiro, 40m);

        Esito sotto = await ImpostaAsync(c, varianti[0], 3);
        Esito negativa = await ImpostaAsync(c, varianti[0], -1);
        Esito enorme = await ImpostaAsync(c, varianti[0], 1_000_001);
        Esito alMinimo = await ImpostaAsync(c, varianti[0], 4);
        var nonGestire = await c.ComeGiacenzeAsync(c.A, s => s.ImpostaAsync(c.AdminA, varianti[0], null, Ct));
        await c.ComeAsync(c.A, s => s.AnnullaMioAsync(c.Cliente, ordine, Ct));
        var ora = await c.ComeGiacenzeAsync(c.A, s => s.ImpostaAsync(c.AdminA, varianti[0], null, Ct));

        Assert.Equal(CodiceEsito.DatiNonValidi, sotto.Codice);
        Assert.Equal(CodiceEsito.DatiNonValidi, negativa.Codice);
        Assert.Equal(CodiceEsito.DatiNonValidi, enorme.Codice);
        Assert.True(alMinimo.Riuscito);
        Assert.Equal(CodiceEsito.NonConsentito, nonGestire.Codice);
        Assert.True(ora.Riuscito);
        Assert.DoesNotContain(await c.ComeGiacenzeAsync(c.A, s => s.ElencaAsync(c.AdminA, Ct)), g => g.Gestita);
        Assert.Equal(3, (await c.MovimentiAsync(varianti[0])).Count);
    }

    private static async Task<Esito> ImpostaAsync(ContestoNegozio c, Guid variante, int quantita) =>
        new(await c.ComeGiacenzeAsync(c.A, s => s.ImpostaAsync(c.AdminA, variante, quantita, Ct)));

    private sealed record Esito(EsitoOperazione Valore)
    {
        public CodiceEsito Codice => Valore.Codice;

        public bool Riuscito => Valore.Riuscito;
    }

    [Fact]
    public async Task SoloGliAmministratoriDelTenantGestisconoLaGiacenzaEIdatiDiUnAltroTenantNonSiVedono()
    {
        await using var c = await ContestoNegozio.CreaAsync();
        var varianti = await c.CreaProdottoAsync(c.A, "Libro", 4m, ("LIB-1", 10.00m, null));
        var variantiB = await c.CreaProdottoAsync(c.B, "Libro", 4m, ("LIB-1", 10.00m, null));
        await c.ImpostaGiacenzaAsync(c.A, varianti[0], 5);

        var comeCliente = await c.ComeGiacenzeAsync(c.A, s => s.ImpostaAsync(c.Cliente, varianti[0], 99, Ct));
        var comeAdminDiB = await c.ComeGiacenzeAsync(c.A, s => s.ImpostaAsync(c.AdminB, varianti[0], 99, Ct));
        var varianteAltrui = await c.ComeGiacenzeAsync(c.A, s => s.ImpostaAsync(c.AdminA, variantiB[0], 99, Ct));
        var comePlatform = await c.ComeGiacenzeAsync(c.A, s => s.ImpostaAsync(c.Platform, varianti[0], 7, Ct));

        Assert.Equal(CodiceEsito.NonAutorizzato, comeCliente.Codice);
        Assert.Equal(CodiceEsito.NonAutorizzato, comeAdminDiB.Codice);
        Assert.Equal(CodiceEsito.NonTrovato, varianteAltrui.Codice);
        Assert.True(comePlatform.Riuscito);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => c.ComeGiacenzeAsync(c.A, s => s.ElencaAsync(c.Cliente, Ct)));
        Assert.Equal(["LIB-1"], (await c.ComeGiacenzeAsync(c.B, s => s.ElencaAsync(c.AdminB, Ct))).Select(g => g.Sku));
        Assert.DoesNotContain(await c.ComeGiacenzeAsync(c.B, s => s.ElencaAsync(c.AdminB, Ct)), g => g.Gestita);
        Assert.Equal(7, (await c.GiacenzaAsync(varianti[0])).Disponibile);
    }

    [Fact]
    public async Task UnaVarianteConMerceTrattenutaNonSiEliminaDalCatalogoUnaSenzaPrenotazioniSiPortaViaLaGiacenza()
    {
        await using var c = await ContestoNegozio.CreaAsync();
        var varianti = await c.CreaProdottoAsync(c.A, "Libro", 4m, ("LIB-1", 10.00m, "Base"), ("LIB-2", 5.00m, "Extra"));
        var ritiro = await c.CreaRitiroAsync(c.A);
        await c.ImpostaGiacenzaAsync(c.A, varianti[0], 5);
        await c.ImpostaGiacenzaAsync(c.A, varianti[1], 5);
        var ordine = await OrdinaAsync(c, c.Cliente, varianti[0], 1, ritiro, 10m);

        var rifiutata = await c.TentaTogliereVarianteAsync(c.A, "LIB-1");
        await c.ComeAsync(c.A, s => s.AnnullaMioAsync(c.Cliente, ordine, Ct));
        var riuscita = await c.TentaTogliereVarianteAsync(c.A, "LIB-1");

        Assert.Equal(CodiceEsito.NonConsentito, rifiutata.Codice);
        Assert.True(riuscita.Riuscito, riuscita.Codice.ToString());
        await using var ambito = c.Web.Fabbrica.Services.CreateAsyncScope();
        Assert.Equal(1, await ambito.ServiceProvider.GetRequiredService<ContestoDatabase>().GiacenzeVarianti.IgnoreQueryFilters().CountAsync(Ct));
    }

    [Fact]
    public async Task LaManutenzionePeriodicaFaScadereLePrenotazioni()
    {
        await using var c = await ContestoNegozio.CreaAsync();
        var varianti = await c.CreaProdottoAsync(c.A, "Libro", 4m, ("LIB-1", 10.00m, null));
        var ritiro = await c.CreaRitiroAsync(c.A);
        await c.ImpostaGiacenzaAsync(c.A, varianti[0], 3);
        var ordine = await OrdinaAsync(c, c.Cliente, varianti[0], 2, ritiro, 20m);
        await c.ScadiPrenotazioniAsync(ordine);

        await using (var ambito = c.Web.Fabbrica.Services.CreateAsyncScope())
        {
            await ambito.ServiceProvider.GetRequiredService<ManutenzioneDati>().EseguiAsync(Ct);
        }

        Assert.Equal(StatoOrdine.Annullato, await c.StatoOrdineAsync(ordine));
        Assert.Equal(0, (await c.GiacenzaAsync(varianti[0])).Riservata);
    }

    [Fact]
    public async Task IlCarrelloDiceQuantaMerceEDisponibileEdAvvisaSeNonBasta()
    {
        await using var c = await ContestoNegozio.CreaAsync();
        var varianti = await c.CreaProdottoAsync(c.A, "Libro", 4m, ("LIB-1", 10.00m, "Base"), ("LIB-2", 5.00m, "Libera"));
        var ritiro = await c.CreaRitiroAsync(c.A);
        await c.ImpostaGiacenzaAsync(c.A, varianti[0], 5);
        await OrdinaAsync(c, c.AltroCliente, varianti[0], 3, ritiro, 30m);
        await c.AggiungiAsync(c.Cliente, varianti[0], 4);
        await c.AggiungiAsync(c.Cliente, varianti[1], 4);

        var vista = await c.ComeCarrelloAsync(c.A, s => s.LeggiAsync(c.Cliente, "it", Ct));

        Assert.Equal([2, 99], vista.Righe.Select(r => r.Massimo));
        Assert.Equal([4, 4], vista.Righe.Select(r => r.Quantita));
    }
}
