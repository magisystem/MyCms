using DvCms.Contracts.Email;
using DvCms.DTO.Email;
using DvCms.Models.Email;

namespace DvCms.Emails;

/// <summary>
/// Microsoft 365 con consenso dell'utente: <c>POST /me/sendMail</c> con il token delegato (permesso <c>Mail.Send</c>), che invia
/// dalla casella che ha dato il consenso. Il token arriva già pronto in <c>TokenAccesso</c> (lo ottiene <see cref="GestoreAccessoEmailOAuth"/>).
/// </summary>
public sealed class AdapterMicrosoftOAuth : IAdapterEmail
{
    private readonly IHttpClientFactory _fabbrica;

    public AdapterMicrosoftOAuth(IHttpClientFactory fabbrica)
    {
        _fabbrica = fabbrica;
    }

    public ProviderEmail Provider => ProviderEmail.MicrosoftOAuth;

    public async Task InviaAsync(ImpostazioniEmail impostazioni, MessaggioEmail messaggio, CancellationToken annullamento)
    {
        ArgumentNullException.ThrowIfNull(impostazioni);
        ArgumentNullException.ThrowIfNull(messaggio);
        ComunicazioneEmail.ControllaMessaggio(messaggio);

        var token = impostazioni.Valore("TokenAccesso") ?? throw new InvioEmailFallitoException("Microsoft 365: token di accesso mancante.");
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

        var client = _fabbrica.CreateClient(ComunicazioneEmail.NomeClientHttp);
        using var richiesta = ComunicazioneEmail.ConBearer(HttpMethod.Post, "https://graph.microsoft.com/v1.0/me/sendMail", token, ComunicazioneEmail.Json(corpo));
        using var risposta = await ComunicazioneEmail.InviaAsync(client, "Microsoft 365", richiesta, annullamento);
    }
}

/// <summary>
/// Google Mail con consenso dell'utente: messaggio MIME inviato con <c>users/me/messages/send</c> usando il token dell'utente che ha
/// dato il consenso (ambito <c>gmail.send</c>). Il mittente è la casella collegata.
/// </summary>
public sealed class AdapterGoogleOAuth : IAdapterEmail
{
    private const string IndirizzoInvio = "https://gmail.googleapis.com/gmail/v1/users/me/messages/send";

    private readonly IHttpClientFactory _fabbrica;

    public AdapterGoogleOAuth(IHttpClientFactory fabbrica)
    {
        _fabbrica = fabbrica;
    }

    public ProviderEmail Provider => ProviderEmail.GoogleOAuth;

    public async Task InviaAsync(ImpostazioniEmail impostazioni, MessaggioEmail messaggio, CancellationToken annullamento)
    {
        ArgumentNullException.ThrowIfNull(impostazioni);
        ArgumentNullException.ThrowIfNull(messaggio);

        var token = impostazioni.Valore("TokenAccesso") ?? throw new InvioEmailFallitoException("Google: token di accesso mancante.");
        var casella = impostazioni.Valore("Casella") ?? throw new InvioEmailFallitoException("Google: casella mittente mancante.");
        var mime = ComunicazioneEmail.CostruisciMime(casella, null, messaggio);
        using var flusso = new MemoryStream();
        await mime.WriteToAsync(flusso, annullamento);
        var raw = Convert.ToBase64String(flusso.ToArray()).Replace('+', '-').Replace('/', '_').TrimEnd('=');

        var client = _fabbrica.CreateClient(ComunicazioneEmail.NomeClientHttp);
        using var richiesta = ComunicazioneEmail.ConBearer(HttpMethod.Post, IndirizzoInvio, token, ComunicazioneEmail.Json(new { raw }));
        using var risposta = await ComunicazioneEmail.InviaAsync(client, "Google", richiesta, annullamento);
    }
}
