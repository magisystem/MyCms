using DvCms.Models.Negozio;

namespace DvCms.Application.Negozio;

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

/// <summary>
/// Verifica di prontezza del negozio di un sito prima di accendere i pagamenti reali (ADR-0020, operazioni): credenziali PayPal e
/// webhook, interruttore di produzione, catalogo, spedizioni, canale email, pagine legali, manutenzione. Sola lettura, riservata al
/// <c>PlatformAdmin</c> (ruolo riverificato); non mostra mai i segreti.
/// </summary>
public interface IServizioProntezzaNegozio
{
    /// <summary>Il rapporto del sito, o <c>null</c> se il sito non è del tenant indicato. Lancia <see cref="UnauthorizedAccessException"/> a chi non è PlatformAdmin.</summary>
    Task<RapportoProntezza?> VerificaAsync(Guid idAttore, Guid idTenant, Guid idSito, CancellationToken annullamento);
}
