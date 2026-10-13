using DvCms.Models.Accessi;
using DvCms.Models.Tenancy;

namespace DvCms.DTO.Piattaforma;

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

    /// <summary>Un servizio esterno (provider email, pagamenti) ha rifiutato la richiesta o non ha risposto come previsto.</summary>
    ServizioEsternoNonRiuscito = 11,

    /// <summary>L'utente ha negato il consenso al provider, oppure il collegamento non corrisponde alla richiesta (stato scaduto o diverso).</summary>
    ConsensoNegato = 12,
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
