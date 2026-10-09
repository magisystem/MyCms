namespace DvCms.Domain.Email;

/// <summary>Canale di invio delle email di un tenant (ADR-0015). Ogni tenant ne usa uno solo.</summary>
public enum ProviderEmail
{
    Smtp = 1,
    Graph = 2,
    Gmail = 3,
    SendGrid = 4,
}

/// <summary>
/// Impostazioni di invio email di un tenant (ADR-0015): il canale scelto e i suoi valori. I valori non segreti sono in
/// <see cref="ValoriJson"/>; quelli segreti (password, chiavi API, segreti client) solo cifrati in <see cref="SegretiProtetti"/>.
/// </summary>
public class ImpostazioniEmailTenant
{
    public Guid Id { get; set; }

    /// <summary>Un tenant ha al più una configurazione.</summary>
    public Guid IdTenant { get; set; }

    public ProviderEmail Provider { get; set; }

    /// <summary>Valori non segreti del canale, come oggetto JSON nome-valore.</summary>
    public string ValoriJson { get; set; } = "{}";

    /// <summary>Valori segreti del canale (JSON nome-valore) cifrati con la protezione dei segreti; nulli se non ce ne sono.</summary>
    public string? SegretiProtetti { get; set; }

    public DateTime AggiornatoIlUtc { get; set; }

    public Guid? AggiornatoDaIdUtenza { get; set; }

    public byte[] TokenConcorrenza { get; set; } = [];
}
