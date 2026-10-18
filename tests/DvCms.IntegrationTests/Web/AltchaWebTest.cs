using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Altcha;
using DvCms.Contracts.Piattaforma;
using DvCms.Data;
using DvCms.IntegrationTests.Supporto;
using DvCms.Models.Accessi;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DvCms.IntegrationTests.Web;

/// <summary>Prova anti-abuso ALTCHA sulle form pubbliche (ADR-0026): widget, sfida, verifica, uso unico e ambienti.</summary>
public class AltchaWebTest
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Dictionary<string, string?> Attivo() => new()
    {
        ["Altcha:Abilitato"] = "true",
        ["Altcha:HmacSignatureSecret"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
        // Sfide leggere: i test devono risolverle in un attimo (in produzione valgono i predefiniti della libreria).
        ["Altcha:Challenge:Cost"] = "10",
        ["Altcha:Challenge:CounterMin"] = "1",
        ["Altcha:Challenge:CounterMax"] = "30",
        ["Sicurezza:ChiaveSegreti"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
    };

    /// <summary>Chiede una sfida al server e la risolve come farebbe il widget: restituisce la prova pronta per il campo <c>altcha</c>.</summary>
    private static async Task<string> ProvaAsync(SessioneProva sessione)
    {
        using var risposta = await sessione.GetAsync("/altcha/sfida");
        Assert.Equal(HttpStatusCode.OK, risposta.StatusCode);
        var sfida = JsonSerializer.Deserialize<Challenge>(await risposta.Content.ReadAsStringAsync(Ct), AltchaJson.SerializerOptions)!;
        var soluzione = AltchaPow.SolveChallenge(new SolveChallengeOptions { Challenge = sfida }, Ct);
        Assert.NotNull(soluzione);
        return Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(new Payload { Challenge = sfida, Solution = soluzione }, AltchaJson.SerializerOptions));
    }

    private static async Task<int> MessaggiOutboxAsync(ScenarioWeb scenario)
    {
        await using var ambito = scenario.Fabbrica.Services.CreateAsyncScope();
        return await ambito.ServiceProvider.GetRequiredService<ContestoDatabase>().MessaggiOutbox.IgnoreQueryFilters().CountAsync(Ct);
    }

    [Fact]
    public async Task LeFormMostranoIlWidgetEFileDellaStessaOrigineSoloSeLaProtezioneEAccesa()
    {
        await using var acceso = await ScenarioWeb.CreaAsync(impostazioni: Attivo());
        await using var spento = await ScenarioWeb.CreaAsync();

        var conWidget = await (await acceso.SessioneA().GetAsync("/admin/recupero")).Content.ReadAsStringAsync(Ct);
        var senzaWidget = await (await spento.SessioneA().GetAsync("/admin/recupero")).Content.ReadAsStringAsync(Ct);

        Assert.Contains("<altcha-widget", conWidget, StringComparison.Ordinal);
        Assert.Contains("challenge=\"/altcha/sfida\"", conWidget, StringComparison.Ordinal);
        Assert.Contains("/lib/altcha/altcha.min.js", conWidget, StringComparison.Ordinal);
        Assert.Contains("/js/altcha-avvio.js", conWidget, StringComparison.Ordinal);
        Assert.DoesNotContain("https://cdn", conWidget, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<altcha-widget", senzaWidget, StringComparison.Ordinal);
        Assert.DoesNotContain("/lib/altcha/", senzaWidget, StringComparison.Ordinal);

        using var sfidaSpenta = await spento.SessioneA().GetAsync("/altcha/sfida");
        Assert.Equal(HttpStatusCode.NotFound, sfidaSpenta.StatusCode);
    }

    [Fact]
    public async Task GliFileDelWidgetSiServonoDallaStessaOrigineELaSfidaNonVaInCache()
    {
        await using var scenario = await ScenarioWeb.CreaAsync(impostazioni: Attivo());
        var sessione = scenario.SessioneA();

        foreach (var percorso in new[] { "/lib/altcha/altcha.min.js", "/lib/altcha/altcha.css", "/lib/altcha/workers/pbkdf2.js", "/lib/altcha/i18n/it.js", "/js/altcha-avvio.js" })
        {
            using var file = await sessione.GetAsync(percorso);
            Assert.Equal(HttpStatusCode.OK, file.StatusCode);
        }

        using var sfida = await sessione.GetAsync("/altcha/sfida");
        Assert.Equal(HttpStatusCode.OK, sfida.StatusCode);
        Assert.Equal("application/json", sfida.Content.Headers.ContentType?.MediaType);
        Assert.Contains("no-store", sfida.Headers.CacheControl?.ToString() ?? string.Empty, StringComparison.Ordinal);
        Assert.Contains("script-src 'self'", string.Join(';', sfida.Headers.GetValues("Content-Security-Policy")), StringComparison.Ordinal);
        using var licenza = await sessione.GetAsync("/lib/altcha/LICENSE.txt");
        Assert.Equal(HttpStatusCode.NotFound, licenza.StatusCode);
    }

    [Fact]
    public async Task SenzaProvaOConUnaProvaSbagliataIlRecuperoNonFaNulla()
    {
        await using var scenario = await ScenarioWeb.CreaAsync(impostazioni: Attivo());
        await scenario.CreaTenantAdminAsync("anna@esempio.it");
        var anonimo = scenario.SessioneA();

        using var senza = await anonimo.InviaModuloAsync("/admin/recupero", [new("Email", "anna@esempio.it")]);
        using var vuota = await anonimo.InviaModuloAsync("/admin/recupero", [new("Email", "anna@esempio.it"), new("altcha", string.Empty)]);
        using var spazzatura = await anonimo.InviaModuloAsync("/admin/recupero", [new("Email", "anna@esempio.it"), new("altcha", "non-e-una-prova")]);
        var alterata = JsonSerializer.Deserialize<Payload>(Convert.FromBase64String(await ProvaAsync(anonimo)), AltchaJson.SerializerOptions)!;
        alterata.Solution.DerivedKey = new string('0', alterata.Solution.DerivedKey.Length);
        using var sbagliata = await anonimo.InviaModuloAsync(
            "/admin/recupero",
            [new("Email", "anna@esempio.it"), new("altcha", Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(alterata, AltchaJson.SerializerOptions)))]);

        foreach (var risposta in new[] { senza, vuota, spazzatura, sbagliata })
        {
            Assert.Equal(HttpStatusCode.OK, risposta.StatusCode);
            Assert.Contains("alert-danger", await risposta.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        }

        Assert.Equal(0, await MessaggiOutboxAsync(scenario));
        Assert.False(anonimo.Cookie.ContainsKey(".DvCms.Recupero"));
    }

    [Fact]
    public async Task UnaProvaValidaFaProcedereMaSiUsaUnaVoltaSola()
    {
        await using var scenario = await ScenarioWeb.CreaAsync(impostazioni: Attivo());
        await scenario.CreaTenantAdminAsync("anna@esempio.it");
        var anonimo = scenario.SessioneA();
        var prova = await ProvaAsync(anonimo);

        using var prima = await anonimo.InviaModuloAsync("/admin/recupero", [new("Email", "anna@esempio.it"), new("altcha", prova)]);
        Assert.Equal("/admin/recupero/codice", SessioneProva.Destinazione(prima));
        Assert.Equal(1, await MessaggiOutboxAsync(scenario));

        using var riuso = await anonimo.InviaModuloAsync("/admin/recupero", [new("Email", "anna@esempio.it"), new("altcha", prova)]);
        Assert.Equal(HttpStatusCode.OK, riuso.StatusCode);
        Assert.Contains("alert-danger", await riuso.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        Assert.Equal(1, await MessaggiOutboxAsync(scenario));
    }

    [Fact]
    public async Task LaRegistrazioneDegliAcquirentiRichiedeLaProva()
    {
        await using var scenario = await ScenarioWeb.CreaAsync(impostazioni: Attivo());
        await using var server = new ServerSmtpFinto();
        await DatiProva.ConfiguraSmtpAsync(scenario.Fabbrica.Services, scenario.A.IdTenant, server.Porta, Ct);
        var platform = await scenario.CreaPlatformAdminAsync("platform@esempio.it");
        await using (var ambito = scenario.Fabbrica.Services.CreateAsyncScope())
        {
            var piattaforma = ambito.ServiceProvider.GetRequiredService<IServizioPiattaforma>();
            Assert.True((await piattaforma.ImpostaCommercioAsync(platform, scenario.A.IdTenant, scenario.A.IdSito, true, Ct)).Riuscito);
            Assert.True((await piattaforma.ImpostaRegistrazioneAcquirentiAsync(platform, scenario.A.IdTenant, scenario.A.IdSito, true, Ct)).Riuscito);
        }

        var anonimo = scenario.SessioneA();
        var pagina = await (await anonimo.GetAsync("/account/registrazione")).Content.ReadAsStringAsync(Ct);
        Assert.Contains("<altcha-widget", pagina, StringComparison.Ordinal);

        using var senza = await anonimo.InviaModuloAsync("/account/registrazione", [new("Email", "cliente@esempio.it"), new("Nome", "Carla")]);
        var htmlSenza = await senza.Content.ReadAsStringAsync(Ct);
        Assert.Equal(HttpStatusCode.OK, senza.StatusCode);
        Assert.Contains("role=\"alert\"", htmlSenza, StringComparison.Ordinal);
        Assert.DoesNotContain("role=\"status\"", htmlSenza, StringComparison.Ordinal);
        Assert.Equal(0, await ContaUtenzeAsync(scenario, "cliente@esempio.it"));

        using var con = await anonimo.InviaModuloAsync("/account/registrazione", [new("Email", "cliente@esempio.it"), new("Nome", "Carla"), new("altcha", await ProvaAsync(anonimo))]);
        Assert.Contains("role=\"status\"", await con.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        Assert.Equal(1, await ContaUtenzeAsync(scenario, "cliente@esempio.it"));
    }

    [Fact]
    public async Task LaSfidaHaUnLimiteDiRichiestePerIndirizzo()
    {
        var impostazioni = Attivo();
        impostazioni["Altcha:RichiesteSfidaAlMinuto"] = "3";
        await using var scenario = await ScenarioWeb.CreaAsync(impostazioni: impostazioni);
        var sessione = scenario.SessioneA();

        var codici = new List<HttpStatusCode>();
        for (var i = 0; i < 5; i++)
        {
            using var risposta = await sessione.GetAsync("/altcha/sfida");
            codici.Add(risposta.StatusCode);
        }

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.TooManyRequests, HttpStatusCode.TooManyRequests], codici);
    }

    private static async Task<int> ContaUtenzeAsync(ScenarioWeb scenario, string email)
    {
        await using var ambito = scenario.Fabbrica.Services.CreateAsyncScope();
        return await ambito.ServiceProvider.GetRequiredService<ContestoDatabase>().Utenze.IgnoreQueryFilters().CountAsync(u => u.Email == email && u.Ruolo == RuoloUtenza.Utente, Ct);
    }
}
