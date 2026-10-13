namespace DvCms.Models.Cms;

/// <summary>
/// Segnala che i contenuti pubblici di un sito sono cambiati (G-002). Si scrive nella stessa transazione della modifica (pubblicazione,
/// ritiro, cambio di slug o di tema, lingue...) e ogni istanza dell'applicazione lo legge per svuotare la propria cache: così una
/// modifica fatta su un'istanza arriva a tutte, entro l'intervallo di lettura, anche senza un bus di messaggi.
/// </summary>
public class InvalidazioneCache
{
    /// <summary>Numero progressivo (identità): le istanze ricordano l'ultimo letto.</summary>
    public long Id { get; set; }

    public Guid IdTenant { get; set; }

    public Guid IdSito { get; set; }

    public DateTime CreatoIlUtc { get; set; }
}
