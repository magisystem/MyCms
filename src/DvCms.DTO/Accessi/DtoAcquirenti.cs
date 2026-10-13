
namespace DvCms.DTO.Accessi;

/// <summary>Parametri della conferma dell'email degli acquirenti (configurazione <c>Sicurezza:Acquirenti:*</c>).</summary>
public sealed record OpzioniAcquirenti(TimeSpan ValiditaConferma, int RichiesteOrarieMassimeConferma, TimeSpan ConservazioneNonConfermati)
{
    public static OpzioniAcquirenti Predefinite { get; } = new(TimeSpan.FromHours(24), 3, TimeSpan.FromDays(7));
}

public sealed record DatiRegistrazioneAcquirente(string? Email, string? NomeVisualizzato);

public enum EsitoRegistrazione
{
    /// <summary>
    /// Richiesta presa in carico. È la stessa risposta qualunque sia lo stato dell'indirizzo (nuovo, in attesa, già registrato): non
    /// si rivela mai se un account esiste.
    /// </summary>
    Accettata = 1,

    /// <summary>La registrazione non è disponibile (negozio o registrazione non attivi, o canale email non configurato).</summary>
    NonDisponibile = 2,

    /// <summary>Indirizzo o nome non validi.</summary>
    DatiNonValidi = 3,
}

public sealed record RisultatoRegistrazione(EsitoRegistrazione Esito, IReadOnlyList<string>? CampiNonValidi = null);
