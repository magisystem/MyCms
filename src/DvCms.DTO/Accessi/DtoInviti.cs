namespace DvCms.DTO.Accessi;

/// <summary>Parametri degli inviti (configurazione <c>Sicurezza:Inviti:*</c>).</summary>
public sealed record OpzioniInviti(TimeSpan Validita)
{
    public static OpzioniInviti Predefinite { get; } = new(TimeSpan.FromHours(72));
}
