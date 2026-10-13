using DvCms.Models.Email;

namespace DvCms.Application.Email;

/// <summary>Messaggio da inviare. Il mittente non è qui: lo decide la configurazione del tenant.</summary>
public sealed record MessaggioEmail(string Destinatario, string? NomeDestinatario, string Oggetto, string CorpoTesto, string? CorpoHtml = null);

/// <summary>Impostazioni complete di un tenant, segreti compresi, pronte per un adapter. Non va mai registrata nei log.</summary>
public sealed record ImpostazioniEmail(ProviderEmail Provider, IReadOnlyDictionary<string, string> Valori)
{
    public string? Valore(string nome) => Valori.TryGetValue(nome, out var valore) && !string.IsNullOrWhiteSpace(valore) ? valore : null;

    public override string ToString() => $"ImpostazioniEmail({Provider}, {Valori.Count} valori)";
}

/// <summary>
/// Contratto unico di invio (ADR-0015): sceglie il canale dalle impostazioni del tenant. Si usa dal worker, mai dalla
/// richiesta web: per inviare dalle pagine si accoda con <see cref="IAccodatoreEmail"/>.
/// </summary>
public interface IInvioEmail
{
    /// <exception cref="ConfigurazioneEmailNonValidaException">Il tenant non ha una configurazione valida.</exception>
    Task InviaAsync(Guid idTenant, MessaggioEmail messaggio, CancellationToken annullamento);
}

/// <summary>Un canale di invio (SMTP, Graph, Gmail, SendGrid). Riceve impostazioni già validate.</summary>
public interface IAdapterEmail
{
    ProviderEmail Provider { get; }

    Task InviaAsync(ImpostazioniEmail impostazioni, MessaggioEmail messaggio, CancellationToken annullamento);
}

/// <summary>
/// Accodamento di un'email nell'outbox, nella stessa transazione del chiamante: l'invio avviene poi nel worker.
/// </summary>
public interface IAccodatoreEmail
{
    /// <param name="idTenant">Tenant che invia (e di cui si usa la configurazione).</param>
    /// <param name="messaggio">Il messaggio.</param>
    /// <param name="chiaveIdempotenza">Se già accodata per il tenant, l'accodamento fallisce al salvataggio.</param>
    /// <param name="riservato">
    /// Per messaggi che contengono segreti (codici, link): il contenuto è cifrato anche nella tabella dell'outbox.
    /// </param>
    Guid Accoda(Guid idTenant, MessaggioEmail messaggio, string? chiaveIdempotenza = null, bool riservato = false);
}

/// <summary>Cifratura dei segreti a riposo (ADR-0015). La chiave sta nella configurazione di ambiente, mai nel database.</summary>
public interface IProtettoreSegreti
{
    string Proteggi(string testo);

    /// <exception cref="InvalidOperationException">Il testo non è valido o la chiave non corrisponde.</exception>
    string Rivela(string protetto);
}

/// <summary>Il tenant non ha impostazioni email valide: nessun invio. Il messaggio non contiene valori delle impostazioni.</summary>
public sealed class ConfigurazioneEmailNonValidaException : Exception
{
    public ConfigurazioneEmailNonValidaException()
    {
    }

    public ConfigurazioneEmailNonValidaException(string message)
        : base(message)
    {
    }

    public ConfigurazioneEmailNonValidaException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
