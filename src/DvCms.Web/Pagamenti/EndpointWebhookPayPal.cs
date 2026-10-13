using System.Text;
using DvCms.Contracts.Negozio;
using DvCms.DTO.Negozio;

namespace DvCms.Web.Pagamenti;

/// <summary>
/// Webhook di PayPal (ADR-0020): <c>POST /webhooks/paypal</c> sul dominio del sito, perché le credenziali sono per sito. È pubblico
/// ma non fa nulla senza una firma verificata dal provider; il corpo è limitato e il contenuto non viene mai riflesso nella risposta.
/// </summary>
public static class EndpointWebhookPayPal
{
    public const string Percorso = "/webhooks/paypal";
    public const int DimensioneMassima = 256 * 1024;

    public static void MapWebhookPayPal(this IEndpointRouteBuilder rotte)
    {
        ArgumentNullException.ThrowIfNull(rotte);
        rotte.MapPost(Percorso, GestisciAsync).AllowAnonymous().DisableAntiforgery();
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

        var intestazioni = contesto.Request.Headers;
        string Intestazione(string nome) => intestazioni.TryGetValue(nome, out var valore) ? valore.ToString() : string.Empty;
        var firma = new IntestazioniWebhook(
            Intestazione("PAYPAL-AUTH-ALGO"),
            Intestazione("PAYPAL-CERT-URL"),
            Intestazione("PAYPAL-TRANSMISSION-ID"),
            Intestazione("PAYPAL-TRANSMISSION-SIG"),
            Intestazione("PAYPAL-TRANSMISSION-TIME"));
        if (new[] { firma.AuthAlgo, firma.CertUrl, firma.TransmissionId, firma.TransmissionSig, firma.TransmissionTime }.Any(string.IsNullOrWhiteSpace))
        {
            return Results.StatusCode(StatusCodes.Status401Unauthorized);
        }

        return await pagamenti.ElaboraWebhookAsync(firma, corpo, annullamento) switch
        {
            EsitoWebhook.FirmaNonValida => Results.StatusCode(StatusCodes.Status401Unauthorized),
            EsitoWebhook.Malformato => Results.StatusCode(StatusCodes.Status400BadRequest),
            EsitoWebhook.NonConfigurato => Results.StatusCode(StatusCodes.Status404NotFound),
            EsitoWebhook.ErroreProvider => Results.StatusCode(StatusCodes.Status503ServiceUnavailable),
            _ => Results.Ok(),
        };
    }
}
