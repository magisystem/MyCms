using DvCms.Infrastructure;
using DvCms.Infrastructure.Persistenza;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DvCms.IntegrationTests.Supporto;

/// <summary>
/// Crea un fornitore di servizi con la stessa registrazione dell'applicazione, su un database di prova
/// con le migrazioni già applicate. Il servizio in background dei testi statici non viene avviato.
/// </summary>
public static class ServiziProva
{
    public static async Task<ServiceProvider> CreaConMigrazioniAsync(
        string connessione,
        IDictionary<string, string?>? impostazioniAggiuntive = null,
        CancellationToken annullamento = default)
    {
        var impostazioni = new Dictionary<string, string?>
        {
            ["ConnectionStrings:Predefinita"] = connessione,
            ["Sicurezza:Password:FattoreLavoroBcrypt"] = "4",
        };
        if (impostazioniAggiuntive is not null)
        {
            foreach (var coppia in impostazioniAggiuntive)
            {
                impostazioni[coppia.Key] = coppia.Value;
            }
        }

        var configurazione = new ConfigurationBuilder().AddInMemoryCollection(impostazioni).Build();

        var servizi = new ServiceCollection();
        servizi.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Warning));
        servizi.AddSingleton<IConfiguration>(configurazione);
        servizi.AggiungiInfrastruttura(configurazione);

        var fornitore = servizi.BuildServiceProvider();
        await using (var ambito = fornitore.CreateAsyncScope())
        {
            await ambito.ServiceProvider.GetRequiredService<ContestoDatabase>().Database.MigrateAsync(annullamento);
        }

        return fornitore;
    }
}
