namespace DvCms.Web.Configurazione;

/// <summary>
/// File di configurazione locale, <b>mai versionato</b>: <c>appsettings.Local.json</c> accanto agli altri <c>appsettings</c>. Ci vanno la
/// stringa di connessione e gli altri valori dell'installazione (sviluppo sul proprio computer o server di produzione), così non si
/// modificano i file del repository e un nuovo rilascio non li sovrascrive (il file non fa parte del pacchetto pubblicato).
/// </summary>
public static class ConfigurazioneLocale
{
    public const string NomeFile = "appsettings.Local.json";

    /// <summary>
    /// Aggiunge il file (facoltativo) dopo <c>appsettings.json</c> e <c>appsettings.{Ambiente}.json</c>, quindi lo fa prevalere su di essi.
    /// Variabili d'ambiente e argomenti della riga di comando restano più forti del file: si riaggiungono dopo di esso.
    /// </summary>
    public static void Aggiungi(ConfigurationManager configurazione, string[] argomenti)
    {
        ArgumentNullException.ThrowIfNull(configurazione);
        ArgumentNullException.ThrowIfNull(argomenti);

        configurazione.AddJsonFile(NomeFile, optional: true, reloadOnChange: true);
        configurazione.AddEnvironmentVariables();
        if (argomenti.Length > 0)
        {
            configurazione.AddCommandLine(argomenti);
        }
    }
}
