using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

namespace DvCms.Web.Middleware;

/// <summary>
/// Intestazioni inoltrate (<c>X-Forwarded-*</c>) accettate solo dai proxy configurati esplicitamente in
/// <c>ProxyAttendibili:Indirizzi</c> (indirizzi IP) e <c>ProxyAttendibili:Reti</c> (reti in notazione CIDR).
/// Senza proxy configurati le intestazioni sono ignorate del tutto.
/// </summary>
public static class ConfigurazioneProxy
{
    public static bool Aggiungi(IServiceCollection servizi, IConfiguration configurazione)
    {
        ArgumentNullException.ThrowIfNull(servizi);
        ArgumentNullException.ThrowIfNull(configurazione);

        var sezione = configurazione.GetSection("ProxyAttendibili");
        var indirizzi = sezione.GetSection("Indirizzi").Get<string[]>() ?? [];
        var reti = sezione.GetSection("Reti").Get<string[]>() ?? [];
        if (indirizzi.Length == 0 && reti.Length == 0)
        {
            return false;
        }

        // Un valore non valido fa fallire subito l'avvio: meglio che ignorare in silenzio una configurazione di sicurezza.
        var indirizziValidi = indirizzi.Select(valore =>
            IPAddress.TryParse(valore, out var indirizzo)
                ? indirizzo
                : throw new InvalidOperationException($"ProxyAttendibili:Indirizzi contiene un indirizzo IP non valido: '{valore}'.")).ToList();
        var retiValide = reti.Select(valore =>
            System.Net.IPNetwork.TryParse(valore, out var rete)
                ? rete
                : throw new InvalidOperationException($"ProxyAttendibili:Reti contiene una rete non valida (attesa notazione CIDR): '{valore}'.")).ToList();

        servizi.Configure<ForwardedHeadersOptions>(opzioni =>
        {
            opzioni.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
            opzioni.ForwardLimit = 1;
            opzioni.KnownProxies.Clear();
            opzioni.KnownIPNetworks.Clear();

            foreach (var indirizzo in indirizziValidi)
            {
                opzioni.KnownProxies.Add(indirizzo);
            }

            foreach (var rete in retiValide)
            {
                opzioni.KnownIPNetworks.Add(rete);
            }
        });

        return true;
    }
}
