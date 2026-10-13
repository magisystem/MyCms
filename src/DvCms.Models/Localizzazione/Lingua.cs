namespace DvCms.Models.Localizzazione;

/// <summary>
/// Lingua disponibile nella piattaforma (ADR-0018). L'italiano è la lingua di base e predefinita.
/// </summary>
public class Lingua
{
    public const string CodiceItaliano = "it";
    public const string CodiceInglese = "en";

    /// <summary>Identificatori fissi delle lingue iniziali, inserite dalla migrazione.</summary>
    public static readonly Guid IdItaliano = new("01996a00-0000-7000-8000-000000000001");

    public static readonly Guid IdInglese = new("01996a00-0000-7000-8000-000000000002");

    public Guid Id { get; set; }

    /// <summary>Codice breve (<c>it</c>, <c>en</c>).</summary>
    public string Codice { get; set; } = string.Empty;

    public string Nome { get; set; } = string.Empty;

    /// <summary>Cultura .NET per date e numeri (<c>it-IT</c>, <c>en-GB</c>).</summary>
    public string Cultura { get; set; } = string.Empty;

    public bool Predefinita { get; set; }

    public bool Attiva { get; set; }
}
