namespace DvCms.Domain.Negozio;

public enum StatoTentativoPagamento
{
    /// <summary>Ordine creato presso il provider, in attesa che il cliente approvi e che si incassi.</summary>
    Creato = 1,

    /// <summary>Incasso in sospeso presso il provider (ad esempio controlli sul conto del cliente): l'ordine non è ancora pagato.</summary>
    InSospeso = 2,

    /// <summary>Denaro incassato e ordine segnato come pagato.</summary>
    Catturato = 3,

    /// <summary>Pagamento rifiutato dal provider.</summary>
    Fallito = 4,

    /// <summary>Non più utilizzabile: l'ordine è stato pagato in un altro modo o chiuso prima dell'incasso. Non si è incassato nulla.</summary>
    Annullato = 5,

    /// <summary>
    /// Il provider ha incassato ma l'ordine non può risultare pagato (chiuso nel frattempo, importo diverso, merce non più
    /// garantita): serve un rimborso manuale. Finché non è approvata la politica A5 non c'è un flusso di rimborso automatico.
    /// </summary>
    DaRimborsare = 6,
}

/// <summary>
/// Un tentativo di pagare un ordine con il provider (ADR-0020). L'identificativo del tentativo è anche il <c>PayPal-Request-Id</c>
/// della creazione e della cattura: ripetere la richiesta non crea né incassa due volte. Importo e valuta sono quelli dell'ordine,
/// decisi dal server.
/// </summary>
public class TentativoPagamento
{
    public Guid Id { get; set; }

    public Guid IdTenant { get; set; }

    public Guid IdSito { get; set; }

    public Guid IdOrdine { get; set; }

    public string Provider { get; set; } = "PayPal";

    /// <summary>Identificativo dell'ordine di pagamento presso il provider.</summary>
    public string IdProvider { get; set; } = string.Empty;

    public decimal Importo { get; set; }

    public string Valuta { get; set; } = VarianteProdotto.ValutaPredefinita;

    public StatoTentativoPagamento Stato { get; set; } = StatoTentativoPagamento.Creato;

    /// <summary>Identificativo dell'incasso presso il provider, quando c'è.</summary>
    public string? IdCattura { get; set; }

    public DateTime CreatoIlUtc { get; set; }

    public DateTime AggiornatoIlUtc { get; set; }
}

/// <summary>
/// Evento ricevuto da un webhook del provider, registrato per non elaborarlo due volte. Non conserva il contenuto (può contenere dati
/// personali del cliente): solo l'identificativo, il tipo e l'esito.
/// </summary>
public class EventoWebhook
{
    public const string EsitoRicevuto = "Ricevuto";
    public const string EsitoElaborato = "Elaborato";
    public const string EsitoIgnorato = "Ignorato";
    public const string EsitoScartato = "Scartato";

    public Guid Id { get; set; }

    public Guid IdTenant { get; set; }

    public Guid IdSito { get; set; }

    public string Provider { get; set; } = "PayPal";

    public string IdEvento { get; set; } = string.Empty;

    public string Tipo { get; set; } = string.Empty;

    public string Esito { get; set; } = EsitoRicevuto;

    public DateTime RicevutoIlUtc { get; set; }
}
