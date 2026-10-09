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
    string CodiceLingua);

/// <summary>Esito dell'accesso: i fallimenti sono sempre indistinguibili dall'esterno (nessuna enumerazione di account).</summary>
public sealed record RisultatoAccesso(bool EsitoPositivo, IdentitaUtenza? Identita)
{
    public static RisultatoAccesso Fallito { get; } = new(false, null);

    public static RisultatoAccesso Riuscito(IdentitaUtenza identita) => new(true, identita);
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
