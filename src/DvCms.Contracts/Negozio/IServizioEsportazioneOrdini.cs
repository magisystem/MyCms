using DvCms.DTO.Negozio;
using DvCms.Models.Negozio;

namespace DvCms.Contracts.Negozio;

/// <summary>
/// Esportazione degli ordini del sito in CSV (ADR-0020: fatture e ricevute sono fuori dal sistema, il tenant parte da qui). Solo
/// l'amministrazione del tenant (<c>TenantAdmin</c>, <c>PlatformAdmin</c>), riverificata sul database a ogni operazione; ogni
/// esportazione, che contiene dati personali, è registrata nell'audit con il numero di righe e il filtro, mai con i dati.
/// </summary>
public interface IServizioEsportazioneOrdini
{
    /// <summary>Numero massimo di ordini per esportazione.</summary>
    const int MassimoOrdini = 50_000;

    /// <exception cref="UnauthorizedAccessException">L'attore non amministra il tenant.</exception>
    Task<EsportazioneOrdini> EsportaAsync(Guid idAttore, FiltroEsportazioneOrdini filtro, CancellationToken annullamento);
}
