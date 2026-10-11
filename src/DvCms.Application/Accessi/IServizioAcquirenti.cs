using DvCms.Application.Piattaforma;

namespace DvCms.Application.Accessi;

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

/// <summary>
/// Registrazione degli acquirenti (F-000, ADR-0011): solo nei siti con negozio e registrazione attivi, sempre e solo con ruolo
/// <c>Utente</c>, nel tenant dell'host. Si indicano email e nome; la **password si sceglie dal link di conferma** inviato
/// all'indirizzo (monouso, valido 24 ore), così nessuno può registrare l'indirizzo altrui con una password che conosce.
/// </summary>
public interface IServizioAcquirenti
{
    /// <summary>Vero se la pagina di registrazione deve esistere e funzionare (negozio e registrazione attivi, canale email configurato).</summary>
    Task<bool> RegistrazioneDisponibileAsync(CancellationToken annullamento);

    /// <summary>Vero se la registrazione è abilitata per il sito dell'host (la pagina esiste), anche se l'email non è configurata.</summary>
    Task<bool> RegistrazioneAbilitataAsync(CancellationToken annullamento);

    Task<RisultatoRegistrazione> RegistraAsync(DatiRegistrazioneAcquirente dati, DatiLinkInvito link, CancellationToken annullamento);

    /// <summary>Vero se il link di conferma è ancora utilizzabile (serve a mostrare il modulo o un errore generico).</summary>
    Task<bool> LinkValidoAsync(string? token, CancellationToken annullamento);

    /// <summary>Conferma l'email, imposta la password scelta e attiva l'account; il link si consuma.</summary>
    Task<EsitoReimpostazione> ConfermaAsync(string? token, string? password, CancellationToken annullamento);
}
