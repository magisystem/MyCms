using System.Text.Json.Nodes;
using DvCms.Data;
using DvCms.DTO.Negozio;
using DvCms.DTO.Piattaforma;
using DvCms.Infrastructure.Persistenza;
using DvCms.IntegrationTests.Negozio;
using DvCms.IntegrationTests.Supporto;
using DvCms.Models.Negozio;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DvCms.IntegrationTests.Pagamenti;

/// <summary>Rimborsi (A5): totali e parziali, importo impegnato prima del provider, esiti incerti, webhook, permessi e isolamento.</summary>
public class ServizioRimborsiTest
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static IntestazioniWebhook Firma() => new("SHA256withRSA", "https://api.paypal.com/cert", "TRX-1", PayPalFinto.FirmaValida, "2026-10-11T10:00:00Z");

    private static string EventoRimborso(string idEvento, string tipo, string idRimborso) => new JsonObject
    {
        ["id"] = idEvento,
        ["event_type"] = tipo,
        ["resource"] = new JsonObject { ["id"] = idRimborso },
    }.ToJsonString();

    /// <summary>Un ordine da 10 EUR già pagato (ritiro in sede), con email del tenant configurata.</summary>
    private static async Task<(ContestoNegozio C, PayPalFinto Finto, Guid Ordine)> OrdinePagatoAsync(bool conEmail = false)
    {
        var finto = new PayPalFinto();
        var c = await ContestoNegozio.CreaAsync(payPal: finto, conEmail: conEmail);
        var varianti = await c.CreaProdottoAsync(c.A, "Libro", 4m, ("LIB-1", 10.00m, null));
        var ritiro = await c.CreaRitiroAsync(c.A);
        await c.ConfiguraPagamentoAsync(c.A);
        await c.AggiungiAsync(c.Cliente, varianti[0], 1);
        var ordine = (await c.ComeAsync(c.A, s => s.CreaDaCarrelloAsync(c.Cliente, "it", new DatiOrdine(ritiro, null, null, null, null, null, null, 10m), Ct))).Id!.Value;
        await c.ComePagamentiAsync(c.A, s => s.AvviaAsync(c.Cliente, ordine, DvCms.Models.Negozio.NomiProviderPagamento.PayPal, ContestoNegozio.Link, Ct));
        var idProvider = (await c.TentativiAsync(ordine)).Single().IdProvider;
        finto.Approva(idProvider);
        Assert.Equal(RisultatoPagamento.Pagato, await c.ComePagamentiAsync(c.A, s => s.CompletaAsync(c.Cliente, ordine, idProvider, Ct)));
        return (c, finto, ordine);
    }

    private static Task<RisultatoRimborso> Rimborsa(ContestoNegozio c, Guid ordine, decimal? importo, string? motivo = null) =>
        c.ComeRimborsiAsync(c.A, s => s.RimborsaOrdineAsync(c.AdminA, ordine, importo, motivo, Ct));

    private static async Task<Ordine> LeggiOrdineAsync(ContestoNegozio c, Guid id)
    {
        await using var ambito = c.Web.Fabbrica.Services.CreateAsyncScope();
        return await ambito.ServiceProvider.GetRequiredService<ContestoDatabase>().Ordini.IgnoreQueryFilters().AsNoTracking().SingleAsync(o => o.Id == id, Ct);
    }

    [Fact]
    public async Task UnOrdineInteramenteRimborsatoRientraInGiacenzaUnaSolaVoltaEConStoricoDeiMovimenti()
    {
        var finto = new PayPalFinto();
        await using var c = await ContestoNegozio.CreaAsync(payPal: finto);
        var varianti = await c.CreaProdottoAsync(c.A, "Libro", 4m, ("LIB-1", 10.00m, null));
        var ritiro = await c.CreaRitiroAsync(c.A);
        await c.ImpostaGiacenzaAsync(c.A, varianti[0], 5);
        await c.ConfiguraPagamentoAsync(c.A);
        await c.AggiungiAsync(c.Cliente, varianti[0], 2);
        var ordine = (await c.ComeAsync(c.A, s => s.CreaDaCarrelloAsync(c.Cliente, "it", new DatiOrdine(ritiro, null, null, null, null, null, null, 20m), Ct))).Id!.Value;
        await c.ComePagamentiAsync(c.A, s => s.AvviaAsync(c.Cliente, ordine, DvCms.Models.Negozio.NomiProviderPagamento.PayPal, ContestoNegozio.Link, Ct));
        var idProvider = (await c.TentativiAsync(ordine)).Single().IdProvider;
        finto.Approva(idProvider);
        await c.ComePagamentiAsync(c.A, s => s.CompletaAsync(c.Cliente, ordine, idProvider, Ct));
        Assert.Equal(3, (await c.GiacenzaAsync(varianti[0])).Disponibile);

        var troppoPresto = await c.ComeGiacenzeAsync(c.A, s => s.ReinserisciOrdineAsync(c.AdminA, ordine, Ct));
        Assert.Equal(RisultatoRimborso.Completato, await Rimborsa(c, ordine, 5m));
        var parziale = await c.ComeGiacenzeAsync(c.A, s => s.ReinserisciOrdineAsync(c.AdminA, ordine, Ct));
        Assert.Equal(RisultatoRimborso.Completato, await Rimborsa(c, ordine, null));
        var comeCliente = await c.ComeGiacenzeAsync(c.A, s => s.ReinserisciOrdineAsync(c.Cliente, ordine, Ct));
        var daB = await c.ComeGiacenzeAsync(c.B, s => s.ReinserisciOrdineAsync(c.AdminB, ordine, Ct));
        var riuscito = await c.ComeGiacenzeAsync(c.A, s => s.ReinserisciOrdineAsync(c.AdminA, ordine, Ct));
        var ripetuto = await c.ComeGiacenzeAsync(c.A, s => s.ReinserisciOrdineAsync(c.AdminA, ordine, Ct));

        Assert.Equal(CodiceEsito.NonConsentito, troppoPresto.Codice);
        Assert.Equal(CodiceEsito.NonConsentito, parziale.Codice);
        Assert.Equal(CodiceEsito.NonAutorizzato, comeCliente.Codice);
        Assert.Equal(CodiceEsito.NonTrovato, daB.Codice);
        Assert.Equal(CodiceEsito.Riuscito, riuscito.Codice);
        Assert.Equal(CodiceEsito.Conflitto, ripetuto.Codice);
        Assert.Equal(5, (await c.GiacenzaAsync(varianti[0])).Disponibile);
        var reso = Assert.Single(await c.MovimentiAsync(varianti[0]), m => m.Motivo == MotivoMovimento.Reso);
        Assert.Equal((2, 5, ordine, c.AdminA), (reso.Variazione, reso.DisponibileDopo, reso.IdOrdine!.Value, reso.IdAttore!.Value));
        Assert.NotNull((await LeggiOrdineAsync(c, ordine)).GiacenzaReinseritaIlUtc);
    }

    [Fact]
    public async Task IlRimborsoTotaleChiudeLOrdineAvvisaIlClienteEUsaUnaRichiestaIdempotente()
    {
        var (c, finto, ordine) = await OrdinePagatoAsync(conEmail: true);
        await using var _ = c;
        await c.ElaboraEmailAsync();

        var esito = await Rimborsa(c, ordine, null, "Prodotto restituito");

        Assert.Equal(RisultatoRimborso.Completato, esito);
        var dopo = await LeggiOrdineAsync(c, ordine);
        Assert.Equal((StatoOrdine.Rimborsato, 10m), (dopo.Stato, dopo.TotaleRimborsato));
        var rimborso = (await c.ComeRimborsiAsync(c.A, s => s.ElencaAsync(c.AdminA, ordine, Ct))).Single();
        Assert.Equal((10m, StatoRimborso.Completato, "Prodotto restituito"), (rimborso.Importo, rimborso.Stato, rimborso.Motivo));
        var richiesta = finto.Richieste.Single(r => r.Percorso.EndsWith("/refund", StringComparison.Ordinal));
        Assert.Equal(rimborso.Id.ToString("D"), richiesta.IdRichiesta);
        Assert.Contains("\"value\":\"10.00\"", richiesta.Corpo, StringComparison.Ordinal);
        Assert.Contains("Prodotto restituito", richiesta.Corpo, StringComparison.Ordinal);
        var email = Assert.Single(await c.ElaboraEmailAsync());
        Assert.Contains("Rimborso per l'ordine n. 1", email.Oggetto, StringComparison.Ordinal);
        Assert.Contains("rimborsato per intero", email.Testo, StringComparison.Ordinal);
        Assert.Contains("10,00 EUR", email.Testo, StringComparison.Ordinal);
        Assert.Equal(RisultatoRimborso.NonRimborsabile, await Rimborsa(c, ordine, null));
    }

    [Fact]
    public async Task IRimborsiParzialiSiSommanoELOrdineSiChiudeSoloQuandoCopronoIlTotale()
    {
        var (c, finto, ordine) = await OrdinePagatoAsync();
        await using var _ = c;

        var primo = await Rimborsa(c, ordine, 4m);
        var dopoPrimo = await LeggiOrdineAsync(c, ordine);
        var troppo = await Rimborsa(c, ordine, 6.01m);
        var secondo = await Rimborsa(c, ordine, 6m);
        var dopoSecondo = await LeggiOrdineAsync(c, ordine);

        Assert.Equal(RisultatoRimborso.Completato, primo);
        Assert.Equal((StatoOrdine.Pagato, 4m), (dopoPrimo.Stato, dopoPrimo.TotaleRimborsato));
        Assert.Equal(RisultatoRimborso.DatiNonValidi, troppo);
        Assert.Equal(RisultatoRimborso.Completato, secondo);
        Assert.Equal((StatoOrdine.Rimborsato, 10m), (dopoSecondo.Stato, dopoSecondo.TotaleRimborsato));
        Assert.Equal(2, finto.Conta(HttpMethod.Post, "/v2/payments/captures/CAP-ORD-0001/refund"));
    }

    [Fact]
    public async Task UnOrdineSpeditoSiRimborsaEDiventaRimborsato()
    {
        var (c, _, ordine) = await OrdinePagatoAsync();
        await using var _ = c;
        await c.ComeAsync(c.A, s => s.SegnaEvasoAsync(c.AdminA, ordine, Ct));
        Assert.Equal(StatoOrdine.Ritirato, (await LeggiOrdineAsync(c, ordine)).Stato);

        var esito = await Rimborsa(c, ordine, null);

        Assert.Equal(RisultatoRimborso.Completato, esito);
        Assert.Equal(StatoOrdine.Rimborsato, (await LeggiOrdineAsync(c, ordine)).Stato);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("10.001")]
    [InlineData("10.01")]
    public async Task ImportiNonValidiNonPartonoAlProvider(string importo)
    {
        var (c, finto, ordine) = await OrdinePagatoAsync();
        await using var _ = c;

        var esito = await Rimborsa(c, ordine, decimal.Parse(importo, System.Globalization.CultureInfo.InvariantCulture));
        var motivoLungo = await Rimborsa(c, ordine, 1m, new string('x', 201));
        var motivoConControllo = await Rimborsa(c, ordine, 1m, "a\u0007b");

        Assert.Equal(RisultatoRimborso.DatiNonValidi, esito);
        Assert.Equal(RisultatoRimborso.DatiNonValidi, motivoLungo);
        Assert.Equal(RisultatoRimborso.DatiNonValidi, motivoConControllo);
        Assert.Equal(0, finto.Conta(HttpMethod.Post, "/v2/payments/captures/CAP-ORD-0001/refund"));
        Assert.Equal(0m, (await LeggiOrdineAsync(c, ordine)).TotaleRimborsato);
    }

    [Fact]
    public async Task DueRimborsiInsiemeNonSuperanoMaiIlTotale()
    {
        var (c, finto, ordine) = await OrdinePagatoAsync();
        await using var _ = c;

        var esiti = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Rimborsa(c, ordine, 6m)));

        Assert.Single(esiti, e => e == RisultatoRimborso.Completato);
        Assert.Equal(3, esiti.Count(e => e == RisultatoRimborso.DatiNonValidi));
        Assert.Equal(6m, (await LeggiOrdineAsync(c, ordine)).TotaleRimborsato);
        Assert.Equal(1, finto.Conta(HttpMethod.Post, "/v2/payments/captures/CAP-ORD-0001/refund"));
    }

    [Fact]
    public async Task UnRifiutoDelProviderLiberaLImportoESiPuoRiprovare()
    {
        var (c, finto, ordine) = await OrdinePagatoAsync();
        await using var _ = c;
        finto.EsitoRimborso = "FAILED";

        var rifiutato = await Rimborsa(c, ordine, 10m);
        var dopoRifiuto = await LeggiOrdineAsync(c, ordine);
        finto.EsitoRimborso = "COMPLETED";
        var riprovato = await Rimborsa(c, ordine, 10m);

        Assert.Equal(RisultatoRimborso.Rifiutato, rifiutato);
        Assert.Equal((StatoOrdine.Pagato, 0m), (dopoRifiuto.Stato, dopoRifiuto.TotaleRimborsato));
        Assert.Equal(RisultatoRimborso.Completato, riprovato);
        Assert.Equal([StatoRimborso.Fallito, StatoRimborso.Completato], (await c.ComeRimborsiAsync(c.A, s => s.ElencaAsync(c.AdminA, ordine, Ct))).Select(r => r.Stato));
    }

    [Fact]
    public async Task UnRimborsoInSospesoRestaInCorsoELImportoImpegnatoFinoAlWebhook()
    {
        var (c, finto, ordine) = await OrdinePagatoAsync();
        await using var _ = c;
        finto.EsitoRimborso = "PENDING";

        var esito = await Rimborsa(c, ordine, 10m);
        var secondoTentativo = await Rimborsa(c, ordine, 1m);
        finto.ConcludiRimborso("REF-0001");
        var sconosciuto = await c.ComePagamentiAsync(c.A, s => s.ElaboraWebhookAsync(Firma(), EventoRimborso("WH-0", "PAYMENT.CAPTURE.REFUNDED", "REF-9999"), Ct));
        var webhook = await c.ComePagamentiAsync(c.A, s => s.ElaboraWebhookAsync(Firma(), EventoRimborso("WH-1", "PAYMENT.CAPTURE.REFUNDED", "REF-0001"), Ct));
        var doppione = await c.ComePagamentiAsync(c.A, s => s.ElaboraWebhookAsync(Firma(), EventoRimborso("WH-1", "PAYMENT.CAPTURE.REFUNDED", "REF-0001"), Ct));

        Assert.Equal(RisultatoRimborso.InCorso, esito);
        Assert.Equal(RisultatoRimborso.DatiNonValidi, secondoTentativo);
        Assert.Equal(EsitoWebhook.Ignorato, sconosciuto);
        Assert.Equal(EsitoWebhook.Elaborato, webhook);
        Assert.Equal(EsitoWebhook.Duplicato, doppione);
        var dopo = await LeggiOrdineAsync(c, ordine);
        Assert.Equal((StatoOrdine.Rimborsato, 10m), (dopo.Stato, dopo.TotaleRimborsato));
        Assert.Equal(StatoRimborso.Completato, (await c.ComeRimborsiAsync(c.A, s => s.ElencaAsync(c.AdminA, ordine, Ct))).Single().Stato);
    }

    [Fact]
    public async Task UnaRispostaPersaLasciaIlRimborsoInCorsoELaVerificaNonRimborsaDueVolte()
    {
        var (c, finto, ordine) = await OrdinePagatoAsync();
        await using var _ = c;
        finto.Eccezione = new HttpRequestException("giù");

        var perso = await Rimborsa(c, ordine, 10m);
        finto.Eccezione = null;
        var idRimborso = (await c.ComeRimborsiAsync(c.A, s => s.ElencaAsync(c.AdminA, ordine, Ct))).Single().Id;
        var verifica = await c.ComeRimborsiAsync(c.A, s => s.VerificaAsync(c.AdminA, idRimborso, Ct));
        var ancora = await c.ComeRimborsiAsync(c.A, s => s.VerificaAsync(c.AdminA, idRimborso, Ct));

        Assert.Equal(RisultatoRimborso.ErroreProvider, perso);
        Assert.Equal(RisultatoRimborso.Completato, verifica);
        Assert.Equal(RisultatoRimborso.Completato, ancora);
        Assert.Equal(StatoOrdine.Rimborsato, (await LeggiOrdineAsync(c, ordine)).Stato);
        Assert.Equal(1, finto.Conta(HttpMethod.Post, "/v2/payments/captures/CAP-ORD-0001/refund"));
    }

    [Fact]
    public async Task SoloGliAmministratoriDelTenantRimborsanoEUnOrdineNonPagatoOAltruiNo()
    {
        var (c, finto, ordine) = await OrdinePagatoAsync();
        await using var _ = c;

        var comeCliente = await c.ComeRimborsiAsync(c.A, s => s.RimborsaOrdineAsync(c.Cliente, ordine, null, null, Ct));
        var comeAdminDiB = await c.ComeRimborsiAsync(c.A, s => s.RimborsaOrdineAsync(c.AdminB, ordine, null, null, Ct));
        var dalTenantB = await c.ComeRimborsiAsync(c.B, s => s.RimborsaOrdineAsync(c.AdminB, ordine, null, null, Ct));
        var inesistente = await c.ComeRimborsiAsync(c.A, s => s.RimborsaOrdineAsync(c.AdminA, Guid.NewGuid(), null, null, Ct));
        var comePlatform = await c.ComeRimborsiAsync(c.A, s => s.RimborsaOrdineAsync(c.Platform, ordine, 1m, null, Ct));

        Assert.Equal(RisultatoRimborso.NonAutorizzato, comeCliente);
        Assert.Equal(RisultatoRimborso.NonAutorizzato, comeAdminDiB);
        Assert.Equal(RisultatoRimborso.NonTrovato, dalTenantB);
        Assert.Equal(RisultatoRimborso.NonTrovato, inesistente);
        Assert.Equal(RisultatoRimborso.Completato, comePlatform);
        Assert.Equal(1, finto.Conta(HttpMethod.Post, "/v2/payments/captures/CAP-ORD-0001/refund"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => c.ComeRimborsiAsync(c.A, s => s.ElencaAsync(c.Cliente, ordine, Ct)));
    }

    [Fact]
    public async Task UnOrdineNonPagatoNonSiRimborsa()
    {
        var finto = new PayPalFinto();
        await using var c = await ContestoNegozio.CreaAsync(payPal: finto);
        var varianti = await c.CreaProdottoAsync(c.A, "Libro", 4m, ("LIB-1", 10m, null));
        var ritiro = await c.CreaRitiroAsync(c.A);
        await c.ConfiguraPagamentoAsync(c.A);
        await c.AggiungiAsync(c.Cliente, varianti[0], 1);
        var ordine = (await c.ComeAsync(c.A, s => s.CreaDaCarrelloAsync(c.Cliente, "it", new DatiOrdine(ritiro, null, null, null, null, null, null, 10m), Ct))).Id!.Value;

        var esito = await Rimborsa(c, ordine, null);

        Assert.Equal(RisultatoRimborso.NonRimborsabile, esito);
        Assert.Empty(finto.Richieste);
    }

    [Fact]
    public async Task UnIncassoDiImportoSbagliatoSiRestituisceSenzaToccareLOrdine()
    {
        var finto = new PayPalFinto { ImportoCatturato = "1.00" };
        await using var c = await ContestoNegozio.CreaAsync(payPal: finto);
        var varianti = await c.CreaProdottoAsync(c.A, "Libro", 4m, ("LIB-1", 10m, null));
        var ritiro = await c.CreaRitiroAsync(c.A);
        await c.ConfiguraPagamentoAsync(c.A);
        await c.AggiungiAsync(c.Cliente, varianti[0], 1);
        var ordine = (await c.ComeAsync(c.A, s => s.CreaDaCarrelloAsync(c.Cliente, "it", new DatiOrdine(ritiro, null, null, null, null, null, null, 10m), Ct))).Id!.Value;
        await c.ComePagamentiAsync(c.A, s => s.AvviaAsync(c.Cliente, ordine, DvCms.Models.Negozio.NomiProviderPagamento.PayPal, ContestoNegozio.Link, Ct));
        var idProvider = (await c.TentativiAsync(ordine)).Single().IdProvider;
        finto.Approva(idProvider);
        Assert.Equal(RisultatoPagamento.DaRimborsare, await c.ComePagamentiAsync(c.A, s => s.CompletaAsync(c.Cliente, ordine, idProvider, Ct)));
        var tentativo = (await c.TentativiAsync(ordine)).Single();

        var comeCliente = await c.ComeRimborsiAsync(c.A, s => s.RimborsaIncassoAsync(c.Cliente, tentativo.Id, null, Ct));
        var rimborsato = await c.ComeRimborsiAsync(c.A, s => s.RimborsaIncassoAsync(c.AdminA, tentativo.Id, "Importo non corrispondente", Ct));
        var ripetuto = await c.ComeRimborsiAsync(c.A, s => s.RimborsaIncassoAsync(c.AdminA, tentativo.Id, null, Ct));

        Assert.Equal(RisultatoRimborso.NonAutorizzato, comeCliente);
        Assert.Equal(RisultatoRimborso.Completato, rimborsato);
        Assert.Equal(RisultatoRimborso.NonRimborsabile, ripetuto);
        Assert.Equal(StatoTentativoPagamento.Rimborsato, (await c.TentativiAsync(ordine)).Single().Stato);
        var rimborso = (await c.ComeRimborsiAsync(c.A, s => s.ElencaAsync(c.AdminA, ordine, Ct))).Single();
        Assert.Equal((1.00m, true), (rimborso.Importo, rimborso.IncassoOrfano));
        var ordineDopo = await LeggiOrdineAsync(c, ordine);
        Assert.Equal((StatoOrdine.InAttesaDiPagamento, 0m), (ordineDopo.Stato, ordineDopo.TotaleRimborsato));
    }
}
