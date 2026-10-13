using DvCms.Contracts.Negozio;
using DvCms.DTO.Negozio;
using DvCms.Models.Negozio;

namespace DvCms.Infrastructure.Pagamenti;

/// <summary>PayPal come <see cref="IGatewayPagamento"/>: delega al client Orders v2 e all'archivio delle credenziali PayPal del sito.</summary>
public sealed class GatewayPayPal : IGatewayPagamento
{
    private readonly IProviderPagamento _provider;
    private readonly IArchivioCredenzialiPagamento _archivio;

    public GatewayPayPal(IProviderPagamento provider, IArchivioCredenzialiPagamento archivio)
    {
        _provider = provider;
        _archivio = archivio;
    }

    public string Nome => NomiProviderPagamento.PayPal;

    public async Task<CredenzialiGateway?> OttieniCredenzialiAsync(Guid idTenant, Guid idSito, CancellationToken annullamento) =>
        await _archivio.OttieniAsync(idTenant, idSito, annullamento);

    public Task<OrdineProvider> CreaOrdineAsync(CredenzialiGateway credenziali, RichiestaOrdineProvider richiesta, CancellationToken annullamento) =>
        _provider.CreaOrdineAsync(Paypal(credenziali), richiesta, annullamento);

    public Task<EsitoCatturaProvider> CatturaAsync(CredenzialiGateway credenziali, string idProvider, Guid idRichiesta, CancellationToken annullamento) =>
        _provider.CatturaAsync(Paypal(credenziali), idProvider, idRichiesta, annullamento);

    public Task<EsitoCatturaProvider> LeggiOrdineAsync(CredenzialiGateway credenziali, string idProvider, CancellationToken annullamento) =>
        _provider.LeggiOrdineAsync(Paypal(credenziali), idProvider, annullamento);

    public Task<IReadOnlyList<RimborsoDelProvider>> LeggiRimborsiAsync(CredenzialiGateway credenziali, string idProvider, CancellationToken annullamento) =>
        Task.FromResult<IReadOnlyList<RimborsoDelProvider>>([]);

    private static CredenzialiPayPal Paypal(CredenzialiGateway credenziali) =>
        credenziali as CredenzialiPayPal ?? throw new ArgumentException("Le credenziali non sono quelle di PayPal.", nameof(credenziali));
}

/// <summary>
/// Nexi XPay come <see cref="IGatewayPagamento"/> (ADR-0022): pagina ospitata di Nexi. L'incasso è automatico, quindi «catturare» al ritorno
/// del cliente significa leggere l'esito dell'ordine presso Nexi. Il token di sicurezza restituito alla creazione serve a riconoscere le notifiche.
/// </summary>
public sealed class GatewayNexi : IGatewayPagamento
{
    private readonly ClienteNexi _cliente;
    private readonly ServizioImpostazioniNexi _archivio;

    public GatewayNexi(ClienteNexi cliente, ServizioImpostazioniNexi archivio)
    {
        _cliente = cliente;
        _archivio = archivio;
    }

    public string Nome => NomiProviderPagamento.Nexi;

    public async Task<CredenzialiGateway?> OttieniCredenzialiAsync(Guid idTenant, Guid idSito, CancellationToken annullamento) =>
        await _archivio.OttieniAsync(idTenant, idSito, annullamento);

    public async Task<OrdineProvider> CreaOrdineAsync(CredenzialiGateway credenziali, RichiestaOrdineProvider richiesta, CancellationToken annullamento)
    {
        ArgumentNullException.ThrowIfNull(richiesta);
        var idOrdine = ClienteNexi.NuovoIdOrdine(richiesta.NumeroOrdine, richiesta.IdRichiesta);
        var creato = await _cliente.CreaPagamentoAsync(Nexi(credenziali), idOrdine, richiesta, annullamento);
        return new OrdineProvider(idOrdine, creato.PaginaPagamento, creato.TokenSicurezza);
    }

    public Task<EsitoCatturaProvider> CatturaAsync(CredenzialiGateway credenziali, string idProvider, Guid idRichiesta, CancellationToken annullamento) =>
        _cliente.LeggiOrdineAsync(Nexi(credenziali), idProvider, annullamento);

    public Task<EsitoCatturaProvider> LeggiOrdineAsync(CredenzialiGateway credenziali, string idProvider, CancellationToken annullamento) =>
        _cliente.LeggiOrdineAsync(Nexi(credenziali), idProvider, annullamento);

    public Task<IReadOnlyList<RimborsoDelProvider>> LeggiRimborsiAsync(CredenzialiGateway credenziali, string idProvider, CancellationToken annullamento) =>
        _cliente.LeggiRimborsiAsync(Nexi(credenziali), idProvider, annullamento);

    private static CredenzialiNexi Nexi(CredenzialiGateway credenziali) =>
        credenziali as CredenzialiNexi ?? throw new ArgumentException("Le credenziali non sono quelle di Nexi.", nameof(credenziali));
}
