namespace DvCms.Models.Accessi;

public enum StatoConfermaEmail
{
    /// <summary>Link inviato e ancora utilizzabile (finché non scade).</summary>
    Inviata = 1,

    /// <summary>L'acquirente ha confermato l'email e scelto la password.</summary>
    Confermata = 2,

    /// <summary>Sostituita da una nuova richiesta o non più valida.</summary>
    Annullata = 3,
}

/// <summary>
/// Conferma dell'email di un acquirente appena registrato (F-000): un link monouso, inviato all'indirizzo indicato, con cui chi lo
/// riceve conferma di possederlo **e sceglie la password** (la registrazione non chiede password: così chi registra l'indirizzo di
/// qualcun altro non può prendere l'account). Del link si conserva solo l'hash SHA-256.
/// </summary>
public class ConfermaEmailUtenza
{
    public Guid Id { get; set; }

    public Guid IdTenant { get; set; }

    public Guid IdUtenza { get; set; }

    /// <summary>SHA-256 del token del link (256 bit casuali).</summary>
    public byte[] HashToken { get; set; } = [];

    public DateTime ScadeIlUtc { get; set; }

    public StatoConfermaEmail Stato { get; set; } = StatoConfermaEmail.Inviata;

    public DateTime CreatoIlUtc { get; set; }

    public DateTime? ConfermataIlUtc { get; set; }
}
