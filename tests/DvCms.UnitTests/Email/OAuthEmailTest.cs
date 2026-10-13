using System.Net;
using System.Text.Json;
using DvCms.Contracts.Email;
using DvCms.DTO.Email;
using DvCms.Emails;
using DvCms.Models.Email;
using Xunit;

namespace DvCms.UnitTests.Email;

/// <summary>Canali email con consenso OAuth (A-010, ADR-0021): regole dei campi, PKCE, URL di autorizzazione e adapter con un servizio HTTP finto.</summary>
public class OAuthEmailTest
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Dictionary<string, string> AppMicrosoft() => new() { ["TenantMicrosoft"] = "common", ["IdClient"] = "client-1", ["SegretoClient"] = "segreto" };

    [Theory]
    [InlineData(ProviderEmail.MicrosoftOAuth, true)]
    [InlineData(ProviderEmail.GoogleOAuth, true)]
    [InlineData(ProviderEmail.Smtp, false)]
    [InlineData(ProviderEmail.Graph, false)]
    [InlineData(ProviderEmail.Gmail, false)]
    [InlineData(ProviderEmail.SendGrid, false)]
    public void SoloIDueCanaliConConsensoUsanoOAuth(ProviderEmail provider, bool atteso)
    {
        Assert.Equal(atteso, provider.UsaOAuth());
    }

    [Fact]
    public void LAppDeiCanaliOAuthHaSoloIDatiDellApp()
    {
        Assert.Equal(["TenantMicrosoft", "IdClient", "SegretoClient", "ScadenzaSegretoClient"], DefinizioniProviderEmail.Campi(ProviderEmail.MicrosoftOAuth).Select(c => c.Nome));
        Assert.Equal(["IdClient", "SegretoClient", "ScadenzaSegretoClient"], DefinizioniProviderEmail.Campi(ProviderEmail.GoogleOAuth).Select(c => c.Nome));
        Assert.True(DefinizioniProviderEmail.Campi(ProviderEmail.GoogleOAuth).Single(c => c.Nome == "SegretoClient").Segreto);
        Assert.Empty(DefinizioniProviderEmail.Valida(ProviderEmail.MicrosoftOAuth, AppMicrosoft()));
        Assert.Empty(DefinizioniProviderEmail.Valida(ProviderEmail.GoogleOAuth, new Dictionary<string, string> { ["IdClient"] = "x.apps.googleusercontent.com", ["SegretoClient"] = "s" }));
    }

    [Theory]
    [InlineData("TenantMicrosoft")]
    [InlineData("IdClient")]
    [InlineData("SegretoClient")]
    public void UnCampoObbligatorioMancanteDellAppVieneSegnalato(string campo)
    {
        var valori = AppMicrosoft();
        valori.Remove(campo);

        Assert.Equal([campo], DefinizioniProviderEmail.Valida(ProviderEmail.MicrosoftOAuth, valori));
    }

    [Theory]
    [InlineData("2027-03-31", true)]
    [InlineData("", true)]
    [InlineData("31/03/2027", false)]
    [InlineData("2027-02-30", false)]
    [InlineData("domani", false)]
    public void LaScadenzaDelSegretoEFacoltativaEDeveEssereUnaData(string scadenza, bool valida)
    {
        var valori = AppMicrosoft();
        valori[DefinizioniProviderEmail.CampoScadenzaSegreto] = scadenza;

        Assert.Equal(valida, DefinizioniProviderEmail.Valida(ProviderEmail.MicrosoftOAuth, valori).Count == 0);
    }

    [Fact]
    public void IdentificativiConCaratteriDaIndirizzoSonoRifiutati()
    {
        var valori = AppMicrosoft();
        valori["TenantMicrosoft"] = "x/../evil";

        Assert.Equal(["TenantMicrosoft"], DefinizioniProviderEmail.Valida(ProviderEmail.MicrosoftOAuth, valori));
    }

    [Fact]
    public void LaSfidaPkceSegueLEsempioDellRfc7636()
    {
        Assert.Equal("E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM", ClienteOAuthEmail.Sfida("dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk"));
        Assert.NotEqual(ClienteOAuthEmail.NuovoVerificatore(), ClienteOAuthEmail.NuovoVerificatore());
        Assert.NotEqual(ClienteOAuthEmail.NuovoStato(), ClienteOAuthEmail.NuovoStato());
        Assert.InRange(ClienteOAuthEmail.NuovoVerificatore().Length, 43, 128);
    }

    [Fact]
    public void LUrlDiAutorizzazioneHaGliAmbitiMinimiEScapaIValori()
    {
        var microsoft = ClienteOAuthEmail.CostruisciUrlAutorizzazione(ProviderEmail.MicrosoftOAuth, AppMicrosoft(), "https://a.esempio.it/admin/email/collegamento?x=1&y=2", "stato", "sfida");
        var google = ClienteOAuthEmail.CostruisciUrlAutorizzazione(ProviderEmail.GoogleOAuth, new Dictionary<string, string> { ["IdClient"] = "g", ["SegretoClient"] = "s" }, "https://a.esempio.it/r", "stato", "sfida");

        Assert.StartsWith("https://login.microsoftonline.com/common/oauth2/v2.0/authorize?", microsoft, StringComparison.Ordinal);
        Assert.Contains("redirect_uri=https%3A%2F%2Fa.esempio.it%2Fadmin%2Femail%2Fcollegamento%3Fx%3D1%26y%3D2", microsoft, StringComparison.Ordinal);
        Assert.Contains("scope=offline_access%20Mail.Send%20User.Read", microsoft, StringComparison.Ordinal);
        Assert.DoesNotContain("segreto", microsoft, StringComparison.Ordinal);
        Assert.StartsWith("https://accounts.google.com/o/oauth2/v2/auth?", google, StringComparison.Ordinal);
        Assert.Contains("gmail.send", google, StringComparison.Ordinal);
        Assert.DoesNotContain("https://www.googleapis.com/auth/gmail.readonly", google, StringComparison.Ordinal);
        Assert.Contains("code_challenge_method=S256", google, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ProviderEmail.MicrosoftOAuth, "Mail.Send User.Read openid profile", true)]
    [InlineData(ProviderEmail.MicrosoftOAuth, "https://graph.microsoft.com/Mail.Send", true)]
    [InlineData(ProviderEmail.MicrosoftOAuth, "mail.send", true)]
    [InlineData(ProviderEmail.MicrosoftOAuth, "Mail.Read User.Read", false)]
    [InlineData(ProviderEmail.MicrosoftOAuth, "", false)]
    [InlineData(ProviderEmail.GoogleOAuth, "openid https://www.googleapis.com/auth/gmail.send", true)]
    [InlineData(ProviderEmail.GoogleOAuth, "openid email", false)]
    [InlineData(ProviderEmail.GoogleOAuth, "https://www.googleapis.com/auth/gmail.sendfoo", false)]
    public void IlConsensoValeSoloSeConcedeLInvio(ProviderEmail provider, string ambiti, bool atteso)
    {
        Assert.Equal(atteso, ClienteOAuthEmail.ConcedeInvio(provider, ambiti));
    }

    [Fact]
    public async Task MicrosoftInviaDallaCasellaDelTokenDelegatoSenzaRichiedereAltriToken()
    {
        var finto = new GestoreFinto();
        var impostazioni = new ImpostazioniEmail(ProviderEmail.MicrosoftOAuth, new Dictionary<string, string> { ["TokenAccesso"] = "TOKEN-UTENTE", ["Casella"] = "ufficio@esempio.it" });

        await new AdapterMicrosoftOAuth(new FabbricaFinta(finto)).InviaAsync(impostazioni, new MessaggioEmail("anna@esempio.it", "Anna", "Ciao", "Testo", "<b>Html</b>"), Ct);

        var invio = Assert.Single(finto.Richieste);
        Assert.Equal("https://graph.microsoft.com/v1.0/me/sendMail", invio.Uri.AbsoluteUri);
        Assert.Equal("Bearer TOKEN-UTENTE", invio.Autorizzazione);
        using var documento = JsonDocument.Parse(invio.Corpo);
        var messaggio = documento.RootElement.GetProperty("message");
        Assert.Equal("HTML", messaggio.GetProperty("body").GetProperty("contentType").GetString());
        Assert.Equal("anna@esempio.it", messaggio.GetProperty("toRecipients")[0].GetProperty("emailAddress").GetProperty("address").GetString());
        Assert.DoesNotContain("TOKEN-UTENTE", invio.Corpo, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ProviderEmail.MicrosoftOAuth)]
    [InlineData(ProviderEmail.GoogleOAuth)]
    public async Task SenzaTokenOConIntestazioniConACapoNonSiInviaNulla(ProviderEmail provider)
    {
        var finto = new GestoreFinto();
        IAdapterEmail adapter = provider == ProviderEmail.MicrosoftOAuth
            ? new AdapterMicrosoftOAuth(new FabbricaFinta(finto))
            : new AdapterGoogleOAuth(new FabbricaFinta(finto));

        await Assert.ThrowsAsync<InvioEmailFallitoException>(() => adapter.InviaAsync(new ImpostazioniEmail(provider, new Dictionary<string, string> { ["Casella"] = "a@esempio.it" }), new MessaggioEmail("x@esempio.it", null, "Oggetto", "Corpo"), Ct));
        var conToken = new ImpostazioniEmail(provider, new Dictionary<string, string> { ["TokenAccesso"] = "T", ["Casella"] = "a@esempio.it" });
        await Assert.ThrowsAsync<ArgumentException>(() => adapter.InviaAsync(conToken, new MessaggioEmail("x@esempio.it", null, "Oggetto\r\nBcc: y@esempio.it", "Corpo"), Ct));

        Assert.Empty(finto.Richieste);
    }

    [Theory]
    [InlineData(ProviderEmail.MicrosoftOAuth)]
    [InlineData(ProviderEmail.GoogleOAuth)]
    public async Task UnaRispostaDiErroreNonRivelaCorpoNeToken(ProviderEmail provider)
    {
        var finto = new GestoreFinto { Risposta = new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = new StringContent("errore con TOKEN-UTENTE") } };
        IAdapterEmail adapter = provider == ProviderEmail.MicrosoftOAuth
            ? new AdapterMicrosoftOAuth(new FabbricaFinta(finto))
            : new AdapterGoogleOAuth(new FabbricaFinta(finto));

        var errore = await Assert.ThrowsAsync<InvioEmailFallitoException>(() => adapter.InviaAsync(
            new ImpostazioniEmail(provider, new Dictionary<string, string> { ["TokenAccesso"] = "TOKEN-UTENTE", ["Casella"] = "a@esempio.it" }),
            new MessaggioEmail("x@esempio.it", null, "Oggetto", "Corpo"),
            Ct));

        Assert.Contains("403", errore.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("TOKEN-UTENTE", errore.Message, StringComparison.Ordinal);
    }

    private sealed record Ricevuta(Uri Uri, string? Autorizzazione, string Corpo);

    private sealed class GestoreFinto : HttpMessageHandler
    {
        public List<Ricevuta> Richieste { get; } = [];

        public HttpResponseMessage Risposta { get; init; } = new(HttpStatusCode.Accepted);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Richieste.Add(new Ricevuta(request.RequestUri!, request.Headers.Authorization?.ToString(), request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken)));
            return Risposta;
        }
    }

    private sealed class FabbricaFinta : IHttpClientFactory
    {
        private readonly HttpMessageHandler _gestore;

        public FabbricaFinta(HttpMessageHandler gestore) => _gestore = gestore;

        public HttpClient CreateClient(string name) => new(_gestore, disposeHandler: false);
    }
}
