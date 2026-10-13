namespace DvCms.Domain.Negozio;

public enum AmbientePagamento
{
    /// <summary>Conti di prova del provider: nessun denaro reale.</summary>
    Sandbox = 1,

    /// <summary>Conto reale del commerciante. Il checkout di produzione resta spento finché la politica è bloccata (ADR-0020, A5/A7).</summary>
    Live = 2,
}

/// <summary>
/// Credenziali PayPal di un sito (ADR-0020): ogni tenant usa il proprio conto commerciante. L'identificativo client non è segreto;
/// il segreto del client e l'identificativo del webhook stanno solo cifrati in <see cref="SegretiProtetti"/> (JSON nome-valore),
/// in sola scrittura nell'interfaccia e mai nei log.
/// </summary>
public class ImpostazioniPagamentoSito
{
    public const int LunghezzaMassimaIdClient = 200;

    public Guid Id { get; set; }

    public Guid IdTenant { get; set; }

    public Guid IdSito { get; set; }

    public AmbientePagamento Ambiente { get; set; } = AmbientePagamento.Sandbox;

    public string IdClient { get; set; } = string.Empty;

    public string? SegretiProtetti { get; set; }

    public DateTime AggiornatoIlUtc { get; set; }

    public Guid? AggiornatoDaIdUtenza { get; set; }

    public byte[] TokenConcorrenza { get; set; } = [];
}

/// <summary>
/// Credenziali Nexi XPay di un sito (ADR-0022): ogni tenant usa il proprio terminale. La chiave API (<c>X-API-KEY</c>) sta solo cifrata
/// in <see cref="SegretiProtetti"/> (JSON nome-valore), in sola scrittura nell'interfaccia e mai nei log. L'ambiente decide l'indirizzo
/// del gateway (test o produzione).
/// </summary>
public class ImpostazioniNexiSito
{
    public Guid Id { get; set; }

    public Guid IdTenant { get; set; }

    public Guid IdSito { get; set; }

    public AmbientePagamento Ambiente { get; set; } = AmbientePagamento.Sandbox;

    public string? SegretiProtetti { get; set; }

    public DateTime AggiornatoIlUtc { get; set; }

    public Guid? AggiornatoDaIdUtenza { get; set; }

    public byte[] TokenConcorrenza { get; set; } = [];
}
