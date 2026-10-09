using DvCms.Application.Tenancy;

namespace DvCms.Web.Middleware;

/// <summary>
/// Risolve il tenant dall'host della richiesta e lo imposta nel contesto della richiesta (ADR-0011): è l'unica fonte
/// del tenant. Host sconosciuti rispondono 404 senza corpo; tenant o sito sospesi rispondono 503.
/// </summary>
public sealed class MiddlewareTenant
{
    public const string NomeChiaveLog = "IdTenant";

    private readonly RequestDelegate _successivo;
    private readonly ILogger<MiddlewareTenant> _logger;

    public MiddlewareTenant(RequestDelegate successivo, ILogger<MiddlewareTenant> logger)
    {
        _successivo = successivo;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext contesto, IRisolutoreTenant risolutore, ContestoTenant contestoTenant)
    {
        ArgumentNullException.ThrowIfNull(contesto);
        ArgumentNullException.ThrowIfNull(risolutore);
        ArgumentNullException.ThrowIfNull(contestoTenant);

        // Solo l'host della richiesta (Request.Host, già corretto dalle intestazioni inoltrate dei proxy attendibili).
        var risultato = await risolutore.RisolviAsync(contesto.Request.Host.Host, contesto.RequestAborted);

        switch (risultato.Esito)
        {
            case EsitoRisoluzioneTenant.Risolto when risultato.Dati is not null:
                contestoTenant.Imposta(risultato.Dati);
                using (_logger.BeginScope(new Dictionary<string, object> { [NomeChiaveLog] = risultato.Dati.IdTenant }))
                {
                    await _successivo(contesto);
                }

                break;
            case EsitoRisoluzioneTenant.NonDisponibile:
                contesto.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                break;
            default:
                contesto.Response.StatusCode = StatusCodes.Status404NotFound;
                break;
        }
    }
}
