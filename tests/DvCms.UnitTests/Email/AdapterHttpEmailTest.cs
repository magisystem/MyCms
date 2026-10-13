using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DvCms.Contracts.Email;
using DvCms.DTO.Email;
using DvCms.Emails;
using DvCms.Models.Email;
using MimeKit;
using Xunit;

namespace DvCms.UnitTests.Email;

/// <summary>
/// Adapter Graph, Gmail e SendGrid (A-006, A-007, A-008) con un servizio HTTP finto: nessuna richiesta esce dal test.
/// Il test di contratto è lo stesso per tutti e tre; poi ognuno ha le sue verifiche sul formato delle richieste.
/// </summary>
public class AdapterHttpEmailTest
{
    private const string Segreto = "SEGRETISSIMO-12345";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static TheoryData<ProviderEmail> Provider => new() { ProviderEmail.Graph, ProviderEmail.Gmail, ProviderEmail.SendGrid };

    [Theory]
    [MemberData(nameof(Provider))]
    public async Task IlMessaggioArrivaAlServizioConLeCredenziali(ProviderEmail provider)
    {
        var finto = new ServizioHttpFinto();
        var messaggio = new MessaggioEmail("anna@esempio.it", "Anna", "Oggetto di prova", "Corpo di testo", "<p>Corpo html</p>");

        await Adapter(provider, finto).InviaAsync(Impostazioni(provider), messaggio, Ct);

        var invio = finto.Richieste.Last();
        Assert.Equal(HttpMethod.Post, invio.Metodo);
        Assert.StartsWith("Bearer ", invio.Autorizzazione, StringComparison.Ordinal);
        Assert.Contains(provider == ProviderEmail.SendGrid ? Segreto : "TOKEN-FINTO", invio.Autorizzazione ?? string.Empty, StringComparison.Ordinal);
        Assert.Contains("anna@esempio.it", invio.Corpo.Contains("anna@esempio.it", StringComparison.Ordinal) ? invio.Corpo : DecodificaRaw(invio.Corpo), StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Provider))]
    public async Task UnOggettoConACapoERifiutatoSenzaNessunaRichiesta(ProviderEmail provider)
    {
        var finto = new ServizioHttpFinto();
        var messaggio = new MessaggioEmail("anna@esempio.it", "Anna", "Oggetto\r\nBcc: x@y.it", "Corpo");

        await Assert.ThrowsAsync<ArgumentException>(() => Adapter(provider, finto).InviaAsync(Impostazioni(provider), messaggio, Ct));

        Assert.Empty(finto.Richieste);
    }

    [Theory]
    [MemberData(nameof(Provider))]
    public async Task UnaRispostaDiErroreNonRivelaCorpoNeSegreti(ProviderEmail provider)
    {
        var finto = new ServizioHttpFinto { RispostaInvio = _ => new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent($"errore con {Segreto}") } };

        var errore = await Assert.ThrowsAsync<InvioEmailFallitoException>(() =>
            Adapter(provider, finto).InviaAsync(Impostazioni(provider), Messaggio(), Ct));

        Assert.Contains("500", errore.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Segreto, errore.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("errore con", errore.Message, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Provider))]
    public async Task ServizioNonRaggiungibileDiventaUnErroreDiInvioSenzaDettagli(ProviderEmail provider)
    {
        var finto = new ServizioHttpFinto { Eccezione = new HttpRequestException($"connessione rifiutata {Segreto}") };

        var errore = await Assert.ThrowsAsync<InvioEmailFallitoException>(() =>
            Adapter(provider, finto).InviaAsync(Impostazioni(provider), Messaggio(), Ct));

        Assert.DoesNotContain(Segreto, errore.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ProviderEmail.Graph)]
    [InlineData(ProviderEmail.Gmail)]
    public async Task SeIlTokenNonSiOttieneNonSiInviaNulla(ProviderEmail provider)
    {
        var finto = new ServizioHttpFinto { RispostaToken = _ => new HttpResponseMessage(HttpStatusCode.Unauthorized) };

        await Assert.ThrowsAsync<InvioEmailFallitoException>(() => Adapter(provider, finto).InviaAsync(Impostazioni(provider), Messaggio(), Ct));

        Assert.Single(finto.Richieste);
    }

    [Theory]
    [InlineData(ProviderEmail.Graph)]
    [InlineData(ProviderEmail.Gmail)]
    public async Task UnaRispostaDiTokenSenzaTokenERifiutata(ProviderEmail provider)
    {
        var finto = new ServizioHttpFinto { RispostaToken = _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"altro\":1}") } };

        await Assert.ThrowsAsync<InvioEmailFallitoException>(() => Adapter(provider, finto).InviaAsync(Impostazioni(provider), Messaggio(), Ct));

        Assert.Single(finto.Richieste);
    }

    [Fact]
    public async Task GraphChiedeIlTokenAlTenantMicrosoftEInviaDallaCasellaIndicata()
    {
        var finto = new ServizioHttpFinto();
        var impostazioni = Impostazioni(ProviderEmail.Graph, ("CasellaMittente", "noreply+tag@esempio.it"));

        await Adapter(ProviderEmail.Graph, finto).InviaAsync(impostazioni, new MessaggioEmail("anna@esempio.it", "Anna", "Ciao", "Testo", "<b>Html</b>"), Ct);

        var token = finto.Richieste[0];
        Assert.Equal("https://login.microsoftonline.com/tenant-ms/oauth2/v2.0/token", token.Uri.ToString());
        Assert.Contains("grant_type=client_credentials", token.Corpo, StringComparison.Ordinal);
        Assert.Contains("client_id=client-1", token.Corpo, StringComparison.Ordinal);
        Assert.Contains($"client_secret={Segreto}", token.Corpo, StringComparison.Ordinal);
        Assert.Contains("scope=https%3A%2F%2Fgraph.microsoft.com%2F.default", token.Corpo, StringComparison.Ordinal);

        var invio = finto.Richieste[1];
        Assert.Equal("https://graph.microsoft.com/v1.0/users/noreply%2Btag%40esempio.it/sendMail", invio.Uri.AbsoluteUri);
        Assert.DoesNotContain(Segreto, invio.Corpo, StringComparison.Ordinal);
        using var documento = JsonDocument.Parse(invio.Corpo);
        var messaggio = documento.RootElement.GetProperty("message");
        Assert.Equal("Ciao", messaggio.GetProperty("subject").GetString());
        Assert.Equal("HTML", messaggio.GetProperty("body").GetProperty("contentType").GetString());
        Assert.Equal("anna@esempio.it", messaggio.GetProperty("toRecipients")[0].GetProperty("emailAddress").GetProperty("address").GetString());
        Assert.False(documento.RootElement.GetProperty("saveToSentItems").GetBoolean());
    }

    [Fact]
    public async Task GraphSenzaHtmlInviaTestoSemplice()
    {
        var finto = new ServizioHttpFinto();

        await Adapter(ProviderEmail.Graph, finto).InviaAsync(Impostazioni(ProviderEmail.Graph), new MessaggioEmail("anna@esempio.it", null, "Ciao", "Solo testo"), Ct);

        using var documento = JsonDocument.Parse(finto.Richieste[1].Corpo);
        var corpo = documento.RootElement.GetProperty("message").GetProperty("body");
        Assert.Equal("Text", corpo.GetProperty("contentType").GetString());
        Assert.Equal("Solo testo", corpo.GetProperty("content").GetString());
    }

    [Fact]
    public async Task GmailFirmaUnJwtValidoPerLUtenteMittenteEInviaIlMimeInBase64Url()
    {
        using var rsa = RSA.Create(2048);
        var finto = new ServizioHttpFinto();
        var orologio = new OrologioFisso(new DateTimeOffset(2026, 10, 10, 8, 0, 0, TimeSpan.Zero));
        var impostazioni = new ImpostazioniEmail(ProviderEmail.Gmail, new Dictionary<string, string>
        {
            ["UtenteMittente"] = "noreply@esempio.it",
            ["CredenzialiJson"] = CredenzialiGoogle(rsa),
        });

        await new AdapterGmail(new FabbricaHttpFinta(finto), orologio).InviaAsync(
            impostazioni,
            new MessaggioEmail("anna@esempio.it", "Anna", "Oggetto con àccenti", "Corpo di testo"),
            Ct);

        // Token: l'asserzione è firmata con la chiave dell'account e intestata all'utente mittente, con scope minimo.
        var token = finto.Richieste[0];
        Assert.Equal("https://oauth2.googleapis.com/token", token.Uri.ToString());
        var assertion = System.Web.HttpUtility.ParseQueryString(token.Corpo)["assertion"]!;
        var parti = assertion.Split('.');
        Assert.True(rsa.VerifyData(Encoding.ASCII.GetBytes($"{parti[0]}.{parti[1]}"), DaBase64Url(parti[2]), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
        using var rivendicazioni = JsonDocument.Parse(DaBase64Url(parti[1]));
        var radice = rivendicazioni.RootElement;
        Assert.Equal("servizio@progetto.iam.gserviceaccount.com", radice.GetProperty("iss").GetString());
        Assert.Equal("noreply@esempio.it", radice.GetProperty("sub").GetString());
        Assert.Equal("https://www.googleapis.com/auth/gmail.send", radice.GetProperty("scope").GetString());
        Assert.Equal("https://oauth2.googleapis.com/token", radice.GetProperty("aud").GetString());
        Assert.Equal(orologio.GetUtcNow().ToUnixTimeSeconds(), radice.GetProperty("iat").GetInt64());
        Assert.Equal(orologio.GetUtcNow().ToUnixTimeSeconds() + 3600, radice.GetProperty("exp").GetInt64());
        Assert.DoesNotContain("PRIVATE KEY", token.Corpo, StringComparison.Ordinal);

        // Invio: il MIME completo, in base64url.
        var invio = finto.Richieste[1];
        Assert.Equal("https://gmail.googleapis.com/gmail/v1/users/me/messages/send", invio.Uri.ToString());
        using var corpo = JsonDocument.Parse(invio.Corpo);
        using var flusso = new MemoryStream(DaBase64Url(corpo.RootElement.GetProperty("raw").GetString()!));
        var mime = await MimeMessage.LoadAsync(flusso, Ct);
        Assert.Equal("noreply@esempio.it", mime.From.Mailboxes.Single().Address);
        Assert.Equal("anna@esempio.it", mime.To.Mailboxes.Single().Address);
        Assert.Equal("Oggetto con àccenti", mime.Subject);
        Assert.Contains("Corpo di testo", mime.TextBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GmailConCredenzialiNonUtilizzabiliNonFaRichieste()
    {
        var finto = new ServizioHttpFinto();
        var impostazioni = new ImpostazioniEmail(ProviderEmail.Gmail, new Dictionary<string, string>
        {
            ["UtenteMittente"] = "noreply@esempio.it",
            ["CredenzialiJson"] = "{\"type\":\"service_account\",\"client_email\":\"a@b.it\",\"private_key\":\"-----BEGIN PRIVATE KEY-----\\nnon-valida\\n-----END PRIVATE KEY-----\"}",
        });

        var errore = await Assert.ThrowsAsync<InvioEmailFallitoException>(() => Adapter(ProviderEmail.Gmail, finto).InviaAsync(impostazioni, Messaggio(), Ct));

        Assert.Empty(finto.Richieste);
        Assert.DoesNotContain("non-valida", errore.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SendGridInviaMittenteDestinatarioEContenuti()
    {
        var finto = new ServizioHttpFinto();

        await Adapter(ProviderEmail.SendGrid, finto).InviaAsync(
            Impostazioni(ProviderEmail.SendGrid),
            new MessaggioEmail("anna@esempio.it", "Anna", "Ciao", "Testo", "<p>Html</p>"),
            Ct);

        var invio = Assert.Single(finto.Richieste);
        Assert.Equal("https://api.sendgrid.com/v3/mail/send", invio.Uri.ToString());
        Assert.Equal($"Bearer {Segreto}", invio.Autorizzazione);
        Assert.DoesNotContain(Segreto, invio.Corpo, StringComparison.Ordinal);
        using var documento = JsonDocument.Parse(invio.Corpo);
        var radice = documento.RootElement;
        Assert.Equal("noreply@esempio.it", radice.GetProperty("from").GetProperty("email").GetString());
        Assert.Equal("Mittente", radice.GetProperty("from").GetProperty("name").GetString());
        var destinatario = radice.GetProperty("personalizations")[0].GetProperty("to")[0];
        Assert.Equal("anna@esempio.it", destinatario.GetProperty("email").GetString());
        Assert.Equal("Anna", destinatario.GetProperty("name").GetString());
        Assert.Equal("Ciao", radice.GetProperty("subject").GetString());
        Assert.Equal(["text/plain", "text/html"], radice.GetProperty("content").EnumerateArray().Select(c => c.GetProperty("type").GetString()));
    }

    [Fact]
    public async Task SendGridSenzaNomiEHtmlOmetteICampiVuoti()
    {
        var finto = new ServizioHttpFinto();
        var impostazioni = new ImpostazioniEmail(ProviderEmail.SendGrid, new Dictionary<string, string> { ["ChiaveApi"] = Segreto, ["IndirizzoMittente"] = "noreply@esempio.it" });

        await Adapter(ProviderEmail.SendGrid, finto).InviaAsync(impostazioni, new MessaggioEmail("anna@esempio.it", null, "Ciao", "Testo"), Ct);

        using var documento = JsonDocument.Parse(Assert.Single(finto.Richieste).Corpo);
        Assert.False(documento.RootElement.GetProperty("from").TryGetProperty("name", out _));
        Assert.False(documento.RootElement.GetProperty("personalizations")[0].GetProperty("to")[0].TryGetProperty("name", out _));
        Assert.Single(documento.RootElement.GetProperty("content").EnumerateArray());
    }

    /// <summary>Per Gmail il messaggio è dentro il campo <c>raw</c> in base64url.</summary>
    private static string DecodificaRaw(string corpo)
    {
        using var documento = JsonDocument.Parse(corpo);
        return Encoding.UTF8.GetString(DaBase64Url(documento.RootElement.GetProperty("raw").GetString()!));
    }

    private static MessaggioEmail Messaggio() => new("anna@esempio.it", "Anna", "Oggetto", "Corpo");

    private static IAdapterEmail Adapter(ProviderEmail provider, ServizioHttpFinto finto)
    {
        var fabbrica = new FabbricaHttpFinta(finto);
        return provider switch
        {
            ProviderEmail.Graph => new AdapterGraph(fabbrica),
            ProviderEmail.Gmail => new AdapterGmail(fabbrica, TimeProvider.System),
            ProviderEmail.SendGrid => new AdapterSendGrid(fabbrica),
            _ => throw new ArgumentOutOfRangeException(nameof(provider)),
        };
    }

    private static ImpostazioniEmail Impostazioni(ProviderEmail provider, params (string Nome, string Valore)[] sostituzioni)
    {
        var valori = provider switch
        {
            ProviderEmail.Graph => new Dictionary<string, string>
            {
                ["TenantMicrosoft"] = "tenant-ms",
                ["IdClient"] = "client-1",
                ["SegretoClient"] = Segreto,
                ["CasellaMittente"] = "noreply@esempio.it",
            },
            ProviderEmail.Gmail => new Dictionary<string, string>
            {
                ["UtenteMittente"] = "noreply@esempio.it",
                ["CredenzialiJson"] = CredenzialiGoogle(_chiaveProva),
            },
            _ => new Dictionary<string, string>
            {
                ["ChiaveApi"] = Segreto,
                ["IndirizzoMittente"] = "noreply@esempio.it",
                ["NomeMittente"] = "Mittente",
            },
        };
        foreach (var (nome, valore) in sostituzioni)
        {
            valori[nome] = valore;
        }

        Assert.Empty(DefinizioniProviderEmail.Valida(provider, valori));
        return new ImpostazioniEmail(provider, valori);
    }

    private static readonly RSA _chiaveProva = RSA.Create(2048);

    private static string CredenzialiGoogle(RSA rsa) => JsonSerializer.Serialize(new Dictionary<string, string>
    {
        ["type"] = "service_account",
        ["client_email"] = "servizio@progetto.iam.gserviceaccount.com",
        ["private_key"] = rsa.ExportPkcs8PrivateKeyPem(),
        ["token_uri"] = "https://indirizzo-ostile.esempio.it/token",
    });

    private static byte[] DaBase64Url(string testo)
    {
        var base64 = testo.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(base64.PadRight(base64.Length + ((4 - (base64.Length % 4)) % 4), '='));
    }

    private sealed record RichiestaRicevuta(HttpMethod Metodo, Uri Uri, string? Autorizzazione, string Corpo);

    /// <summary>Servizio HTTP finto: registra le richieste e risponde come un endpoint OAuth (token) o come l'API di invio.</summary>
    private sealed class ServizioHttpFinto : HttpMessageHandler
    {
        public List<RichiestaRicevuta> Richieste { get; } = [];

        public Exception? Eccezione { get; init; }

        public Func<HttpRequestMessage, HttpResponseMessage> RispostaToken { get; init; } =
            _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"access_token\":\"TOKEN-FINTO\",\"expires_in\":3600}") };

        public Func<HttpRequestMessage, HttpResponseMessage> RispostaInvio { get; init; } = _ => new HttpResponseMessage(HttpStatusCode.Accepted);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Richieste.Add(new RichiestaRicevuta(
                request.Method,
                request.RequestUri!,
                request.Headers.Authorization?.ToString(),
                request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken)));
            if (Eccezione is not null)
            {
                throw Eccezione;
            }

            var token = request.RequestUri!.Host is "login.microsoftonline.com" or "oauth2.googleapis.com";
            return token ? RispostaToken(request) : RispostaInvio(request);
        }
    }

    private sealed class FabbricaHttpFinta : IHttpClientFactory
    {
        private readonly HttpMessageHandler _gestore;

        public FabbricaHttpFinta(HttpMessageHandler gestore)
        {
            _gestore = gestore;
        }

        public HttpClient CreateClient(string name) => new(_gestore, disposeHandler: false);
    }

    private sealed class OrologioFisso : TimeProvider
    {
        private readonly DateTimeOffset _adesso;

        public OrologioFisso(DateTimeOffset adesso)
        {
            _adesso = adesso;
        }

        public override DateTimeOffset GetUtcNow() => _adesso;
    }
}
