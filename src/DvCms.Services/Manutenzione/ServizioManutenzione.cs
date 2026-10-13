using DvCms.DTO.Manutenzione;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DvCms.Services.Manutenzione;

/// <summary>Esegue la manutenzione dei dati a intervalli regolari (G-001). Si disattiva con <c>Manutenzione:Abilitata=false</c>.</summary>
public sealed partial class ServizioManutenzione : BackgroundService
{
    private readonly ManutenzioneDati _manutenzione;
    private readonly OpzioniManutenzione _opzioni;
    private readonly ILogger<ServizioManutenzione> _logger;

    public ServizioManutenzione(ManutenzioneDati manutenzione, OpzioniManutenzione opzioni, ILogger<ServizioManutenzione> logger)
    {
        _manutenzione = manutenzione;
        _opzioni = opzioni;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_opzioni.Abilitata)
        {
            return;
        }

        // Un primo passaggio dopo qualche minuto dall'avvio (non subito: l'avvio ha altro da fare), poi a intervalli regolari.
        var attesa = TimeSpan.FromMinutes(2);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(attesa, stoppingToken);
                await _manutenzione.EseguiAsync(stoppingToken);
                attesa = _opzioni.Intervallo;
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception eccezione)
            {
                RegistraErrore(_logger, eccezione);
                attesa = _opzioni.Intervallo;
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Manutenzione dei dati: errore, nuovo tentativo al prossimo intervallo")]
    private static partial void RegistraErrore(ILogger logger, Exception eccezione);
}
