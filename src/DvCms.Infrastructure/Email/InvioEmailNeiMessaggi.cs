using System.Text.Json;
using DvCms.Contracts.Email;
using DvCms.Contracts.Outbox;
using DvCms.DTO.Email;
using DvCms.DTO.Outbox;

namespace DvCms.Infrastructure.Email;

public static class TipiEventoEmail
{
    public const string Invio = "InvioEmail";
}

/// <summary>Contenuto del messaggio outbox di invio email: in chiaro o, per i messaggi riservati, cifrato.</summary>
internal sealed record PayloadInvioEmail(MessaggioEmail? Messaggio, string? Protetto);

public sealed class AccodatoreEmail : IAccodatoreEmail
{
    private readonly IOutbox _outbox;
    private readonly IProtettoreSegreti _protettore;

    public AccodatoreEmail(IOutbox outbox, IProtettoreSegreti protettore)
    {
        _outbox = outbox;
        _protettore = protettore;
    }

    public Guid Accoda(Guid idTenant, MessaggioEmail messaggio, string? chiaveIdempotenza = null, bool riservato = false)
    {
        ArgumentNullException.ThrowIfNull(messaggio);

        var payload = riservato
            ? new PayloadInvioEmail(null, _protettore.Proteggi(JsonSerializer.Serialize(messaggio)))
            : new PayloadInvioEmail(messaggio, null);
        return _outbox.Accoda(idTenant, TipiEventoEmail.Invio, payload, chiaveIdempotenza);
    }
}

/// <summary>Gestore dell'outbox che invia le email: l'invio avviene qui, nel worker, mai nella richiesta web.</summary>
public sealed class GestoreInvioEmail : IGestoreOutbox
{
    private readonly IInvioEmail _invio;
    private readonly IProtettoreSegreti _protettore;

    public GestoreInvioEmail(IInvioEmail invio, IProtettoreSegreti protettore)
    {
        _invio = invio;
        _protettore = protettore;
    }

    public string Tipo => TipiEventoEmail.Invio;

    public async Task GestisciAsync(MessaggioDaElaborare messaggio, CancellationToken annullamento)
    {
        ArgumentNullException.ThrowIfNull(messaggio);

        var payload = JsonSerializer.Deserialize<PayloadInvioEmail>(messaggio.Payload)
            ?? throw new InvalidOperationException("Payload dell'invio email non valido.");
        var email = payload.Messaggio
            ?? JsonSerializer.Deserialize<MessaggioEmail>(_protettore.Rivela(payload.Protetto ?? string.Empty))
            ?? throw new InvalidOperationException("Payload dell'invio email non valido.");
        await _invio.InviaAsync(messaggio.IdTenant, email, annullamento);
    }
}
