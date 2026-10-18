using System.Net;
using DvCms.Data;
using DvCms.IntegrationTests.Supporto;
using DvCms.Models.Moduli;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DvCms.IntegrationTests.Web;

/// <summary>Pagine di gestione dei moduli di contatto (ADR-0027): accesso riservato, creazione con campi, anteprima e cambi di stato.</summary>
public class ModuliWebTest
{
    private const string Elenco = "/admin/moduli";
    private const string Nuova = "/admin/moduli/modifica";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static List<KeyValuePair<string, string>> Campi(string nome = "Contatti", string stato = "Bozza")
    {
        List<KeyValuePair<string, string>> campi =
        [
            new("Input.Nome", nome),
            new("Input.Stato", stato),
            new("Input.Pulsante[it]", "Invia"),
            new("Input.Pulsante[en]", "Send"),
            new("Input.Conferma[it]", "Grazie, ti risponderemo."),
            new("Input.Conferma[en]", string.Empty),
            new("Input.TuttiGliAmministratori", "true"),
            new("Input.GiorniConservazione", "365"),
            new("Input.Campi[0].Id", "c1"),
            new("Input.Campi[0].Tipo", "TestoBreve"),
            new("Input.Campi[0].LunghezzaMassima", "100"),
            new("Input.Campi[0].Obbligatorio", "true"),
            new("Input.Campi[0].Etichette[it]", "Il tuo nome"),
            new("Input.Campi[0].Etichette[en]", "Your name"),
            new("Input.Campi[1].Id", "c2"),
            new("Input.Campi[1].Tipo", "Email"),
            new("Input.Campi[1].LunghezzaMassima", "254"),
            new("Input.Campi[1].Obbligatorio", "true"),
            new("Input.Campi[1].Etichette[it]", "Email"),
        ];
        return campi;
    }

    [Fact]
    public async Task SoloIlTenantAdminAccedeALleSchermate()
    {
        await using var scenario = await ScenarioWeb.CreaAsync();
        await scenario.CreaTenantAdminAsync("anna@esempio.it");
        await scenario.CreaPlatformAdminAsync("root@esempio.it");

        using var anonimo = await scenario.SessioneA().GetAsync(Elenco);
        var platform = scenario.SessioneA();
        await platform.AccediAsync("root@esempio.it", DatiProva.PasswordProva);
        using var comePlatform = await platform.GetAsync(Elenco);
        using var comePlatformModifica = await platform.GetAsync(Nuova);
        var anna = scenario.SessioneA();
        await anna.AccediAsync("anna@esempio.it", DatiProva.PasswordProva);
        using var comeAnna = await anna.GetAsync(Elenco);
        using var modificaAnna = await anna.GetAsync(Nuova);

        Assert.True(SessioneProva.EReindirizzamento(anonimo) || anonimo.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden);
        Assert.Equal(HttpStatusCode.Forbidden, comePlatform.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, comePlatformModifica.StatusCode);
        Assert.Equal(HttpStatusCode.OK, comeAnna.StatusCode);
        Assert.Equal(HttpStatusCode.OK, modificaAnna.StatusCode);
        Assert.Contains("Moduli di contatto", await comeAnna.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnTenantAdminDiUnAltroTenantNonEntra()
    {
        await using var scenario = await ScenarioWeb.CreaAsync();
        await scenario.CreaTenantAdminAsync("anna@esempio.it");
        var anna = scenario.SessioneB();
        await anna.AccediAsync("anna@esempio.it", DatiProva.PasswordProva);

        using var elenco = await anna.GetAsync(Elenco);
        using var salva = await anna.InviaModuloAsync(Nuova, Campi(), $"{Nuova}?handler=Salva", conToken: false);

        Assert.NotEqual(HttpStatusCode.OK, elenco.StatusCode);
        Assert.NotEqual(HttpStatusCode.OK, salva.StatusCode);
    }

    [Fact]
    public async Task CreaUnModuloConICampiLoMostraEPoiLoPubblica()
    {
        await using var scenario = await ScenarioWeb.CreaAsync();
        await scenario.CreaTenantAdminAsync("anna@esempio.it");
        var anna = scenario.SessioneA();
        await anna.AccediAsync("anna@esempio.it", DatiProva.PasswordProva);

        using var creato = await anna.InviaModuloAsync(Nuova, Campi(), $"{Nuova}?handler=Salva");
        Assert.True(SessioneProva.EReindirizzamento(creato), await creato.Content.ReadAsStringAsync(Ct));
        var destinazione = SessioneProva.Destinazione(creato)!;
        Assert.StartsWith("/admin/moduli/modifica/", destinazione, StringComparison.Ordinal);

        var modifica = await (await anna.GetAsync(destinazione)).Content.ReadAsStringAsync(Ct);
        Assert.Contains("Modulo salvato.", modifica, StringComparison.Ordinal);
        Assert.Contains("value=\"Contatti\"", modifica, StringComparison.Ordinal);
        Assert.Contains("value=\"Il tuo nome\"", modifica, StringComparison.Ordinal);
        Assert.Contains("value=\"Your name\"", modifica, StringComparison.Ordinal);

        var elenco = await (await anna.GetAsync(Elenco)).Content.ReadAsStringAsync(Ct);
        Assert.Contains("Contatti", elenco, StringComparison.Ordinal);
        Assert.Contains("Bozza", elenco, StringComparison.Ordinal);

        var id = Guid.Parse(destinazione[(destinazione.LastIndexOf('/') + 1)..]);
        using var pubblica = await anna.InviaModuloAsync(Elenco, [], $"{Elenco}?handler=Pubblica&id={id}");
        Assert.True(SessioneProva.EReindirizzamento(pubblica));
        Assert.Contains("Pubblicato", await (await anna.GetAsync(Elenco)).Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);

        await using var ambito = scenario.Fabbrica.Services.CreateAsyncScope();
        var riga = await ambito.ServiceProvider.GetRequiredService<ContestoDatabase>().Moduli.IgnoreQueryFilters().SingleAsync(m => m.Id == id, Ct);
        Assert.Equal(StatoModulo.Attivo, riga.Stato);
        Assert.Equal(scenario.A.IdSito, riga.IdSito);
    }

    [Fact]
    public async Task UnModuloNonValidoMostraGliErroriENonSiSalva()
    {
        await using var scenario = await ScenarioWeb.CreaAsync();
        await scenario.CreaTenantAdminAsync("anna@esempio.it");
        var anna = scenario.SessioneA();
        await anna.AccediAsync("anna@esempio.it", DatiProva.PasswordProva);
        var campi = Campi(nome: " ");
        campi.RemoveAll(c => c.Key == "Input.Campi[0].Etichette[it]");
        campi.Add(new("Input.Campi[0].Etichette[it]", string.Empty));

        using var risposta = await anna.InviaModuloAsync(Nuova, campi, $"{Nuova}?handler=Salva");
        var html = await risposta.Content.ReadAsStringAsync(Ct);

        Assert.Equal(HttpStatusCode.OK, risposta.StatusCode);
        Assert.Contains("alert-danger", html, StringComparison.Ordinal);
        Assert.Contains("nome del modulo", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Contatti", await (await anna.GetAsync(Elenco)).Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AggiungereTogliereESpostareICampiRicaricaLaPaginaSenzaSalvare()
    {
        await using var scenario = await ScenarioWeb.CreaAsync();
        await scenario.CreaTenantAdminAsync("anna@esempio.it");
        var anna = scenario.SessioneA();
        await anna.AccediAsync("anna@esempio.it", DatiProva.PasswordProva);

        var aggiunto = await (await anna.InviaModuloAsync(Nuova, [.. Campi(), new("nuovoTipo", "SceltaSingola")], $"{Nuova}?handler=AggiungiCampo")).Content.ReadAsStringAsync(Ct);
        Assert.Contains("Input.Campi[2].Id", aggiunto, StringComparison.Ordinal);
        Assert.Contains("value=\"c3\"", aggiunto, StringComparison.Ordinal);
        Assert.Contains("Opzioni, una per riga", aggiunto, StringComparison.Ordinal);

        var tolto = await (await anna.InviaModuloAsync(Nuova, Campi(), $"{Nuova}?handler=RimuoviCampo&indice=0")).Content.ReadAsStringAsync(Ct);
        Assert.DoesNotContain("value=\"Il tuo nome\"", tolto, StringComparison.Ordinal);
        Assert.Contains("value=\"c2\"", tolto, StringComparison.Ordinal);

        var spostato = await (await anna.InviaModuloAsync(Nuova, Campi(), $"{Nuova}?handler=SpostaCampo&indice=0&verso=1")).Content.ReadAsStringAsync(Ct);
        Assert.True(spostato.IndexOf("value=\"c2\"", StringComparison.Ordinal) < spostato.IndexOf("value=\"c1\"", StringComparison.Ordinal));
        Assert.DoesNotContain("Contatti", await (await anna.GetAsync(Elenco)).Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task LAnteprimaMostraIlModuloComeLoVedraIlVisitatore()
    {
        await using var scenario = await ScenarioWeb.CreaAsync();
        await scenario.CreaTenantAdminAsync("anna@esempio.it");
        var anna = scenario.SessioneA();
        await anna.AccediAsync("anna@esempio.it", DatiProva.PasswordProva);

        var html = await (await anna.InviaModuloAsync(Nuova, Campi(), $"{Nuova}?handler=Anteprima")).Content.ReadAsStringAsync(Ct);

        Assert.Contains("class=\"dv-modulo\"", html, StringComparison.Ordinal);
        Assert.Contains("name=\"campo_c1\"", html, StringComparison.Ordinal);
        Assert.Contains("type=\"email\"", html, StringComparison.Ordinal);
        Assert.Contains("disabled", html[html.IndexOf("class=\"dv-modulo\"", StringComparison.Ordinal)..], StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnModuloConRichiesteNonSiEliminaDallElenco()
    {
        await using var scenario = await ScenarioWeb.CreaAsync();
        await scenario.CreaTenantAdminAsync("anna@esempio.it");
        var anna = scenario.SessioneA();
        await anna.AccediAsync("anna@esempio.it", DatiProva.PasswordProva);
        using var creato = await anna.InviaModuloAsync(Nuova, Campi(), $"{Nuova}?handler=Salva");
        var destinazione = SessioneProva.Destinazione(creato)!;
        var id = Guid.Parse(destinazione[(destinazione.LastIndexOf('/') + 1)..]);

        using var elimina = await anna.InviaModuloAsync(Elenco, [], $"{Elenco}?handler=Elimina&id={id}");

        Assert.True(SessioneProva.EReindirizzamento(elimina));
        Assert.DoesNotContain("Contatti", await (await anna.GetAsync(Elenco)).Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
    }
}
