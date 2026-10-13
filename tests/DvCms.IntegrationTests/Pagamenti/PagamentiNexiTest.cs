using System.Text.Json.Nodes;
using DvCms.Application.Negozio;
using DvCms.Application.Piattaforma;
using DvCms.Domain.Negozio;
using DvCms.Infrastructure.Persistenza;
using DvCms.IntegrationTests.Negozio;
using DvCms.IntegrationTests.Supporto;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DvCms.IntegrationTests.Pagamenti;

/// <summary>Pagamenti con Nexi XPay (ADR-0022): pagina ospitata, esito dal gateway, notifiche con token, riconciliazione, produzione bloccata e nessun rimborso dal pannello.</summary>
public class PagamentiNexiTest
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static DatiOrdine Ritiro(Guid metodo, decimal atteso) => new(metodo, null, null, null, null, null, null, atteso);

    /// <summary>Negozio con Nexi configurato (e, se chiesto, anche PayPal), un articolo con giacenza 5 a 10 EUR e un ordine da 10 EUR.</summary>
    private static async Task<(ContestoNegozio C, NexiFinto Nexi, Guid Variante, Guid Ordine)> PreparaAsync(bool conPayPal = false, bool produzione = false)
    {
        var nexi = new NexiFinto();
        var c = await ContestoNegozio.CreaAsync(payPal: new PayPalFinto(), nexi: nexi, produzioneAbilitata: produzione);
        var varianti = await c.CreaProdottoAsync(c.A, "Libro", 4m, ("LIB-1", 10.00m, null));
        var ritiro = await c.CreaRitiroAsync(c.A);
        await c.ImpostaGiacenzaAsync(c.A, varianti[0], 5);
        await c.ConfiguraNexiAsync(c.A);
        if (conPayPal)
        {
            await c.ConfiguraPagamentoAsync(c.A);
        }

        await c.AggiungiAsync(c.Cliente, varianti[0], 1);
        var esito = await c.ComeAsync(c.A, s => s.CreaDaCarrelloAsync(c.Cliente, "it", Ritiro(ritiro, 10m), Ct));
        Assert.True(esito.Riuscito, esito.Codice.ToString());
        return (c, nexi, varianti[0], esito.Id!.Value);
    }

    private static Task<EsitoAvvioPagamento> AvviaAsync(ContestoNegozio c, Guid ordine, string provider = NomiProviderPagamento.Nexi, Guid? cliente = null) =>
        c.ComePagamentiAsync(c.A, s => s.AvviaAsync(cliente ?? c.Cliente, ordine, provider, ContestoNegozio.Link, Ct));

    private static Task<RisultatoPagamento> CompletaAsync(ContestoNegozio c, Guid ordine, string idProvider) =>
        c.ComePagamentiAsync(c.A, s => s.CompletaAsync(c.Cliente, ordine, idProvider, Ct));

    [Fact]
    public async Task AvviaCreaIlPagamentoSullaPaginaDiNexiConImportoDelServerENotifica()
    {
        var (c, nexi, _, ordine) = await PreparaAsync();
        await using var _ = c;

        var avvio = await AvviaAsync(c, ordine);

        Assert.Equal(RisultatoPagamento.Avviato, avvio.Risultato);
        var tentativo = Assert.Single(await c.TentativiAsync(ordine));
        Assert.Equal(("Nexi", StatoTentativoPagamento.Creato, 10m), (tentativo.Provider, tentativo.Stato, tentativo.Importo));
        Assert.StartsWith("NX1-", tentativo.IdProvider, StringComparison.Ordinal);
        Assert.InRange(tentativo.IdProvider.Length, 5, 27);
        Assert.Equal($"https://xpaysandbox.nexigroup.com/hpp/nexi/{tentativo.IdProvider}?operationId=1", avvio.UrlApprovazione!.AbsoluteUri);

        var richiesta = Assert.Single(nexi.Richieste, r => r.Metodo == HttpMethod.Post);
        Assert.Equal(NexiFinto.ChiaveValida, richiesta.ChiaveApi);
        Assert.StartsWith("https://xpaysandbox.nexigroup.com/api/phoenix-0.0/psp/api/v1/orders/hpp", richiesta.Indirizzo.AbsoluteUri, StringComparison.Ordinal);
        var corpo = nexi.CorpoCreazione(tentativo.IdProvider);
        Assert.Equal("1000", corpo["order"]!["amount"]!.GetValue<string>());
        Assert.Equal("EUR", corpo["order"]!["currency"]!.GetValue<string>());
        Assert.Equal("cliente@esempio.it", corpo["order"]!["customerInfo"]!["cardHolderEmail"]!.GetValue<string>());
        Assert.Equal(("PAY", "1000", "IMPLICIT", "ita"), (
            corpo["paymentSession"]!["actionType"]!.GetValue<string>(),
            corpo["paymentSession"]!["amount"]!.GetValue<string>(),
            corpo["paymentSession"]!["captureType"]!.GetValue<string>(),
            corpo["paymentSession"]!["language"]!.GetValue<string>()));
        Assert.Equal($"https://{c.A.Host}/account/ordini/{ordine}/pagamento", corpo["paymentSession"]!["resultUrl"]!.GetValue<string>());
        Assert.Equal($"https://{c.A.Host}/account/ordini/{ordine}", corpo["paymentSession"]!["cancelUrl"]!.GetValue<string>());
        Assert.Equal($"https://{c.A.Host}/webhooks/nexi", corpo["paymentSession"]!["notificationUrl"]!.GetValue<string>());

        // Del token di sicurezza si conserva solo l'impronta.
        Assert.NotNull(tentativo.HashTokenNotifica);
        Assert.Equal(64, tentativo.HashTokenNotifica!.Length);
        Assert.DoesNotContain(nexi.Token(tentativo.IdProvider), tentativo.HashTokenNotifica, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AlRitornoDelClienteLEsitoSiLeggeDaNexiELOrdineDiventaPagato()
    {
        var (c, nexi, variante, ordine) = await PreparaAsync();
        await using var _ = c;
        await AvviaAsync(c, ordine);
        var idProvider = (await c.TentativiAsync(ordine)).Single().IdProvider;
        nexi.Paga(idProvider);

        var risultato = await CompletaAsync(c, ordine, idProvider);

        Assert.Equal(RisultatoPagamento.Pagato, risultato);
        Assert.Equal(StatoOrdine.Pagato, await c.StatoOrdineAsync(ordine));
        var tentativo = Assert.Single(await c.TentativiAsync(ordine));
        Assert.Equal((StatoTentativoPagamento.Catturato, 10m), (tentativo.Stato, tentativo.ImportoIncassato));
        Assert.StartsWith("OP-2-", tentativo.IdCattura, StringComparison.Ordinal);
        var giacenza = await c.GiacenzaAsync(variante);
        Assert.Equal(4, giacenza.Disponibile);
        Assert.Equal(0, giacenza.Riservata);

        // Ripetere il ritorno non cambia nulla.
        Assert.Equal(RisultatoPagamento.Pagato, await CompletaAsync(c, ordine, idProvider));
    }

    [Fact]
    public async Task UnRitornoSenzaPagareLasciaLOrdineInAttesaEPoiSiPuoPagare()
    {
        var (c, nexi, _, ordine) = await PreparaAsync();
        await using var _ = c;
        await AvviaAsync(c, ordine);
        var idProvider = (await c.TentativiAsync(ordine)).Single().IdProvider;

        Assert.Equal(RisultatoPagamento.NonApprovato, await CompletaAsync(c, ordine, idProvider));
        Assert.Equal(StatoOrdine.InAttesaDiPagamento, await c.StatoOrdineAsync(ordine));

        nexi.Annulla(idProvider);
        Assert.Equal(RisultatoPagamento.NonApprovato, await CompletaAsync(c, ordine, idProvider));

        nexi.Paga(idProvider);
        Assert.Equal(RisultatoPagamento.Pagato, await CompletaAsync(c, ordine, idProvider));
    }

    [Fact]
    public async Task UnPagamentoRifiutatoFaFallireLOrdineEPermetteDiRiprovare()
    {
        var (c, nexi, _, ordine) = await PreparaAsync();
        await using var _ = c;
        await AvviaAsync(c, ordine);
        var primo = (await c.TentativiAsync(ordine)).Single().IdProvider;
        nexi.Rifiuta(primo);

        Assert.Equal(RisultatoPagamento.Fallito, await CompletaAsync(c, ordine, primo));
        Assert.Equal(StatoOrdine.PagamentoFallito, await c.StatoOrdineAsync(ordine));

        var nuovo = await AvviaAsync(c, ordine);
        Assert.Equal(RisultatoPagamento.Avviato, nuovo.Risultato);
        var tentativi = await c.TentativiAsync(ordine);
        Assert.Equal(2, tentativi.Count);
        Assert.NotEqual(tentativi[0].IdProvider, tentativi[1].IdProvider);
        nexi.Paga(tentativi[1].IdProvider);
        Assert.Equal(RisultatoPagamento.Pagato, await CompletaAsync(c, ordine, tentativi[1].IdProvider));
    }

    [Fact]
    public async Task UnPagamentoInSospesoRestaApertoFinoAllEsito()
    {
        var (c, nexi, _, ordine) = await PreparaAsync();
        await using var _ = c;
        await AvviaAsync(c, ordine);
        var idProvider = (await c.TentativiAsync(ordine)).Single().IdProvider;
        nexi.MettiInSospeso(idProvider);

        Assert.Equal(RisultatoPagamento.InSospeso, await CompletaAsync(c, ordine, idProvider));
        Assert.Equal(StatoOrdine.InAttesaDiPagamento, await c.StatoOrdineAsync(ordine));
        Assert.Equal(StatoTentativoPagamento.InSospeso, Assert.Single(await c.TentativiAsync(ordine)).Stato);
    }

    [Fact]
    public async Task UnImportoIncassatoDiversoNonDiventaPagatoMaDaRimborsare()
    {
        var (c, nexi, _, ordine) = await PreparaAsync();
        await using var _ = c;
        await AvviaAsync(c, ordine);
        var idProvider = (await c.TentativiAsync(ordine)).Single().IdProvider;
        nexi.Paga(idProvider, importo: "900");

        Assert.Equal(RisultatoPagamento.DaRimborsare, await CompletaAsync(c, ordine, idProvider));

        Assert.Equal(StatoOrdine.InAttesaDiPagamento, await c.StatoOrdineAsync(ordine));
        var tentativo = Assert.Single(await c.TentativiAsync(ordine));
        Assert.Equal((StatoTentativoPagamento.DaRimborsare, 9m), (tentativo.Stato, tentativo.ImportoIncassato));
    }

    [Fact]
    public async Task LaNotificaConIlTokenGiustoPagaSenzaIlRitornoEUnaRipetutaNonFaNulla()
    {
        var (c, nexi, _, ordine) = await PreparaAsync();
        await using var _ = c;
        await AvviaAsync(c, ordine);
        var idProvider = (await c.TentativiAsync(ordine)).Single().IdProvider;
        nexi.Paga(idProvider);
        var notifica = nexi.Notifica(idProvider, idEvento: "EVT-1");

        var prima = await c.ComePagamentiAsync(c.A, s => s.ElaboraNotificaNexiAsync(notifica, Ct));
        var seconda = await c.ComePagamentiAsync(c.A, s => s.ElaboraNotificaNexiAsync(notifica, Ct));

        Assert.Equal(EsitoWebhook.Elaborato, prima);
        Assert.Equal(EsitoWebhook.Duplicato, seconda);
        Assert.Equal(StatoOrdine.Pagato, await c.StatoOrdineAsync(ordine));
        var evento = Assert.Single(await c.EventiAsync());
        Assert.Equal(("Nexi", "EVT-1", EventoWebhook.EsitoElaborato), (evento.Provider, evento.IdEvento, evento.Esito));
    }

    [Fact]
    public async Task UnaNotificaConTokenSbagliatoOSenzaOrdineNonHaEffetto()
    {
        var (c, nexi, _, ordine) = await PreparaAsync();
        await using var _ = c;
        await AvviaAsync(c, ordine);
        var idProvider = (await c.TentativiAsync(ordine)).Single().IdProvider;
        nexi.Paga(idProvider);

        var tokenSbagliato = await c.ComePagamentiAsync(c.A, s => s.ElaboraNotificaNexiAsync(nexi.Notifica(idProvider, token: "token-inventato"), Ct));
        var ordineSconosciuto = new JsonObject
        {
            ["eventId"] = "EVT-9",
            ["securityToken"] = nexi.Token(idProvider),
            ["operation"] = new JsonObject { ["orderId"] = "NX99-ZZZZZZZZZZ", ["operationType"] = "CAPTURE", ["operationResult"] = "EXECUTED" },
        }.ToJsonString();
        var sconosciuto = await c.ComePagamentiAsync(c.A, s => s.ElaboraNotificaNexiAsync(ordineSconosciuto, Ct));
        var malformata = await c.ComePagamentiAsync(c.A, s => s.ElaboraNotificaNexiAsync("{non json", Ct));
        var incompleta = await c.ComePagamentiAsync(c.A, s => s.ElaboraNotificaNexiAsync("{\"eventId\":\"E\"}", Ct));

        Assert.Equal(EsitoWebhook.FirmaNonValida, tokenSbagliato);
        Assert.Equal(EsitoWebhook.Ignorato, sconosciuto);
        Assert.Equal(EsitoWebhook.Malformato, malformata);
        Assert.Equal(EsitoWebhook.Malformato, incompleta);
        Assert.Equal(StatoOrdine.InAttesaDiPagamento, await c.StatoOrdineAsync(ordine));
        Assert.Empty(await c.EventiAsync());
    }

    [Fact]
    public async Task UnaNotificaDiUnAltroTenantNonTocca()
    {
        var (c, nexi, _, ordine) = await PreparaAsync();
        await using var _ = c;
        await AvviaAsync(c, ordine);
        var idProvider = (await c.TentativiAsync(ordine)).Single().IdProvider;
        nexi.Paga(idProvider);

        // Arrivata sul dominio del tenant B: l'ordine di A lì non esiste.
        var esito = await c.ComePagamentiAsync(c.B, s => s.ElaboraNotificaNexiAsync(nexi.Notifica(idProvider), Ct));

        Assert.Equal(EsitoWebhook.Ignorato, esito);
        Assert.Equal(StatoOrdine.InAttesaDiPagamento, await c.StatoOrdineAsync(ordine));
    }

    [Fact]
    public async Task LaRiconciliazioneRecuperaUnIncassoMaiNotificato()
    {
        var (c, nexi, _, ordine) = await PreparaAsync();
        await using var _ = c;
        await AvviaAsync(c, ordine);
        var idProvider = (await c.TentativiAsync(ordine)).Single().IdProvider;
        nexi.Paga(idProvider);
        await c.InvecchiaTentativiAsync(ordine, TimeSpan.FromMinutes(30));

        var conclusi = await c.RiconciliaAsync();

        Assert.Equal(1, conclusi);
        Assert.Equal(StatoOrdine.Pagato, await c.StatoOrdineAsync(ordine));
    }

    [Fact]
    public async Task IlClienteVedeSoloIProviderConfiguratiEUnoSconosciutoNonSiAvvia()
    {
        var (c, _, _, ordine) = await PreparaAsync();
        await using var _ = c;

        Assert.Equal([NomiProviderPagamento.Nexi], await c.ComePagamentiAsync(c.A, s => s.ProviderDisponibiliAsync(Ct)));
        Assert.Equal(RisultatoPagamento.NonConfigurato, (await AvviaAsync(c, ordine, NomiProviderPagamento.PayPal)).Risultato);
        Assert.Equal(RisultatoPagamento.NonConfigurato, (await AvviaAsync(c, ordine, "Altro")).Risultato);
        Assert.Equal(RisultatoPagamento.NonConfigurato, (await AvviaAsync(c, ordine, null!)).Risultato);

        await c.ConfiguraPagamentoAsync(c.A);
        Assert.Equal([NomiProviderPagamento.PayPal, NomiProviderPagamento.Nexi], await c.ComePagamentiAsync(c.A, s => s.ProviderDisponibiliAsync(Ct)));
        Assert.Empty(await c.ComePagamentiAsync(c.B, s => s.ProviderDisponibiliAsync(Ct)));
    }

    [Fact]
    public async Task InProduzioneNexiNonPartePrimaDellInterruttore()
    {
        var (c, _, _, ordine) = await PreparaAsync();
        await using var _ = c;
        await c.ConfiguraNexiAsync(c.A, AmbientePagamento.Live);

        Assert.Empty(await c.ComePagamentiAsync(c.A, s => s.ProviderDisponibiliAsync(Ct)));
        Assert.Equal(RisultatoPagamento.ProduzioneBloccata, (await AvviaAsync(c, ordine)).Risultato);
    }

    [Fact]
    public async Task UnErroreDelGatewayNonCreaIlTentativoENonRivelaCorpoNeChiave()
    {
        var (c, nexi, _, ordine) = await PreparaAsync();
        await using var _ = c;
        nexi.Eccezione = new HttpRequestException("rete assente");

        var avvio = await AvviaAsync(c, ordine);

        Assert.Equal(RisultatoPagamento.ErroreProvider, avvio.Risultato);
        Assert.Empty(await c.TentativiAsync(ordine));
    }

    [Fact]
    public async Task IlRimborsoDalPannelloNonEsisteSuNexi()
    {
        var (c, nexi, _, ordine) = await PreparaAsync();
        await using var _ = c;
        await AvviaAsync(c, ordine);
        var idProvider = (await c.TentativiAsync(ordine)).Single().IdProvider;
        nexi.Paga(idProvider);
        Assert.Equal(RisultatoPagamento.Pagato, await CompletaAsync(c, ordine, idProvider));

        var rimborso = await c.ComeRimborsiAsync(c.A, s => s.RimborsaOrdineAsync(c.AdminA, ordine, null, "reso", Ct));

        Assert.Equal(RisultatoRimborso.NonRimborsabile, rimborso);
        Assert.Equal(StatoOrdine.Pagato, await c.StatoOrdineAsync(ordine));
    }

    [Fact]
    public async Task LaChiaveApiLaImpostaSoloIlPlatformAdminEntraCifrataENonSiRivela()
    {
        var nexi = new NexiFinto();
        await using var c = await ContestoNegozio.CreaAsync(nexi: nexi);
        await using var ambito = c.Web.Fabbrica.Services.CreateAsyncScope();
        var servizio = ambito.ServiceProvider.GetRequiredService<IServizioImpostazioniNexi>();

        foreach (var attore in new[] { c.AdminA, c.AdminB, c.Cliente })
        {
            Assert.Equal(CodiceEsito.NonAutorizzato, (await servizio.SalvaAsync(attore, c.A.IdTenant, c.A.IdSito, AmbientePagamento.Sandbox, "CHIAVE-SEGRETISSIMA", null, Ct)).Codice);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => servizio.LeggiAsync(attore, c.A.IdTenant, c.A.IdSito, Ct));
        }

        Assert.Equal(CodiceEsito.DatiNonValidi, (await servizio.SalvaAsync(c.Platform, c.A.IdTenant, c.A.IdSito, AmbientePagamento.Sandbox, null, null, Ct)).Codice);
        Assert.Equal(CodiceEsito.DatiNonValidi, (await servizio.SalvaAsync(c.Platform, c.A.IdTenant, c.A.IdSito, AmbientePagamento.Sandbox, "con spazio", null, Ct)).Codice);
        Assert.True((await servizio.SalvaAsync(c.Platform, c.A.IdTenant, c.A.IdSito, AmbientePagamento.Sandbox, "CHIAVE-SEGRETISSIMA", null, Ct)).Riuscito);

        var scheda = (await servizio.LeggiAsync(c.Platform, c.A.IdTenant, c.A.IdSito, Ct))!;
        Assert.True(scheda.ChiaveImpostata);
        Assert.True(scheda.Configurato);
        var contesto = ambito.ServiceProvider.GetRequiredService<ContestoDatabase>();
        var riga = await contesto.ImpostazioniNexiSiti.IgnoreQueryFilters().SingleAsync(Ct);
        Assert.DoesNotContain("CHIAVE-SEGRETISSIMA", riga.SegretiProtetti, StringComparison.Ordinal);
        var audit = await contesto.RegistroAudit.IgnoreQueryFilters().Where(a => a.Azione == DvCms.Application.Accessi.AzioniAudit.ModificaImpostazioniPagamento).ToListAsync(Ct);
        Assert.All(audit, a => Assert.DoesNotContain("CHIAVE-SEGRETISSIMA", a.Dettagli, StringComparison.Ordinal));

        // Una chiave vuota mantiene quella salvata.
        Assert.True((await servizio.SalvaAsync(c.Platform, c.A.IdTenant, c.A.IdSito, AmbientePagamento.Live, string.Empty, scheda.TokenConcorrenza, Ct)).Riuscito);
        Assert.True((await servizio.LeggiAsync(c.Platform, c.A.IdTenant, c.A.IdSito, Ct))!.ChiaveImpostata);
    }
}
