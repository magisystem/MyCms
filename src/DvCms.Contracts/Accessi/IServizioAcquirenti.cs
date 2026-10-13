using DvCms.DTO.Accessi;
using DvCms.DTO.Piattaforma;

namespace DvCms.Contracts.Accessi;

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
