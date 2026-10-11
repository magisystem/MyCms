using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace DvCms.Web.Salute;

/// <summary>
/// Endpoint di salute (M6). <c>/health/live</c>: il processo risponde (nessun controllo esterno, per i riavvii automatici).
/// <c>/health/ready</c>: database e archivio pronti (per l'inclusione nel bilanciatore). Si montano come middleware <b>prima</b> della
/// risoluzione del tenant, perché le sonde usano l'indirizzo dell'istanza e non il dominio di un sito. Le risposte non contengono
/// dettagli (né messaggi di errore né nomi di host): solo lo stato di ogni controllo; mai in cache.
/// </summary>
public static class EstensioniSalute
{
    public const string PercorsoVivo = "/health/live";
    public const string PercorsoPronto = "/health/ready";
    private const string EtichettaPronto = "pronto";

    public static IServiceCollection AggiungiSalute(this IServiceCollection servizi)
    {
        servizi.AddHealthChecks()
            .AddCheck<ControlloDatabase>("database", HealthStatus.Unhealthy, [EtichettaPronto], TimeSpan.FromSeconds(5))
            .AddCheck<ControlloArchivio>("archivio", HealthStatus.Unhealthy, [EtichettaPronto], TimeSpan.FromSeconds(5));
        return servizi;
    }

    public static IApplicationBuilder UsaSalute(this IApplicationBuilder applicazione)
    {
        applicazione.UseHealthChecks(PercorsoVivo, new HealthCheckOptions { Predicate = _ => false, ResponseWriter = Scrivi });
        applicazione.UseHealthChecks(PercorsoPronto, new HealthCheckOptions
        {
            Predicate = controllo => controllo.Tags.Contains(EtichettaPronto),
            ResponseWriter = Scrivi,
        });
        return applicazione;
    }

    private static Task Scrivi(HttpContext contesto, HealthReport rapporto)
    {
        contesto.Response.ContentType = "application/json; charset=utf-8";
        contesto.Response.Headers.CacheControl = "no-store";
        var controlli = rapporto.Entries.ToDictionary(voce => voce.Key, voce => voce.Value.Status == HealthStatus.Healthy ? "sano" : "non sano");
        return contesto.Response.WriteAsync(JsonSerializer.Serialize(new { stato = rapporto.Status == HealthStatus.Healthy ? "sano" : "non sano", controlli }), contesto.RequestAborted);
    }
}
