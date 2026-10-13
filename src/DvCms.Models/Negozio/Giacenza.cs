namespace DvCms.Models.Negozio;

/// <summary>
/// Giacenza di una variante (F-005, ADR-0020). La gestione è facoltativa e per variante: senza riga la variante si vende senza limite
/// (servizi, prodotti digitali); con la riga <c>Disponibile</c> è la quantità fisica in magazzino e <c>Riservata</c> la parte
/// trattenuta da prenotazioni attive. Il vincolo <c>Riservata ≤ Disponibile</c> è del database: nessun percorso di codice può
/// vendere più di quanto c'è.
/// </summary>
public class GiacenzaVariante
{
    public const int QuantitaMassima = 1_000_000;

    public Guid IdTenant { get; set; }

    public Guid IdSito { get; set; }

    public Guid IdVariante { get; set; }

    public int Disponibile { get; set; }

    public int Riservata { get; set; }

    public DateTime AggiornataIlUtc { get; set; }

    public byte[] TokenConcorrenza { get; set; } = [];
}

public enum StatoPrenotazione
{
    /// <summary>Quantità trattenuta in attesa del pagamento.</summary>
    Attiva = 1,

    /// <summary>Pagamento incassato: la quantità è uscita dal magazzino.</summary>
    Confermata = 2,

    /// <summary>Scaduta, fallita o annullata: la quantità è tornata libera.</summary>
    Rilasciata = 3,
}

/// <summary>
/// Quantità di una variante trattenuta per una riga d'ordine fino a <see cref="ScadeIlUtc"/> (15 minuti dalla creazione dell'ordine).
/// Non ha chiavi verso la variante, come le righe d'ordine; l'ordine invece sì.
/// </summary>
public class PrenotazioneGiacenza
{
    public const int DurataMinuti = 15;

    /// <summary>Scadenza delle prenotazioni che non scadono (contrassegno, ADR-0023): la merce resta trattenuta finché l'ordine non si spedisce o si annulla.</summary>
    public static readonly DateTime SenzaScadenza = new(9999, 12, 31, 0, 0, 0, DateTimeKind.Utc);

    public Guid Id { get; set; }

    public Guid IdTenant { get; set; }

    public Guid IdSito { get; set; }

    public Guid IdOrdine { get; set; }

    public Guid IdVariante { get; set; }

    public int Quantita { get; set; }

    public StatoPrenotazione Stato { get; set; } = StatoPrenotazione.Attiva;

    public DateTime ScadeIlUtc { get; set; }

    public DateTime AggiornataIlUtc { get; set; }
}

public enum MotivoMovimento
{
    /// <summary>Quantità impostata dall'amministrazione (carico, rettifica, inventario).</summary>
    Impostazione = 1,

    /// <summary>Uscita per un ordine pagato.</summary>
    Vendita = 2,

    /// <summary>Rientro in giacenza degli articoli di un ordine interamente rimborsato, confermato dall'amministrazione.</summary>
    Reso = 3,
}

/// <summary>Storico dei cambiamenti della quantità fisica. Sopravvive alla variante (nessuna chiave verso di lei).</summary>
public class MovimentoGiacenza
{
    public Guid Id { get; set; }

    public Guid IdTenant { get; set; }

    public Guid IdSito { get; set; }

    public Guid IdVariante { get; set; }

    public MotivoMovimento Motivo { get; set; }

    /// <summary>Variazione della quantità disponibile (negativa per le uscite).</summary>
    public int Variazione { get; set; }

    public int DisponibileDopo { get; set; }

    public Guid? IdOrdine { get; set; }

    public Guid? IdAttore { get; set; }

    public DateTime CreatoIlUtc { get; set; }
}
