namespace DvCms.Models.Email;

/// <summary>
/// Modello di un'email scritto dal tenant per una lingua (ADR-0025). Oggetto e corpi sono sorgenti Liquid (Fluid): niente codice
/// eseguibile, solo variabili e poche istruzioni. Dove manca la riga vale il modello di fabbrica. Una riga per tenant, codice e lingua.
/// </summary>
public class ModelloEmail
{
    public Guid Id { get; set; }

    public Guid IdTenant { get; set; }

    /// <summary>Codice del modello nel catalogo (ad esempio <c>recupero-codice</c>).</summary>
    public string Codice { get; set; } = string.Empty;

    /// <summary>Codice della lingua (<c>it</c>, <c>en</c>...).</summary>
    public string Lingua { get; set; } = string.Empty;

    public string Oggetto { get; set; } = string.Empty;

    public string CorpoHtml { get; set; } = string.Empty;

    /// <summary>Alternativa testuale dello stesso messaggio.</summary>
    public string CorpoTesto { get; set; } = string.Empty;

    public DateTime AggiornatoIlUtc { get; set; }

    public Guid? AggiornatoDaIdUtenza { get; set; }

    public byte[] TokenConcorrenza { get; set; } = [];
}
