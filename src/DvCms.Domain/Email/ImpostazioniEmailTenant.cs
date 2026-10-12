namespace DvCms.Domain.Email;

/// <summary>Canale di invio delle email di un tenant (ADR-0015). Ogni tenant ne usa uno solo.</summary>
public enum ProviderEmail
{
    Smtp = 1,
    Graph = 2,
    Gmail = 3,
    SendGrid = 4,

    /// <summary>Microsoft 365 con consenso dell'utente (OAuth 2.0 delegato): l'app OAuth è del tenant, gestita dal suo <c>TenantAdmin</c> (ADR-0021).</summary>
    MicrosoftOAuth = 5,

    /// <summary>Google Mail con consenso dell'utente (OAuth 2.0): l'app OAuth è del tenant, gestita dal suo <c>TenantAdmin</c> (ADR-0021).</summary>
    GoogleOAuth = 6,
}

public static class ProviderEmailEstensioni
{
    /// <summary>I canali con consenso OAuth: la loro configurazione spetta solo al <c>TenantAdmin</c>, mai al <c>PlatformAdmin</c>.</summary>
    public static bool UsaOAuth(this ProviderEmail provider) => provider is ProviderEmail.MicrosoftOAuth or ProviderEmail.GoogleOAuth;
}

/// <summary>Stato del collegamento OAuth fra un tenant e la sua casella (ADR-0021).</summary>
public enum StatoConnessioneEmail
{
    /// <summary>Consenso valido: si possono ottenere token.</summary>
    Attiva = 1,

    /// <summary>Il provider ha rifiutato il rinnovo (consenso revocato, password cambiata, token scaduto): serve ricollegare.</summary>
    Revocata = 2,
}

/// <summary>
/// Collegamento OAuth di un tenant alla sua casella Microsoft 365 o Google (ADR-0021). Refresh token e token di accesso stanno
/// solo cifrati con la protezione dei segreti; una riga per tenant.
/// </summary>
public class ConnessioneEmailOAuth
{
    public Guid Id { get; set; }

    public Guid IdTenant { get; set; }

    /// <summary>Canale per cui è stato dato il consenso: se il tenant cambia canale o app, il collegamento non vale più.</summary>
    public ProviderEmail Provider { get; set; }

    /// <summary>Impronta dell'app OAuth (tenant Microsoft e identificativo client) usata al consenso.</summary>
    public string ImprontaApp { get; set; } = string.Empty;

    /// <summary>Indirizzo della casella collegata, come l'ha comunicato il provider: è il mittente delle email.</summary>
    public string Casella { get; set; } = string.Empty;

    public string TokenAggiornamentoProtetto { get; set; } = string.Empty;

    public string? TokenAccessoProtetto { get; set; }

    public DateTime? ScadenzaAccessoUtc { get; set; }

    public StatoConnessioneEmail Stato { get; set; } = StatoConnessioneEmail.Attiva;

    public DateTime CollegataIlUtc { get; set; }

    public Guid CollegataDaIdUtenza { get; set; }

    public DateTime? UltimoRinnovoIlUtc { get; set; }

    /// <summary>Ultimo errore del provider (solo il codice, mai il corpo della risposta).</summary>
    public string? UltimoErrore { get; set; }

    public DateTime? UltimoErroreIlUtc { get; set; }

    public byte[] TokenConcorrenza { get; set; } = [];
}

/// <summary>
/// Impostazioni di invio email di un tenant (ADR-0015): il canale scelto e i suoi valori. I valori non segreti sono in
/// <see cref="ValoriJson"/>; quelli segreti (password, chiavi API, segreti client) solo cifrati in <see cref="SegretiProtetti"/>.
/// </summary>
public class ImpostazioniEmailTenant
{
    public Guid Id { get; set; }

    /// <summary>Un tenant ha al più una configurazione.</summary>
    public Guid IdTenant { get; set; }

    public ProviderEmail Provider { get; set; }

    /// <summary>Valori non segreti del canale, come oggetto JSON nome-valore.</summary>
    public string ValoriJson { get; set; } = "{}";

    /// <summary>Valori segreti del canale (JSON nome-valore) cifrati con la protezione dei segreti; nulli se non ce ne sono.</summary>
    public string? SegretiProtetti { get; set; }

    public DateTime AggiornatoIlUtc { get; set; }

    public Guid? AggiornatoDaIdUtenza { get; set; }

    public byte[] TokenConcorrenza { get; set; } = [];
}
