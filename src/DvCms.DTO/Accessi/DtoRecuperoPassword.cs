namespace DvCms.DTO.Accessi;

/// <summary>Parametri del recupero password (configurazione <c>Sicurezza:RecuperoPassword:*</c>, valori iniziali di ADR-0011).</summary>
public sealed record OpzioniRecuperoPassword(
    TimeSpan ValiditaCodice,
    int TentativiCodice,
    TimeSpan ValiditaLink,
    int RichiesteOrarieAccount,
    int RichiesteOrarieIp)
{
    public static OpzioniRecuperoPassword Predefinite { get; } = new(TimeSpan.FromMinutes(10), 5, TimeSpan.FromMinutes(30), 3, 10);
}

public enum EsitoVerificaCodice
{
    /// <summary>Codice corretto: la seconda email è stata accodata. Per le richieste non reali non si ottiene mai.</summary>
    Accettato = 1,

    /// <summary>Codice errato, scaduto, troppi tentativi o richiesta sconosciuta: tutti indistinguibili.</summary>
    NonValido = 2,
}

public enum EsitoReimpostazione
{
    Riuscita = 1,
    LinkNonValido = 2,
    PasswordNonValida = 3,
}
