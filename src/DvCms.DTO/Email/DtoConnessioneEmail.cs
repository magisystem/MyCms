using DvCms.DTO.Piattaforma;
using DvCms.Models.Email;

namespace DvCms.DTO.Email;

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
