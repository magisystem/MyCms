namespace DvCms.Contracts.Tenancy;

/// <summary>
/// Tenant della richiesta corrente. È deciso solo dall'host dell'URL (ADR-0011): nessun valore proveniente dal
/// client (campo di form, intestazione, query string, cookie) può sostituirlo.
/// </summary>
public interface IContestoTenant
{
    bool Risolto { get; }

    /// <summary>Tenant risolto, oppure nullo se la richiesta non ha un tenant (comandi di servizio, host non risolto).</summary>
    Guid? IdTenantOpzionale { get; }

    /// <summary>Tenant risolto; solleva un'eccezione se non è stato risolto.</summary>
    Guid IdTenant { get; }

    Guid IdSito { get; }

    string Host { get; }

    bool TenantPrimario { get; }
}
