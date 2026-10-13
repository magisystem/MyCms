
namespace DvCms.DTO.Negozio;

/// <summary>Parametri dei dati personali degli acquirenti (configurazione <c>Negozio:ConservazioneOrdiniAnni</c>).</summary>
public sealed record OpzioniDatiPersonali(int ConservazioneOrdiniAnni)
{
    /// <summary>Gli ordini si conservano 10 anni per gli obblighi fiscali (ADR-0020, A7).</summary>
    public static OpzioniDatiPersonali Predefinite { get; } = new(10);
}

/// <summary>Copia in formato leggibile dalle macchine dei dati che il negozio ha su un acquirente (portabilità, GDPR art. 20).</summary>
public sealed record EsportazioneDatiPersonali(string NomeFile, string Json);
