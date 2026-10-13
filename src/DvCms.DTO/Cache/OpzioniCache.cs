namespace DvCms.DTO.Cache;

/// <summary>Parametri della cache (configurazione <c>Cache:*</c>).</summary>
public sealed record OpzioniCache(bool Abilitata, TimeSpan Durata, int VociMassime, TimeSpan IntervalloLettura)
{
    public static OpzioniCache Predefinite { get; } = new(true, TimeSpan.FromMinutes(5), 5000, TimeSpan.FromSeconds(2));
}
