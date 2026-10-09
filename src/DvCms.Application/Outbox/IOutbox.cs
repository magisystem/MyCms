namespace DvCms.Application.Outbox;

/// <summary>
/// Accodamento di eventi nella stessa transazione della modifica di dominio (ADR-0010): <see cref="Accoda"/> non salva,
/// il messaggio viene scritto dal <c>SaveChanges</c> del chiamante insieme ai suoi dati, o non viene scritto affatto.
/// </summary>
public interface IOutbox
{
    /// <summary>Accoda l'evento. Con una chiave di idempotenza già presente per il tenant la scrittura fallisce al salvataggio.</summary>
    Guid Accoda(Guid idTenant, string tipo, object payload, string? chiaveIdempotenza = null);
}

/// <summary>Messaggio consegnato a un gestore.</summary>
public sealed record MessaggioDaElaborare(Guid Id, Guid IdTenant, string Tipo, string Payload, int Tentativo);

/// <summary>
/// Gestore di un tipo di evento. Può essere invocato più volte per lo stesso messaggio (consegna almeno una volta) e
/// deve tollerarlo. Un'eccezione fa ritentare il messaggio con attesa crescente fino al limite di tentativi.
/// </summary>
public interface IGestoreOutbox
{
    string Tipo { get; }

    Task GestisciAsync(MessaggioDaElaborare messaggio, CancellationToken annullamento);
}
