using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Altcha;
using DvCms.Contracts.Moduli;
using DvCms.Core.Moduli;
using DvCms.Core.Tenancy;
using DvCms.Data;
using DvCms.DTO.Moduli;
using DvCms.DTO.Tenancy;
using DvCms.IntegrationTests.Supporto;
using DvCms.Models.Moduli;
using DvCms.Services.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DvCms.IntegrationTests.Web;

/// <summary>Invio pubblico dei moduli di contatto (ADR-0027): ALTCHA, validazione, salvataggio con istantanea, moduli non pubblicati, tenant e limiti.</summary>
public class ModuloPubblicoWebTest
{
    private const string PaginaConToken = "/admin/recupero";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Dictionary<string, string?> Attivo(Dictionary<string, string?>? extra = null)
    {
        var impostazioni = new Dictionary<string, string?>
        {
            ["Altcha:Abilitato"] = "true",
            ["Altcha:HmacSignatureSecret"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
            ["Altcha:Challenge:Cost"] = "10",
            ["Altcha:Challenge:CounterMin"] = "1",
            ["Altcha:Challenge:CounterMax"] = "30",
            ["Sicurezza:ChiaveSegreti"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
        };
        foreach (var coppia in extra ?? [])
        {
            impostazioni[coppia.Key] = coppia.Value;
        }

        return impostazioni;
    }

    private static async Task<string> ProvaAsync(SessioneProva sessione)
    {
        using var risposta = await sessione.GetAsync("/altcha/sfida");
        Assert.Equal(HttpStatusCode.OK, risposta.StatusCode);
        var sfida = JsonSerializer.Deserialize<Challenge>(await risposta.Content.ReadAsStringAsync(Ct), AltchaJson.SerializerOptions)!;
        var soluzione = AltchaPow.SolveChallenge(new SolveChallengeOptions { Challenge = sfida }, Ct);
        Assert.NotNull(soluzione);
        return Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(new Payload { Challenge = sfida, Solution = soluzione }, AltchaJson.SerializerOptions));
    }

    private static Dictionary<string, string> It(string testo, string? en = null) =>
        en is null ? new() { ["it"] = testo } : new() { ["it"] = testo, ["en"] = en };

    internal static DatiModulo Dati(StatoModulo stato = StatoModulo.Attivo, string? percorsoDopoInvio = null, string nome = "Contatti", ImpostazioniModulo? impostazioni = null) =>
        new(
            nome,
            stato,
            new DefinizioneModulo(
                [
                    new CampoModulo("c1", TipoCampoModulo.TestoBreve, true, 100, It("Il tuo nome"), new Dictionary<string, string>(), []),
                    new CampoModulo("c2", TipoCampoModulo.Email, true, 254, It("Email"), new Dictionary<string, string>(), []),
                    new CampoModulo("c3", TipoCampoModulo.SceltaSingola, false, 200, It("Argomento"), new Dictionary<string, string>(), [It("Informazioni"), It("Assistenza")]),
                    new CampoModulo("c4", TipoCampoModulo.Consenso, true, 200, It("Accetto l'informativa"), new Dictionary<string, string>(), []),
                ],
                It("Invia"),
                It("Grazie, ti risponderemo presto."),
                percorsoDopoInvio,
                "/privacy"),
            impostazioni ?? ImpostazioniModulo.Predefinite);

    internal static async Task<Guid> CreaModuloAsync(ScenarioWeb scenario, DatiProva.TenantCreato tenant, Guid attore, DatiModulo dati)
    {
        await using var ambito = scenario.Fabbrica.Services.CreateAsyncScope();
        ambito.ServiceProvider.GetRequiredService<ContestoTenant>().Imposta(new DatiTenantRisolto(tenant.IdTenant, tenant.IdSito, tenant.IdDominio, tenant.Host, false));
        var esito = await ambito.ServiceProvider.GetRequiredService<IServizioModuli>().CreaAsync(attore, dati, Ct);
        Assert.True(esito.Riuscito, string.Join(";", esito.Errori));
        return esito.Id!.Value;
    }

    private static async Task<List<InvioModulo>> InviiAsync(ScenarioWeb scenario, Guid? idModulo = null)
    {
        await using var ambito = scenario.Fabbrica.Services.CreateAsyncScope();
        return await ambito.ServiceProvider.GetRequiredService<ContestoDatabase>().InviiModulo.IgnoreQueryFilters().AsNoTracking()
            .Where(i => idModulo == null || i.IdModulo == idModulo).ToListAsync(Ct);
    }

    private static List<KeyValuePair<string, string>> Valori(string prova, string nome = "Anna <b>Rossi</b>", string email = "Anna@Esempio.it", string? consenso = "si") =>
    [
        new("campo_c1", nome),
        new("campo_c2", email),
        new("campo_c3", "o2"),
        .. consenso is null ? [] : new[] { new KeyValuePair<string, string>("campo_c4", consenso) },
        new("lingua", "it"),
        new("altcha", prova),
    ];

    [Fact]
    public async Task SenzaProvaOConUnaProvaSbagliataNessunInvioVieneSalvato()
    {
        await using var scenario = await ScenarioWeb.CreaAsync(impostazioni: Attivo());
        var admin = await scenario.CreaTenantAdminAsync("anna@esempio.it");
        var id = await CreaModuloAsync(scenario, scenario.A, admin, Dati());
        var anonimo = scenario.SessioneA();
        var alterata = JsonSerializer.Deserialize<Payload>(Convert.FromBase64String(await ProvaAsync(anonimo)), AltchaJson.SerializerOptions)!;
        alterata.Solution.DerivedKey = new string('0', alterata.Solution.DerivedKey.Length);

        foreach (var prova in new[] { string.Empty, "non-e-una-prova", Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(alterata, AltchaJson.SerializerOptions)) })
        {
            using var risposta = await anonimo.InviaModuloAsync(PaginaConToken, Valori(prova), $"/moduli/{id:D}");
            var html = await risposta.Content.ReadAsStringAsync(Ct);
            Assert.Equal(HttpStatusCode.OK, risposta.StatusCode);
            Assert.Contains("dv-modulo-errore", html, StringComparison.Ordinal);
            Assert.Contains("value=\"Anna &lt;b&gt;Rossi&lt;/b&gt;\"", html, StringComparison.Ordinal);
            Assert.DoesNotContain("<b>Rossi", html, StringComparison.Ordinal);
        }

        Assert.Empty(await InviiAsync(scenario));
    }

    [Fact]
    public async Task UnaProvaValidaSalvaLIstantaneaEReindirizzaAllaConferma()
    {
        await using var scenario = await ScenarioWeb.CreaAsync(impostazioni: Attivo());
        var admin = await scenario.CreaTenantAdminAsync("anna@esempio.it");
        var id = await CreaModuloAsync(scenario, scenario.A, admin, Dati());
        var anonimo = scenario.SessioneA();

        using var risposta = await anonimo.InviaModuloAsync(PaginaConToken, [.. Valori(await ProvaAsync(anonimo)), new("Ruolo", "TenantAdmin"), new("campo_c9", "intruso")], $"/moduli/{id:D}");

        Assert.Equal($"/moduli/{id:D}/inviato?lingua=it", SessioneProva.Destinazione(risposta));
        var invio = Assert.Single(await InviiAsync(scenario));
        Assert.Equal(id, invio.IdModulo);
        Assert.Equal(scenario.A.IdTenant, invio.IdTenant);
        Assert.Equal(StatoInvioModulo.Nuovo, invio.Stato);
        Assert.Equal("it", invio.CodiceLingua);
        var contenuto = SerializzatoreModulo.LeggiContenuto(invio.ContenutoJson);
        Assert.Equal(["c1", "c2", "c3", "c4"], contenuto.Select(v => v.Id));
        Assert.Equal(["Anna <b>Rossi</b>"], contenuto[0].Valori);
        Assert.Equal("Il tuo nome", contenuto[0].Etichetta);
        Assert.Equal(["anna@esempio.it"], contenuto[1].Valori);
        Assert.Equal(["Assistenza"], contenuto[2].Valori);
        Assert.DoesNotContain("intruso", invio.ContenutoJson, StringComparison.Ordinal);
        Assert.DoesNotContain("TenantAdmin", invio.ContenutoJson, StringComparison.Ordinal);

        var conferma = await (await anonimo.GetAsync(SessioneProva.Destinazione(risposta)!)).Content.ReadAsStringAsync(Ct);
        Assert.Contains("Grazie, ti risponderemo presto.", conferma, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnaProvaSiUsaUnaVoltaSola()
    {
        await using var scenario = await ScenarioWeb.CreaAsync(impostazioni: Attivo());
        var admin = await scenario.CreaTenantAdminAsync("anna@esempio.it");
        var id = await CreaModuloAsync(scenario, scenario.A, admin, Dati());
        var anonimo = scenario.SessioneA();
        var prova = await ProvaAsync(anonimo);

        using var prima = await anonimo.InviaModuloAsync(PaginaConToken, Valori(prova), $"/moduli/{id:D}");
        using var riuso = await anonimo.InviaModuloAsync(PaginaConToken, Valori(prova), $"/moduli/{id:D}");

        Assert.True(SessioneProva.EReindirizzamento(prima));
        Assert.Equal(HttpStatusCode.OK, riuso.StatusCode);
        Assert.Single(await InviiAsync(scenario));
    }

    [Fact]
    public async Task IValoriNonValidiMostranoGliErroriPerCampoENonSalvanoNulla()
    {
        await using var scenario = await ScenarioWeb.CreaAsync(impostazioni: Attivo());
        var admin = await scenario.CreaTenantAdminAsync("anna@esempio.it");
        var id = await CreaModuloAsync(scenario, scenario.A, admin, Dati());
        var anonimo = scenario.SessioneA();

        using var risposta = await anonimo.InviaModuloAsync(PaginaConToken, Valori(await ProvaAsync(anonimo), nome: " ", email: "non-una-email", consenso: null), $"/moduli/{id:D}");
        var html = WebUtility.HtmlDecode(await risposta.Content.ReadAsStringAsync(Ct));

        Assert.Equal(HttpStatusCode.OK, risposta.StatusCode);
        Assert.Contains("Questo campo è obbligatorio.", html, StringComparison.Ordinal);
        Assert.Contains("Il valore non è valido.", html, StringComparison.Ordinal);
        Assert.Contains("value=\"non-una-email\"", html, StringComparison.Ordinal);
        Assert.Empty(await InviiAsync(scenario));
    }

    [Fact]
    public async Task UnModuloInBozzaArchiviatoOInesistenteNonAccettaInvii()
    {
        await using var scenario = await ScenarioWeb.CreaAsync(impostazioni: Attivo());
        var admin = await scenario.CreaTenantAdminAsync("anna@esempio.it");
        var bozza = await CreaModuloAsync(scenario, scenario.A, admin, Dati(StatoModulo.Bozza));
        var archiviato = await CreaModuloAsync(scenario, scenario.A, admin, Dati(StatoModulo.Archiviato, nome: "Vecchio"));
        var anonimo = scenario.SessioneA();

        foreach (var id in new[] { bozza, archiviato, Guid.NewGuid() })
        {
            using var risposta = await anonimo.InviaModuloAsync(PaginaConToken, Valori(await ProvaAsync(anonimo)), $"/moduli/{id:D}");
            Assert.Equal(HttpStatusCode.NotFound, risposta.StatusCode);
            using var conferma = await anonimo.GetAsync($"/moduli/{id:D}/inviato");
            Assert.Equal(HttpStatusCode.NotFound, conferma.StatusCode);
        }

        Assert.Empty(await InviiAsync(scenario));
    }

    [Fact]
    public async Task UnModuloDiUnAltroTenantNonSiPuInviareDalSitoSbagliato()
    {
        await using var scenario = await ScenarioWeb.CreaAsync(impostazioni: Attivo());
        var admin = await scenario.CreaTenantAdminAsync("anna@esempio.it");
        var id = await CreaModuloAsync(scenario, scenario.A, admin, Dati());
        var daB = scenario.SessioneB();

        using var risposta = await daB.InviaModuloAsync(PaginaConToken, Valori(await ProvaAsync(daB)), $"/moduli/{id:D}");

        Assert.Equal(HttpStatusCode.NotFound, risposta.StatusCode);
        Assert.Empty(await InviiAsync(scenario));
    }

    [Fact]
    public async Task ConLaProtezioneSpentaFuoriDalloSviluppoIlModuloNonAccettaNulla()
    {
        await using var scenario = await ScenarioWeb.CreaAsync("Staging");
        var admin = await scenario.CreaTenantAdminAsync("anna@esempio.it");
        var id = await CreaModuloAsync(scenario, scenario.A, admin, Dati());
        var anonimo = scenario.SessioneA();

        using var risposta = await anonimo.InviaModuloAsync(PaginaConToken, Valori(string.Empty), $"/moduli/{id:D}");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, risposta.StatusCode);
        Assert.Empty(await InviiAsync(scenario));
    }

    [Fact]
    public async Task InSviluppoSenzaProtezioneIlModuloFunzionaComunque()
    {
        await using var scenario = await ScenarioWeb.CreaAsync();
        var admin = await scenario.CreaTenantAdminAsync("anna@esempio.it");
        var id = await CreaModuloAsync(scenario, scenario.A, admin, Dati());

        using var risposta = await scenario.SessioneA().InviaModuloAsync(PaginaConToken, Valori(string.Empty), $"/moduli/{id:D}");

        Assert.True(SessioneProva.EReindirizzamento(risposta));
        Assert.Single(await InviiAsync(scenario));
    }

    [Fact]
    public async Task IlTettoGiornalieroDegliInviiRispondeConTroppeRichieste()
    {
        await using var scenario = await ScenarioWeb.CreaAsync(impostazioni: Attivo(new() { ["Moduli:InviiMassimiAlGiorno"] = "2" }));
        var admin = await scenario.CreaTenantAdminAsync("anna@esempio.it");
        var id = await CreaModuloAsync(scenario, scenario.A, admin, Dati());
        var anonimo = scenario.SessioneA();

        var codici = new List<HttpStatusCode>();
        for (var i = 0; i < 3; i++)
        {
            using var risposta = await anonimo.InviaModuloAsync(PaginaConToken, Valori(await ProvaAsync(anonimo)), $"/moduli/{id:D}");
            codici.Add(SessioneProva.EReindirizzamento(risposta) ? HttpStatusCode.Redirect : risposta.StatusCode);
        }

        Assert.Equal([HttpStatusCode.Redirect, HttpStatusCode.Redirect, HttpStatusCode.TooManyRequests], codici);
        Assert.Equal(2, (await InviiAsync(scenario)).Count);
    }

    [Fact]
    public async Task ConUnPercorsoDopoLInvioSiTornaALaPaginaScelta()
    {
        await using var scenario = await ScenarioWeb.CreaAsync(impostazioni: Attivo());
        var admin = await scenario.CreaTenantAdminAsync("anna@esempio.it");
        var id = await CreaModuloAsync(scenario, scenario.A, admin, Dati(percorsoDopoInvio: "/grazie"));
        var anonimo = scenario.SessioneA();

        using var risposta = await anonimo.InviaModuloAsync(PaginaConToken, Valori(await ProvaAsync(anonimo)), $"/moduli/{id:D}");

        Assert.Equal("/grazie", SessioneProva.Destinazione(risposta));
        Assert.Single(await InviiAsync(scenario));
    }

    [Fact]
    public async Task UnaRichiestaGetAlLIndirizzoDelModuloTornaAllaHome()
    {
        await using var scenario = await ScenarioWeb.CreaAsync();

        using var risposta = await scenario.SessioneA().GetAsync($"/moduli/{Guid.NewGuid():D}");

        Assert.Equal("/", SessioneProva.Destinazione(risposta));
    }

    [Fact]
    public async Task SenzaTokenAntiforgeryLInvioEBloccato()
    {
        await using var scenario = await ScenarioWeb.CreaAsync();
        var admin = await scenario.CreaTenantAdminAsync("anna@esempio.it");
        var id = await CreaModuloAsync(scenario, scenario.A, admin, Dati());

        using var risposta = await scenario.SessioneA().InviaModuloAsync(PaginaConToken, Valori(string.Empty), $"/moduli/{id:D}", conToken: false);

        Assert.Equal(HttpStatusCode.BadRequest, risposta.StatusCode);
        Assert.Empty(await InviiAsync(scenario));
    }

    private static async Task<IReadOnlyList<EmailRicevuta>> RecapitaAsync(ScenarioWeb scenario, ServerSmtpFinto server)
    {
        var prima = server.Ricevute.Count;
        while (await scenario.Fabbrica.Services.GetRequiredService<ElaboratoreOutbox>().ElaboraLottoAsync(Ct) > 0)
        {
        }

        return [.. server.Ricevute.Skip(prima)];
    }

    [Fact]
    public async Task LaRichiestaAvvisaIDestinatariDelModuloConIDatiInviatiESenzaDatiInChiaroNellOutbox()
    {
        await using var scenario = await ScenarioWeb.CreaAsync(impostazioni: Attivo());
        await using var server = new ServerSmtpFinto();
        await DatiProva.ConfiguraSmtpAsync(scenario.Fabbrica.Services, scenario.A.IdTenant, server.Porta, Ct);
        var admin = await scenario.CreaTenantAdminAsync("anna@esempio.it");
        var impostazioni = new ImpostazioniModulo(["vendite@esempio.it", "Info@Esempio.it"], false, false, 365);
        var id = await CreaModuloAsync(scenario, scenario.A, admin, Dati(impostazioni: impostazioni));
        var anonimo = scenario.SessioneA();

        using var risposta = await anonimo.InviaModuloAsync(PaginaConToken, Valori(await ProvaAsync(anonimo), nome: "Carla Verdi"), $"/moduli/{id:D}");
        Assert.True(SessioneProva.EReindirizzamento(risposta));
        var invio = Assert.Single(await InviiAsync(scenario));

        await using (var ambito = scenario.Fabbrica.Services.CreateAsyncScope())
        {
            var payload = string.Concat(await ambito.ServiceProvider.GetRequiredService<ContestoDatabase>().MessaggiOutbox.IgnoreQueryFilters().Select(m => m.Payload).ToListAsync(Ct));
            Assert.DoesNotContain("Carla", payload, StringComparison.Ordinal);
            Assert.DoesNotContain("anna@esempio.it", payload, StringComparison.Ordinal);
        }

        var email = await RecapitaAsync(scenario, server);
        Assert.Equal(["info@esempio.it", "vendite@esempio.it"], email.SelectMany(e => e.Destinatari).Select(d => d.Trim('<', '>').ToLowerInvariant()).Order(StringComparer.Ordinal));
        Assert.All(email, e =>
        {
            Assert.Contains("Contatti", e.Oggetto, StringComparison.Ordinal);
            Assert.Contains("Il tuo nome: Carla Verdi", e.Testo, StringComparison.Ordinal);
            Assert.Contains("Email: anna@esempio.it", e.Testo, StringComparison.Ordinal);
            Assert.Contains($"https://a.esempio.it/admin/moduli/richieste/{id:D}/{invio.Id:D}", e.Testo, StringComparison.Ordinal);
        });
        Assert.DoesNotContain(email, e => e.Destinatari.Any(d => d.Contains("anna@esempio.it", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public async Task ConTuttiGliAmministratoriERispostaAutomaticaAvvisaGliAdminERispondeAlVisitatore()
    {
        await using var scenario = await ScenarioWeb.CreaAsync(impostazioni: Attivo());
        await using var server = new ServerSmtpFinto();
        await DatiProva.ConfiguraSmtpAsync(scenario.Fabbrica.Services, scenario.A.IdTenant, server.Porta, Ct);
        var admin = await scenario.CreaTenantAdminAsync("anna@esempio.it");
        var id = await CreaModuloAsync(scenario, scenario.A, admin, Dati(impostazioni: new ImpostazioniModulo([], true, true, 365)));
        var anonimo = scenario.SessioneA();

        using var risposta = await anonimo.InviaModuloAsync(PaginaConToken, Valori(await ProvaAsync(anonimo), nome: "Carla Verdi", email: "carla@esempio.it"), $"/moduli/{id:D}");
        Assert.True(SessioneProva.EReindirizzamento(risposta));
        var email = await RecapitaAsync(scenario, server);

        Assert.Equal(2, email.Count);
        var perAdmin = Assert.Single(email, e => e.Destinatari.Any(d => d.Contains("anna@esempio.it", StringComparison.OrdinalIgnoreCase)));
        Assert.Contains("Carla Verdi", perAdmin.Testo, StringComparison.Ordinal);
        var perVisitatore = Assert.Single(email, e => e.Destinatari.Any(d => d.Contains("carla@esempio.it", StringComparison.OrdinalIgnoreCase)));
        Assert.Contains("Grazie, ti risponderemo presto.", perVisitatore.Testo, StringComparison.Ordinal);
        Assert.Contains("Il tuo nome: Carla Verdi", perVisitatore.Testo, StringComparison.Ordinal);
        Assert.DoesNotContain("/admin/", perVisitatore.Testo, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SenzaRispostaAutomaticaOSenzaCanaleEmailNonPartePiuDelNecessario()
    {
        await using var senzaCanale = await ScenarioWeb.CreaAsync(impostazioni: Attivo());
        var admin = await senzaCanale.CreaTenantAdminAsync("anna@esempio.it");
        var id = await CreaModuloAsync(senzaCanale, senzaCanale.A, admin, Dati(impostazioni: new ImpostazioniModulo([], true, true, 365)));
        var anonimo = senzaCanale.SessioneA();

        using var risposta = await anonimo.InviaModuloAsync(PaginaConToken, Valori(await ProvaAsync(anonimo)), $"/moduli/{id:D}");

        Assert.True(SessioneProva.EReindirizzamento(risposta));
        Assert.Single(await InviiAsync(senzaCanale));
        await using var ambito = senzaCanale.Fabbrica.Services.CreateAsyncScope();
        Assert.Equal(0, await ambito.ServiceProvider.GetRequiredService<ContestoDatabase>().MessaggiOutbox.IgnoreQueryFilters().CountAsync(m => m.Tipo == "InvioEmail", Ct));

        await using var scenario = await ScenarioWeb.CreaAsync(impostazioni: Attivo());
        await using var server = new ServerSmtpFinto();
        await DatiProva.ConfiguraSmtpAsync(scenario.Fabbrica.Services, scenario.A.IdTenant, server.Porta, Ct);
        var adminB = await scenario.CreaTenantAdminAsync("anna@esempio.it");
        var idB = await CreaModuloAsync(scenario, scenario.A, adminB, Dati(impostazioni: new ImpostazioniModulo([], true, false, 365)));
        var visitatore = scenario.SessioneA();
        using var senzaRisposta = await visitatore.InviaModuloAsync(PaginaConToken, Valori(await ProvaAsync(visitatore), email: "carla@esempio.it"), $"/moduli/{idB:D}");
        var email = await RecapitaAsync(scenario, server);

        Assert.True(SessioneProva.EReindirizzamento(senzaRisposta));
        Assert.Single(email);
        Assert.DoesNotContain(email, e => e.Destinatari.Any(d => d.Contains("carla@esempio.it", StringComparison.OrdinalIgnoreCase)));
    }
}
