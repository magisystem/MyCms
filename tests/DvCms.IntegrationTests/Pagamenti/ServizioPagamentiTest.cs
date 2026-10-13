using System.Text.Json.Nodes;
using DvCms.Application.Negozio;
using DvCms.Application.Piattaforma;
using DvCms.Domain.Negozio;
using DvCms.IntegrationTests.Negozio;
using DvCms.IntegrationTests.Supporto;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DvCms.IntegrationTests.Pagamenti;

/// <summary>Pagamenti PayPal (F-004): avvio, cattura, esiti, importo, ordini chiusi, webhook firmati e deduplicati, concorrenza.</summary>
public class ServizioPagamentiTest
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static DatiOrdine Ritiro(Guid metodo, decimal atteso) => new(metodo, null, null, null, null, null, null, atteso);

    private static IntestazioniWebhook Firma(string firma = PayPalFinto.FirmaValida, string id = "TRX-1") =>
        new("SHA256withRSA", "https://api.paypal.com/cert", id, firma, "2026-10-11T10:00:00Z");

    private static string Evento(string id, string tipo, string idOrdineProvider) => new JsonObject
    {
        ["id"] = id,
        ["event_type"] = tipo,
        ["resource"] = tipo.StartsWith("CHECKOUT.ORDER.", StringComparison.Ordinal)
            ? new JsonObject { ["id"] = idOrdineProvider, ["status"] = "APPROVED" }
            : new JsonObject { ["id"] = "CAP-X", ["supplementary_data"] = new JsonObject { ["related_ids"] = new JsonObject { ["order_id"] = idOrdineProvider } } },
    }.ToJsonString();

    /// <summary>Scenario base: negozio con credenziali PayPal, un articolo con giacenza 5 a 10 EUR e un ordine da 10 EUR in attesa di pagamento.</summary>
    private static async Task<(ContestoNegozio C, PayPalFinto Finto, Guid Variante, Guid Ordine)> PreparaAsync(bool produzione = false)
    {
        var finto = new PayPalFinto();
        var c = await ContestoNegozio.CreaAsync(payPal: finto, produzioneAbilitata: produzione);
        var varianti = await c.CreaProdottoAsync(c.A, "Libro", 4m, ("LIB-1", 10.00m, null));
        var ritiro = await c.CreaRitiroAsync(c.A);
        await c.ImpostaGiacenzaAsync(c.A, varianti[0], 5);
        await c.ConfiguraPagamentoAsync(c.A);
        await c.AggiungiAsync(c.Cliente, varianti[0], 1);
        var esito = await c.ComeAsync(c.A, s => s.CreaDaCarrelloAsync(c.Cliente, "it", Ritiro(ritiro, 10m), Ct));
        Assert.True(esito.Riuscito, esito.Codice.ToString());
        return (c, finto, varianti[0], esito.Id!.Value);
    }

    private static async Task<string> AvviaEApprovaAsync(ContestoNegozio c, PayPalFinto finto, Guid ordine)
    {
        var avvio = await c.ComePagamentiAsync(c.A, s => s.AvviaAsync(c.Cliente, ordine, NomiProviderPagamento.PayPal, ContestoNegozio.Link, Ct));
        Assert.Equal(RisultatoPagamento.Avviato, avvio.Risultato);
        var idProvider = (await c.TentativiAsync(ordine)).Last().IdProvider;
        finto.Approva(idProvider);
        return idProvider;
    }

    [Fact]
    public async Task IlPagamentoRiuscitoSegnaPagatoLOrdineEFaUscireLaMerce()
    {
        var (c, finto, variante, ordine) = await PreparaAsync();
        await using var _ = c;

        var avvio = await c.ComePagamentiAsync(c.A, s => s.AvviaAsync(c.Cliente, ordine, NomiProviderPagamento.PayPal, ContestoNegozio.Link, Ct));
        var idProvider = (await c.TentativiAsync(ordine)).Single().IdProvider;
        finto.Approva(idProvider);
        var completa = await c.ComePagamentiAsync(c.A, s => s.CompletaAsync(c.Cliente, ordine, idProvider, Ct));
        var ripetuta = await c.ComePagamentiAsync(c.A, s => s.CompletaAsync(c.Cliente, ordine, idProvider, Ct));

        Assert.Equal(RisultatoPagamento.Avviato, avvio.Risultato);
        Assert.Equal("https://www.sandbox.paypal.com/checkoutnow?token=ORD-0001", avvio.UrlApprovazione!.AbsoluteUri);
        Assert.Equal("10.00", finto.ImportoOrdine(idProvider));
        Assert.Equal(RisultatoPagamento.Pagato, completa);
        Assert.Equal(RisultatoPagamento.Pagato, ripetuta);
        Assert.Equal(StatoOrdine.Pagato, await c.StatoOrdineAsync(ordine));
        var tentativo = (await c.TentativiAsync(ordine)).Single();
        Assert.Equal((StatoTentativoPagamento.Catturato, 10m, "EUR", "CAP-ORD-0001"), (tentativo.Stato, tentativo.Importo, tentativo.Valuta, tentativo.IdCattura));
        Assert.Equal(1, finto.Conta(HttpMethod.Post, "/v2/checkout/orders/ORD-0001/capture"));
        var giacenza = await c.GiacenzaAsync(variante);
        Assert.Equal((4, 0), (giacenza.Disponibile, giacenza.Riservata));
        Assert.Equal(StatoPrenotazione.Confermata, (await c.PrenotazioniAsync(ordine)).Single().Stato);
        Assert.Contains(await c.MovimentiAsync(variante), m => m.Motivo == MotivoMovimento.Vendita && m.IdOrdine == ordine);
    }

    [Fact]
    public async Task ConUnTotaleFissatoDalServerNonSiPaganoImportiDecisiDalCliente()
    {
        var (c, finto, _, ordine) = await PreparaAsync();
        await using var _ = c;

        await c.ComePagamentiAsync(c.A, s => s.AvviaAsync(c.Cliente, ordine, NomiProviderPagamento.PayPal, ContestoNegozio.Link, Ct));

        var creazione = finto.Richieste.Single(r => r.Percorso == "/v2/checkout/orders");
        Assert.Contains("\"value\":\"10.00\"", creazione.Corpo, StringComparison.Ordinal);
        Assert.Contains("\"return_url\":\"https://a.esempio.it/account/ordini/" + ordine.ToString("D") + "/pagamento\"", creazione.Corpo, StringComparison.Ordinal);
        Assert.Equal((await c.TentativiAsync(ordine)).Single().Id.ToString("D"), creazione.IdRichiesta);
    }

    [Fact]
    public async Task UnPagamentoNonApprovatoNonIncassaENonCambiaNulla()
    {
        var (c, finto, variante, ordine) = await PreparaAsync();
        await using var _ = c;
        await c.ComePagamentiAsync(c.A, s => s.AvviaAsync(c.Cliente, ordine, NomiProviderPagamento.PayPal, ContestoNegozio.Link, Ct));
        var idProvider = (await c.TentativiAsync(ordine)).Single().IdProvider;

        var esito = await c.ComePagamentiAsync(c.A, s => s.CompletaAsync(c.Cliente, ordine, idProvider, Ct));

        Assert.Equal(RisultatoPagamento.NonApprovato, esito);
        Assert.Equal(StatoOrdine.InAttesaDiPagamento, await c.StatoOrdineAsync(ordine));
        Assert.Equal(StatoTentativoPagamento.Creato, (await c.TentativiAsync(ordine)).Single().Stato);
        Assert.Equal(1, (await c.GiacenzaAsync(variante)).Riservata);
    }

    [Fact]
    public async Task UnPagamentoRifiutatoMetteLOrdineInPagamentoFallitoESiPuoRiprovare()
    {
        var (c, finto, variante, ordine) = await PreparaAsync();
        await using var _ = c;
        finto.EsitoCattura = "DECLINED";
        var primo = await AvviaEApprovaAsync(c, finto, ordine);

        var rifiutato = await c.ComePagamentiAsync(c.A, s => s.CompletaAsync(c.Cliente, ordine, primo, Ct));
        var statoDopoRifiuto = await c.StatoOrdineAsync(ordine);
        finto.EsitoCattura = "COMPLETED";
        var secondo = await AvviaEApprovaAsync(c, finto, ordine);
        var statoDopoNuovoAvvio = await c.StatoOrdineAsync(ordine);
        var pagato = await c.ComePagamentiAsync(c.A, s => s.CompletaAsync(c.Cliente, ordine, secondo, Ct));

        Assert.Equal(RisultatoPagamento.Fallito, rifiutato);
        Assert.Equal(StatoOrdine.PagamentoFallito, statoDopoRifiuto);
        Assert.Equal(StatoOrdine.InAttesaDiPagamento, statoDopoNuovoAvvio);
        Assert.NotEqual(primo, secondo);
        Assert.Equal(RisultatoPagamento.Pagato, pagato);
        Assert.Equal([StatoTentativoPagamento.Fallito, StatoTentativoPagamento.Catturato], (await c.TentativiAsync(ordine)).Select(t => t.Stato));
        Assert.Equal((4, 0), ((await c.GiacenzaAsync(variante)).Disponibile, (await c.GiacenzaAsync(variante)).Riservata));
    }

    [Fact]
    public async Task UnIncassoInSospesoNonPagaLOrdineFinoAllaConclusione()
    {
        var (c, finto, _, ordine) = await PreparaAsync();
        await using var _ = c;
        finto.EsitoCattura = "PENDING";
        var idProvider = await AvviaEApprovaAsync(c, finto, ordine);

        var inSospeso = await c.ComePagamentiAsync(c.A, s => s.CompletaAsync(c.Cliente, ordine, idProvider, Ct));
        Assert.Equal(RisultatoPagamento.InSospeso, inSospeso);
        Assert.Equal(StatoOrdine.InAttesaDiPagamento, await c.StatoOrdineAsync(ordine));

        finto.ConcludiSospeso(idProvider);
        var webhook = await c.ComePagamentiAsync(c.A, s => s.ElaboraWebhookAsync(Firma(), Evento("WH-1", "PAYMENT.CAPTURE.COMPLETED", idProvider), Ct));

        Assert.Equal(EsitoWebhook.Elaborato, webhook);
        Assert.Equal(StatoOrdine.Pagato, await c.StatoOrdineAsync(ordine));
    }

    [Fact]
    public async Task UnImportoIncassatoDiversoDaQuelloDovutoNonDichiaraPagatoLOrdine()
    {
        var (c, finto, variante, ordine) = await PreparaAsync();
        await using var _ = c;
        finto.ImportoCatturato = "1.00";
        var idProvider = await AvviaEApprovaAsync(c, finto, ordine);

        var esito = await c.ComePagamentiAsync(c.A, s => s.CompletaAsync(c.Cliente, ordine, idProvider, Ct));

        Assert.Equal(RisultatoPagamento.DaRimborsare, esito);
        Assert.Equal(StatoOrdine.InAttesaDiPagamento, await c.StatoOrdineAsync(ordine));
        Assert.Equal(StatoTentativoPagamento.DaRimborsare, (await c.TentativiAsync(ordine)).Single().Stato);
        Assert.Equal((5, 1), ((await c.GiacenzaAsync(variante)).Disponibile, (await c.GiacenzaAsync(variante)).Riservata));
    }

    [Fact]
    public async Task UnOrdineAnnullatoOScadutoPrimaDellIncassoNonSiIncassaAffatto()
    {
        var (c, finto, variante, annullato) = await PreparaAsync();
        await using var _ = c;
        var idAnnullato = await AvviaEApprovaAsync(c, finto, annullato);
        await c.ComeAsync(c.A, s => s.AnnullaMioAsync(c.Cliente, annullato, Ct));

        var daAnnullato = await c.ComePagamentiAsync(c.A, s => s.CompletaAsync(c.Cliente, annullato, idAnnullato, Ct));
        var nuovoAvvio = await c.ComePagamentiAsync(c.A, s => s.AvviaAsync(c.Cliente, annullato, NomiProviderPagamento.PayPal, ContestoNegozio.Link, Ct));

        await c.AggiungiAsync(c.Cliente, variante, 1);
        var ritiro = (await c.ComeAsync(c.A, s => s.ElencaMieiAsync(c.Cliente, Ct))).Count;
        Assert.Equal(1, ritiro);
        Assert.Equal(RisultatoPagamento.OrdineChiuso, daAnnullato);
        Assert.Equal(RisultatoPagamento.OrdineChiuso, nuovoAvvio.Risultato);
        Assert.Equal(0, finto.Conta(HttpMethod.Post, "/v2/checkout/orders/ORD-0001/capture"));
        Assert.Equal(StatoTentativoPagamento.Annullato, (await c.TentativiAsync(annullato)).Single().Stato);
    }

    [Fact]
    public async Task UnaPrenotazioneScadutaChiudeLOrdineEIlRitornoDalProviderNonIncassa()
    {
        var (c, finto, _, ordine) = await PreparaAsync();
        await using var _ = c;
        var idProvider = await AvviaEApprovaAsync(c, finto, ordine);
        await c.ScadiPrenotazioniAsync(ordine);

        var esito = await c.ComePagamentiAsync(c.A, s => s.CompletaAsync(c.Cliente, ordine, idProvider, Ct));

        Assert.Equal(RisultatoPagamento.OrdineChiuso, esito);
        Assert.Equal(StatoOrdine.Annullato, await c.StatoOrdineAsync(ordine));
        Assert.Equal(0, finto.Conta(HttpMethod.Post, "/v2/checkout/orders/ORD-0001/capture"));
    }

    [Fact]
    public async Task SenzaCredenzialiOConProduzioneBloccataNonSiAvviaNulla()
    {
        var finto = new PayPalFinto();
        await using var c = await ContestoNegozio.CreaAsync(payPal: finto);
        var varianti = await c.CreaProdottoAsync(c.A, "Libro", 4m, ("LIB-1", 10.00m, null));
        var ritiro = await c.CreaRitiroAsync(c.A);
        await c.AggiungiAsync(c.Cliente, varianti[0], 1);
        var ordine = (await c.ComeAsync(c.A, s => s.CreaDaCarrelloAsync(c.Cliente, "it", Ritiro(ritiro, 10m), Ct))).Id!.Value;

        var senza = await c.ComePagamentiAsync(c.A, s => s.AvviaAsync(c.Cliente, ordine, NomiProviderPagamento.PayPal, ContestoNegozio.Link, Ct));
        await c.ConfiguraPagamentoAsync(c.A, AmbientePagamento.Live);
        var live = await c.ComePagamentiAsync(c.A, s => s.AvviaAsync(c.Cliente, ordine, NomiProviderPagamento.PayPal, ContestoNegozio.Link, Ct));
        await c.ConfiguraPagamentoAsync(c.A);
        var sandbox = await c.ComePagamentiAsync(c.A, s => s.AvviaAsync(c.Cliente, ordine, NomiProviderPagamento.PayPal, ContestoNegozio.Link, Ct));

        Assert.Equal(RisultatoPagamento.NonConfigurato, senza.Risultato);
        Assert.Equal(RisultatoPagamento.ProduzioneBloccata, live.Risultato);
        Assert.Equal(RisultatoPagamento.Avviato, sandbox.Risultato);
        Assert.Single(finto.IdOrdini);
    }

    [Fact]
    public async Task ConIlCheckoutDiProduzioneAbilitatoLAmbienteLiveFunziona()
    {
        var (c, _, _, ordine) = await PreparaAsync(produzione: true);
        await using var _ = c;
        await c.ConfiguraPagamentoAsync(c.A, AmbientePagamento.Live);

        var avvio = await c.ComePagamentiAsync(c.A, s => s.AvviaAsync(c.Cliente, ordine, NomiProviderPagamento.PayPal, ContestoNegozio.Link, Ct));

        Assert.Equal(RisultatoPagamento.Avviato, avvio.Risultato);
    }

    [Fact]
    public async Task UnOrdineAltruiOdiUnAltroTenantNonSiPagaNeSiCompleta()
    {
        var (c, finto, _, ordine) = await PreparaAsync();
        await using var _ = c;
        var idProvider = await AvviaEApprovaAsync(c, finto, ordine);

        var avvioAltro = await c.ComePagamentiAsync(c.A, s => s.AvviaAsync(c.AltroCliente, ordine, NomiProviderPagamento.PayPal, ContestoNegozio.Link, Ct));
        var completaAltro = await c.ComePagamentiAsync(c.A, s => s.CompletaAsync(c.AltroCliente, ordine, idProvider, Ct));
        var avvioAltroTenant = await c.ComePagamentiAsync(c.B, s => s.AvviaAsync(c.ClienteDiB, ordine, NomiProviderPagamento.PayPal, ContestoNegozio.Link, Ct));
        var accountAltroTenant = await c.ComePagamentiAsync(c.A, s => s.CompletaAsync(c.ClienteDiB, ordine, idProvider, Ct));
        var tokenSbagliato = await c.ComePagamentiAsync(c.A, s => s.CompletaAsync(c.Cliente, ordine, "ORD-9999", Ct));
        var tokenNullo = await c.ComePagamentiAsync(c.A, s => s.CompletaAsync(c.Cliente, ordine, null, Ct));

        Assert.Equal(RisultatoPagamento.NonTrovato, avvioAltro.Risultato);
        Assert.Equal(RisultatoPagamento.NonTrovato, completaAltro);
        Assert.Equal(RisultatoPagamento.NonTrovato, avvioAltroTenant.Risultato);
        Assert.Equal(RisultatoPagamento.NonAutorizzato, accountAltroTenant);
        Assert.Equal(RisultatoPagamento.NonTrovato, tokenSbagliato);
        Assert.Equal(RisultatoPagamento.NonTrovato, tokenNullo);
        Assert.Equal(0, finto.Conta(HttpMethod.Post, "/v2/checkout/orders/ORD-0001/capture"));
    }

    [Fact]
    public async Task UnProviderIrraggiungibileDaUnErroreSenzaCambiareLOrdine()
    {
        var (c, finto, _, ordine) = await PreparaAsync();
        await using var _ = c;
        finto.Eccezione = new HttpRequestException("giù");

        var avvio = await c.ComePagamentiAsync(c.A, s => s.AvviaAsync(c.Cliente, ordine, NomiProviderPagamento.PayPal, ContestoNegozio.Link, Ct));

        Assert.Equal(RisultatoPagamento.ErroreProvider, avvio.Risultato);
        Assert.Empty(await c.TentativiAsync(ordine));
        Assert.Equal(StatoOrdine.InAttesaDiPagamento, await c.StatoOrdineAsync(ordine));
    }

    [Fact]
    public async Task UnWebhookFirmatoDiApprovazioneIncassaAncheSeIlClienteNonTorna()
    {
        var (c, finto, _, ordine) = await PreparaAsync();
        await using var _ = c;
        var idProvider = await AvviaEApprovaAsync(c, finto, ordine);

        var esito = await c.ComePagamentiAsync(c.A, s => s.ElaboraWebhookAsync(Firma(), Evento("WH-1", "CHECKOUT.ORDER.APPROVED", idProvider), Ct));

        Assert.Equal(EsitoWebhook.Elaborato, esito);
        Assert.Equal(StatoOrdine.Pagato, await c.StatoOrdineAsync(ordine));
        Assert.Equal(EventoWebhook.EsitoElaborato, (await c.EventiAsync()).Single().Esito);
    }

    [Fact]
    public async Task LoStessoWebhookRipetutoNonHaPiuEffettoEUnaFirmaSbagliataNonNeHaNessuno()
    {
        var (c, finto, variante, ordine) = await PreparaAsync();
        await using var _ = c;
        var idProvider = await AvviaEApprovaAsync(c, finto, ordine);
        var evento = Evento("WH-1", "CHECKOUT.ORDER.APPROVED", idProvider);

        var falso = await c.ComePagamentiAsync(c.A, s => s.ElaboraWebhookAsync(Firma("manomessa"), evento, Ct));
        Assert.Equal(EsitoWebhook.FirmaNonValida, falso);
        Assert.Equal(StatoOrdine.InAttesaDiPagamento, await c.StatoOrdineAsync(ordine));
        Assert.Empty(await c.EventiAsync());

        var primo = await c.ComePagamentiAsync(c.A, s => s.ElaboraWebhookAsync(Firma(), evento, Ct));
        var secondo = await c.ComePagamentiAsync(c.A, s => s.ElaboraWebhookAsync(Firma(), evento, Ct));
        var altro = await c.ComePagamentiAsync(c.A, s => s.ElaboraWebhookAsync(Firma(), Evento("WH-2", "PAYMENT.CAPTURE.COMPLETED", idProvider), Ct));

        Assert.Equal(EsitoWebhook.Elaborato, primo);
        Assert.Equal(EsitoWebhook.Duplicato, secondo);
        Assert.Equal(EsitoWebhook.Elaborato, altro);
        Assert.Equal(2, (await c.EventiAsync()).Count);
        Assert.Equal(1, finto.Conta(HttpMethod.Post, "/v2/checkout/orders/ORD-0001/capture"));
        Assert.Single(await c.MovimentiAsync(variante), m => m.Motivo == MotivoMovimento.Vendita);
    }

    [Fact]
    public async Task WebhookMalformatiIgnotiOChePuntanoAPagamentiSconosciutiNonFannoDanni()
    {
        var (c, finto, _, ordine) = await PreparaAsync();
        await using var _ = c;
        await AvviaEApprovaAsync(c, finto, ordine);

        var nonJson = await c.ComePagamentiAsync(c.A, s => s.ElaboraWebhookAsync(Firma(), "{non è json", Ct));
        var senzaId = await c.ComePagamentiAsync(c.A, s => s.ElaboraWebhookAsync(Firma(), "{\"event_type\":\"X\"}", Ct));
        var tipoIgnoto = await c.ComePagamentiAsync(c.A, s => s.ElaboraWebhookAsync(Firma(), Evento("WH-3", "BILLING.PLAN.CREATED", "ORD-0001"), Ct));
        var sconosciuto = await c.ComePagamentiAsync(c.A, s => s.ElaboraWebhookAsync(Firma(), Evento("WH-4", "CHECKOUT.ORDER.APPROVED", "ORD-7777"), Ct));
        var altroSito = await c.ComePagamentiAsync(c.B, s => s.ElaboraWebhookAsync(Firma(), Evento("WH-5", "CHECKOUT.ORDER.APPROVED", "ORD-0001"), Ct));

        // Un corpo che non è JSON non ha una firma verificabile: lo scarta già la verifica.
        Assert.Equal(EsitoWebhook.FirmaNonValida, nonJson);
        Assert.Equal(EsitoWebhook.Malformato, senzaId);
        Assert.Equal(EsitoWebhook.Ignorato, tipoIgnoto);
        Assert.Equal(EsitoWebhook.Ignorato, sconosciuto);
        Assert.Equal(EsitoWebhook.NonConfigurato, altroSito);
        Assert.Equal(StatoOrdine.InAttesaDiPagamento, await c.StatoOrdineAsync(ordine));
        Assert.Equal(0, finto.Conta(HttpMethod.Post, "/v2/checkout/orders/ORD-0001/capture"));
    }

    [Fact]
    public async Task RitornoDelClienteEWebhookInsiemeIncassanoUnaSolaVolta()
    {
        var (c, finto, variante, ordine) = await PreparaAsync();
        await using var _ = c;
        var idProvider = await AvviaEApprovaAsync(c, finto, ordine);

        var esiti = await Task.WhenAll(
            c.ComePagamentiAsync(c.A, s => s.CompletaAsync(c.Cliente, ordine, idProvider, Ct)),
            c.ComePagamentiAsync(c.A, async s => (RisultatoPagamento)(await s.ElaboraWebhookAsync(Firma(), Evento("WH-9", "CHECKOUT.ORDER.APPROVED", idProvider), Ct) is EsitoWebhook.ErroreProvider ? 8 : 2)),
            c.ComePagamentiAsync(c.A, s => s.CompletaAsync(c.Cliente, ordine, idProvider, Ct)));

        Assert.All(esiti, e => Assert.Equal(RisultatoPagamento.Pagato, e));
        Assert.Equal(StatoOrdine.Pagato, await c.StatoOrdineAsync(ordine));
        Assert.Equal(StatoTentativoPagamento.Catturato, (await c.TentativiAsync(ordine)).Single().Stato);
        var giacenza = await c.GiacenzaAsync(variante);
        Assert.Equal((4, 0), (giacenza.Disponibile, giacenza.Riservata));
        Assert.Single(await c.MovimentiAsync(variante), m => m.Motivo == MotivoMovimento.Vendita);
    }

    [Fact]
    public async Task LAmministrazioneSegnaSpeditoORitiratoSoloUnOrdinePagato()
    {
        var (c, finto, _, ordine) = await PreparaAsync();
        await using var _ = c;

        var daNonPagato = await c.ComeAsync(c.A, s => s.SegnaEvasoAsync(c.AdminA, ordine, Ct));
        var idProvider = await AvviaEApprovaAsync(c, finto, ordine);
        await c.ComePagamentiAsync(c.A, s => s.CompletaAsync(c.Cliente, ordine, idProvider, Ct));
        var comeCliente = await c.ComeAsync(c.A, s => s.SegnaEvasoAsync(c.Cliente, ordine, Ct));
        var comeAdminDiB = await c.ComeAsync(c.A, s => s.SegnaEvasoAsync(c.AdminB, ordine, Ct));
        var evaso = await c.ComeAsync(c.A, s => s.SegnaEvasoAsync(c.AdminA, ordine, Ct));
        var ancora = await c.ComeAsync(c.A, s => s.SegnaEvasoAsync(c.AdminA, ordine, Ct));

        Assert.Equal(CodiceEsito.NonConsentito, daNonPagato.Codice);
        Assert.Equal(CodiceEsito.NonAutorizzato, comeCliente.Codice);
        Assert.Equal(CodiceEsito.NonAutorizzato, comeAdminDiB.Codice);
        Assert.True(evaso.Riuscito);
        Assert.Equal(CodiceEsito.NonConsentito, ancora.Codice);
        Assert.Equal(StatoOrdine.Ritirato, await c.StatoOrdineAsync(ordine));
    }

    private static async Task<(ContestoNegozio C, PayPalFinto Finto, Guid Ordine)> PreparaConEmailAsync(string lingua = "it")
    {
        var finto = new PayPalFinto();
        var c = await ContestoNegozio.CreaAsync(payPal: finto, conEmail: true);
        var varianti = await c.CreaProdottoAsync(c.A, "Libro <raro>", 4m, ("LIB-1", 10.00m, null));
        var indirizzo = await c.CreaMetodoAsync(c.A, "Corriere", 5m, AreaSpedizione.Italia);
        await c.ConfiguraPagamentoAsync(c.A);
        await c.AggiungiAsync(c.Cliente, varianti[0], 2);
        var esito = await c.ComeAsync(c.A, s => s.CreaDaCarrelloAsync(c.Cliente, lingua, new DatiOrdine(indirizzo, "Mario Rossi", "Via Roma 1", "Scala B", "20100", "Milano", "IT", 25m), Ct));
        Assert.True(esito.Riuscito, $"{esito.Codice} {string.Join(',', esito.CampiNonValidi ?? [])}");
        return (c, finto, esito.Id!.Value);
    }

    [Fact]
    public async Task IlPagamentoRiuscitoAccodaUnaSolaConfermaAlClienteCifrataEConIDatiDellOrdine()
    {
        var (c, finto, ordine) = await PreparaConEmailAsync();
        await using var _ = c;
        Assert.Equal(0, await c.ContaMessaggiAsync());
        var idProvider = await AvviaEApprovaAsync(c, finto, ordine);

        await c.ComePagamentiAsync(c.A, s => s.CompletaAsync(c.Cliente, ordine, idProvider, Ct));
        await c.ComePagamentiAsync(c.A, s => s.CompletaAsync(c.Cliente, ordine, idProvider, Ct));
        await c.ComePagamentiAsync(c.A, s => s.ElaboraWebhookAsync(Firma(), Evento("WH-1", "CHECKOUT.ORDER.APPROVED", idProvider), Ct));
        var tutte = await c.ElaboraEmailAsync();
        var email = Assert.Single(tutte, e => e.Oggetto.Contains("confermato", StringComparison.Ordinal));
        var avviso = Assert.Single(tutte, e => e.Oggetto.Contains("Nuovo ordine", StringComparison.Ordinal));

        Assert.Equal(2, tutte.Count);
        Assert.Equal(["anna@esempio.it"], avviso.Destinatari.Select(d => d.Trim('<', '>')));
        Assert.Contains("Nuovo ordine n. 1", avviso.Oggetto, StringComparison.Ordinal);
        Assert.Contains($"https://a.esempio.it/admin/negozio/ordini/{ordine:D}", avviso.Testo, StringComparison.Ordinal);
        Assert.Contains("cliente@esempio.it", avviso.Testo, StringComparison.Ordinal);
        Assert.Equal(["cliente@esempio.it"], email.Destinatari.Select(d => d.Trim('<', '>')));
        Assert.Contains("Ordine n. 1 confermato", email.Oggetto, StringComparison.Ordinal);
        Assert.Contains("Libro <raro>", email.Testo, StringComparison.Ordinal);
        Assert.Contains("LIB-1", email.Testo, StringComparison.Ordinal);
        Assert.Contains("× 2", email.Testo, StringComparison.Ordinal);
        Assert.Contains("Totale (IVA inclusa): 25,00 EUR", email.Testo, StringComparison.Ordinal);
        Assert.Contains("Mario Rossi", email.Testo, StringComparison.Ordinal);
        Assert.Contains("20100 Milano (IT)", email.Testo, StringComparison.Ordinal);
        Assert.Contains($"https://a.esempio.it/account/ordini/{ordine:D}", email.Testo, StringComparison.Ordinal);
        Assert.Equal(2, await c.ContaMessaggiAsync());
    }

    [Fact]
    public async Task LAvvisoAgliAmministratoriSiPuoSpegnereConLaConfigurazione()
    {
        var finto = new PayPalFinto();
        await using var c = await ContestoNegozio.CreaAsync(payPal: finto, conEmail: true, avvisoAmministratori: false);
        var varianti = await c.CreaProdottoAsync(c.A, "Libro", 4m, ("LIB-1", 10.00m, null));
        var ritiro = await c.CreaRitiroAsync(c.A);
        await c.ConfiguraPagamentoAsync(c.A);
        await c.AggiungiAsync(c.Cliente, varianti[0], 1);
        var ordine = (await c.ComeAsync(c.A, s => s.CreaDaCarrelloAsync(c.Cliente, "it", new DatiOrdine(ritiro, null, null, null, null, null, null, 10m), Ct))).Id!.Value;
        var idProvider = await AvviaEApprovaAsync(c, finto, ordine);

        await c.ComePagamentiAsync(c.A, s => s.CompletaAsync(c.Cliente, ordine, idProvider, Ct));

        var email = Assert.Single(await c.ElaboraEmailAsync());
        Assert.Equal(["cliente@esempio.it"], email.Destinatari.Select(d => d.Trim('<', '>')));
    }

    [Fact]
    public async Task UnPagamentoNonRiuscitoNonInviaConfermeESenzaCanaleEmailNonSiAccodaNulla()
    {
        var (c, finto, ordine) = await PreparaConEmailAsync();
        await using var _ = c;
        finto.EsitoCattura = "DECLINED";
        var idProvider = await AvviaEApprovaAsync(c, finto, ordine);

        await c.ComePagamentiAsync(c.A, s => s.CompletaAsync(c.Cliente, ordine, idProvider, Ct));

        Assert.Equal(0, await c.ContaMessaggiAsync());
        Assert.Empty(await c.ElaboraEmailAsync());

        // Tenant senza canale email: il pagamento riesce lo stesso e non si accoda nulla.
        var (senza, fintoSenza, _, ordineSenza) = await PreparaAsync();
        await using var _2 = senza;
        var idSenza = await AvviaEApprovaAsync(senza, fintoSenza, ordineSenza);
        var pagato = await senza.ComePagamentiAsync(senza.A, s => s.CompletaAsync(senza.Cliente, ordineSenza, idSenza, Ct));
        Assert.Equal(RisultatoPagamento.Pagato, pagato);
        Assert.Equal(0, await senza.ContaMessaggiAsync());
    }

    [Fact]
    public async Task LaLinguaDelCompratoreSiSalvaSullOrdineSoloSeEAbilitata()
    {
        var (c, _, ordineEn) = await PreparaConEmailAsync("en");
        await using var _ = c;
        var (altro, _, ordineIgnota) = await PreparaConEmailAsync("xx");
        await using var _2 = altro;

        await using var ambito = c.Web.Fabbrica.Services.CreateAsyncScope();
        var lingua = await ambito.ServiceProvider.GetRequiredService<DvCms.Infrastructure.Persistenza.ContestoDatabase>().Ordini.IgnoreQueryFilters().Where(o => o.Id == ordineEn).Select(o => o.CodiceLingua).SingleAsync(Ct);
        await using var ambito2 = altro.Web.Fabbrica.Services.CreateAsyncScope();
        var ignota = await ambito2.ServiceProvider.GetRequiredService<DvCms.Infrastructure.Persistenza.ContestoDatabase>().Ordini.IgnoreQueryFilters().Where(o => o.Id == ordineIgnota).Select(o => o.CodiceLingua).SingleAsync(Ct);

        Assert.Equal("en", lingua);
        Assert.Equal("it", ignota);
    }
}
