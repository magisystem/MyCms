using DvCms.Models.Moduli;

namespace DvCms.DTO.Moduli;

/// <summary>Una riga della casella delle richieste: il tipo di stato e un'anteprima del primo valore, senza esporre l'intero contenuto.</summary>
public sealed record RiepilogoRichiesta(Guid Id, StatoInvioModulo Stato, string CodiceLingua, DateTime CreatoIlUtc, string Anteprima);

/// <summary>Una pagina della casella delle richieste di un modulo, con i contatori per stato (di tutto il modulo, non solo della pagina).</summary>
public sealed record ElencoRichieste(
    Guid IdModulo,
    string NomeModulo,
    IReadOnlyList<RiepilogoRichiesta> Richieste,
    int Totale,
    int Nuove,
    int Pagina,
    int DimensionePagina,
    StatoInvioModulo? Filtro)
{
    public const int DimensionePredefinita = 25;

    public int Pagine => Math.Max(1, (int)Math.Ceiling(Totale / (double)DimensionePagina));
}

/// <summary>Una richiesta per intero, con i valori inviati così come erano al momento dell'invio.</summary>
public sealed record VistaRichiesta(
    Guid Id,
    Guid IdModulo,
    string NomeModulo,
    StatoInvioModulo Stato,
    string CodiceLingua,
    DateTime CreatoIlUtc,
    IReadOnlyList<ValoreInviato> Valori);

/// <summary>File CSV con le richieste di un modulo; <paramref name="Troncata"/> se superano il massimo esportabile.</summary>
public sealed record EsportazioneRichieste(string NomeFile, byte[] Contenuto, int Righe, bool Troncata);
