using DvCms.Contracts.Email;
using DvCms.DTO.Email;
using DvCms.Models.Email;

namespace DvCms.Infrastructure.Email;

/// <summary>
/// Canale Microsoft Graph: token con il flusso client credentials (app registrata in Microsoft Entra con il permesso
/// applicativo <c>Mail.Send</c>) e invio con <c>users/{casella}/sendMail</c>. Nessuna libreria: due richieste HTTP.
/// </summary>
public sealed class AdapterGraph : IAdapterEmail
{
    private readonly IHttpClientFactory _fabbrica;

    public AdapterGraph(IHttpClientFactory fabbrica)
    {
        _fabbrica = fabbrica;
    }

    public ProviderEmail Provider => ProviderEmail.Graph;

    public async Task InviaAsync(ImpostazioniEmail impostazioni, MessaggioEmail messaggio, CancellationToken annullamento)
    {
        ArgumentNullException.ThrowIfNull(impostazioni);
        ArgumentNullException.ThrowIfNull(messaggio);
        ComunicazioneEmail.ControllaMessaggio(messaggio);

        var client = _fabbrica.CreateClient(ComunicazioneEmail.NomeClientHttp);
        var token = await OttieniTokenAsync(client, impostazioni, annullamento);

        var html = !string.IsNullOrEmpty(messaggio.CorpoHtml);
        var destinatario = new Dictionary<string, string> { ["address"] = messaggio.Destinatario };
        if (!string.IsNullOrWhiteSpace(messaggio.NomeDestinatario))
        {
            destinatario["name"] = messaggio.NomeDestinatario;
        }

        var corpo = new
        {
            message = new
            {
                subject = messaggio.Oggetto,
                body = new { contentType = html ? "HTML" : "Text", content = html ? messaggio.CorpoHtml : messaggio.CorpoTesto },
                toRecipients = new[] { new { emailAddress = destinatario } },
            },
            saveToSentItems = false,
        };

        var indirizzo = $"https://graph.microsoft.com/v1.0/users/{Uri.EscapeDataString(impostazioni.Valore("CasellaMittente")!)}/sendMail";
        using var richiesta = ComunicazioneEmail.ConBearer(HttpMethod.Post, indirizzo, token, ComunicazioneEmail.Json(corpo));
        using var risposta = await ComunicazioneEmail.InviaAsync(client, "Graph", richiesta, annullamento);
    }

    private static async Task<string> OttieniTokenAsync(HttpClient client, ImpostazioniEmail impostazioni, CancellationToken annullamento)
    {
        var indirizzo = $"https://login.microsoftonline.com/{Uri.EscapeDataString(impostazioni.Valore("TenantMicrosoft")!)}/oauth2/v2.0/token";
        using var richiesta = new HttpRequestMessage(HttpMethod.Post, indirizzo)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = impostazioni.Valore("IdClient")!,
                ["client_secret"] = impostazioni.Valore("SegretoClient")!,
                ["scope"] = "https://graph.microsoft.com/.default",
            }),
        };
        using var risposta = await ComunicazioneEmail.InviaAsync(client, "Graph", richiesta, annullamento);
        return await ComunicazioneEmail.LeggiTokenAsync(risposta, "Graph", annullamento);
    }
}
