using DvCms.Contracts.Accessi;
using DvCms.Contracts.Tenancy;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace DvCms.Web.Accessi;

/// <summary>
/// A ogni richiesta verifica che la sessione sia ancora valida: account attivo, <c>TimbroSicurezza</c> invariato e
/// account ammesso sul tenant dell'host. Cambio password, disabilitazione o un host diverso invalidano la sessione.
/// </summary>
public static class ValidatoreSessioneAdmin
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

        // Il principal si ricostruisce ogni volta dai dati aggiornati (ruolo, lingua, obbligo di cambio password).
        contesto.ReplacePrincipal(PrincipalSessione.Crea(identita, contestoTenant.IdTenant));
    }

    private static async Task Rifiuta(CookieValidatePrincipalContext contesto)
    {
        contesto.RejectPrincipal();
        await contesto.HttpContext.SignOutAsync(Schemi.Admin);
    }
}
