using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using DvCms.Application.Email;
using DvCms.Domain.Email;
using MimeKit;

namespace DvCms.Infrastructure.Email;

/// <summary>
/// Il servizio esterno ha rifiutato l'invio o non ha risposto come previsto. Il messaggio riporta il canale e il codice HTTP:
/// mai il corpo della risposta (potrebbe riprendere indirizzi o segreti inviati).
/// </summary>
public sealed class InvioEmailFallitoException : Exception
{
    public InvioEmailFallitoException()
    {
    }

    public InvioEmailFallitoException(string message)
        : base(message)
    {
    }

    public InvioEmailFallitoException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>Parti comuni agli adapter che parlano con servizi HTTP (Graph, Gmail, SendGrid) e a SMTP.</summary>
internal static class ComunicazioneEmail
{
    public const string NomeClientHttp = "email";

    private static readonly TimeSpan _attesaMassima = TimeSpan.FromSeconds(30);

    /// <summary>Rifiuta intestazioni con a capo (header injection) e costruisce il messaggio MIME.</summary>
    public static MimeMessage CostruisciMime(string indirizzoMittente, string? nomeMittente, MessaggioEmail messaggio)
    {
        ArgumentNullException.ThrowIfNull(messaggio);
        ControllaMessaggio(messaggio);

        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(nomeMittente ?? string.Empty, indirizzoMittente));
        mime.To.Add(new MailboxAddress(messaggio.NomeDestinatario ?? string.Empty, messaggio.Destinatario));
        mime.Subject = messaggio.Oggetto;
        mime.Body = new BodyBuilder { TextBody = messaggio.CorpoTesto, HtmlBody = messaggio.CorpoHtml }.ToMessageBody();
        return mime;
    }

    public static void ControllaMessaggio(MessaggioEmail messaggio)
    {
        if (messaggio.Oggetto.Any(c => c is '\r' or '\n')
            || (messaggio.NomeDestinatario?.Any(c => c is '\r' or '\n') ?? false)
            || messaggio.Destinatario.Any(c => c is '\r' or '\n'))
        {
            throw new ArgumentException("Oggetto, destinatario o nome del destinatario non validi.", nameof(messaggio));
        }
    }

    public static StringContent Json(object contenuto) =>
        new(JsonSerializer.Serialize(contenuto), Encoding.UTF8, "application/json");

    /// <summary>Invia la richiesta con un'attesa massima; se la risposta non è un successo solleva un'eccezione senza corpo né segreti.</summary>
    public static async Task<HttpResponseMessage> InviaAsync(HttpClient client, string canale, HttpRequestMessage richiesta, CancellationToken annullamento)
    {
        using var limite = CancellationTokenSource.CreateLinkedTokenSource(annullamento);
        limite.CancelAfter(_attesaMassima);
        HttpResponseMessage risposta;
        try
        {
            risposta = await client.SendAsync(richiesta, limite.Token);
        }
        catch (HttpRequestException eccezione)
        {
            throw new InvioEmailFallitoException($"{canale}: servizio non raggiungibile.", eccezione);
        }
        catch (OperationCanceledException) when (!annullamento.IsCancellationRequested)
        {
            throw new InvioEmailFallitoException($"{canale}: tempo di attesa scaduto.");
        }

        if (!risposta.IsSuccessStatusCode)
        {
            var codice = (int)risposta.StatusCode;
            risposta.Dispose();
            throw new InvioEmailFallitoException($"{canale}: il servizio ha risposto con HTTP {codice}.");
        }

        return risposta;
    }

    /// <summary>Legge <c>access_token</c> dalla risposta di un endpoint OAuth.</summary>
    public static async Task<string> LeggiTokenAsync(HttpResponseMessage risposta, string canale, CancellationToken annullamento)
    {
        try
        {
            using var documento = await JsonDocument.ParseAsync(await risposta.Content.ReadAsStreamAsync(annullamento), cancellationToken: annullamento);
            if (documento.RootElement.TryGetProperty("access_token", out var token) && token.ValueKind == JsonValueKind.String && token.GetString() is { Length: > 0 } valore)
            {
                return valore;
            }
        }
        catch (JsonException)
        {
            // Risposta non JSON: trattata come mancanza del token.
        }

        throw new InvioEmailFallitoException($"{canale}: risposta di autenticazione non valida.");
    }

    public static HttpRequestMessage ConBearer(HttpMethod metodo, string indirizzo, string token, HttpContent contenuto)
    {
        var richiesta = new HttpRequestMessage(metodo, indirizzo) { Content = contenuto };
        richiesta.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return richiesta;
    }
}
