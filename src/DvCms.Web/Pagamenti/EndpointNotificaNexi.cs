using System.Text;
using DvCms.Contracts.Negozio;
using DvCms.DTO.Negozio;

namespace DvCms.Web.Pagamenti;

/// <summary>
/// Notifiche di Nexi XPay (ADR-0022): <c>POST /webhooks/nexi</c> sul dominio del sito, perché le credenziali sono per sito. È pubblico ma non
/// fa nulla senza il token di sicurezza ricevuto alla creazione del pagamento; il corpo è limitato e il contenuto non viene mai riflesso nella
/// risposta. L'esito vero si rilegge dal gateway.
/// </summary>
public static class EndpointNotificaNexi
{
    public const int DimensioneMassima = 64 * 1024;

    public static void MapNotificaNexi(this IEndpointRouteBuilder rotte)
    {
        ArgumentNullException.ThrowIfNull(rotte);
        rotte.MapPost(Infrastructure.Pagamenti.ServizioPagamenti.PercorsoNotificaNexi, GestisciAsync).AllowAnonymous().DisableAntiforgery();
    }

    private static async Task<IResult> GestisciAsync(HttpContext contesto, IServizioPagamenti pagamenti, CancellationToken annullamento)
    {
        if (contesto.Request.ContentLength is > DimensioneMassima)
        {
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
        }

        string corpo;
        using (var lettore = new StreamReader(contesto.Request.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 4096, leaveOpen: true))
        {
            var buffer = new char[DimensioneMassima + 1];
            var letti = await lettore.ReadBlockAsync(buffer, annullamento);
            if (letti > DimensioneMassima)
            {
                return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
            }

            corpo = new string(buffer, 0, letti);
        }

        return await pagamenti.ElaboraNotificaNexiAsync(corpo, annullamento) switch
        {
            EsitoWebhook.FirmaNonValida => Results.StatusCode(StatusCodes.Status401Unauthorized),
            EsitoWebhook.Malformato => Results.StatusCode(StatusCodes.Status400BadRequest),
            EsitoWebhook.NonConfigurato => Results.StatusCode(StatusCodes.Status404NotFound),
            EsitoWebhook.ErroreProvider => Results.StatusCode(StatusCodes.Status503ServiceUnavailable),
            _ => Results.Ok(),
        };
    }
}
