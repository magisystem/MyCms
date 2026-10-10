using DvCms.Infrastructure;
using DvCms.Infrastructure.Persistenza;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
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
        string ambiente = "Development",
        TimeProvider? orologio = null,
        Action<IServiceCollection>? configura = null,
        CancellationToken annullamento = default)
    {
        var impostazioni = new Dictionary<string, string?>
        {
            ["ConnectionStrings:Predefinita"] = connessione,
            ["Sicurezza:Password:FattoreLavoroBcrypt"] = "4",
            ["Archivio:Percorso"] = Path.Combine(Path.GetTempPath(), "dvcms-prova-" + Guid.NewGuid().ToString("N")),
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
        servizi.AddSingleton<IHostEnvironment>(new AmbienteProva(ambiente));
        if (orologio is not null)
        {
            servizi.AddSingleton(orologio);
        }

        servizi.AggiungiInfrastruttura(configurazione);
        configura?.Invoke(servizi);

        var fornitore = servizi.BuildServiceProvider();
        await using (var ambito = fornitore.CreateAsyncScope())
        {
            await ambito.ServiceProvider.GetRequiredService<ContestoDatabase>().Database.MigrateAsync(annullamento);
        }

        return fornitore;
    }
}

internal sealed class AmbienteProva : IHostEnvironment
{
    public AmbienteProva(string nome)
    {
        EnvironmentName = nome;
    }

    public string EnvironmentName { get; set; }

    public string ApplicationName { get; set; } = "DvCms.Prova";

    public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}

/// <summary>Orologio di prova controllabile.</summary>
public sealed class OrologioProva : TimeProvider
{
    private DateTimeOffset _adesso;

    public OrologioProva(DateTimeOffset? iniziale = null)
    {
        _adesso = iniziale ?? new DateTimeOffset(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);
    }

    public void Avanza(TimeSpan intervallo) => _adesso += intervallo;

    public override DateTimeOffset GetUtcNow() => _adesso;
}
