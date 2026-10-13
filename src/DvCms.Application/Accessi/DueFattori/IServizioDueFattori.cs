using DvCms.Models.Accessi;

namespace DvCms.Application.Accessi.DueFattori;

/// <summary>Chi è obbligato ad avere la 2FA (<c>Sicurezza:DueFattori:Obbligo</c>, ADR-0019).</summary>
public enum ObbligoDueFattori
{
    Nessuno = 0,
    PlatformAdmin = 1,
    Amministratori = 2,
}

public sealed record OpzioniDueFattori(ObbligoDueFattori Obbligo, string Emittente)
{
    public static OpzioniDueFattori Predefinite { get; } = new(ObbligoDueFattori.Amministratori, "DvCms");

    public bool RichiestaPer(RuoloUtenza ruolo) => Obbligo switch
    {
        ObbligoDueFattori.Amministratori => ruolo is RuoloUtenza.PlatformAdmin or RuoloUtenza.TenantAdmin,
        ObbligoDueFattori.PlatformAdmin => ruolo is RuoloUtenza.PlatformAdmin,
        _ => false,
    };
}

/// <summary>Stato della 2FA di un account, per la pagina di gestione.</summary>
public sealed record StatoDueFattori(bool Attivi, bool Obbligatoria, bool ConfigurazioneInCorso, int CodiciRecuperoResidui);

/// <summary>Segreto in attesa di conferma, da mostrare una sola volta (URI e chiave testuale; il QR è derivato dall'URI).</summary>
public sealed record ConfigurazioneDueFattori(string Uri, string ChiaveTestuale);

public enum EsitoDueFattori
{
    Riuscito = 1,

    /// <summary>Codice, password o sfida non validi (nessun dettaglio sulla causa).</summary>
    NonValido = 2,

    /// <summary>La 2FA è già attiva e per l'operazione servono password e codice.</summary>
    RichiedeVerifica = 3,

    /// <summary>Vietato dalle regole (ad esempio disattivare la 2FA quando è obbligatoria).</summary>
    NonConsentito = 4,

    /// <summary>Nessuna configurazione in corso da confermare.</summary>
    NessunaConfigurazione = 5,

    /// <summary>La chiave dei segreti (<c>Sicurezza:ChiaveSegreti</c>) non è configurata: impossibile cifrare il segreto TOTP.</summary>
    ChiaveSegretiMancante = 6,
}

public sealed record RisultatoDueFattori<T>(EsitoDueFattori Esito, T? Valore = default)
{
    public bool Riuscito => Esito == EsitoDueFattori.Riuscito;
}

/// <summary>Attivazione confermata: nuova identità (timbro cambiato) e codici di recupero, da mostrare una sola volta.</summary>
public sealed record AttivazioneDueFattori(IdentitaUtenza Identita, IReadOnlyList<string> CodiciRecupero);

/// <summary>Codici di recupero rigenerati (i precedenti non valgono più), da mostrare una sola volta.</summary>
public sealed record RigenerazioneCodiciRecupero(IReadOnlyList<string> CodiciRecupero);

/// <summary>Gestione e verifica del secondo fattore (ADR-0019). Ogni operazione riverifica account e tenant sul database.</summary>
public interface IServizioDueFattori
{
    /// <summary>
    /// Completa l'accesso dopo la password: verifica un codice TOTP o un codice di recupero per l'account della sfida.
    /// Ogni codice errato conta come tentativo fallito (stesso contatore e stesso blocco della password).
    /// </summary>
    Task<RisultatoAccesso> CompletaAccessoAsync(Guid idUtenza, Guid timbroSicurezza, string? codice, CancellationToken annullamento);

    Task<StatoDueFattori?> LeggiStatoAsync(Guid idUtenza, CancellationToken annullamento);

    /// <summary>
    /// Genera un segreto in attesa di conferma. Se la 2FA è già attiva (cambio di dispositivo) servono password e codice attuali.
    /// </summary>
    Task<RisultatoDueFattori<ConfigurazioneDueFattori>> AvviaConfigurazioneAsync(
        Guid idUtenza,
        string? password,
        string? codice,
        CancellationToken annullamento);

    /// <summary>Conferma il segreto in attesa con un codice generato da esso: attiva la 2FA e genera i codici di recupero.</summary>
    Task<RisultatoDueFattori<AttivazioneDueFattori>> ConfermaAttivazioneAsync(Guid idUtenza, string? codice, CancellationToken annullamento);

    Task<RisultatoDueFattori<RigenerazioneCodiciRecupero>> RigeneraCodiciRecuperoAsync(
        Guid idUtenza,
        string? password,
        string? codice,
        CancellationToken annullamento);

    /// <summary>Disattiva la 2FA (password e codice); vietato se è obbligatoria per il ruolo. Invalida le sessioni.</summary>
    Task<EsitoDueFattori> DisattivaAsync(Guid idUtenza, string? password, string? codice, CancellationToken annullamento);

    /// <summary>
    /// Comando operativo: azzera la 2FA di un <c>PlatformAdmin</c> (per email). Agisce solo su <c>PlatformAdmin</c> e lascia
    /// traccia nell'audit; chi lo esegue ha già accesso al server e al database. Falso se l'account non esiste.
    /// </summary>
    Task<bool> ReimpostaPlatformAdminAsync(string? email, CancellationToken annullamento);
}
