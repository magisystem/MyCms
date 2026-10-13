using DvCms.Contracts.Email;
using DvCms.DTO.Email;
using DvCms.Models.Email;

namespace DvCms.Infrastructure.Email;

/// <summary>Canale SendGrid (API v3 <c>mail/send</c>, chiave API). Nessuna libreria: è una sola richiesta HTTP.</summary>
public sealed class AdapterSendGrid : IAdapterEmail
{
    private const string Indirizzo = "https://api.sendgrid.com/v3/mail/send";

    private readonly IHttpClientFactory _fabbrica;

    public AdapterSendGrid(IHttpClientFactory fabbrica)
    {
        _fabbrica = fabbrica;
    }

    public ProviderEmail Provider => ProviderEmail.SendGrid;

    public async Task InviaAsync(ImpostazioniEmail impostazioni, MessaggioEmail messaggio, CancellationToken annullamento)
    {
        ArgumentNullException.ThrowIfNull(impostazioni);
        ArgumentNullException.ThrowIfNull(messaggio);
        ComunicazioneEmail.ControllaMessaggio(messaggio);

        var contenuti = new List<object> { new { type = "text/plain", value = messaggio.CorpoTesto } };
        if (!string.IsNullOrEmpty(messaggio.CorpoHtml))
        {
            contenuti.Add(new { type = "text/html", value = messaggio.CorpoHtml });
        }

        var corpo = new
        {
            personalizations = new[] { new { to = new[] { Persona(messaggio.Destinatario, messaggio.NomeDestinatario) } } },
            from = Persona(impostazioni.Valore("IndirizzoMittente")!, impostazioni.Valore("NomeMittente")),
            subject = messaggio.Oggetto,
            content = contenuti,
        };

        using var richiesta = ComunicazioneEmail.ConBearer(HttpMethod.Post, Indirizzo, impostazioni.Valore("ChiaveApi")!, ComunicazioneEmail.Json(corpo));
        using var risposta = await ComunicazioneEmail.InviaAsync(_fabbrica.CreateClient(ComunicazioneEmail.NomeClientHttp), "SendGrid", richiesta, annullamento);
    }

    private static Dictionary<string, string> Persona(string indirizzo, string? nome)
    {
        var persona = new Dictionary<string, string> { ["email"] = indirizzo };
        if (!string.IsNullOrWhiteSpace(nome))
        {
            persona["name"] = nome;
        }

        return persona;
    }
}
