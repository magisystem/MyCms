namespace DvCms.DTO.Accessi;

/// <summary>Parametri di sicurezza degli accessi (configurazione <c>Sicurezza:*</c>).</summary>
public sealed record OpzioniSicurezza(int FattoreLavoroBcrypt, int TentativiMassimi, TimeSpan DurataBlocco)
{
    public const int FattoreLavoroPredefinito = 12;
    public const int TentativiMassimiPredefiniti = 5;
    public const int MinutiBloccoPredefiniti = 5;

    public static OpzioniSicurezza Predefinite { get; } =
        new(FattoreLavoroPredefinito, TentativiMassimiPredefiniti, TimeSpan.FromMinutes(MinutiBloccoPredefiniti));
}
