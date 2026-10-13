using DvCms.Models.Negozio;

namespace DvCms.DTO.Negozio;

public enum RisultatoRimborso
{
    /// <summary>Rimborsato: il provider ha restituito il denaro.</summary>
    Completato = 1,

    /// <summary>Richiesto ma non ancora concluso (il provider lo sta elaborando o la risposta non è arrivata): l'importo resta impegnato e si può verificare di nuovo.</summary>
    InCorso = 2,

    /// <summary>Il provider ha rifiutato: l'importo torna rimborsabile.</summary>
    Rifiutato = 3,

    DatiNonValidi = 4,

    /// <summary>L'ordine o l'incasso non è in uno stato rimborsabile.</summary>
    NonRimborsabile = 5,

    ErroreProvider = 6,

    NonConfigurato = 7,

    NonTrovato = 8,

    NonAutorizzato = 9,
}

public sealed record RimborsoVista(Guid Id, decimal Importo, string Valuta, string? Motivo, StatoRimborso Stato, bool IncassoOrfano, DateTime CreatoIlUtc, bool Esterno = false);
