using DvCms.Models.Negozio;

namespace DvCms.DTO.Negozio;

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
