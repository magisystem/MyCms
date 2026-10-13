namespace DvCms.Models.Segreti;

/// <summary>
/// Riga del key ring di Data Protection usato solo per i segreti dei tenant (ADR-0015). L'XML è cifrato con la chiave
/// di ambiente (<c>Sicurezza:ChiaveSegreti</c>): una copia del database non basta a leggere i segreti.
/// </summary>
public class ChiaveSegreta
{
    public Guid Id { get; set; }

    public string NomeAmichevole { get; set; } = string.Empty;

    public string Xml { get; set; } = string.Empty;

    public DateTime CreatoIlUtc { get; set; }
}
