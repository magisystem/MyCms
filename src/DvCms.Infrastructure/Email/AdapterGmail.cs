using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DvCms.Contracts.Email;
using DvCms.DTO.Email;
using DvCms.Models.Email;

namespace DvCms.Infrastructure.Email;

/// <summary>
/// Canale Gmail API con account di servizio e delega a livello di dominio: si firma un JWT (RS256) con la chiave
/// dell'account, lo si scambia con un token per lo scope <c>gmail.send</c> intestato all'utente mittente e si invia il
/// messaggio MIME con <c>users/me/messages/send</c>. L'endpoint del token è fisso (quello indicato nel JSON non si usa).
/// </summary>
public sealed class AdapterGmail : IAdapterEmail
{
    private const string IndirizzoToken = "https://oauth2.googleapis.com/token";
    private const string IndirizzoInvio = "https://gmail.googleapis.com/gmail/v1/users/me/messages/send";
    private const string Scope = "https://www.googleapis.com/auth/gmail.send";

    private readonly IHttpClientFactory _fabbrica;
    private readonly TimeProvider _orologio;

    public AdapterGmail(IHttpClientFactory fabbrica, TimeProvider orologio)
    {
        _fabbrica = fabbrica;
        _orologio = orologio;
    }

    public ProviderEmail Provider => ProviderEmail.Gmail;

    public async Task InviaAsync(ImpostazioniEmail impostazioni, MessaggioEmail messaggio, CancellationToken annullamento)
    {
        ArgumentNullException.ThrowIfNull(impostazioni);
        ArgumentNullException.ThrowIfNull(messaggio);

        var utente = impostazioni.Valore("UtenteMittente")!;
        var mime = ComunicazioneEmail.CostruisciMime(utente, null, messaggio);
        using var flusso = new MemoryStream();
        await mime.WriteToAsync(flusso, annullamento);

        var client = _fabbrica.CreateClient(ComunicazioneEmail.NomeClientHttp);
        var token = await OttieniTokenAsync(client, impostazioni.Valore("CredenzialiJson")!, utente, annullamento);

        using var richiesta = ComunicazioneEmail.ConBearer(HttpMethod.Post, IndirizzoInvio, token, ComunicazioneEmail.Json(new { raw = Base64Url(flusso.ToArray()) }));
        using var risposta = await ComunicazioneEmail.InviaAsync(client, "Gmail", richiesta, annullamento);
    }

    private async Task<string> OttieniTokenAsync(HttpClient client, string credenzialiJson, string utente, CancellationToken annullamento)
    {
        string assertion;
        try
        {
            assertion = FirmaAsserzione(credenzialiJson, utente);
        }
        catch (Exception eccezione) when (eccezione is JsonException or CryptographicException or ArgumentException or KeyNotFoundException or InvalidOperationException)
        {
            throw new InvioEmailFallitoException("Gmail: credenziali non utilizzabili.", eccezione);
        }

        using var richiesta = new HttpRequestMessage(HttpMethod.Post, IndirizzoToken)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "urn:ietf:params:oauth:grant-type:jwt-bearer",
                ["assertion"] = assertion,
            }),
        };
        using var risposta = await ComunicazioneEmail.InviaAsync(client, "Gmail", richiesta, annullamento);
        return await ComunicazioneEmail.LeggiTokenAsync(risposta, "Gmail", annullamento);
    }

    private string FirmaAsserzione(string credenzialiJson, string utente)
    {
        using var documento = JsonDocument.Parse(credenzialiJson);
        var emailServizio = documento.RootElement.GetProperty("client_email").GetString()!;
        var chiavePrivata = documento.RootElement.GetProperty("private_key").GetString()!;

        var emesso = _orologio.GetUtcNow().ToUnixTimeSeconds();
        var intestazione = Base64Url(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { alg = "RS256", typ = "JWT" })));
        var rivendicazioni = Base64Url(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            iss = emailServizio,
            sub = utente,
            scope = Scope,
            aud = IndirizzoToken,
            iat = emesso,
            exp = emesso + 3600,
        })));

        using var rsa = RSA.Create();
        rsa.ImportFromPem(chiavePrivata);
        var firma = rsa.SignData(Encoding.ASCII.GetBytes($"{intestazione}.{rivendicazioni}"), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return $"{intestazione}.{rivendicazioni}.{Base64Url(firma)}";
    }

    private static string Base64Url(byte[] dati) => Convert.ToBase64String(dati).Replace('+', '-').Replace('/', '_').TrimEnd('=');
}
