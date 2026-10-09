namespace DvCms.Domain.Localizzazione;

/// <summary>
/// Testo statico di interfaccia in una lingua. La chiave è il testo italiano di base così come scritto nel codice
/// o nelle viste; è globale alla piattaforma (non appartiene a un tenant).
/// </summary>
public class TestoStatico
{
    public const int LunghezzaMassimaChiave = 1000;
    public const int LunghezzaMassimaTesto = 4000;

    public Guid Id { get; set; }

    public Guid IdLingua { get; set; }

    public string Chiave { get; set; } = string.Empty;

    /// <summary>SHA-256 della chiave (UTF-8), usato nell'indice univoco.</summary>
    public byte[] HashChiave { get; set; } = [];

    public string Testo { get; set; } = string.Empty;

    /// <summary>Vero se il testo è stato creato in automatico con il testo italiano e non è ancora stato tradotto.</summary>
    public bool DaTradurre { get; set; }

    public DateTime AggiornatoIlUtc { get; set; }

    public Guid? AggiornatoDaIdUtenza { get; set; }
}
