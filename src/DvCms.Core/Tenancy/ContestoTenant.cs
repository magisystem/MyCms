using DvCms.Contracts.Tenancy;
using DvCms.DTO.Tenancy;

namespace DvCms.Core.Tenancy;

/// <summary>Implementazione per ambito (richiesta). Si imposta una sola volta, dal middleware di risoluzione.</summary>
public sealed class ContestoTenant : IContestoTenant
{
    private DatiTenantRisolto? _dati;

    public bool Risolto => _dati is not null;

    public Guid? IdTenantOpzionale => _dati?.IdTenant;

    public Guid IdTenant => Dati.IdTenant;

    public Guid IdSito => Dati.IdSito;

    public string Host => Dati.Host;

    public bool TenantPrimario => Dati.TenantPrimario;

    private DatiTenantRisolto Dati =>
        _dati ?? throw new InvalidOperationException("Tenant non risolto per questa operazione.");

    public void Imposta(DatiTenantRisolto dati)
    {
        ArgumentNullException.ThrowIfNull(dati);
        if (_dati is not null)
        {
            throw new InvalidOperationException("Il tenant della richiesta è già stato risolto e non può essere sostituito.");
        }

        _dati = dati;
    }
}
