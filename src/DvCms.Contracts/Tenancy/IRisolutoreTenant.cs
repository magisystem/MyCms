using DvCms.DTO.Tenancy;

namespace DvCms.Contracts.Tenancy;

public interface IRisolutoreTenant
{
    /// <summary>Risolve l'host della richiesta (anche con porta) nel tenant e nel sito. Solo host registrati risolvono.</summary>
    Task<RisultatoRisoluzioneTenant> RisolviAsync(string? host, CancellationToken annullamento);
}
