using DvCms.Data.Popolamento;

namespace DvCms.Web.Comandi;

/// <summary>
/// Comando <c>DvCms.Web seed</c>: esegue il popolamento idempotente (ADR-0014) e termina. Se la password del
/// <c>PlatformAdmin</c> non è in configurazione e il terminale è interattivo, la chiede senza mostrarla.
/// </summary>
public static partial class ComandoSeed
{
    public const string Nome = "seed";

    public static async Task<int> EseguiAsync(IServiceProvider servizi, bool consentiRichiestaInterattiva, CancellationToken annullamento = default)
    {
        ArgumentNullException.ThrowIfNull(servizi);

        var logger = servizi.GetRequiredService<ILoggerFactory>().CreateLogger("DvCms.Popolamento");
        try
        {
            await using var ambito = servizi.CreateAsyncScope();
            var configurazione = ambito.ServiceProvider.GetRequiredService<IConfiguration>();
            string? passwordInserita = null;
            if (consentiRichiestaInterattiva
                && string.IsNullOrEmpty(configurazione["Seed:PlatformAdmin:Password"])
                && !Console.IsInputRedirected)
            {
                passwordInserita = LeggiPasswordNascosta();
            }

            var servizio = ambito.ServiceProvider.GetRequiredService<ServizioPopolamento>();
            await servizio.EseguiAsync(passwordInserita, annullamento);
            return 0;
        }
        catch (Exception eccezione)
        {
            RegistraErrore(logger, eccezione.Message);
            return 1;
        }
    }

    private static string LeggiPasswordNascosta()
    {
        Console.Error.Write("Password del PlatformAdmin (se deve essere creato): ");
        var password = new System.Text.StringBuilder();
        while (Console.ReadKey(intercept: true) is var tasto && tasto.Key != ConsoleKey.Enter)
        {
            if (tasto.Key == ConsoleKey.Backspace)
            {
                if (password.Length > 0)
                {
                    password.Length--;
                }
            }
            else if (!char.IsControl(tasto.KeyChar))
            {
                password.Append(tasto.KeyChar);
            }
        }

        Console.Error.WriteLine();
        return password.ToString();
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Popolamento non riuscito: {Motivo}")]
    private static partial void RegistraErrore(ILogger logger, string motivo);
}
