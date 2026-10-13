using System.Net;
using System.Security.Cryptography;
using DvCms.Infrastructure.Outbox;
using DvCms.Infrastructure.Persistenza;
using DvCms.IntegrationTests.Supporto;
using DvCms.Models.Accessi;
using DvCms.Models.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DvCms.IntegrationTests.Web;

/// <summary>Schermata di configurazione email del tenant nel menu Platform (B-009) attraverso l'applicazione web.</summary>
public class EmailTenantWebTest
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly Dictionary<string, string?> _conChiave = new() { ["Sicurezza:ChiaveSegreti"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) };

    [Fact]
    public async Task UnTenantAdminRiceve403SuLeggereScrivereEProvare()
    {
        await using var scenario = await ScenarioWeb.CreaAsync(impostazioni: _conChiave);
        await scenario.CreaTenantAdminAsync("anna@esempio.it");
        var anna = scenario.SessioneA();
        await anna.AccediAsync("anna@esempio.it", DatiProva.PasswordProva);
        var pagina = $"/admin/platform/tenant/{scenario.A.IdTenant}/email";

        using var lettura = await anna.GetAsync(pagina);
        using var salva = await anna.InviaModuloAsync("/admin", [new("provider", "Smtp"), new("v_Host", "x")], $"{pagina}?handler=Salva");
        using var prova = await anna.InviaModuloAsync("/admin", [new("destinatario", "x@esempio.it")], $"{pagina}?handler=Prova");

        Assert.Equal(HttpStatusCode.Forbidden, lettura.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, salva.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, prova.StatusCode);
    }

    [Fact]
    public async Task MostraSoloICampiDelCanaleScelto()
    {
        await using var scenario = await ScenarioWeb.CreaAsync(impostazioni: _conChiave);
        await scenario.CreaPlatformAdminAsync("platform@esempio.it");
        var sessione = scenario.SessioneB();
        await sessione.AccediAsync("platform@esempio.it", DatiProva.PasswordProva);
        var pagina = $"/admin/platform/tenant/{scenario.A.IdTenant}/email";

        var smtp = await (await sessione.GetAsync(pagina)).Content.ReadAsStringAsync(Ct);
        var sendGrid = await (await sessione.GetAsync($"{pagina}?provider=SendGrid")).Content.ReadAsStringAsync(Ct);

        Assert.Contains("name=\"v_Host\"", smtp, StringComparison.Ordinal);
        Assert.Contains("name=\"v_Password\"", smtp, StringComparison.Ordinal);
        Assert.DoesNotContain("name=\"v_ChiaveApi\"", smtp, StringComparison.Ordinal);
        Assert.Contains("name=\"v_ChiaveApi\"", sendGrid, StringComparison.Ordinal);
        Assert.DoesNotContain("name=\"v_Host\"", sendGrid, StringComparison.Ordinal);
        Assert.Contains("type=\"password\"", sendGrid, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SalvaIlCanaleEIlSegretoNonTornaMaiNellaPagina()
    {
        await using var scenario = await ScenarioWeb.CreaAsync(impostazioni: _conChiave);
        await scenario.CreaPlatformAdminAsync("platform@esempio.it");
        var sessione = scenario.SessioneB();
        await sessione.AccediAsync("platform@esempio.it", DatiProva.PasswordProva);
        var pagina = $"/admin/platform/tenant/{scenario.A.IdTenant}/email";

        using var salva = await sessione.InviaModuloAsync(
            pagina,
            [
                new("provider", "Smtp"), new("v_Host", "smtp.esempio.it"), new("v_Porta", "587"), new("v_ModalitaTls", "StartTls"),
                new("v_Utente", "mittente"), new("v_Password", "PASSWORD-SEGRETA"), new("v_IndirizzoMittente", "noreply@esempio.it"),
            ],
            $"{pagina}?handler=Salva");
        using var dopo = await sessione.GetAsync(pagina);

        Assert.True(SessioneProva.EReindirizzamento(salva));
        var html = await dopo.Content.ReadAsStringAsync(Ct);
        Assert.Contains("smtp.esempio.it", html, StringComparison.Ordinal);
        Assert.DoesNotContain("PASSWORD-SEGRETA", html, StringComparison.Ordinal);
        Assert.Contains("lascia vuoto per non cambiarlo", html, StringComparison.Ordinal);
        await using var ambito = scenario.Fabbrica.Services.CreateAsyncScope();
        var riga = await ambito.ServiceProvider.GetRequiredService<ContestoDatabase>().ImpostazioniEmailTenant.IgnoreQueryFilters().SingleAsync(Ct);
        Assert.DoesNotContain("PASSWORD-SEGRETA", riga.ValoriJson + riga.SegretiProtetti, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnModuloNonValidoEvidenziaICampiENonRimandaIlSegreto()
    {
        await using var scenario = await ScenarioWeb.CreaAsync(impostazioni: _conChiave);
        await scenario.CreaPlatformAdminAsync("platform@esempio.it");
        var sessione = scenario.SessioneB();
        await sessione.AccediAsync("platform@esempio.it", DatiProva.PasswordProva);
        var pagina = $"/admin/platform/tenant/{scenario.A.IdTenant}/email";

        using var risposta = await sessione.InviaModuloAsync(
            pagina,
            [new("provider", "Smtp"), new("v_Host", "smtp.esempio.it"), new("v_Porta", "0"), new("v_ModalitaTls", "StartTls"), new("v_Password", "PASSWORD-SEGRETA"), new("v_IndirizzoMittente", "noreply@esempio.it")],
            $"{pagina}?handler=Salva");

        Assert.Equal(HttpStatusCode.OK, risposta.StatusCode);
        var html = await risposta.Content.ReadAsStringAsync(Ct);
        Assert.Contains("is-invalid", html, StringComparison.Ordinal);
        Assert.Contains("smtp.esempio.it", html, StringComparison.Ordinal);
        Assert.DoesNotContain("PASSWORD-SEGRETA", html, StringComparison.Ordinal);
        await using var ambito = scenario.Fabbrica.Services.CreateAsyncScope();
        Assert.Equal(0, await ambito.ServiceProvider.GetRequiredService<ContestoDatabase>().ImpostazioniEmailTenant.IgnoreQueryFilters().CountAsync(Ct));
    }

    [Fact]
    public async Task SenzaChiaveDeiSegretiIlSalvataggioConSegretoDaUnMessaggioChiaro()
    {
        await using var scenario = await ScenarioWeb.CreaAsync();
        await scenario.CreaPlatformAdminAsync("platform@esempio.it");
        var sessione = scenario.SessioneB();
        await sessione.AccediAsync("platform@esempio.it", DatiProva.PasswordProva);
        var pagina = $"/admin/platform/tenant/{scenario.A.IdTenant}/email";

        using var risposta = await sessione.InviaModuloAsync(
            pagina,
            [new("provider", "Smtp"), new("v_Host", "smtp.esempio.it"), new("v_Porta", "587"), new("v_ModalitaTls", "StartTls"), new("v_Password", "x"), new("v_IndirizzoMittente", "noreply@esempio.it")],
            $"{pagina}?handler=Salva");
        using var dopo = await sessione.GetAsync(pagina);

        Assert.Contains("chiave per cifrare i segreti", await dopo.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task LaProvaViaggiaNellOutboxEArrivaAlServerSmtp()
    {
        await using var server = new ServerSmtpFinto();
        await using var scenario = await ScenarioWeb.CreaAsync(impostazioni: _conChiave);
        await scenario.CreaPlatformAdminAsync("platform@esempio.it");
        var sessione = scenario.SessioneB();
        await sessione.AccediAsync("platform@esempio.it", DatiProva.PasswordProva);
        var pagina = $"/admin/platform/tenant/{scenario.A.IdTenant}/email";
        await sessione.InviaModuloAsync(
            pagina,
            [
                new("provider", "Smtp"), new("v_Host", "127.0.0.1"), new("v_Porta", server.Porta.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                new("v_ModalitaTls", "Nessuna"), new("v_IndirizzoMittente", "noreply@a.esempio.it"),
            ],
            $"{pagina}?handler=Salva");

        using var prova = await sessione.InviaModuloAsync(pagina, [new("destinatario", "prova@esempio.it")], $"{pagina}?handler=Prova");

        Assert.True(SessioneProva.EReindirizzamento(prova));
        Assert.Empty(server.Ricevute);
        await scenario.Fabbrica.Services.GetRequiredService<ElaboratoreOutbox>().ElaboraLottoAsync(Ct);
        var ricevuta = Assert.Single(server.Ricevute);
        Assert.Equal(["prova@esempio.it"], ricevuta.Destinatari);
        await using var ambito = scenario.Fabbrica.Services.CreateAsyncScope();
        Assert.Equal(StatoMessaggioOutbox.Completato, (await ambito.ServiceProvider.GetRequiredService<ContestoDatabase>().MessaggiOutbox.IgnoreQueryFilters().SingleAsync(Ct)).Stato);
    }

    [Fact]
    public async Task ErroriDiInvioEDestinatarioNonValidoSonoMostrati()
    {
        await using var scenario = await ScenarioWeb.CreaAsync(impostazioni: _conChiave);
        await scenario.CreaPlatformAdminAsync("platform@esempio.it");
        var sessione = scenario.SessioneB();
        await sessione.AccediAsync("platform@esempio.it", DatiProva.PasswordProva);
        var pagina = $"/admin/platform/tenant/{scenario.A.IdTenant}/email";

        // Senza impostazioni salvate la prova non parte.
        using var senza = await sessione.InviaModuloAsync(pagina, [new("destinatario", "prova@esempio.it")], $"{pagina}?handler=Prova");
        var htmlSenza = await (await sessione.GetAsync(pagina)).Content.ReadAsStringAsync(Ct);
        Assert.Contains("Salva prima le impostazioni email", htmlSenza, StringComparison.Ordinal);

        // Una configurazione irraggiungibile produce un errore visibile dopo l'elaborazione.
        await sessione.InviaModuloAsync(
            pagina,
            [new("provider", "Smtp"), new("v_Host", "127.0.0.1"), new("v_Porta", "1"), new("v_ModalitaTls", "Nessuna"), new("v_IndirizzoMittente", "noreply@a.esempio.it")],
            $"{pagina}?handler=Salva");
        await sessione.InviaModuloAsync(pagina, [new("destinatario", "prova@esempio.it")], $"{pagina}?handler=Prova");
        await scenario.Fabbrica.Services.GetRequiredService<ElaboratoreOutbox>().ElaboraLottoAsync(Ct);
        var htmlProblema = await (await sessione.GetAsync(pagina)).Content.ReadAsStringAsync(Ct);
        Assert.Contains("Nuovo tentativo", htmlProblema, StringComparison.Ordinal);
    }
}
