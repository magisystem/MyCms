using DvCms.Models.Accessi;

namespace DvCms.DTO.Accessi;

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
