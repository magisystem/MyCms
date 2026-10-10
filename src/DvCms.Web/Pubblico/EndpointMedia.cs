using DvCms.Application.Media;
using Microsoft.Net.Http.Headers;

namespace DvCms.Web.Pubblico;

/// <summary>
/// <c>/media/{id}</c>: le risorse pubbliche del sito dell'host (D-003). Il tenant arriva dall'host come per ogni richiesta, quindi
/// una risorsa di un altro tenant o sito, privata, in quarantena o inesistente risponde sempre 404 (indistinguibili). Il tipo
/// è quello rilevato dal server, mai quello dichiarato dal client; il file non è mai interpretato come contenuto attivo.
/// </summary>
public static class EndpointMedia
{
    public static IEndpointRouteBuilder MapMedia(this IEndpointRouteBuilder rotte)
    {
        ArgumentNullException.ThrowIfNull(rotte);

        string[] metodi = [HttpMethods.Get, HttpMethods.Head];
        rotte.MapMethods("/media/{id:guid}", metodi, async (Guid id, HttpContext contesto, IServizioMedia media) =>
        {
            var contenuto = await media.ApriPubblicaAsync(id, contesto.RequestAborted);
            if (contenuto is null)
            {
                return Results.NotFound();
            }

            IntestazioniSicure(contesto.Response);
            contesto.Response.Headers.CacheControl = "public, max-age=3600";
            return Results.Stream(
                contenuto.Flusso,
                contenuto.TipoContenuto,
                enableRangeProcessing: true,
                entityTag: new EntityTagHeaderValue($"\"{contenuto.Etag}\""));
        });

        return rotte;
    }

    /// <summary>Intestazioni per servire file caricati dagli utenti: nessun sniffing e nessun contenuto attivo, nemmeno per errore.</summary>
    public static void IntestazioniSicure(HttpResponse risposta)
    {
        ArgumentNullException.ThrowIfNull(risposta);

        risposta.Headers.XContentTypeOptions = "nosniff";
        risposta.Headers["Content-Security-Policy"] = "default-src 'none'; sandbox";
        risposta.Headers["Cross-Origin-Resource-Policy"] = "same-site";
        risposta.Headers.ContentDisposition = "inline";
    }
}
