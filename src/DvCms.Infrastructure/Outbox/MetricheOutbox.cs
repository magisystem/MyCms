using System.Diagnostics.Metrics;

namespace DvCms.Infrastructure.Outbox;

/// <summary>
/// Metriche dell'outbox (G-001), raccoglibili con OpenTelemetry o <c>dotnet-counters</c> sul meter <c>DvCms.Outbox</c>:
/// messaggi elaborati con l'esito (<c>riuscito</c>, <c>ritentato</c>, <c>fallito</c>) e il tipo. Nessun dato dei messaggi nei tag.
/// </summary>
public static class MetricheOutbox
{
    public const string NomeMeter = "DvCms.Outbox";

    private static readonly Meter _meter = new(NomeMeter);
    private static readonly Counter<long> _elaborati = _meter.CreateCounter<long>("dvcms.outbox.elaborati", description: "Messaggi elaborati, per tipo ed esito");
    private static readonly Counter<long> _ripuliti = _meter.CreateCounter<long>("dvcms.manutenzione.righe_eliminate", description: "Righe scadute eliminate dalla manutenzione, per tabella");

    public static void Elaborato(string tipo, string esito) =>
        _elaborati.Add(1, new KeyValuePair<string, object?>("tipo", tipo), new KeyValuePair<string, object?>("esito", esito));

    public static void Ripulite(string tabella, int righe)
    {
        if (righe > 0)
        {
            _ripuliti.Add(righe, new KeyValuePair<string, object?>("tabella", tabella));
        }
    }
}
