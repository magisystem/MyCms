using DvCms.Extender;

namespace DvCms.Models.Tenancy;

/// <summary>
/// Lingua abilitata per un sito (ADR-0018). Ogni sito ha almeno una lingua e una sola predefinita: la lingua predefinita
/// non ha prefisso nell'URL, le altre sì (<c>/en/...</c>).
/// </summary>
public class LinguaSito
{
    public Guid Id { get; set; }

    public Guid IdTenant { get; set; }

    public Guid IdSito { get; set; }

    public Guid IdLingua { get; set; }

    public bool Predefinita { get; set; }

    public DateTime CreatoIlUtc { get; set; }

    public static LinguaSito Crea(Guid idTenant, Guid idSito, Guid idLingua, bool predefinita, TimeProvider orologio)
    {
        ArgumentNullException.ThrowIfNull(orologio);

        return new LinguaSito
        {
            Id = Identificativi.Nuovo(orologio),
            IdTenant = idTenant,
            IdSito = idSito,
            IdLingua = idLingua,
            Predefinita = predefinita,
            CreatoIlUtc = orologio.GetUtcNow().UtcDateTime,
        };
    }
}
