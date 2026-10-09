namespace DvCms.Application.Tenancy;

public enum EsitoRisoluzioneTenant
{
    /// <summary>Host registrato, verificato e attivo, con tenant e sito attivi.</summary>
    Risolto = 1,

    /// <summary>Host sconosciuto, non valido, non verificato, disattivato o riservato allo sviluppo.</summary>
    NonRegistrato = 2,

    /// <summary>Host registrato ma tenant o sito sospesi/disattivati.</summary>
    NonDisponibile = 3,
}

public sealed record RisultatoRisoluzioneTenant(EsitoRisoluzioneTenant Esito, DatiTenantRisolto? Dati)
{
    public static RisultatoRisoluzioneTenant NonRegistrato { get; } = new(EsitoRisoluzioneTenant.NonRegistrato, null);

    public static RisultatoRisoluzioneTenant NonDisponibile { get; } = new(EsitoRisoluzioneTenant.NonDisponibile, null);

    public static RisultatoRisoluzioneTenant Risolto(DatiTenantRisolto dati) => new(EsitoRisoluzioneTenant.Risolto, dati);
}

public interface IRisolutoreTenant
{
    /// <summary>Risolve l'host della richiesta (anche con porta) nel tenant e nel sito. Solo host registrati risolvono.</summary>
    Task<RisultatoRisoluzioneTenant> RisolviAsync(string? host, CancellationToken annullamento);
}
