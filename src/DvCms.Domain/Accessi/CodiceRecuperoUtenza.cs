namespace DvCms.Domain.Accessi;

/// <summary>
/// Codice di recupero monouso per il secondo fattore (ADR-0019). Si conserva solo l'hash SHA-256 del codice, che ha 80 bit di
/// entropia. Un codice usato resta in tabella con <see cref="UsatoIlUtc"/> valorizzato, a documentare l'uso.
/// </summary>
public class CodiceRecuperoUtenza
{
    public Guid Id { get; set; }

    public Guid IdUtenza { get; set; }

    public byte[] HashCodice { get; set; } = [];

    public DateTime CreatoIlUtc { get; set; }

    public DateTime? UsatoIlUtc { get; set; }
}
