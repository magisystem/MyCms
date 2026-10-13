using System.Runtime.CompilerServices;

namespace DvCms.IntegrationTests.Supporto;

/// <summary>
/// Ogni applicazione di prova avviata dai test osserva file e configurazione: con l'osservatore predefinito (inotify su Linux) la suite, che
/// ne avvia centinaia, esaurisce il limite del sistema (128 istanze per utente) e le ultime richieste rispondono 500. L'osservatore a
/// sondaggio non usa risorse del sistema e per i test basta.
/// </summary>
internal static class InizializzazioneTest
{
    [ModuleInitializer]
    internal static void UsaOsservatoreASondaggio() => Environment.SetEnvironmentVariable("DOTNET_USE_POLLING_FILE_WATCHER", "true");
}
