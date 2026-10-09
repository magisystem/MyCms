using System.Text.Json;
using DvCms.Application.Outbox;
using DvCms.Domain.Outbox;
using DvCms.Infrastructure.Persistenza;

namespace DvCms.Infrastructure.Outbox;

public sealed class ScrittoreOutbox : IOutbox
{
    private readonly ContestoDatabase _contesto;
    private readonly TimeProvider _orologio;

    public ScrittoreOutbox(ContestoDatabase contesto, TimeProvider orologio)
    {
        _contesto = contesto;
        _orologio = orologio;
    }

    public Guid Accoda(Guid idTenant, string tipo, object payload, string? chiaveIdempotenza = null)
    {
        ArgumentNullException.ThrowIfNull(payload);

        var messaggio = MessaggioOutbox.Crea(idTenant, tipo, JsonSerializer.Serialize(payload), chiaveIdempotenza, _orologio);
        _contesto.MessaggiOutbox.Add(messaggio);
        return messaggio.Id;
    }
}
