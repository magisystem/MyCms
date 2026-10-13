using System.Net;
using DvCms.Contracts.Negozio;
using DvCms.DTO.Negozio;
using DvCms.IntegrationTests.Supporto;
using DvCms.Models.Negozio;
using DvCms.Services.Negozio.Pagamenti;
using Xunit;

namespace DvCms.IntegrationTests.Pagamenti;

/// <summary>Client PayPal Orders v2 (F-004) contro il PayPal finto: richieste, idempotenza, esiti, errori senza segreti.</summary>
public class ClientePayPalTest
{
    private const string Segreto = "SEGRETO-MOLTO-RISERVATO";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static CredenzialiPayPal Credenziali(AmbientePagamento ambiente = AmbientePagamento.Sandbox, string segreto = Segreto) =>
        new(ambiente, "client-di-prova", segreto, "webhook-di-prova");

    private static RichiestaOrdineProvider Richiesta(Guid? id = null, decimal importo = 46.7m, string valuta = "EUR") =>
        new(id ?? Guid.NewGuid(), importo, valuta, "ordine-123", "Ordine #123", new Uri("https://negozio.esempio.it/ritorno"), new Uri("https://negozio.esempio.it/annullo"));

    private static (ClientePayPal Client, PayPalFinto Finto) Crea(string segreto = Segreto)
    {
        var finto = new PayPalFinto { SegretoClient = segreto };
        return (new ClientePayPal(new FabbricaHttpPayPalFinta(finto)), finto);
    }

    [Fact]
    public async Task LOrdineSiCreaConImportoValutaIdentificativoDiIdempotenzaEUrlDiApprovazione()
    {
        var (client, finto) = Crea();
        var id = Guid.NewGuid();

        var ordine = await client.CreaOrdineAsync(Credenziali(), Richiesta(id), Ct);

        Assert.Equal("ORD-0001", ordine.IdProvider);
        Assert.Equal("https://www.sandbox.paypal.com/checkoutnow?token=ORD-0001", ordine.UrlApprovazione.AbsoluteUri);
        var creazione = finto.Richieste.Single(r => r.Percorso == "/v2/checkout/orders");
        Assert.Equal(id.ToString("D"), creazione.IdRichiesta);
        Assert.Equal("Bearer TOKEN-FINTO", creazione.Autorizzazione);
        Assert.Contains("\"value\":\"46.70\"", creazione.Corpo, StringComparison.Ordinal);
        Assert.Contains("\"currency_code\":\"EUR\"", creazione.Corpo, StringComparison.Ordinal);
        Assert.Contains("\"return_url\":\"https://negozio.esempio.it/ritorno\"", creazione.Corpo, StringComparison.Ordinal);
        Assert.Contains("\"intent\":\"CAPTURE\"", creazione.Corpo, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RipetereLaCreazioneConLoStessoIdentificativoNonCreaUnSecondoOrdine()
    {
        var (client, finto) = Crea();
        var id = Guid.NewGuid();

        var primo = await client.CreaOrdineAsync(Credenziali(), Richiesta(id), Ct);
        var secondo = await client.CreaOrdineAsync(Credenziali(), Richiesta(id), Ct);

        Assert.Equal(primo.IdProvider, secondo.IdProvider);
        Assert.Single(finto.IdOrdini);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task UnImportoNonPositivoOUnaValutaDiversaNonPartonoNemmeno(int importo)
    {
        var (client, finto) = Crea();

        await Assert.ThrowsAsync<ArgumentException>(() => client.CreaOrdineAsync(Credenziali(), Richiesta(importo: importo), Ct));
        await Assert.ThrowsAsync<ArgumentException>(() => client.CreaOrdineAsync(Credenziali(), Richiesta(valuta: "USD"), Ct));

        Assert.Empty(finto.Richieste);
    }

    [Fact]
    public async Task LaCatturaDiUnOrdineApprovatoRiesceELaStessaRichiestaRipetutaDaLoStessoEsito()
    {
        var (client, finto) = Crea();
        var ordine = await client.CreaOrdineAsync(Credenziali(), Richiesta(), Ct);
        finto.Approva(ordine.IdProvider);
        var richiesta = Guid.NewGuid();

        var primo = await client.CatturaAsync(Credenziali(), ordine.IdProvider, richiesta, Ct);
        var secondo = await client.CatturaAsync(Credenziali(), ordine.IdProvider, richiesta, Ct);

        Assert.Equal((StatoCatturaProvider.Completata, "CAP-ORD-0001", 46.70m, "EUR"), (primo.Stato, primo.IdCattura, primo.Importo, primo.Valuta));
        Assert.Equal(primo, secondo);
    }

    [Fact]
    public async Task UnOrdineNonApprovatoNonSiCatturaEUnOrdineGiaIncassatoSiLeggeSenzaErrore()
    {
        var (client, finto) = Crea();
        var ordine = await client.CreaOrdineAsync(Credenziali(), Richiesta(), Ct);

        var nonApprovato = await client.CatturaAsync(Credenziali(), ordine.IdProvider, Guid.NewGuid(), Ct);
        finto.Approva(ordine.IdProvider);
        await client.CatturaAsync(Credenziali(), ordine.IdProvider, Guid.NewGuid(), Ct);
        var giaIncassato = await client.CatturaAsync(Credenziali(), ordine.IdProvider, Guid.NewGuid(), Ct);

        Assert.Equal(StatoCatturaProvider.NonApprovata, nonApprovato.Stato);
        Assert.Equal(StatoCatturaProvider.Completata, giaIncassato.Stato);
        Assert.Equal(46.70m, giaIncassato.Importo);
    }

    [Theory]
    [InlineData("DECLINED", StatoCatturaProvider.Rifiutata)]
    [InlineData("PENDING", StatoCatturaProvider.InSospeso)]
    public async Task UnaCatturaRifiutataOInSospesoNonSiScambiaPerCompletata(string esitoProvider, StatoCatturaProvider atteso)
    {
        var (client, finto) = Crea();
        finto.EsitoCattura = esitoProvider;
        var ordine = await client.CreaOrdineAsync(Credenziali(), Richiesta(), Ct);
        finto.Approva(ordine.IdProvider);

        var esito = await client.CatturaAsync(Credenziali(), ordine.IdProvider, Guid.NewGuid(), Ct);

        Assert.Equal(atteso, esito.Stato);
    }

    [Fact]
    public async Task LaLetturaDiUnOrdineDiceSeEStatoIncassato()
    {
        var (client, finto) = Crea();
        var ordine = await client.CreaOrdineAsync(Credenziali(), Richiesta(), Ct);

        var prima = await client.LeggiOrdineAsync(Credenziali(), ordine.IdProvider, Ct);
        finto.Approva(ordine.IdProvider);
        await client.CatturaAsync(Credenziali(), ordine.IdProvider, Guid.NewGuid(), Ct);
        var dopo = await client.LeggiOrdineAsync(Credenziali(), ordine.IdProvider, Ct);

        Assert.Equal(StatoCatturaProvider.NonApprovata, prima.Stato);
        Assert.Equal(StatoCatturaProvider.Completata, dopo.Stato);
    }

    [Fact]
    public async Task LaFirmaDelWebhookSiVerificaConIlServizioDelProvider()
    {
        var (client, finto) = Crea();
        var intestazioni = new IntestazioniWebhook("SHA256withRSA", "https://api.paypal.com/cert", "trasmissione-1", PayPalFinto.FirmaValida, "2026-10-11T10:00:00Z");
        const string Corpo = "{\"id\":\"WH-1\",\"event_type\":\"PAYMENT.CAPTURE.COMPLETED\"}";

        var valida = await client.VerificaWebhookAsync(Credenziali(), intestazioni, Corpo, Ct);
        var manomessa = await client.VerificaWebhookAsync(Credenziali(), intestazioni with { TransmissionSig = "altra" }, Corpo, Ct);
        var nonJson = await client.VerificaWebhookAsync(Credenziali(), intestazioni, "non è json", Ct);
        var senzaWebhook = await client.VerificaWebhookAsync(new CredenzialiPayPal(AmbientePagamento.Sandbox, "client-di-prova", Segreto, string.Empty), intestazioni, Corpo, Ct);

        Assert.True(valida);
        Assert.False(manomessa);
        Assert.False(nonJson);
        Assert.False(senzaWebhook);
        var verifica = finto.Richieste.First(r => r.Percorso == "/v1/notifications/verify-webhook-signature");
        Assert.Contains("\"webhook_id\":\"webhook-di-prova\"", verifica.Corpo, StringComparison.Ordinal);
        Assert.Contains("\"event_type\":\"PAYMENT.CAPTURE.COMPLETED\"", verifica.Corpo, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CredenzialiSbagliateDannoUnErroreSenzaSegretiEUnServizioIrraggiungibileUnErroreSenzaDettagli()
    {
        var (client, finto) = Crea();

        var errore = await Assert.ThrowsAsync<ProviderPagamentoException>(() => client.CreaOrdineAsync(Credenziali(segreto: "sbagliato-" + Segreto), Richiesta(), Ct));
        finto.Eccezione = new HttpRequestException($"connessione rifiutata {Segreto}");
        var irraggiungibile = await Assert.ThrowsAsync<ProviderPagamentoException>(() => client.CreaOrdineAsync(Credenziali(), Richiesta(), Ct));

        Assert.Contains("401", errore.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Segreto, errore.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Segreto, irraggiungibile.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Segreto, Credenziali().ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnIdentificativoDiOrdineConCaratteriStraniNonDiventaUnPercorso()
    {
        var (client, finto) = Crea();

        await Assert.ThrowsAsync<ArgumentException>(() => client.CatturaAsync(Credenziali(), "../v1/identity", Guid.NewGuid(), Ct));
        await Assert.ThrowsAsync<ArgumentException>(() => client.LeggiOrdineAsync(Credenziali(), "ORD 1?x=y", Ct));

        Assert.Empty(finto.Richieste);
    }

    [Fact]
    public async Task LAmbienteDecideSeSiParlaConSandboxOProduzione()
    {
        var (client, finto) = Crea();
        var catturate = new List<Uri>();
        var gestore = new CatturaIndirizzi(finto, catturate);
        var cliente = new ClientePayPal(new FabbricaHttpPayPalFinta(gestore));

        await cliente.CreaOrdineAsync(Credenziali(AmbientePagamento.Sandbox), Richiesta(), Ct);
        await cliente.CreaOrdineAsync(Credenziali(AmbientePagamento.Live), Richiesta(), Ct);

        Assert.Contains(catturate, u => u.Host == "api-m.sandbox.paypal.com");
        Assert.Contains(catturate, u => u.Host == "api-m.paypal.com");
        Assert.NotNull(client);
    }

    private sealed class CatturaIndirizzi : DelegatingHandler
    {
        private readonly List<Uri> _indirizzi;

        public CatturaIndirizzi(HttpMessageHandler interno, List<Uri> indirizzi)
            : base(interno)
        {
            _indirizzi = indirizzi;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            _indirizzi.Add(request.RequestUri!);
            return base.SendAsync(request, cancellationToken);
        }
    }
}
