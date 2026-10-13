using DvCms.Models.Negozio;

namespace DvCms.Application.Negozio;

public enum FormatoEsportazioneOrdini
{
    /// <summary>Una riga per ordine, con i totali e i dati dell'acquirente e della spedizione.</summary>
    Ordini = 1,

    /// <summary>Una riga per articolo ordinato (con la spedizione come riga a parte), per la contabilità.</summary>
    Righe = 2,
}

/// <summary>Intervallo di date (UTC, estremi inclusi) sulla data di creazione e stato facoltativo.</summary>
public sealed record FiltroEsportazioneOrdini(DateOnly? Dal, DateOnly? Al, StatoOrdine? Stato, FormatoEsportazioneOrdini Formato);

/// <param name="Righe">Righe di dati scritte (intestazione esclusa).</param>
/// <param name="Troncata">Vera se gli ordini erano più del massimo esportabile in una volta: restringere l'intervallo.</param>
public sealed record EsportazioneOrdini(string NomeFile, byte[] Contenuto, int Righe, bool Troncata);

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
