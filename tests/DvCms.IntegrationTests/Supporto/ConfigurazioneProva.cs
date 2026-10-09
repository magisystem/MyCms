using Xunit;

namespace DvCms.IntegrationTests.Supporto;

/// <summary>
/// Accesso alla configurazione dei test di integrazione. I test sul database usano un'istanza reale di SQL Server
/// indicata dalla variabile d'ambiente <c>DVCMS_CONNESSIONE_TEST</c> (stringa di connessione a un server con permesso
/// di creare ed eliminare database). Ogni test crea un proprio database temporaneo.
/// </summary>
public static class ConfigurazioneProva
{
    public const string NomeVariabileConnessione = "DVCMS_CONNESSIONE_TEST";

    /// <summary>
    /// Restituisce la stringa di connessione al server di prova. Se manca, in locale il test risulta saltato
    /// (mai "passato"); in CI (variabile <c>CI=true</c>) il test fallisce, perché la CI deve sempre provare il database.
    /// </summary>
    public static string RichiediConnessioneServer()
    {
        var connessione = Environment.GetEnvironmentVariable(NomeVariabileConnessione);
        if (!string.IsNullOrWhiteSpace(connessione))
        {
            return connessione;
        }

        var messaggio = $"Variabile d'ambiente {NomeVariabileConnessione} non impostata: serve un SQL Server di prova.";
        if (string.Equals(Environment.GetEnvironmentVariable("CI"), "true", StringComparison.OrdinalIgnoreCase))
        {
            Assert.Fail(messaggio);
        }

        Assert.Skip(messaggio);
        return string.Empty;
    }
}
