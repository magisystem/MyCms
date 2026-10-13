using System.Text.Json.Nodes;
using DvCms.Core.Accessi;
using DvCms.Data;
using DvCms.DTO.Negozio;
using DvCms.IntegrationTests.Negozio;
using DvCms.IntegrationTests.Supporto;
using DvCms.Models.Negozio;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DvCms.IntegrationTests.Pagamenti;

/// <summary>Rimborsi fatti a mano dai pannelli di PayPal e Nexi (ADR-0022): riconosciuti dalle notifiche e applicati all'ordine, senza doppi conteggi.</summary>
public class RimborsiEsterniTest
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static IntestazioniWebhook Firma() => new("SHA256withRSA", "https://api.paypal.com/cert", "TRX-1", PayPalFinto.FirmaValida, "2026-10-13T10:00:00Z");

    private static string EventoRimborso(string idEvento, string idRimborso, string tipo = "PAYMENT.CAPTURE.REFUNDED") => new JsonObject
    {
        ["id"] = idEvento,
        ["event_type"] = tipo,
        ["resource"] = new JsonObject { ["id"] = idRimborso, ["status"] = "COMPLETED" },
    }.ToJsonString();

    /// <summary>Ordine da 10 EUR già pagato con il provider indicato.</summary>
    private static async Task<(ContestoNegozio C, PayPalFinto PayPal, NexiFinto Nexi, Guid Ordine, string IdProvider)> PagatoAsync(string provider)
    {
        var payPal = new PayPalFinto();
        var nexi = new NexiFinto();
        var c = await ContestoNegozio.CreaAsync(payPal: payPal, nexi: nexi);
        var varianti = await c.CreaProdottoAsync(c.A, "Libro", 4m, ("LIB-1", 10.00m, null));
        var ritiro = await c.CreaRitiroAsync(c.A);
        await c.ImpostaGiacenzaAsync(c.A, varianti[0], 5);
        await c.ConfiguraPagamentoAsync(c.A);
        await c.ConfiguraNexiAsync(c.A);
        await c.AggiungiAsync(c.Cliente, varianti[0], 1);
        var ordine = (await c.ComeAsync(c.A, s => s.CreaDaCarrelloAsync(c.Cliente, "it", new DatiOrdine(ritiro, null, null, null, null, null, null, 10m), Ct))).Id!.Value;
        await c.ComePagamentiAsync(c.A, s => s.AvviaAsync(c.Cliente, ordine, provider, ContestoNegozio.Link, Ct));
        var idProvider = (await c.TentativiAsync(ordine)).Single().IdProvider;
        if (provider == NomiProviderPagamento.PayPal)
        {
            payPal.Approva(idProvider);
        }
        else
        {
            nexi.Paga(idProvider);
        }

        Assert.Equal(RisultatoPagamento.Pagato, await c.ComePagamentiAsync(c.A, s => s.CompletaAsync(c.Cliente, ordine, idProvider, Ct)));
        return (c, payPal, nexi, ordine, idProvider);
    }

    private static async Task<List<Rimborso>> RimborsiAsync(ContestoNegozio c, Guid ordine)
    {
        await using var ambito = c.Web.Fabbrica.Services.CreateAsyncScope();
        return await ambito.ServiceProvider.GetRequiredService<ContestoDatabase>().Rimborsi.IgnoreQueryFilters().AsNoTracking().Where(r => r.IdOrdine == ordine).OrderBy(r => r.CreatoIlUtc).ToListAsync(Ct);
    }

    private static Task<EsitoWebhook> WebhookPayPalAsync(ContestoNegozio c, string corpo) =>
        c.ComePagamentiAsync(c.A, s => s.ElaboraWebhookAsync(Firma(), corpo, Ct));

    private static Task<EsitoWebhook> NotificaNexiAsync(ContestoNegozio c, string corpo) =>
        c.ComePagamentiAsync(c.A, s => s.ElaboraNotificaNexiAsync(corpo, Ct));

    [Fact]
    public async Task UnRimborsoPayPalFattoAManoAggiornaLOrdineFinoAllaChiusura()
    {
        var (c, payPal, _, ordine, idProvider) = await PagatoAsync(NomiProviderPagamento.PayPal);
        await using var _ = c;

        var primo = payPal.RimborsaEsternamente(idProvider, 4m);
        Assert.Equal(EsitoWebhook.Elaborato, await WebhookPayPalAsync(c, EventoRimborso("WH-R1", primo)));

        var parziale = await c.OrdineAsync(ordine);
        Assert.Equal((StatoOrdine.Pagato, 4m), (parziale.Stato, parziale.TotaleRimborsato));
        var riga = Assert.Single(await RimborsiAsync(c, ordine));
        Assert.Equal((StatoRimborso.Completato, 4m, true, false, primo), (riga.Stato, riga.Importo, riga.Esterno, riga.IncassoOrfano, riga.IdProvider));
        Assert.Null(riga.IdUtenzaRichiedente);

        var secondo = payPal.RimborsaEsternamente(idProvider, 6m);
        Assert.Equal(EsitoWebhook.Elaborato, await WebhookPayPalAsync(c, EventoRimborso("WH-R2", secondo, "PAYMENT.REFUND.COMPLETED")));

        var totale = await c.OrdineAsync(ordine);
        Assert.Equal((StatoOrdine.Rimborsato, 10m), (totale.Stato, totale.TotaleRimborsato));
        Assert.Equal(2, (await RimborsiAsync(c, ordine)).Count);
        await using var ambito = c.Web.Fabbrica.Services.CreateAsyncScope();
        var audit = await ambito.ServiceProvider.GetRequiredService<ContestoDatabase>().RegistroAudit.IgnoreQueryFilters().CountAsync(a => a.Azione == AzioniAudit.RimborsoEsterno && a.Esito == DvCms.Models.Accessi.RegistroAudit.EsitoRiuscito, Ct);
        Assert.Equal(2, audit);
    }

    [Fact]
    public async Task LoStessoRimborsoNotificatoDueVolteOConDueEventiSiConteggiaUnaVolta()
    {
        var (c, payPal, _, ordine, idProvider) = await PagatoAsync(NomiProviderPagamento.PayPal);
        await using var _ = c;
        var rimborso = payPal.RimborsaEsternamente(idProvider, 4m);

        var uno = await WebhookPayPalAsync(c, EventoRimborso("WH-1", rimborso));
        var stessoEvento = await WebhookPayPalAsync(c, EventoRimborso("WH-1", rimborso));
        var altroEvento = await WebhookPayPalAsync(c, EventoRimborso("WH-2", rimborso, "PAYMENT.REFUND.COMPLETED"));

        Assert.Equal(EsitoWebhook.Elaborato, uno);
        Assert.Equal(EsitoWebhook.Duplicato, stessoEvento);
        Assert.Equal(EsitoWebhook.Elaborato, altroEvento);
        Assert.Equal(4m, (await c.OrdineAsync(ordine)).TotaleRimborsato);
        Assert.Single(await RimborsiAsync(c, ordine));
    }

    [Fact]
    public async Task UnRimborsoChiestoDalPannelloLaCuiRispostaSiESmarritaNonVieneContatoDueVolte()
    {
        var (c, payPal, _, ordine, idProvider) = await PagatoAsync(NomiProviderPagamento.PayPal);
        await using var _ = c;

        // Il pannello ha impegnato 4,00 e chiesto il rimborso, ma la risposta non è arrivata: il rimborso è «in corso» senza identificativo.
        var tentativo = (await c.TentativiAsync(ordine)).Single();
        await using (var ambito = c.Web.Fabbrica.Services.CreateAsyncScope())
        {
            var contesto = ambito.ServiceProvider.GetRequiredService<ContestoDatabase>();
            contesto.Rimborsi.Add(new Rimborso
            {
                Id = Guid.NewGuid(),
                IdTenant = c.A.IdTenant,
                IdSito = c.A.IdSito,
                IdOrdine = ordine,
                IdTentativo = tentativo.Id,
                Importo = 4m,
                Stato = StatoRimborso.InCorso,
                IdUtenzaRichiedente = c.AdminA,
                CreatoIlUtc = DateTime.UtcNow,
                AggiornatoIlUtc = DateTime.UtcNow,
            });
            await contesto.Ordini.IgnoreQueryFilters().Where(o => o.Id == ordine).ExecuteUpdateAsync(x => x.SetProperty(o => o.TotaleRimborsato, 4m), Ct);
            await contesto.SaveChangesAsync(Ct);
        }

        var inProvider = payPal.RimborsaEsternamente(idProvider, 4m);
        Assert.Equal(EsitoWebhook.Elaborato, await WebhookPayPalAsync(c, EventoRimborso("WH-A", inProvider)));

        var righe = await RimborsiAsync(c, ordine);
        var riga = Assert.Single(righe);
        Assert.Equal((StatoRimborso.Completato, false, inProvider), (riga.Stato, riga.Esterno, riga.IdProvider));
        Assert.Equal(4m, (await c.OrdineAsync(ordine)).TotaleRimborsato);
    }

    [Fact]
    public async Task UnRimborsoOltreIlResiduoNonSiRegistraENonRompeIlConto()
    {
        var (c, payPal, _, ordine, idProvider) = await PagatoAsync(NomiProviderPagamento.PayPal);
        await using var _ = c;
        var eccessivo = payPal.RimborsaEsternamente(idProvider, 12m);

        var esito = await WebhookPayPalAsync(c, EventoRimborso("WH-X", eccessivo));

        Assert.Equal(EsitoWebhook.Elaborato, esito);
        Assert.Equal((StatoOrdine.Pagato, 0m), ((await c.OrdineAsync(ordine)).Stato, (await c.OrdineAsync(ordine)).TotaleRimborsato));
        Assert.Empty(await RimborsiAsync(c, ordine));
        await using var ambito = c.Web.Fabbrica.Services.CreateAsyncScope();
        var anomalie = await ambito.ServiceProvider.GetRequiredService<ContestoDatabase>().RegistroAudit.IgnoreQueryFilters().CountAsync(a => a.Azione == AzioniAudit.RimborsoEsterno && a.Esito == DvCms.Models.Accessi.RegistroAudit.EsitoNegato, Ct);
        Assert.Equal(1, anomalie);
    }

    [Fact]
    public async Task UnIncassoDaRimborsareRestituitoAManoDiventaRimborsato()
    {
        var payPal = new PayPalFinto();
        var c = await ContestoNegozio.CreaAsync(payPal: payPal);
        await using var _ = c;
        var varianti = await c.CreaProdottoAsync(c.A, "Libro", 4m, ("LIB-1", 10.00m, null));
        var ritiro = await c.CreaRitiroAsync(c.A);
        await c.ImpostaGiacenzaAsync(c.A, varianti[0], 5);
        await c.ConfiguraPagamentoAsync(c.A);
        await c.AggiungiAsync(c.Cliente, varianti[0], 1);
        var ordine = (await c.ComeAsync(c.A, s => s.CreaDaCarrelloAsync(c.Cliente, "it", new DatiOrdine(ritiro, null, null, null, null, null, null, 10m), Ct))).Id!.Value;
        await c.ComePagamentiAsync(c.A, s => s.AvviaAsync(c.Cliente, ordine, NomiProviderPagamento.PayPal, ContestoNegozio.Link, Ct));
        var idProvider = (await c.TentativiAsync(ordine)).Single().IdProvider;
        payPal.Approva(idProvider);

        // L'ordine viene chiuso prima che il cliente torni: l'incasso arriva su un ordine annullato → «da rimborsare».
        await c.ImpostaStatoAsync(ordine, StatoOrdine.Annullato);
        Assert.Equal(RisultatoPagamento.OrdineChiuso, await c.ComePagamentiAsync(c.A, s => s.CompletaAsync(c.Cliente, ordine, idProvider, Ct)));
        payPal.Approva(idProvider);
        await c.ComePagamentiAsync(c.A, s => s.RiconciliaAsync(Ct));
        await using (var ambito = c.Web.Fabbrica.Services.CreateAsyncScope())
        {
            // Incasso presente presso il provider e non abbinato: si porta il tentativo allo stato «da rimborsare».
            await ambito.ServiceProvider.GetRequiredService<ContestoDatabase>().TentativiPagamento.IgnoreQueryFilters().Where(t => t.IdOrdine == ordine)
                .ExecuteUpdateAsync(x => x.SetProperty(t => t.Stato, StatoTentativoPagamento.DaRimborsare).SetProperty(t => t.IdCattura, $"CAP-{idProvider}").SetProperty(t => t.ImportoIncassato, 10m), Ct);
        }

        var rimborso = payPal.RimborsaEsternamente(idProvider, 10m);
        Assert.Equal(EsitoWebhook.Elaborato, await WebhookPayPalAsync(c, EventoRimborso("WH-O", rimborso)));

        Assert.Equal(StatoTentativoPagamento.Rimborsato, Assert.Single(await c.TentativiAsync(ordine)).Stato);
        var riga = Assert.Single(await RimborsiAsync(c, ordine));
        Assert.True(riga.IncassoOrfano);
        Assert.True(riga.Esterno);
        Assert.Equal(0m, (await c.OrdineAsync(ordine)).TotaleRimborsato);
    }

    [Fact]
    public async Task UnRimborsoNexiFattoAManoSiRiconosceDallaNotificaEAggiornaLOrdine()
    {
        var (c, _, nexi, ordine, idProvider) = await PagatoAsync(NomiProviderPagamento.Nexi);
        await using var _ = c;

        nexi.RimborsaEsternamente(idProvider, 4m);
        Assert.Equal(EsitoWebhook.Elaborato, await NotificaNexiAsync(c, nexi.Notifica(idProvider, idEvento: "N-1", tipo: "REFUND", risultato: "REFUNDED")));

        var parziale = await c.OrdineAsync(ordine);
        Assert.Equal((StatoOrdine.Pagato, 4m), (parziale.Stato, parziale.TotaleRimborsato));
        var riga = Assert.Single(await RimborsiAsync(c, ordine));
        Assert.Equal((StatoRimborso.Completato, 4m, true), (riga.Stato, riga.Importo, riga.Esterno));

        // Una notifica ripetuta (anche con un altro identificativo di evento) non conta due volte lo stesso rimborso.
        Assert.Equal(EsitoWebhook.Elaborato, await NotificaNexiAsync(c, nexi.Notifica(idProvider, idEvento: "N-2", tipo: "REFUND", risultato: "REFUNDED")));
        Assert.Equal(4m, (await c.OrdineAsync(ordine)).TotaleRimborsato);

        nexi.RimborsaEsternamente(idProvider, 6m);
        Assert.Equal(EsitoWebhook.Elaborato, await NotificaNexiAsync(c, nexi.Notifica(idProvider, idEvento: "N-3", tipo: "REFUND", risultato: "REFUNDED")));

        var totale = await c.OrdineAsync(ordine);
        Assert.Equal((StatoOrdine.Rimborsato, 10m), (totale.Stato, totale.TotaleRimborsato));
        Assert.Equal(2, (await RimborsiAsync(c, ordine)).Count);
    }

    [Fact]
    public async Task UnaNotificaNexiCheNonRiguardaUnRimborsoNonLoCerca()
    {
        var (c, _, nexi, ordine, idProvider) = await PagatoAsync(NomiProviderPagamento.Nexi);
        await using var _ = c;
        nexi.RimborsaEsternamente(idProvider, 4m);
        var richiesteIniziali = nexi.Richieste.Count;

        Assert.Equal(EsitoWebhook.Elaborato, await NotificaNexiAsync(c, nexi.Notifica(idProvider, idEvento: "N-C", tipo: "CAPTURE", risultato: "EXECUTED")));

        Assert.Equal(richiesteIniziali, nexi.Richieste.Count);
        Assert.Equal(0m, (await c.OrdineAsync(ordine)).TotaleRimborsato);
        Assert.Empty(await RimborsiAsync(c, ordine));
    }

    [Fact]
    public async Task IlRimborsoEsternoCompareNelDettaglioAmministrativo()
    {
        var (c, _, nexi, ordine, idProvider) = await PagatoAsync(NomiProviderPagamento.Nexi);
        await using var _ = c;
        nexi.RimborsaEsternamente(idProvider, 10m);
        await NotificaNexiAsync(c, nexi.Notifica(idProvider, idEvento: "N-W", tipo: "REFUND", risultato: "REFUNDED"));
        var admin = c.Web.SessioneSu(c.A.Host);
        using var accesso = await admin.AccediAsync("anna@esempio.it", DatiProva.PasswordProva);

        var html = System.Net.WebUtility.HtmlDecode(await (await admin.GetAsync($"/admin/negozio/ordini/{ordine}")).Content.ReadAsStringAsync(Ct));

        Assert.Contains("Eseguito dal pannello del provider", html, StringComparison.Ordinal);
        Assert.Contains("Rimborsato", html, StringComparison.Ordinal);
    }
}
