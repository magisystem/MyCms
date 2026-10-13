namespace DvCms.Models.Accessi;

public enum StatoInvito
{
    /// <summary>Link inviato e ancora utilizzabile (finché non scade).</summary>
    Inviato = 1,

    /// <summary>L'invitato ha scelto la password con il link.</summary>
    Accettato = 2,

    /// <summary>Sostituito da un nuovo invito o non più valido.</summary>
    Annullato = 3,
}

/// <summary>
/// Invito a un account creato dal <c>PlatformAdmin</c>: un link monouso, inviato per email, con cui l'invitato sceglie la
/// propria password (nessuna password temporanea viene comunicata). Del link si conserva solo l'hash SHA-256.
/// </summary>
public class InvitoAccount
{
    public Guid Id { get; set; }

    public Guid IdTenant { get; set; }

    public Guid IdUtenza { get; set; }

    /// <summary>SHA-256 del token del link (256 bit casuali).</summary>
    public byte[] HashToken { get; set; } = [];

    public DateTime ScadeIlUtc { get; set; }

    public StatoInvito Stato { get; set; } = StatoInvito.Inviato;

    public Guid? CreatoDaIdUtenza { get; set; }

    public DateTime CreatoIlUtc { get; set; }

    public DateTime? AccettatoIlUtc { get; set; }
}
