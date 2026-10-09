namespace DvCms.Application.Tenancy;

/// <summary>Dati del tenant risolto dall'host della richiesta.</summary>
public sealed record DatiTenantRisolto(Guid IdTenant, Guid IdSito, Guid IdDominio, string Host, bool TenantPrimario);

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
