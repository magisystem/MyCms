using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace DvCms.Data;

/// <summary>
/// Garantisce che ogni istante salvato sia UTC. In scrittura accetta solo valori UTC o locali (convertiti);
/// un valore con <see cref="DateTimeKind.Unspecified"/> è un errore di programmazione e viene rifiutato.
/// In lettura restituisce sempre un valore con <see cref="DateTimeKind.Utc"/>.
/// </summary>
public sealed class ConvertitoreDataOraUtc : ValueConverter<DateTime, DateTime>
{
    public ConvertitoreDataOraUtc()
        : base(valore => PerScrittura(valore), valore => PerLettura(valore))
    {
    }

    private static DateTime PerScrittura(DateTime valore) => valore.Kind switch
    {
        DateTimeKind.Utc => valore,
        DateTimeKind.Local => valore.ToUniversalTime(),
        _ => throw new InvalidOperationException(
            "Istante senza informazione sul fuso orario: usare sempre valori UTC (TimeProvider.GetUtcNow())."),
    };

    private static DateTime PerLettura(DateTime valore) => DateTime.SpecifyKind(valore, DateTimeKind.Utc);
}
