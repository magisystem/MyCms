using DvCms.Extender;

namespace DvCms.Models.Outbox;

public enum StatoMessaggioOutbox
{
    /// <summary>Da elaborare (o da ritentare dopo <see cref="MessaggioOutbox.ProssimoTentativoIlUtc"/>).</summary>
    Pendente = 1,

    Completato = 2,

    /// <summary>Tentativi esauriti o tipo sconosciuto: non viene più elaborato, serve un intervento.</summary>
    Fallito = 3,
}

/// <summary>
/// Evento registrato nella stessa transazione della modifica di dominio (ADR-0010) ed elaborato in seguito dal worker.
/// La consegna è "almeno una volta": i gestori devono tollerare messaggi ripetuti. Appartiene sempre a un tenant.
/// </summary>
public class MessaggioOutbox
{
    public const int LunghezzaMassimaTipo = 100;
    public const int LunghezzaMassimaChiave = 200;
    public const int LunghezzaMassimaErrore = 500;

    public Guid Id { get; set; }

    public Guid IdTenant { get; set; }

    /// <summary>Tipo di evento, usato per scegliere il gestore (ad esempio <c>PaginaPubblicata</c>, <c>InvioEmail</c>).</summary>
    public string Tipo { get; set; } = string.Empty;

    /// <summary>Dati dell'evento in JSON. Non deve contenere segreti in chiaro.</summary>
    public string Payload { get; set; } = "{}";

    /// <summary>Se indicata, impedisce di accodare due volte lo stesso evento per lo stesso tenant.</summary>
    public string? ChiaveIdempotenza { get; set; }

    public StatoMessaggioOutbox Stato { get; set; } = StatoMessaggioOutbox.Pendente;

    public int Tentativi { get; set; }

    public DateTime ProssimoTentativoIlUtc { get; set; }

    /// <summary>Prenotazione del messaggio da parte di un worker, valida fino a questo istante (poi un altro worker può riprenderlo).</summary>
    public DateTime? BloccatoFinoAlUtc { get; set; }

    public Guid? BloccatoDa { get; set; }

    /// <summary>Tipo e testo dell'ultima eccezione, troncato. I gestori non devono includere segreti nei messaggi di errore.</summary>
    public string? UltimoErrore { get; set; }

    public DateTime CreatoIlUtc { get; set; }

    public DateTime? ElaboratoIlUtc { get; set; }

    public static MessaggioOutbox Crea(Guid idTenant, string tipo, string payload, string? chiaveIdempotenza, TimeProvider orologio)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tipo);
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(orologio);
        if (tipo.Length > LunghezzaMassimaTipo)
        {
            throw new ArgumentException("Tipo troppo lungo.", nameof(tipo));
        }

        if (chiaveIdempotenza is { Length: > LunghezzaMassimaChiave })
        {
            throw new ArgumentException("Chiave di idempotenza troppo lunga.", nameof(chiaveIdempotenza));
        }

        var adesso = orologio.GetUtcNow().UtcDateTime;
        return new MessaggioOutbox
        {
            Id = Identificativi.Nuovo(orologio),
            IdTenant = idTenant,
            Tipo = tipo,
            Payload = payload,
            ChiaveIdempotenza = string.IsNullOrWhiteSpace(chiaveIdempotenza) ? null : chiaveIdempotenza,
            CreatoIlUtc = adesso,
            ProssimoTentativoIlUtc = adesso,
        };
    }
}
