namespace DvCms.Web.Configurazione;

/// <summary>
/// File di configurazione locale, <b>mai versionato</b>: <c>appsettings.Local.json</c> accanto agli altri <c>appsettings</c>. Ci vanno la
/// stringa di connessione e gli altri valori dell'installazione (sviluppo sul proprio computer o server di produzione), così non si
/// modificano i file del repository e un nuovo rilascio non li sovrascrive (il file non fa parte del pacchetto pubblicato).
/// </summary>
public static class ConfigurazioneLocale
{
    public const string NomeFile = "appsettings.Local.json";

    /// <summary>Dati del popolamento di base (versionati, senza segreti); per ambiente: <c>seed.{Ambiente}.json</c>.</summary>
    public const string NomeFileSeed = "seed.json";

    /// <summary>
    /// Aggiunge il file (facoltativo) dopo <c>appsettings.json</c> e <c>appsettings.{Ambiente}.json</c>, quindi lo fa prevalere su di essi.
    /// Variabili d'ambiente e argomenti della riga di comando restano più forti del file: si riaggiungono dopo di esso.
    /// </summary>
    public static void Aggiungi(ConfigurationManager configurazione, string[] argomenti, string? ambiente = null)
    {
        ArgumentNullException.ThrowIfNull(configurazione);
        ArgumentNullException.ThrowIfNull(argomenti);

        // Dati del popolamento (sezione "Seed", ADR-0014) in file dedicati, fuori da appsettings.json; i file locali, le variabili e la riga di comando li battono.
        configurazione.AddJsonFile(NomeFileSeed, optional: true, reloadOnChange: true);
        if (!string.IsNullOrEmpty(ambiente))
        {
            configurazione.AddJsonFile($"seed.{ambiente}.json", optional: true, reloadOnChange: true);
        }

        configurazione.AddJsonFile(NomeFile, optional: true, reloadOnChange: true);
        configurazione.AddEnvironmentVariables();
        if (argomenti.Length > 0)
        {
            configurazione.AddCommandLine(argomenti);
        }
    }
}
