namespace DvCms.DTO.Tenancy;

/// <summary>Dati del tenant risolto dall'host della richiesta.</summary>
public sealed record DatiTenantRisolto(Guid IdTenant, Guid IdSito, Guid IdDominio, string Host, bool TenantPrimario);
