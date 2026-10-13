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

    /// <summary>Rimborso dell'incasso avviato presso il provider, esito non ancora noto.</summary>
    RimborsoInCorso = 7,

    /// <summary>L'incasso che non poteva diventare un ordine pagato è stato restituito al cliente.</summary>
    Rimborsato = 8,
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

    /// <summary>Quanto il provider ha davvero incassato (può differire da <see cref="Importo"/> se qualcosa è andato storto): è l'importo massimo rimborsabile di questo incasso.</summary>
    public decimal? ImportoIncassato { get; set; }

    /// <summary>
    /// Impronta (SHA-256, esadecimale) del token di sicurezza che il provider ha restituito alla creazione del pagamento e che ripete nelle
    /// notifiche (Nexi): serve a riconoscere una notifica autentica senza conservare il token. Nulla per PayPal, che firma i webhook.
    /// </summary>
    public string? HashTokenNotifica { get; set; }

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

public enum StatoRimborso
{
    /// <summary>Richiesto al provider, esito non ancora noto (in sospeso presso il provider o risposta non arrivata). L'importo resta impegnato.</summary>
    InCorso = 1,

    Completato = 2,

    /// <summary>Rifiutato dal provider: l'importo torna disponibile.</summary>
    Fallito = 3,
}

/// <summary>
/// Rimborso totale o parziale di un incasso (A5, ADR-0020), richiesto dal tenant e eseguito dal provider. L'identificativo è anche il
/// <c>PayPal-Request-Id</c>: ripetere la richiesta (ad esempio dopo una risposta persa) non rimborsa due volte. Un rimborso non si
/// cancella: si conclude o fallisce.
/// </summary>
public class Rimborso
{
    public const int LunghezzaMassimaMotivo = 200;

    public Guid Id { get; set; }

    public Guid IdTenant { get; set; }

    public Guid IdSito { get; set; }

    public Guid IdOrdine { get; set; }

    public Guid IdTentativo { get; set; }

    /// <summary>Vero se restituisce un incasso che non era diventato un ordine pagato (nessun conteggio sull'ordine, che non è mai risultato pagato).</summary>
    public bool IncassoOrfano { get; set; }

    public decimal Importo { get; set; }

    public string Valuta { get; set; } = VarianteProdotto.ValutaPredefinita;

    /// <summary>Motivo indicato dal tenant; se presente lo si comunica anche a PayPal come nota per il cliente.</summary>
    public string? Motivo { get; set; }

    public StatoRimborso Stato { get; set; } = StatoRimborso.InCorso;

    /// <summary>Identificativo del rimborso presso il provider, quando c'è.</summary>
    public string? IdProvider { get; set; }

    /// <summary>Chi l'ha richiesto dal pannello; nullo per un rimborso fatto fuori dal sistema (<see cref="Esterno"/>).</summary>
    public Guid? IdUtenzaRichiedente { get; set; }

    /// <summary>
    /// Vero se il rimborso è stato eseguito direttamente dal pannello del provider (PayPal, Nexi) e il negozio l'ha riconosciuto da una
    /// notifica: nasce già concluso e aggiorna l'ordine come un rimborso richiesto da qui.
    /// </summary>
    public bool Esterno { get; set; }

    public DateTime CreatoIlUtc { get; set; }

    public DateTime AggiornatoIlUtc { get; set; }
}
