using DvCms.DTO.Accessi;
using DvCms.Models.Accessi;

namespace DvCms.Contracts.Accessi;

public interface IServizioAccesso
{
    /// <summary>
    /// Accesso dell'account con quell'email nel tenant dell'host (o un <c>PlatformAdmin</c>, da qualunque host).
    /// Blocco di 5 minuti dopo 5 tentativi falliti consecutivi.
    /// </summary>
    Task<RisultatoAccesso> AccediAsync(string? email, string? password, CancellationToken annullamento);

    /// <summary>
    /// Accesso dell'area cliente: solo un account <c>Utente</c> (acquirente) del tenant dell'host. Gli account amministrativi non
    /// entrano da qui e non sono nemmeno cercati (nessun effetto sui loro contatori di tentativi, nessun audit di piattaforma).
    /// Stessi tempi, blocco e riservatezza dell'accesso amministrativo.
    /// </summary>
    Task<RisultatoAccesso> AccediComeAcquirenteAsync(string? email, string? password, CancellationToken annullamento);

    /// <summary>
    /// Verifica a ogni richiesta che la sessione sia ancora valida: account attivo, <c>TimbroSicurezza</c> invariato
    /// e account ammesso sul tenant dell'host. Restituisce l'identità aggiornata, oppure nulla se non valida.
    /// </summary>
    Task<IdentitaUtenza?> ConvalidaSessioneAsync(Guid idUtenza, Guid timbroSicurezza, CancellationToken annullamento);

    /// <summary>Cambia la password dell'account (richiede quella attuale) e invalida le sessioni esistenti.</summary>
    Task<EsitoCambioPassword> CambiaPasswordAsync(
        Guid idUtenza,
        string? passwordAttuale,
        string? nuovaPassword,
        CancellationToken annullamento);
}
