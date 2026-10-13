using DvCms.DTO.Negozio;
using DvCms.DTO.Piattaforma;
using DvCms.Models.Negozio;

namespace DvCms.Contracts.Negozio;

/// <summary>
/// Pagamenti degli ordini con PayPal (F-004, ADR-0009, ADR-0020). Importo e valuta sono sempre quelli dell'ordine, decisi dal server;
/// il browser non può dichiarare un ordine pagato: lo stato cambia solo dopo una cattura riuscita o un webhook con firma verificata,
/// confermati interrogando il provider. Ogni passaggio di stato è un aggiornamento condizionale, quindi ritorno del cliente e webhook
/// possono arrivare insieme, ripetuti o in disordine senza incassare o confermare due volte.
/// </summary>
public interface IServizioPagamenti
{
    /// <summary>
    /// Avvia il pagamento di un ordine proprio ancora da pagare (o da ripagare dopo un rifiuto) con il provider scelto
    /// (<see cref="NomiProviderPagamento"/>) e restituisce dove mandare il cliente. Un provider sconosciuto o non configurato per il sito
    /// dà <c>NonConfigurato</c>.
    /// </summary>
    Task<EsitoAvvioPagamento> AvviaAsync(Guid idAccount, Guid idOrdine, string provider, DatiLinkInvito link, CancellationToken annullamento);

    /// <summary>I provider con cui il sito può far pagare adesso (credenziali complete e, per la produzione, interruttore acceso), nell'ordine di <see cref="NomiProviderPagamento.Tutti"/>.</summary>
    Task<IReadOnlyList<string>> ProviderDisponibiliAsync(CancellationToken annullamento);

    /// <summary>Al ritorno del cliente dalla pagina del provider: incassa o legge l'esito (una sola volta) e aggiorna l'ordine.</summary>
    Task<RisultatoPagamento> CompletaAsync(Guid idAccount, Guid idOrdine, string? idProvider, CancellationToken annullamento);

    /// <summary>Elabora un webhook di PayPal arrivato sul dominio del sito: verifica la firma, scarta i duplicati e applica l'esito.</summary>
    Task<EsitoWebhook> ElaboraWebhookAsync(IntestazioniWebhook intestazioni, string corpo, CancellationToken annullamento);

    /// <summary>
    /// Elabora una notifica di Nexi arrivata sul dominio del sito: riconosce il pagamento dall'ordine, verifica il token di sicurezza
    /// ricevuto alla creazione, scarta i duplicati e rilegge l'esito dal gateway (il contenuto della notifica non decide nulla).
    /// </summary>
    Task<EsitoWebhook> ElaboraNotificaNexiAsync(string corpo, CancellationToken annullamento);

    /// <summary>
    /// Riconciliazione con il provider per il sito dell'host: rilegge (senza incassare) i tentativi rimasti aperti da più di venti minuti,
    /// per recuperare incassi mai notificati (ritorno del cliente perso, webhook non arrivato). Restituisce quanti tentativi ha concluso.
    /// </summary>
    Task<int> RiconciliaAsync(CancellationToken annullamento);

    /// <summary>Tentativi di pagamento di un ordine del sito (amministrazione).</summary>
    Task<IReadOnlyList<TentativoPagamentoVista>> ElencaTentativiAsync(Guid idAttore, Guid idOrdine, CancellationToken annullamento);
}
