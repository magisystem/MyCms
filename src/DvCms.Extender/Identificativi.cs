namespace DvCms.Extender;

/// <summary>
/// Generazione degli identificatori delle entità. Gli identificatori sono <see cref="Guid"/> creati
/// dall'applicazione, nella versione 7 (ordinabile nel tempo) per limitare la frammentazione degli indici.
/// </summary>
public static class Identificativi
{
    public static Guid Nuovo(TimeProvider orologio)
    {
        ArgumentNullException.ThrowIfNull(orologio);
        return Guid.CreateVersion7(orologio.GetUtcNow());
    }
}
