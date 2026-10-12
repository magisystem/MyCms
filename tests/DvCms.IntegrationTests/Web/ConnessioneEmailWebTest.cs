using System.Net;
using System.Security.Cryptography;
using System.Web;
using DvCms.IntegrationTests.Supporto;
using Xunit;

namespace DvCms.IntegrationTests.Web;

/// <summary>Pagina <c>/admin/email</c> del TenantAdmin (A-010, ADR-0021) attraverso l'applicazione web: accesso, consenso OAuth e ritorno dal provider.</summary>
public class ConnessioneEmailWebTest
{
    private const string Pagina = "/admin/email";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Dictionary<string, string?> Chiave() => new() { ["Sicurezza:ChiaveSegreti"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) };

    [Fact]
    public async Task LaPaginaEDelSoloTenantAdminNonDelPlatformAdminNeDiChiNonHaAccesso()
    {
        await using var scenario = await ScenarioWeb.CreaAsync(impostazioni: Chiave(), oauthEmail: new ProviderOAuthFinto());
        await scenario.CreaTenantAdminAsync("anna@esempio.it");
        await scenario.CreaPlatformAdminAsync("root@esempio.it");
        var anonima = scenario.SessioneA();
        var anna = scenario.SessioneA();
        var platform = scenario.SessioneB();
        await anna.AccediAsync("anna@esempio.it", DatiProva.PasswordProva);
        await platform.AccediAsync("root@esempio.it", DatiProva.PasswordProva);

        using var senzaSessione = await anonima.GetAsync(Pagina);
        using var comeTenantAdmin = await anna.GetAsync(Pagina);
        using var comePlatform = await platform.GetAsync(Pagina);
        using var ritornoComePlatform = await platform.GetAsync($"{Pagina}/collegamento?code=x&state=y");
        using var salvaComePlatform = await platform.InviaModuloAsync("/admin", [new("provider", "MicrosoftOAuth"), new("v_IdClient", "x")], $"{Pagina}?handler=SalvaApp");

        Assert.True(SessioneProva.EReindirizzamento(senzaSessione));
        Assert.Contains("/admin/accesso", SessioneProva.Destinazione(senzaSessione), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, comeTenantAdmin.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, comePlatform.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, ritornoComePlatform.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, salvaComePlatform.StatusCode);

        var html = await comeTenantAdmin.Content.ReadAsStringAsync(Ct);
        Assert.Contains("name=\"v_IdClient\"", html, StringComparison.Ordinal);
        Assert.Contains("//a.esempio.it/admin/email/collegamento", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ILMenuMostraLaVoceEmailSoloAlTenantAdmin()
    {
        await using var scenario = await ScenarioWeb.CreaAsync(impostazioni: Chiave(), oauthEmail: new ProviderOAuthFinto());
        await scenario.CreaTenantAdminAsync("anna@esempio.it");
        await scenario.CreaPlatformAdminAsync("root@esempio.it");
        var anna = scenario.SessioneA();
        var platform = scenario.SessioneB();
        await anna.AccediAsync("anna@esempio.it", DatiProva.PasswordProva);
        await platform.AccediAsync("root@esempio.it", DatiProva.PasswordProva);

        var comeTenantAdmin = await (await anna.GetAsync("/admin")).Content.ReadAsStringAsync(Ct);
        var comePlatform = await (await platform.GetAsync("/admin")).Content.ReadAsStringAsync(Ct);

        Assert.Contains("href=\"/admin/email\"", comeTenantAdmin, StringComparison.Ordinal);
        Assert.DoesNotContain("href=\"/admin/email\"", comePlatform, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ILConsensoPassaDaUnaNavigazioneECompletaIlCollegamentoAlRitorno()
    {
        var provider = new ProviderOAuthFinto { Casella = "ufficio@esempio.it" };
        await using var scenario = await ScenarioWeb.CreaAsync(impostazioni: Chiave(), oauthEmail: provider);
        await scenario.CreaTenantAdminAsync("anna@esempio.it");
        var anna = scenario.SessioneA();
        await anna.AccediAsync("anna@esempio.it", DatiProva.PasswordProva);

        using var salva = await anna.InviaModuloAsync(
            Pagina,
            [new("provider", "MicrosoftOAuth"), new("v_TenantMicrosoft", "common"), new("v_IdClient", "client-1"), new("v_SegretoClient", "SEGRETO-DEL-CLIENT")],
            $"{Pagina}?handler=SalvaApp");
        Assert.True(SessioneProva.EReindirizzamento(salva));
        var dopoSalva = await (await anna.GetAsync(Pagina)).Content.ReadAsStringAsync(Ct);
        Assert.DoesNotContain("SEGRETO-DEL-CLIENT", dopoSalva, StringComparison.Ordinal);
        Assert.Contains("Collega la casella", dopoSalva, StringComparison.Ordinal);

        // Il pulsante Collega risponde con un reindirizzamento interno: il passaggio al provider è una navigazione, non l'invio di un modulo.
        using var collega = await anna.InviaModuloAsync(Pagina, [], $"{Pagina}?handler=Collega");
        Assert.True(SessioneProva.EReindirizzamento(collega));
        Assert.Equal($"{Pagina}?handler=Vai", SessioneProva.Destinazione(collega));
        Assert.True(anna.Cookie.ContainsKey(".DvCms.CollegamentoEmail"));

        using var vai = await anna.GetAsync($"{Pagina}?handler=Vai");
        Assert.True(SessioneProva.EReindirizzamento(vai));
        var verso = vai.Headers.Location!;
        Assert.Equal("login.microsoftonline.com", verso.Host);
        var stato = HttpUtility.ParseQueryString(verso.Query)["state"];
        Assert.False(string.IsNullOrEmpty(stato));

        // Ritorno con lo stato giusto: collegata. L'indirizzo mostrato è quello della casella.
        using var ritorno = await anna.GetAsync($"{Pagina}/collegamento?code={ProviderOAuthFinto.CodiceValido}&state={Uri.EscapeDataString(stato!)}");
        Assert.True(SessioneProva.EReindirizzamento(ritorno));
        Assert.Equal(Pagina, SessioneProva.Destinazione(ritorno));
        Assert.False(anna.Cookie.ContainsKey(".DvCms.CollegamentoEmail"));
        var collegata = await (await anna.GetAsync(Pagina)).Content.ReadAsStringAsync(Ct);
        Assert.Contains("ufficio@esempio.it", collegata, StringComparison.Ordinal);
        Assert.DoesNotContain(ProviderOAuthFinto.CodiceValido, collegata, StringComparison.Ordinal);
        Assert.Contains("Email di prova", collegata, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnRitornoConStatoSbagliatoOSenzaCookieNonCollegaNiente()
    {
        var provider = new ProviderOAuthFinto();
        await using var scenario = await ScenarioWeb.CreaAsync(impostazioni: Chiave(), oauthEmail: provider);
        await scenario.CreaTenantAdminAsync("anna@esempio.it");
        var anna = scenario.SessioneA();
        await anna.AccediAsync("anna@esempio.it", DatiProva.PasswordProva);
        await anna.InviaModuloAsync(
            Pagina,
            [new("provider", "GoogleOAuth"), new("v_IdClient", "client-1"), new("v_SegretoClient", "segreto")],
            $"{Pagina}?handler=SalvaApp");

        // Nessuna richiesta di collegamento in corso: il ritorno è rifiutato senza chiamare il provider.
        using var senzaCookie = await anna.GetAsync($"{Pagina}/collegamento?code={ProviderOAuthFinto.CodiceValido}&state=inventato");
        Assert.Equal(Pagina, SessioneProva.Destinazione(senzaCookie));

        await anna.InviaModuloAsync(Pagina, [], $"{Pagina}?handler=Collega");
        using var statoSbagliato = await anna.GetAsync($"{Pagina}/collegamento?code={ProviderOAuthFinto.CodiceValido}&state=inventato");
        Assert.Equal(Pagina, SessioneProva.Destinazione(statoSbagliato));
        var pagina = await (await anna.GetAsync(Pagina)).Content.ReadAsStringAsync(Ct);

        Assert.Contains("Nessuna casella collegata", pagina, StringComparison.Ordinal);
        Assert.Empty(provider.Richieste);
    }

    [Fact]
    public async Task LaPaginaAvvisaQuandoIlSegretoDelClientStaPerScadereOEScaduto()
    {
        await using var scenario = await ScenarioWeb.CreaAsync(impostazioni: Chiave(), oauthEmail: new ProviderOAuthFinto());
        await scenario.CreaTenantAdminAsync("anna@esempio.it");
        var anna = scenario.SessioneA();
        await anna.AccediAsync("anna@esempio.it", DatiProva.PasswordProva);

        async Task<string> ConScadenzaAsync(int giorniDaOggi)
        {
            var data = DateTime.UtcNow.AddDays(giorniDaOggi).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            await anna.InviaModuloAsync(
                Pagina,
                [new("provider", "MicrosoftOAuth"), new("v_TenantMicrosoft", "common"), new("v_IdClient", "client-1"), new("v_SegretoClient", "segreto"), new("v_ScadenzaSegretoClient", data)],
                $"{Pagina}?handler=SalvaApp");
            return await (await anna.GetAsync(Pagina)).Content.ReadAsStringAsync(Ct);
        }

        var lontana = await ConScadenzaAsync(90);
        var vicina = await ConScadenzaAsync(10);
        var scaduta = await ConScadenzaAsync(-2);

        Assert.DoesNotContain("sta per scadere", lontana, StringComparison.Ordinal);
        Assert.DoesNotContain("alert-danger", lontana, StringComparison.Ordinal);
        Assert.Contains("sta per scadere", vicina, StringComparison.Ordinal);
        Assert.Contains("alert-warning", vicina, StringComparison.Ordinal);
        Assert.Contains("le email non partono finch", scaduta, StringComparison.Ordinal);
        Assert.Contains("alert-danger", scaduta, StringComparison.Ordinal);
        Assert.Contains("name=\"v_ScadenzaSegretoClient\"", lontana, StringComparison.Ordinal);

        // Una data non valida non salva niente e il modulo segnala il campo.
        using var errata = await anna.InviaModuloAsync(
            Pagina,
            [new("provider", "MicrosoftOAuth"), new("v_TenantMicrosoft", "common"), new("v_IdClient", "client-1"), new("v_ScadenzaSegretoClient", "31/12/2027")],
            $"{Pagina}?handler=SalvaApp");
        Assert.Contains("Controlla i campi evidenziati", await errata.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task LaPaginaPlatformNonMostraNeModificaLAppOAuthDelTenant()
    {
        await using var scenario = await ScenarioWeb.CreaAsync(impostazioni: Chiave(), oauthEmail: new ProviderOAuthFinto());
        await scenario.CreaTenantAdminAsync("anna@esempio.it");
        await scenario.CreaPlatformAdminAsync("root@esempio.it");
        var anna = scenario.SessioneA();
        var platform = scenario.SessioneB();
        await anna.AccediAsync("anna@esempio.it", DatiProva.PasswordProva);
        await platform.AccediAsync("root@esempio.it", DatiProva.PasswordProva);
        await anna.InviaModuloAsync(
            Pagina,
            [new("provider", "GoogleOAuth"), new("v_IdClient", "client-riservato"), new("v_SegretoClient", "SEGRETO-DEL-CLIENT")],
            $"{Pagina}?handler=SalvaApp");
        var paginaPlatform = $"/admin/platform/tenant/{scenario.A.IdTenant}/email";

        var html = await (await platform.GetAsync(paginaPlatform)).Content.ReadAsStringAsync(Ct);

        Assert.Contains("amministratore del tenant", html, StringComparison.Ordinal);
        Assert.DoesNotContain("client-riservato", html, StringComparison.Ordinal);
        Assert.DoesNotContain("SEGRETO-DEL-CLIENT", html, StringComparison.Ordinal);
        Assert.DoesNotContain("name=\"v_", html, StringComparison.Ordinal);
        Assert.DoesNotContain("MicrosoftOAuth", html, StringComparison.Ordinal);

        using var salva = await platform.InviaModuloAsync(
            paginaPlatform,
            [new("provider", "Smtp"), new("v_Host", "smtp.esempio.it"), new("v_Porta", "587"), new("v_ModalitaTls", "StartTls"), new("v_IndirizzoMittente", "noreply@esempio.it")],
            $"{paginaPlatform}?handler=Salva");
        Assert.True(SessioneProva.EReindirizzamento(salva));
        var dopo = await (await anna.GetAsync(Pagina)).Content.ReadAsStringAsync(Ct);
        Assert.Contains("client-riservato", dopo, StringComparison.Ordinal);
    }
}
