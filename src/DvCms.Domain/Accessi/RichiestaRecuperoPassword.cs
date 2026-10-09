namespace DvCms.Domain.Accessi;

public enum StatoRecuperoPassword
{
    /// <summary>Codice inviato: si attende che venga digitato.</summary>
    InAttesaCodice = 1,

    /// <summary>Codice corretto: il link monouso è stato inviato con una seconda email.</summary>
    LinkInviato = 2,

    /// <summary>Password reimpostata con il link.</summary>
    Completata = 3,

    /// <summary>Sostituita da una richiesta successiva, o troppi tentativi sul codice.</summary>
    Annullata = 4,

    /// <summary>
    /// Richiesta che non ha prodotto email (account inesistente, non ammesso o limite raggiunto): esiste per applicare i
    /// limiti e per rendere la risposta identica a quella di una richiesta reale. Non può mai avere successo.
    /// </summary>
    Ignorata = 5,
}

/// <summary>
/// Una richiesta di recupero password (ADR-0011): codice OTP (prima email), poi link monouso (seconda email).
/// Di codice, riferimento e link si conservano solo gli hash; il codice e il link in chiaro esistono soltanto nelle email.
/// </summary>
public class RichiestaRecuperoPassword
{
    public Guid Id { get; set; }

    public Guid IdTenant { get; set; }

    /// <summary>Email normalizzata digitata, anche se l'account non esiste: serve al limite per account.</summary>
    public string EmailNormalizzata { get; set; } = string.Empty;

    /// <summary>Account trovato, se la richiesta è reale.</summary>
    public Guid? IdUtenza { get; set; }

    /// <summary>Indirizzo IP della richiesta, per il limite per IP; da ripulire con i dati scaduti.</summary>
    public string? IndirizzoIp { get; set; }

    public StatoRecuperoPassword Stato { get; set; }

    /// <summary>SHA-256 del riferimento casuale tenuto nel cookie del browser che ha fatto la richiesta.</summary>
    public byte[] HashRiferimento { get; set; } = [];

    public byte[] SaleCodice { get; set; } = [];

    /// <summary>SHA-256 di sale e codice a sei cifre.</summary>
    public byte[] HashCodice { get; set; } = [];

    public DateTime ScadenzaCodiceIlUtc { get; set; }

    public int TentativiCodice { get; set; }

    /// <summary>SHA-256 del token del link (256 bit casuali).</summary>
    public byte[]? HashToken { get; set; }

    public DateTime? ScadenzaTokenIlUtc { get; set; }

    public DateTime CreatoIlUtc { get; set; }

    public DateTime? CompletatoIlUtc { get; set; }
}
