using DvCms.Application.Piattaforma;
using DvCms.Models.Negozio;

namespace DvCms.Application.Negozio;

/// <summary>Impostazioni del negozio a livello di installazione (configurazione <c>Negozio:*</c>).</summary>
/// <param name="CheckoutProduzioneAbilitato">
/// Vero solo dopo che il committente ha approvato resi/rimborsi e dati personali degli acquirenti (ADR-0020, A5/A7). Finché è falso un
/// sito con credenziali PayPal in ambiente di produzione non può avviare pagamenti; il Sandbox funziona sempre.
/// </param>
/// <param name="AvvisoNuovoOrdineAmministratori">
/// Vero (predefinito): quando un ordine risulta pagato, una email avvisa gli amministratori attivi del tenant (se il tenant ha un canale
/// email). Si spegne con <c>Negozio:AvvisoNuovoOrdineAmministratori=false</c>.
/// </param>
public sealed record OpzioniNegozio(bool CheckoutProduzioneAbilitato, bool AvvisoNuovoOrdineAmministratori = true)
{
    public static OpzioniNegozio Predefinite { get; } = new(false);
}

public enum RisultatoPagamento
{
    /// <summary>Pagamento avviato: il cliente va approvato sulla pagina del provider.</summary>
    Avviato = 1,

    Pagato = 2,

    /// <summary>Incasso in sospeso presso il provider: l'ordine si aggiornerà quando arriva l'esito.</summary>
    InSospeso = 3,

    /// <summary>Pagamento rifiutato: il cliente può riprovare.</summary>
    Fallito = 4,

    /// <summary>Il cliente non ha (ancora) approvato il pagamento: nulla è stato incassato.</summary>
    NonApprovato = 5,

    /// <summary>L'ordine è chiuso (annullato, scaduto) o non esiste più per il pagamento: nulla è stato incassato.</summary>
    OrdineChiuso = 6,

    /// <summary>Il provider ha incassato ma l'ordine non può risultare pagato: serve un rimborso manuale.</summary>
    DaRimborsare = 7,

    ErroreProvider = 8,

    /// <summary>Il sito non ha credenziali di pagamento complete.</summary>
    NonConfigurato = 9,

    /// <summary>Credenziali di produzione ma il checkout di produzione è spento nell'installazione.</summary>
    ProduzioneBloccata = 10,

    NonTrovato = 11,

    NonAutorizzato = 12,

    /// <summary>L'ordine non ha niente da pagare (totale zero).</summary>
    NonDovuto = 13,
}

public sealed record EsitoAvvioPagamento(RisultatoPagamento Risultato, Uri? UrlApprovazione);

public enum EsitoWebhook
{
    Elaborato = 1,

    /// <summary>Già elaborato in precedenza: nessun effetto.</summary>
    Duplicato = 2,

    /// <summary>Evento di un tipo che il negozio non gestisce, o riferito a un pagamento che non conosce.</summary>
    Ignorato = 3,

    FirmaNonValida = 4,

    Malformato = 5,

    NonConfigurato = 6,

    /// <summary>Il provider non ha risposto: il webhook va ripetuto più tardi.</summary>
    ErroreProvider = 7,
}

public sealed record TentativoPagamentoVista(Guid Id, string IdProvider, StatoTentativoPagamento Stato, decimal Importo, string Valuta, DateTime CreatoIlUtc, DateTime AggiornatoIlUtc, string Provider = NomiProviderPagamento.PayPal);

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
