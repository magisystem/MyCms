namespace DvCms.DTO.Manutenzione;

/// <summary>Parametri della manutenzione (configurazione <c>Manutenzione:*</c>).</summary>
public sealed record OpzioniManutenzione(
    bool Abilitata,
    TimeSpan Intervallo,
    TimeSpan ConservazioneCompletati,
    TimeSpan ConservazioneFalliti,
    TimeSpan ConservazioneAccessi,
    int DimensioneLotto)
{
    public static OpzioniManutenzione Predefinite { get; } =
        new(true, TimeSpan.FromHours(1), TimeSpan.FromDays(7), TimeSpan.FromDays(90), TimeSpan.FromDays(30), 1000);
}
