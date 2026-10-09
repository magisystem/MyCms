using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DvCms.Infrastructure.Outbox;

/// <summary>
/// Worker semplice dell'outbox (C-007): a intervalli regolari elabora lotti di messaggi finché ce ne sono.
/// Può essere disattivato con <c>Outbox:Abilitato=false</c> (ad esempio nei test).
/// </summary>
public sealed partial class ServizioOutbox : BackgroundService
{
    private readonly ElaboratoreOutbox _elaboratore;
    private readonly OpzioniOutbox _opzioni;
    private readonly ILogger<ServizioOutbox> _logger;

    public ServizioOutbox(ElaboratoreOutbox elaboratore, OpzioniOutbox opzioni, ILogger<ServizioOutbox> logger)
    {
        _elaboratore = elaboratore;
        _opzioni = opzioni;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_opzioni.Abilitato)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                int elaborati;
                do
                {
                    elaborati = await _elaboratore.ElaboraLottoAsync(stoppingToken);
                }
                while (elaborati > 0 && !stoppingToken.IsCancellationRequested);

                await Task.Delay(_opzioni.Intervallo, stoppingToken);
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
                    await Task.Delay(_opzioni.Intervallo, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Worker dell'outbox: errore, nuovo tentativo fra poco")]
    private static partial void RegistraErrore(ILogger logger, Exception eccezione);
}
