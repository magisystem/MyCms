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
}

public sealed record EsitoOperazione(CodiceEsito Codice, Guid? Id = null)
{
    public bool Riuscito => Codice == CodiceEsito.Riuscito;
}

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

public sealed record DettaglioSito(
    Guid Id,
    string Nome,
    string FusoOrario,
    StatoSito Stato,
    IReadOnlyList<DettaglioDominio> Domini);

public sealed record DettaglioAccount(
    Guid Id,
    string Email,
    string NomeVisualizzato,
    RuoloUtenza Ruolo,
    StatoUtenza Stato,
    bool DeveCambiarePassword,
    DateTime? UltimoAccessoIlUtc);

public sealed record DettaglioTenant(
    Guid Id,
    string Nome,
    StatoTenant Stato,
    bool Primario,
    DateTime CreatoIlUtc,
    IReadOnlyList<DettaglioSito> Siti,
    IReadOnlyList<DettaglioAccount> Account);

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

    Task<EsitoOperazione> AggiungiDominioAsync(
        Guid idAttore,
        Guid idTenant,
        Guid idSito,
        string? host,
        bool verificato,
        CancellationToken annullamento);

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

    /// <summary>Eleva un account <see cref="RuoloUtenza.Utente"/> del tenant a <see cref="RuoloUtenza.TenantAdmin"/>.</summary>
    Task<EsitoOperazione> ElevaATenantAdminAsync(Guid idAttore, Guid idTenant, Guid idUtenza, CancellationToken annullamento);

    /// <summary>Disabilita o riabilita un account del tenant (mai un <c>PlatformAdmin</c>); la disabilitazione invalida le sessioni.</summary>
    Task<EsitoOperazione> ImpostaUtenzaAttivaAsync(Guid idAttore, Guid idTenant, Guid idUtenza, bool attiva, CancellationToken annullamento);
}
