using DvCms.Domain.Accessi;

namespace DvCms.Application.Accessi;

/// <summary>Identità di un account autenticato, usata per creare e convalidare la sessione.</summary>
public sealed record IdentitaUtenza(
    Guid IdUtenza,
    RuoloUtenza Ruolo,
    Guid? IdTenantAccount,
    string Email,
    string NomeVisualizzato,
    Guid TimbroSicurezza,
    bool DeveCambiarePassword,
    string CodiceLingua,
    bool DueFattoriAttivi = false,
    bool DeveAttivareDueFattori = false);

/// <summary>Password corretta ma serve ancora il secondo fattore: non esiste ancora alcuna sessione.</summary>
public sealed record SfidaSecondoFattore(Guid IdUtenza, Guid TimbroSicurezza);

/// <summary>Esito dell'accesso: i fallimenti sono sempre indistinguibili dall'esterno (nessuna enumerazione di account).</summary>
public sealed record RisultatoAccesso(bool EsitoPositivo, IdentitaUtenza? Identita, SfidaSecondoFattore? Sfida = null)
{
    public static RisultatoAccesso Fallito { get; } = new(false, null);

    public static RisultatoAccesso Riuscito(IdentitaUtenza identita) => new(true, identita);

    /// <summary>Password corretta, secondo fattore richiesto: nessuna identità finché non è verificato.</summary>
    public static RisultatoAccesso SecondoFattoreRichiesto(SfidaSecondoFattore sfida) => new(true, null, sfida);
}

public enum EsitoCambioPassword
{
    Riuscito = 1,
    PasswordAttualeErrata = 2,
    PasswordNonValida = 3,
    NonConsentito = 4,
}

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
