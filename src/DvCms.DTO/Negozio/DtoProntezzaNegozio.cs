using DvCms.Models.Negozio;

namespace DvCms.DTO.Negozio;

public enum StatoControllo
{
    /// <summary>A posto.</summary>
    Ok = 1,

    /// <summary>Non impedisce di vendere ma conviene sistemarlo prima di aprire.</summary>
    Attenzione = 2,

    /// <summary>Senza questo i pagamenti reali non devono partire.</summary>
    Bloccante = 3,
}

/// <summary>Un controllo di prontezza: <c>Codice</c> identifica la voce (l'interfaccia la traduce), <c>Dettaglio</c> è un dato neutro (ambiente, indirizzo, conteggio).</summary>
public sealed record ControlloProntezza(string Codice, StatoControllo Stato, string? Dettaglio = null);

public sealed record RapportoProntezza(string NomeSito, AmbientePagamento Ambiente, IReadOnlyList<ControlloProntezza> Controlli)
{
    /// <summary>Vero se nessun controllo è bloccante.</summary>
    public bool Pronto => Controlli.All(c => c.Stato != StatoControllo.Bloccante);
}
