namespace DvCms.Models.Accessi;

/// <summary>
/// Voce del registro di audit: chi (attore, nullo per le operazioni di sistema come il seeding) ha fatto cosa, su quale
/// risorsa e con quale esito. Non deve contenere segreti né dati personali non necessari.
/// </summary>
public class RegistroAudit
{
    public const int LunghezzaMassimaDettagli = 1000;

    public Guid Id { get; set; }

    /// <summary>Tenant a cui si riferisce l'operazione, se c'è.</summary>
    public Guid? IdTenant { get; set; }

    /// <summary>Account che ha eseguito l'operazione; nullo per le operazioni di sistema.</summary>
    public Guid? IdUtenzaAttore { get; set; }

    public string Azione { get; set; } = string.Empty;

    public string Risorsa { get; set; } = string.Empty;

    public string Esito { get; set; } = EsitoRiuscito;

    public string? Dettagli { get; set; }

    public DateTime CreatoIlUtc { get; set; }

    public const string EsitoRiuscito = "Riuscito";
    public const string EsitoNegato = "Negato";
    public const string EsitoFallito = "Fallito";
}
