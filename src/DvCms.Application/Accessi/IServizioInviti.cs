namespace DvCms.Application.Accessi;

/// <summary>Parametri degli inviti (configurazione <c>Sicurezza:Inviti:*</c>).</summary>
public sealed record OpzioniInviti(TimeSpan Validita)
{
    public static OpzioniInviti Predefinite { get; } = new(TimeSpan.FromHours(72));
}

/// <summary>
/// Accettazione di un invito: l'invitato apre il link ricevuto per email, sul dominio del suo tenant, e sceglie la password.
/// La creazione e il reinvio sono operazioni del <c>PlatformAdmin</c> (<see cref="Piattaforma.IServizioPiattaforma"/>).
/// </summary>
public interface IServizioInviti
{
    /// <summary>Vero se il link è ancora utilizzabile (serve a mostrare il modulo o un errore generico).</summary>
    Task<bool> LinkValidoAsync(string? token, CancellationToken annullamento);

    /// <summary>
    /// Imposta la password scelta e consuma l'invito: il link e gli altri inviti dell'account si invalidano e l'account non
    /// deve più cambiare la password al primo accesso.
    /// </summary>
    Task<EsitoReimpostazione> AccettaAsync(string? token, string? nuovaPassword, CancellationToken annullamento);
}
