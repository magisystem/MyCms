using DvCms.Contracts.Accessi;
using DvCms.Contracts.Tenancy;
using DvCms.Models.Accessi;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace DvCms.Web.Accessi;

/// <summary>
/// Come <see cref="ValidatoreSessioneAdmin"/> ma per il cookie dell'area cliente: oltre ad account attivo, timbro di sicurezza e
/// tenant dell'host (<c>Utente</c> e <c>TenantAdmin</c> del tenant dell'host, <c>PlatformAdmin</c> ovunque: ADR-0011). Sul sito pubblico
/// chiunque ha **solo capacità di acquirente**: il principal ha sempre il ruolo <c>Utente</c>, qualunque sia quello reale, e il
/// cookie non vale nulla nell'area di amministrazione (altro schema, altro cookie). Un account disabilitato o con la password
/// cambiata (timbro di sicurezza) perde la sessione.
/// </summary>
public static class ValidatoreSessioneCliente
{
    public static async Task ConvalidaAsync(CookieValidatePrincipalContext contesto)
    {
        ArgumentNullException.ThrowIfNull(contesto);

        var servizi = contesto.HttpContext.RequestServices;
        var contestoTenant = servizi.GetRequiredService<IContestoTenant>();

        if (!contestoTenant.Risolto || !PrincipalSessione.TentaLeggiSessione(contesto.Principal!, out var idUtenza, out var timbro))
        {
            await Rifiuta(contesto);
            return;
        }

        var identita = await servizi.GetRequiredService<IServizioAccesso>()
            .ConvalidaSessioneAsync(idUtenza, timbro, contesto.HttpContext.RequestAborted);
        if (identita is null)
        {
            await Rifiuta(contesto);
            return;
        }

        contesto.ReplacePrincipal(PrincipalSessione.Crea(identita with { Ruolo = RuoloUtenza.Utente }, contestoTenant.IdTenant, Schemi.Cliente));
    }

    private static async Task Rifiuta(CookieValidatePrincipalContext contesto)
    {
        contesto.RejectPrincipal();
        await contesto.HttpContext.SignOutAsync(Schemi.Cliente);
    }
}
