using Microsoft.Extensions.Logging;

namespace DvCms.Application.Localizzazione;

/// <summary>
/// Scrive nell'archivio le chiavi accodate dal localizzatore e aggiorna la cache. Usato dal servizio in background
/// e, per un'elaborazione deterministica, dai test.
/// </summary>
public sealed partial class ElaboratoreTestiMancanti
{
    private readonly CodaTestiMancanti _coda;
    private readonly CacheTestiStatici _cache;
    private readonly IArchivioTestiStatici _archivio;
    private readonly ILogger<ElaboratoreTestiMancanti> _logger;

    public ElaboratoreTestiMancanti(
        CodaTestiMancanti coda,
        CacheTestiStatici cache,
        IArchivioTestiStatici archivio,
        ILogger<ElaboratoreTestiMancanti> logger)
    {
        _coda = coda;
        _cache = cache;
        _archivio = archivio;
        _logger = logger;
    }

    /// <summary>Carica lingue e testi esistenti nella cache.</summary>
    public async Task CaricaCacheAsync(CancellationToken annullamento)
    {
        _cache.ImpostaLingue(await _archivio.CaricaLingueAsync(annullamento));
        _cache.Carica(await _archivio.CaricaTuttiAsync(annullamento));
    }

    /// <summary>Elabora tutte le chiavi attualmente in coda. Restituisce quante ne ha scritte.</summary>
    public async Task<int> ElaboraAsync(CancellationToken annullamento)
    {
        var scritte = 0;
        while (_coda.TentaPrelevare(out var chiave))
        {
            try
            {
                var righe = await _archivio.RegistraChiaveAsync(chiave, annullamento);
                _cache.Carica(righe);
                scritte++;
            }
            catch (Exception eccezione) when (eccezione is not OperationCanceledException)
            {
                RegistraErrore(_logger, eccezione);
            }
            finally
            {
                _coda.Completata(chiave);
            }
        }

        return scritte;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Scrittura di un testo statico mancante non riuscita: la chiave verrà riprovata alla prossima richiesta")]
    private static partial void RegistraErrore(ILogger logger, Exception eccezione);
}
