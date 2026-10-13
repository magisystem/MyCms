using DvCms.DTO.Piattaforma;
using DvCms.Models.Email;
using DvCms.Models.Outbox;

namespace DvCms.DTO.Email;

/// <summary>Impostazioni di un tenant come si mostrano nella UI: i segreti non ci sono mai, solo il fatto che siano impostati.</summary>
public sealed record ImpostazioniEmailVisibili(
    ProviderEmail Provider,
    IReadOnlyDictionary<string, string> Valori,
    IReadOnlySet<string> SegretiImpostati,
    string TokenConcorrenza,
    DateTime AggiornatoIlUtc);

/// <summary>Un invio non riuscito o in ritentativo, visibile ai <c>PlatformAdmin</c>.</summary>
public sealed record ProblemaInvioEmail(Guid IdMessaggio, StatoMessaggioOutbox Stato, int Tentativi, string? UltimoErrore, DateTime CreatoIlUtc);

public sealed record SchedaEmailTenant(string NomeTenant, ImpostazioniEmailVisibili? Impostazioni, IReadOnlyList<ProblemaInvioEmail> Problemi);

public sealed record EsitoSalvataggioEmail(CodiceEsito Codice, IReadOnlyList<string> CampiNonValidi)
{
    public bool Riuscito => Codice == CodiceEsito.Riuscito;
}
