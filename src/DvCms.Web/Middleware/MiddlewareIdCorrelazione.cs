using System.Text.RegularExpressions;
using DvCms.Domain;

namespace DvCms.Web.Middleware;

/// <summary>
/// Assegna a ogni richiesta un identificatore di correlazione: lo prende dall'intestazione <c>X-Correlation-Id</c>
/// solo se ha un formato sicuro (evita l'iniezione di testo nei log), altrimenti ne genera uno nuovo.
/// L'identificatore viene restituito nella risposta, usato come <c>TraceIdentifier</c> e incluso in ogni riga di log
/// della richiesta come <c>IdCorrelazione</c>.
/// </summary>
public sealed partial class MiddlewareIdCorrelazione
{
    public const string NomeIntestazione = "X-Correlation-Id";
    public const string NomeChiaveLog = "IdCorrelazione";

    private readonly RequestDelegate _successivo;
    private readonly ILogger<MiddlewareIdCorrelazione> _logger;
    private readonly TimeProvider _orologio;

    public MiddlewareIdCorrelazione(
        RequestDelegate successivo,
        ILogger<MiddlewareIdCorrelazione> logger,
        TimeProvider orologio)
    {
        _successivo = successivo;
        _logger = logger;
        _orologio = orologio;
    }

    public async Task InvokeAsync(HttpContext contesto)
    {
        ArgumentNullException.ThrowIfNull(contesto);

        var ricevuto = contesto.Request.Headers[NomeIntestazione].ToString();
        var id = FormatoSicuro().IsMatch(ricevuto)
            ? ricevuto
            : Identificativi.Nuovo(_orologio).ToString("N");

        contesto.TraceIdentifier = id;
        contesto.Response.OnStarting(() =>
        {
            contesto.Response.Headers[NomeIntestazione] = id;
            return Task.CompletedTask;
        });

        using (_logger.BeginScope(new Dictionary<string, object> { [NomeChiaveLog] = id }))
        {
            await _successivo(contesto);
        }
    }

    [GeneratedRegex("^[A-Za-z0-9_-]{1,64}$", RegexOptions.CultureInvariant)]
    private static partial Regex FormatoSicuro();
}
