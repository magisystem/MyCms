using DvCms.Models.Negozio;

namespace DvCms.DTO.Negozio;

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
