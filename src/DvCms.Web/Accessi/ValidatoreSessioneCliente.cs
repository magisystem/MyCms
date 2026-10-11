using DvCms.Application.Accessi;
using DvCms.Application.Tenancy;
using DvCms.Domain.Accessi;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace DvCms.Web.Accessi;

/// <summary>
/// Come <see cref="ValidatoreSessioneAdmin"/> ma per il cookie dell'area cliente: oltre ad account attivo, timbro di sicurezza e
/// tenant dell'host, il ruolo deve essere <c>Utente</c>. Un account che diventa amministratore (o viene disabilitato) perde la sessione.
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
        if (identita is null || identita.Ruolo != RuoloUtenza.Utente)
        {
            await Rifiuta(contesto);
            return;
        }

        contesto.ReplacePrincipal(PrincipalSessione.Crea(identita, contestoTenant.IdTenant, Schemi.Cliente));
    }

    private static async Task Rifiuta(CookieValidatePrincipalContext contesto)
    {
        contesto.RejectPrincipal();
        await contesto.HttpContext.SignOutAsync(Schemi.Cliente);
    }
}
