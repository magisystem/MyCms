using DvCms.Domain.Accessi;
using DvCms.Domain.Tenancy;

namespace DvCms.Application.Piattaforma;

/// <summary>Codice di esito delle operazioni del menu "Platform".</summary>
public enum CodiceEsito
{
    Riuscito = 1,

    /// <summary>L'attore non è un <see cref="RuoloUtenza.PlatformAdmin"/> attivo: operazione negata e registrata nell'audit.</summary>
    NonAutorizzato = 2,

    NonTrovato = 3,

    DatiNonValidi = 4,

    /// <summary>Host o email già in uso.</summary>
    GiaPresente = 5,

    /// <summary>Operazione vietata dalle regole (ad esempio sospendere il tenant primario).</summary>
    NonConsentito = 6,

    /// <summary>Il dato è stato modificato da qualcun altro nel frattempo.</summary>
    Conflitto = 7,

    /// <summary>Esiste già un'altra pagina iniziale per quel sito e quella lingua.</summary>
    PaginaInizialeEsistente = 8,

    /// <summary>La chiave di ambiente per cifrare i segreti (<c>Sicurezza:ChiaveSegreti</c>) manca o non è valida.</summary>
    ChiaveSegretiMancante = 9,

    /// <summary>Il tenant non ha ancora configurato il canale email (serve per gli inviti).</summary>
    EmailNonConfigurata = 10,
}

public sealed record EsitoOperazione(CodiceEsito Codice, Guid? Id = null, IReadOnlyList<string>? CampiNonValidi = null)
{
    public bool Riuscito => Codice == CodiceEsito.Riuscito;
}

/// <summary>Schema (e porta, se non standard) con cui costruire il link dell'invito; l'host è quello del dominio verificato del tenant.</summary>
public sealed record DatiLinkInvito(string Schema, int? Porta);

/// <summary>Esito della creazione/elevazione di un <c>TenantAdmin</c>; la password temporanea è mostrata una sola volta.</summary>
public sealed record EsitoNuovoAmministratore(CodiceEsito Codice, Guid? IdUtenza, string? PasswordTemporanea)
{
    public bool Riuscito => Codice == CodiceEsito.Riuscito;
}

public sealed record DatiNuovoTenant(string? Nome, string? NomeSito, string? FusoOrario, string? Host, bool DominioVerificato);

public sealed record RiepilogoTenant(
    Guid Id,
    string Nome,
    StatoTenant Stato,
    bool Primario,
    int NumeroSiti,
    int NumeroAmministratori,
    DateTime CreatoIlUtc);

public sealed record DettaglioDominio(
    Guid Id,
    string Host,
    bool Attivo,
    bool SoloSviluppo,
    DateTime? VerificatoIlUtc,
    string? VerificatoDa);

public sealed record LinguaDelSito(string Codice, string Nome, bool Predefinita);

public sealed record DettaglioSito(
    Guid Id,
    string Nome,
    string FusoOrario,
    StatoSito Stato,
    IReadOnlyList<DettaglioDominio> Domini,
    IReadOnlyList<LinguaDelSito> Lingue,
    bool CommercioAttivo = false,
    bool RegistrazioneAcquirentiAbilitata = false);

public sealed record DettaglioAccount(
    Guid Id,
    string Email,
    string NomeVisualizzato,
    RuoloUtenza Ruolo,
    StatoUtenza Stato,
    bool DeveCambiarePassword,
    DateTime? UltimoAccessoIlUtc,
    bool DueFattoriAttivi = false);

public sealed record DettaglioTenant(
    Guid Id,
    string Nome,
    StatoTenant Stato,
    bool Primario,
    DateTime CreatoIlUtc,
    IReadOnlyList<DettaglioSito> Siti,
    IReadOnlyList<DettaglioAccount> Account,
    IReadOnlyList<LinguaDelSito> LingueDisponibili,
    bool EmailConfigurata = false);

/// <summary>
/// Operazioni riservate al <see cref="RuoloUtenza.PlatformAdmin"/> (ADR-0014). Ogni metodo verifica sul database che
/// l'attore sia un <c>PlatformAdmin</c> attivo (la verifica non si fida del ruolo dichiarato dal client o dalla sessione)
/// e registra l'operazione in <c>RegistroAudit</c>. Le letture non autorizzate lanciano <see cref="UnauthorizedAccessException"/>;
/// le scritture non autorizzate restituiscono <see cref="CodiceEsito.NonAutorizzato"/> e registrano il tentativo.
/// </summary>
public interface IServizioPiattaforma
{
    Task<IReadOnlyList<RiepilogoTenant>> ElencaTenantAsync(Guid idAttore, CancellationToken annullamento);

    Task<DettaglioTenant?> LeggiTenantAsync(Guid idAttore, Guid idTenant, CancellationToken annullamento);

    Task<EsitoOperazione> CreaTenantAsync(Guid idAttore, DatiNuovoTenant dati, CancellationToken annullamento);

    Task<EsitoOperazione> RinominaTenantAsync(Guid idAttore, Guid idTenant, string? nome, CancellationToken annullamento);

    Task<EsitoOperazione> ImpostaTenantSospesoAsync(Guid idAttore, Guid idTenant, bool sospeso, CancellationToken annullamento);

    Task<EsitoOperazione> CreaSitoAsync(Guid idAttore, Guid idTenant, string? nome, string? fusoOrario, CancellationToken annullamento);

    Task<EsitoOperazione> ModificaSitoAsync(
        Guid idAttore,
        Guid idTenant,
        Guid idSito,
        string? nome,
        string? fusoOrario,
        bool attivo,
        CancellationToken annullamento);

    /// <summary>
    /// Attiva o disattiva il negozio del sito. Disattivarlo spegne anche la registrazione degli acquirenti (che ha senso solo con il
    /// negozio attivo). I dati del catalogo non si toccano.
    /// </summary>
    Task<EsitoOperazione> ImpostaCommercioAsync(Guid idAttore, Guid idTenant, Guid idSito, bool attivo, CancellationToken annullamento);

    /// <summary>
    /// Abilita o disabilita la registrazione degli acquirenti del sito. Abilitarla richiede il negozio attivo (altrimenti
    /// <c>NonConsentito</c>); gli account già registrati restano.
    /// </summary>
    Task<EsitoOperazione> ImpostaRegistrazioneAcquirentiAsync(Guid idAttore, Guid idTenant, Guid idSito, bool abilitata, CancellationToken annullamento);

    Task<EsitoOperazione> AggiungiDominioAsync(
        Guid idAttore,
        Guid idTenant,
        Guid idSito,
        string? host,
        bool verificato,
        CancellationToken annullamento);

    /// <summary>Abilita una lingua (attiva nella piattaforma) per il sito: le sue pagine avranno il prefisso di lingua nell'URL.</summary>
    Task<EsitoOperazione> AbilitaLinguaAsync(Guid idAttore, Guid idTenant, Guid idSito, string? codiceLingua, CancellationToken annullamento);

    /// <summary>
    /// Disabilita una lingua del sito. Non si può disabilitare la lingua predefinita né una lingua che ha ancora pagine
    /// (le ssposterebbero nel nulla): prima si eliminano o si portano altrove.
    /// </summary>
    Task<EsitoOperazione> DisabilitaLinguaAsync(Guid idAttore, Guid idTenant, Guid idSito, string? codiceLingua, CancellationToken annullamento);

    /// <summary>
    /// Cambia la lingua predefinita (quella senza prefisso nell'URL). Cambia gli indirizzi di tutte le pagine, quindi è
    /// consentito solo finché il sito non ha pagine pubblicate.
    /// </summary>
    Task<EsitoOperazione> ImpostaLinguaPredefinitaAsync(Guid idAttore, Guid idTenant, Guid idSito, string? codiceLingua, CancellationToken annullamento);

    /// <summary>Registra la verifica (manuale) del possesso del dominio: chi e quando.</summary>
    Task<EsitoOperazione> VerificaDominioAsync(Guid idAttore, Guid idTenant, Guid idDominio, CancellationToken annullamento);

    Task<EsitoOperazione> ImpostaDominioAttivoAsync(Guid idAttore, Guid idTenant, Guid idDominio, bool attivo, CancellationToken annullamento);

    /// <summary>Crea un <c>TenantAdmin</c> con password temporanea (cambio obbligatorio al primo accesso).</summary>
    Task<EsitoNuovoAmministratore> CreaTenantAdminAsync(
        Guid idAttore,
        Guid idTenant,
        string? email,
        string? nomeVisualizzato,
        CancellationToken annullamento);

    /// <summary>
    /// Crea un <c>TenantAdmin</c> e gli invia un invito via email: nessuna password viene comunicata, l'invitato sceglie la sua
    /// aprendo un link monouso sul dominio del tenant. Richiede il canale email del tenant configurato.
    /// </summary>
    Task<EsitoOperazione> CreaTenantAdminConInvitoAsync(
        Guid idAttore,
        Guid idTenant,
        string? email,
        string? nomeVisualizzato,
        DatiLinkInvito link,
        CancellationToken annullamento);

    /// <summary>
    /// Invia di nuovo l'invito a un <c>TenantAdmin</c> che non ha ancora scelto la propria password; i link precedenti si annullano.
    /// </summary>
    Task<EsitoOperazione> InviaInvitoAsync(Guid idAttore, Guid idTenant, Guid idUtenza, DatiLinkInvito link, CancellationToken annullamento);

    /// <summary>Eleva un account <see cref="RuoloUtenza.Utente"/> del tenant a <see cref="RuoloUtenza.TenantAdmin"/>.</summary>
    Task<EsitoOperazione> ElevaATenantAdminAsync(Guid idAttore, Guid idTenant, Guid idUtenza, CancellationToken annullamento);

    /// <summary>Disabilita o riabilita un account del tenant (mai un <c>PlatformAdmin</c>); la disabilitazione invalida le sessioni.</summary>
    Task<EsitoOperazione> ImpostaUtenzaAttivaAsync(Guid idAttore, Guid idTenant, Guid idUtenza, bool attiva, CancellationToken annullamento);

    /// <summary>
    /// Dispositivo perso: azzera la 2FA di un <c>TenantAdmin</c> del tenant (mai di un <c>PlatformAdmin</c>), cancella i suoi codici di
    /// recupero e invalida le sessioni. Al successivo accesso dovrà configurarla di nuovo (ADR-0019).
    /// </summary>
    Task<EsitoOperazione> ReimpostaDueFattoriAsync(Guid idAttore, Guid idTenant, Guid idUtenza, CancellationToken annullamento);
}
