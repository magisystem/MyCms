using DvCms.Application.Piattaforma;
using DvCms.Models.Email;

namespace DvCms.Application.Email;

/// <summary>App OAuth del tenant come si mostra nella UI: il segreto del client non c'è mai, solo il fatto che sia impostato.</summary>
public sealed record AppOAuthVisibile(
    ProviderEmail Provider,
    IReadOnlyDictionary<string, string> Valori,
    bool SegretoImpostato,
    string TokenConcorrenza,
    DateTime AggiornatoIlUtc);

/// <summary>Collegamento alla casella. <see cref="CorrispondeAllaApp"/> è falso se l'app è stata cambiata dopo il consenso.</summary>
public sealed record ConnessioneVisibile(
    StatoConnessioneEmail Stato,
    string Casella,
    DateTime CollegataIlUtc,
    DateTime? UltimoRinnovoIlUtc,
    string? UltimoErrore,
    DateTime? UltimoErroreIlUtc,
    bool CorrispondeAllaApp);

/// <param name="CanaleAttivo">Il canale oggi configurato per il tenant (anche uno gestito dalla piattaforma), se c'è.</param>
public sealed record SchedaConnessioneEmail(
    ProviderEmail? CanaleAttivo,
    AppOAuthVisibile? App,
    ConnessioneVisibile? Connessione,
    IReadOnlyList<ProblemaInvioEmail> Problemi);

/// <summary>Dove mandare il browser per il consenso, e il valore (cifrato) da tenere in un cookie fino al ritorno.</summary>
public sealed record EsitoAvvioCollegamento(CodiceEsito Codice, string? UrlAutorizzazione, string? StatoProtetto)
{
    public bool Riuscito => Codice == CodiceEsito.Riuscito;
}

public sealed record EsitoCollegamento(CodiceEsito Codice, string? Casella)
{
    public bool Riuscito => Codice == CodiceEsito.Riuscito;
}

/// <summary>
/// Canali email con consenso OAuth (Microsoft 365, Google Mail; ADR-0021). L'app OAuth è del tenant e la gestisce solo il suo
/// <c>TenantAdmin</c> (riverificato sul database a ogni operazione, nel tenant dell'host): il <c>PlatformAdmin</c> non la legge né
/// la modifica. Il segreto del client e i token sono in sola scrittura e cifrati a riposo.
/// </summary>
public interface IServizioConnessioneEmail
{
    /// <exception cref="UnauthorizedAccessException">L'attore non è un <c>TenantAdmin</c> attivo del tenant.</exception>
    Task<SchedaConnessioneEmail> LeggiAsync(Guid idAttore, CancellationToken annullamento);

    /// <summary>
    /// Salva l'app OAuth del canale scelto. Un segreto vuoto mantiene quello già salvato (se il canale non cambia). Se cambiano
    /// canale, tenant Microsoft o identificativo client, il collegamento esistente non vale più e si elimina. Sostituire un canale
    /// non OAuth già configurato richiede <paramref name="sostituisciCanale"/>.
    /// </summary>
    Task<EsitoSalvataggioEmail> SalvaAppAsync(
        Guid idAttore,
        ProviderEmail provider,
        IReadOnlyDictionary<string, string> valori,
        string? tokenConcorrenza,
        bool sostituisciCanale,
        CancellationToken annullamento);

    /// <summary>Prepara l'autorizzazione (PKCE e <c>state</c>). <paramref name="urlRitorno"/> è l'indirizzo registrato nell'app.</summary>
    Task<EsitoAvvioCollegamento> AvviaCollegamentoAsync(Guid idAttore, string urlRitorno, CancellationToken annullamento);

    /// <summary>
    /// Conclude il consenso: controlla <c>state</c> e cookie, scambia il codice, legge l'indirizzo della casella e salva il
    /// refresh token. <paramref name="erroreProvider"/> è il parametro <c>error</c> del ritorno, se presente.
    /// </summary>
    Task<EsitoCollegamento> CompletaCollegamentoAsync(
        Guid idAttore,
        string urlRitorno,
        string? codice,
        string? stato,
        string? erroreProvider,
        string? statoProtetto,
        CancellationToken annullamento);

    /// <summary>Revoca il consenso presso il provider (se lo consente) ed elimina il collegamento; l'app resta salvata.</summary>
    Task<EsitoOperazione> ScollegaAsync(Guid idAttore, CancellationToken annullamento);

    /// <summary>Elimina app e collegamento: il tenant resta senza canale email finché il <c>PlatformAdmin</c> non ne configura uno.</summary>
    Task<EsitoOperazione> RimuoviCanaleAsync(Guid idAttore, CancellationToken annullamento);

    /// <summary>Accoda un messaggio di prova: il tenant deve avere un collegamento attivo.</summary>
    Task<EsitoOperazione> AccodaProvaAsync(Guid idAttore, string? destinatario, MessaggioEmail messaggio, CancellationToken annullamento);
}
