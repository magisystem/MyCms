using System.Net;
using DvCms.Application.Negozio;
using DvCms.Domain.Negozio;
using DvCms.IntegrationTests.Negozio;
using DvCms.IntegrationTests.Supporto;
using Xunit;

namespace DvCms.IntegrationTests.Pagamenti;

/// <summary>Riconciliazione periodica con il provider: recupera gli incassi mai notificati senza incassare mai, e non dimentica quelli arrivati su ordini chiusi.</summary>
public class RiconciliazionePagamentiTest
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly TimeSpan _vecchio = TimeSpan.FromMinutes(45);

    /// <summary>Un ordine da 10 EUR con un tentativo di pagamento avviato presso il provider, non ancora concluso.</summary>
    private static async Task<(ContestoNegozio C, PayPalFinto Finto, Guid Ordine, string IdProvider)> OrdineInAttesaAsync()
    {
        var finto = new PayPalFinto();
        var c = await ContestoNegozio.CreaAsync(payPal: finto);
        var varianti = await c.CreaProdottoAsync(c.A, "Libro", 4m, ("LIB-1", 10.00m, null));
        var ritiro = await c.CreaRitiroAsync(c.A);
        await c.ConfiguraPagamentoAsync(c.A);
        await c.AggiungiAsync(c.Cliente, varianti[0], 1);
        var ordine = (await c.ComeAsync(c.A, s => s.CreaDaCarrelloAsync(c.Cliente, "it", new DatiOrdine(ritiro, null, null, null, null, null, null, 10m), Ct))).Id!.Value;
        await c.ComePagamentiAsync(c.A, s => s.AvviaAsync(c.Cliente, ordine, ContestoNegozio.Link, Ct));
        return (c, finto, ordine, (await c.TentativiAsync(ordine)).Single().IdProvider);
    }

    [Fact]
    public async Task UnIncassoMaiNotificatoSiRecuperaEdIlSecondoPassaggioNonFaNulla()
    {
        var (c, finto, ordine, idProvider) = await OrdineInAttesaAsync();
        await using var _ = c;
        finto.IncassaSenzaAvvisare(idProvider);
        await c.InvecchiaTentativiAsync(ordine, _vecchio);

        var primo = await c.RiconciliaAsync();
        var secondo = await c.RiconciliaAsync();

        Assert.Equal((1, 0), (primo, secondo));
        Assert.Equal(StatoOrdine.Pagato, await c.StatoOrdineAsync(ordine));
        var tentativo = Assert.Single(await c.TentativiAsync(ordine));
        Assert.Equal((StatoTentativoPagamento.Catturato, 10m), (tentativo.Stato, tentativo.ImportoIncassato));
        Assert.DoesNotContain(finto.Richieste, r => r.Percorso.EndsWith("/capture", StringComparison.Ordinal));
    }

    [Fact]
    public async Task NonIncassaMaiENonToccaITentativiRecentiONonApprovati()
    {
        var (c, finto, ordine, idProvider) = await OrdineInAttesaAsync();
        await using var _ = c;
        finto.Approva(idProvider);

        var recente = await c.RiconciliaAsync();
        await c.InvecchiaTentativiAsync(ordine, _vecchio);
        var approvatoNonIncassato = await c.RiconciliaAsync();

        Assert.Equal((0, 0), (recente, approvatoNonIncassato));
        Assert.Equal(StatoOrdine.InAttesaDiPagamento, await c.StatoOrdineAsync(ordine));
        Assert.Equal(StatoTentativoPagamento.Creato, Assert.Single(await c.TentativiAsync(ordine)).Stato);
        Assert.DoesNotContain(finto.Richieste, r => r.Percorso.EndsWith("/capture", StringComparison.Ordinal));
    }

    [Fact]
    public async Task UnIncassoArrivatoSuUnOrdineChiusoOGiaPagatoDiventaDaRimborsare()
    {
        foreach (var statoOrdine in new[] { StatoOrdine.Annullato, StatoOrdine.Pagato })
        {
            var (c, finto, ordine, idProvider) = await OrdineInAttesaAsync();
            await using var _ = c;
            finto.IncassaSenzaAvvisare(idProvider);
            await c.ImpostaStatoAsync(ordine, statoOrdine);
            await c.InvecchiaTentativiAsync(ordine, _vecchio);

            var conclusi = await c.RiconciliaAsync();

            Assert.Equal(1, conclusi);
            var tentativo = Assert.Single(await c.TentativiAsync(ordine));
            Assert.Equal((StatoTentativoPagamento.DaRimborsare, 10m), (tentativo.Stato, tentativo.ImportoIncassato));
            Assert.Equal(statoOrdine, await c.StatoOrdineAsync(ordine));
        }
    }

    [Fact]
    public async Task UnErroreDelProviderNonCambiaNullaERiprovaAlPassaggioSuccessivo()
    {
        var (c, finto, ordine, idProvider) = await OrdineInAttesaAsync();
        await using var _ = c;
        finto.IncassaSenzaAvvisare(idProvider);
        await c.InvecchiaTentativiAsync(ordine, _vecchio);

        finto.Eccezione = new HttpRequestException("rete assente", null, HttpStatusCode.BadGateway);
        var fallito = await c.RiconciliaAsync();
        finto.Eccezione = null;
        var riuscito = await c.RiconciliaAsync();

        Assert.Equal((0, 1), (fallito, riuscito));
        Assert.Equal(StatoOrdine.Pagato, await c.StatoOrdineAsync(ordine));
    }
}
