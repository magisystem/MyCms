using System.Diagnostics.Metrics;

namespace DvCms.Infrastructure.Cache;

/// <summary>
/// Metriche della cache dei contenuti pubblici (M6), sul meter <c>DvCms.Cache</c>: <c>dvcms.cache.accessi</c> con l'esito
/// (<c>trovato</c>, <c>mancato</c>, <c>disabilitata</c>) e <c>dvcms.cache.invalidazioni</c>. Nessuna chiave, sito o contenuto nei tag.
/// </summary>
public static class MetricheCache
{
    public const string NomeMeter = "DvCms.Cache";

    private static readonly Meter _meter = new(NomeMeter);
    private static readonly Counter<long> _accessi = _meter.CreateCounter<long>("dvcms.cache.accessi", description: "Accessi alla cache dei contenuti pubblici, per esito");
    private static readonly Counter<long> _invalidazioni = _meter.CreateCounter<long>("dvcms.cache.invalidazioni", description: "Invalidazioni della cache di un sito");

    public static void Accesso(string esito) => _accessi.Add(1, new KeyValuePair<string, object?>("esito", esito));

    public static void Invalidazione() => _invalidazioni.Add(1);
}
