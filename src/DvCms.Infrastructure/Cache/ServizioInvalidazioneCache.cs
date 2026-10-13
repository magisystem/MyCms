using DvCms.Contracts.Cms;
using DvCms.Infrastructure.Persistenza;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DvCms.Infrastructure.Cache;

/// <summary>
/// Legge a intervalli regolari il registro delle invalidazioni e svuota la cache locale dei siti indicati (G-002): è il modo in
/// cui una modifica fatta da un'istanza raggiunge le altre. All'avvio parte da zero: rileggere righe vecchie (al massimo qualche
/// ora, vedi la manutenzione) svuota una cache ancora vuota, senza conseguenze.
/// </summary>
public sealed partial class ServizioInvalidazioneCache : BackgroundService
{
    private const int DimensioneLettura = 1000;

    private readonly IServiceScopeFactory _fabbricaAmbiti;
    private readonly ICachePubblica _cache;
    private readonly OpzioniCache _opzioni;
    private readonly ILogger<ServizioInvalidazioneCache> _logger;
    private long _ultimoLetto;

    public ServizioInvalidazioneCache(IServiceScopeFactory fabbricaAmbiti, ICachePubblica cache, OpzioniCache opzioni, ILogger<ServizioInvalidazioneCache> logger)
    {
        _fabbricaAmbiti = fabbricaAmbiti;
        _cache = cache;
        _opzioni = opzioni;
        _logger = logger;
    }

    /// <summary>Numero dell'ultima riga letta.</summary>
    public long UltimoLetto => Interlocked.Read(ref _ultimoLetto);

    /// <summary>Legge le nuove righe e invalida i siti indicati; restituisce quante ne ha lette.</summary>
    public async Task<int> LeggiAsync(CancellationToken annullamento)
    {
        var totale = 0;
        while (true)
        {
            await using var ambito = _fabbricaAmbiti.CreateAsyncScope();
            var contesto = ambito.ServiceProvider.GetRequiredService<ContestoDatabase>();
            var dopo = UltimoLetto;
            var righe = await contesto.InvalidazioniCache.IgnoreQueryFilters().AsNoTracking()
                .Where(riga => riga.Id > dopo)
                .OrderBy(riga => riga.Id)
                .Take(DimensioneLettura)
                .Select(riga => new { riga.Id, riga.IdSito })
                .ToListAsync(annullamento);
            if (righe.Count == 0)
            {
                return totale;
            }

            foreach (var idSito in righe.Select(riga => riga.IdSito).Distinct())
            {
                _cache.Invalida(idSito);
            }

            Interlocked.Exchange(ref _ultimoLetto, righe[^1].Id);
            totale += righe.Count;
            if (righe.Count < DimensioneLettura)
            {
                return totale;
            }
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_opzioni.Abilitata)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await LeggiAsync(stoppingToken);
                await Task.Delay(_opzioni.IntervalloLettura, stoppingToken);
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
                    await Task.Delay(_opzioni.IntervalloLettura, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Lettura del registro delle invalidazioni della cache non riuscita, nuovo tentativo fra poco")]
    private static partial void RegistraErrore(ILogger logger, Exception eccezione);
}
