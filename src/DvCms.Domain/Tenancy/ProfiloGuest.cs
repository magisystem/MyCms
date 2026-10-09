namespace DvCms.Domain.Tenancy;

/// <summary>
/// Profilo del <c>Guest</c> (visitatore anonimo) di un tenant, creato insieme al tenant. Senza impostazioni:
/// il Guest vede tutto il contenuto pubblicato (ADR-0011).
/// </summary>
public class ProfiloGuest
{
    public Guid Id { get; set; }

    public Guid IdTenant { get; set; }

    public DateTime CreatoIlUtc { get; set; }

    public static ProfiloGuest Crea(Guid idTenant, TimeProvider orologio)
    {
        ArgumentNullException.ThrowIfNull(orologio);

        return new ProfiloGuest
        {
            Id = Identificativi.Nuovo(orologio),
            IdTenant = idTenant,
            CreatoIlUtc = orologio.GetUtcNow().UtcDateTime,
        };
    }
}
