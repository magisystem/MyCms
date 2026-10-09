using System.Security.Claims;
using DvCms.Application.Tenancy;
using DvCms.Web.Accessi;

namespace DvCms.Web.Middleware;

/// <summary>
/// Se la richiesta non è autenticata, il visitatore è un <c>Guest</c>: un principal anonimo (non autenticato) con il
/// ruolo <c>Guest</c>, sempre legato al tenant dell'host e mai condiviso fra tenant (ADR-0011).
/// </summary>
public sealed class MiddlewareGuest
{
    private readonly RequestDelegate _successivo;

    public MiddlewareGuest(RequestDelegate successivo)
    {
        _successivo = successivo;
    }

    public Task InvokeAsync(HttpContext contesto, IContestoTenant tenant)
    {
        ArgumentNullException.ThrowIfNull(contesto);
        ArgumentNullException.ThrowIfNull(tenant);

        if (contesto.User.Identity?.IsAuthenticated != true && tenant.Risolto)
        {
            // Nessun tipo di autenticazione: l'identità resta "non autenticata", quindi non soddisfa RequireAuthenticatedUser.
            var identita = new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.Role, RuoliGuest.Guest),
                    new Claim(TipiClaim.IdTenant, tenant.IdTenant.ToString()),
                ],
                authenticationType: null);
            contesto.User = new ClaimsPrincipal(identita);
        }

        return _successivo(contesto);
    }
}
