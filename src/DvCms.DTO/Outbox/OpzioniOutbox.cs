namespace DvCms.DTO.Outbox;

/// <summary>Parametri del worker (configurazione <c>Outbox:*</c>).</summary>
public sealed record OpzioniOutbox(bool Abilitato, TimeSpan Intervallo, int DimensioneLotto, int TentativiMassimi, TimeSpan DurataPrenotazione)
{
    public static OpzioniOutbox Predefinite { get; } = new(true, TimeSpan.FromSeconds(5), 20, 8, TimeSpan.FromMinutes(2));
}
