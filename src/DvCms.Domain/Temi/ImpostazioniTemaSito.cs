namespace DvCms.Domain.Temi;

/// <summary>
/// Tema scelto da un sito e le sue impostazioni (D-001). Il tema è una chiave e una versione del registro dei temi, che
/// sta nel codice: nulla qui diventa mai un percorso di vista o un assembly. Un sito senza riga usa il tema predefinito.
/// </summary>
public class ImpostazioniTemaSito
{
    public const int LunghezzaMassimaChiave = 50;

    public Guid Id { get; set; }

    public Guid IdTenant { get; set; }

    public Guid IdSito { get; set; }

    public string ChiaveTema { get; set; } = string.Empty;

    /// <summary>Versione del tema con cui il sito è stato configurato: non cambia da sola quando esce una versione nuova.</summary>
    public int VersioneTema { get; set; } = 1;

    /// <summary>Valori delle impostazioni validate contro lo schema del tema, come oggetto JSON nome-valore.</summary>
    public string ImpostazioniJson { get; set; } = "{}";

    public DateTime AggiornatoIlUtc { get; set; }

    public Guid? AggiornatoDaIdUtenza { get; set; }

    public byte[] TokenConcorrenza { get; set; } = [];

    public IReadOnlyDictionary<string, string> Impostazioni
    {
        get
        {
            try
            {
                return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(ImpostazioniJson) ?? [];
            }
            catch (System.Text.Json.JsonException)
            {
                return new Dictionary<string, string>();
            }
        }
    }
}
