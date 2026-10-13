using DvCms.Core.Localizzazione;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DvCms.Infrastructure.Localizzazione;

/// <summary>
/// Servizio in background dei testi statici: carica la cache all'avvio (riprovando se il database non è ancora
/// raggiungibile; nel frattempo i testi italiani di base restano disponibili) e poi scrive le chiavi mancanti.
/// </summary>
public sealed partial class ServizioTestiStatici : BackgroundService
{
    private static readonly TimeSpan _attesaMassimaTraTentativi = TimeSpan.FromSeconds(30);

    private readonly ElaboratoreTestiMancanti _elaboratore;
    private readonly CodaTestiMancanti _coda;
    private readonly ILogger<ServizioTestiStatici> _logger;

    public ServizioTestiStatici(
        ElaboratoreTestiMancanti elaboratore,
        CodaTestiMancanti coda,
        ILogger<ServizioTestiStatici> logger)
    {
        _elaboratore = elaboratore;
        _coda = coda;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var attesa = TimeSpan.FromSeconds(5);
        var caricata = false;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!caricata)
                {
                    await _elaboratore.CaricaCacheAsync(stoppingToken);
                    caricata = true;
                    RegistraCaricata(_logger);
                }

                await _coda.AttendiAsync(stoppingToken);
                await _elaboratore.ElaboraAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception eccezione)
            {
                RegistraErrore(_logger, eccezione);
                try
                {
                    await Task.Delay(attesa, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                attesa = TimeSpan.FromTicks(Math.Min(attesa.Ticks * 2, _attesaMassimaTraTentativi.Ticks));
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Testi statici caricati in cache")]
    private static partial void RegistraCaricata(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Servizio dei testi statici: database non disponibile, nuovo tentativo fra poco (i testi italiani di base restano usabili)")]
    private static partial void RegistraErrore(ILogger logger, Exception eccezione);
}
