using System.Net;
using System.Text;
using DvCms.Contracts.Manutenzione;
using DvCms.Core.Accessi;
using DvCms.Core.Moduli;
using DvCms.Data;
using DvCms.DTO.Moduli;
using DvCms.IntegrationTests.Supporto;
using DvCms.Models.Moduli;
using DvCms.Services.Cms.Moduli;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DvCms.IntegrationTests.Web;

/// <summary>Casella delle richieste dei moduli (ADR-0027): accesso riservato, stati, cancellazione, esportazione CSV e fine conservazione.</summary>
public class RichiesteModuloWebTest
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Percorso(Guid modulo) => $"/admin/moduli/richieste/{modulo:D}";

    private static async Task<Guid> AggiungiAsync(ScenarioWeb scenario, DatiProva.TenantCreato tenant, Guid modulo, StatoInvioModulo stato = StatoInvioModulo.Nuovo, int giorniFa = 0, params (string Etichetta, string Valore)[] valori)
    {
        await using var ambito = scenario.Fabbrica.Services.CreateAsyncScope();
        var db = ambito.ServiceProvider.GetRequiredService<ContestoDatabase>();
        var id = Guid.NewGuid();
        db.InviiModulo.Add(new InvioModulo
        {
            Id = id,
            IdTenant = tenant.IdTenant,
            IdSito = tenant.IdSito,
            IdModulo = modulo,
            CodiceLingua = "it",
            Stato = stato,
            ContenutoJson = SerializzatoreModulo.ScriviContenuto([.. valori.Select((v, i) => new ValoreInviato("c" + (i + 1), v.Etichetta, TipoCampoModulo.TestoBreve, [v.Valore]))]),
            CreatoIlUtc = DateTime.UtcNow.AddDays(-giorniFa),
        });
        await db.SaveChangesAsync(Ct);
        return id;
    }

    private static async Task<(ScenarioWeb Scenario, SessioneProva Anna, Guid Modulo)> PreparaAsync()
    {
        var scenario = await ScenarioWeb.CreaAsync();
        var admin = await scenario.CreaTenantAdminAsync("anna@esempio.it");
        var modulo = await ModuloPubblicoWebTest.CreaModuloAsync(scenario, scenario.A, admin, ModuloPubblicoWebTest.Dati());
        var anna = scenario.SessioneA();
        await anna.AccediAsync("anna@esempio.it", DatiProva.PasswordProva);
        return (scenario, anna, modulo);
    }

    private static async Task<int> AuditAsync(ScenarioWeb scenario, string azione)
    {
        await using var ambito = scenario.Fabbrica.Services.CreateAsyncScope();
        return await ambito.ServiceProvider.GetRequiredService<ContestoDatabase>().RegistroAudit.IgnoreQueryFilters().CountAsync(a => a.Azione == azione, Ct);
    }

    [Fact]
    public async Task SoloIlTenantAdminDelSitoVedeLeRichieste()
    {
        var (scenario, anna, modulo) = await PreparaAsync();
        await using var _ = scenario;
        var richiesta = await AggiungiAsync(scenario, scenario.A, modulo, valori: ("Nome", "Carla"));
        await scenario.CreaPlatformAdminAsync("root@esempio.it");
        await DatiProva.CreaUtenzaAsync(scenario.Fabbrica.Services, DvCms.Models.Accessi.RuoloUtenza.TenantAdmin, scenario.B.IdTenant, "bruno@esempio.it", annullamento: Ct);

        using var anonimo = await scenario.SessioneA().GetAsync(Percorso(modulo));
        var platform = scenario.SessioneA();
        await platform.AccediAsync("root@esempio.it", DatiProva.PasswordProva);
        using var comePlatform = await platform.GetAsync(Percorso(modulo));
        using var comePlatformDettaglio = await platform.GetAsync($"{Percorso(modulo)}/{richiesta:D}");
        using var comePlatformCsv = await platform.GetAsync($"{Percorso(modulo)}?handler=Esporta");
        var bruno = scenario.SessioneB();
        await bruno.AccediAsync("bruno@esempio.it", DatiProva.PasswordProva);
        using var daB = await bruno.GetAsync(Percorso(modulo));
        using var daBDettaglio = await bruno.GetAsync($"{Percorso(modulo)}/{richiesta:D}");
        using var comeAnna = await anna.GetAsync(Percorso(modulo));

        Assert.True(SessioneProva.EReindirizzamento(anonimo) || anonimo.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden);
        Assert.Equal(HttpStatusCode.Forbidden, comePlatform.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, comePlatformDettaglio.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, comePlatformCsv.StatusCode);
        Assert.NotEqual(HttpStatusCode.OK, daB.StatusCode);
        Assert.NotEqual(HttpStatusCode.OK, daBDettaglio.StatusCode);
        Assert.Equal(HttpStatusCode.OK, comeAnna.StatusCode);
    }

    [Fact]
    public async Task LElencoMostraLeRichiesteConIlFiltroPerStatoELAnteprimaCodificata()
    {
        var (scenario, anna, modulo) = await PreparaAsync();
        await using var _ = scenario;
        await AggiungiAsync(scenario, scenario.A, modulo, valori: ("Nome", "Carla <script>x</script>"));
        await AggiungiAsync(scenario, scenario.A, modulo, StatoInvioModulo.Letto, valori: ("Nome", "Davide"));
        await AggiungiAsync(scenario, scenario.A, modulo, StatoInvioModulo.Archiviato, valori: ("Nome", "Elisa"));

        var tutte = await (await anna.GetAsync(Percorso(modulo))).Content.ReadAsStringAsync(Ct);
        var soloLette = await (await anna.GetAsync($"{Percorso(modulo)}?stato=Letto")).Content.ReadAsStringAsync(Ct);

        Assert.Contains("Carla", tutte, StringComparison.Ordinal);
        Assert.Contains("Davide", tutte, StringComparison.Ordinal);
        Assert.Contains("Elisa", tutte, StringComparison.Ordinal);
        Assert.DoesNotContain("<script>x", tutte, StringComparison.Ordinal);
        Assert.Contains("Davide", soloLette, StringComparison.Ordinal);
        Assert.DoesNotContain("Carla", soloLette, StringComparison.Ordinal);
        Assert.DoesNotContain("Elisa", soloLette, StringComparison.Ordinal);
    }

    [Fact]
    public async Task IlDettaglioMostraIValoriCodificatiEPermetteDiCambiareStatoEdEliminare()
    {
        var (scenario, anna, modulo) = await PreparaAsync();
        await using var _ = scenario;
        var richiesta = await AggiungiAsync(scenario, scenario.A, modulo, valori: [("Nome", "Carla <b>Verdi</b>"), ("Messaggio", "riga uno\nriga due")]);
        var percorso = $"{Percorso(modulo)}/{richiesta:D}";

        var html = await (await anna.GetAsync(percorso)).Content.ReadAsStringAsync(Ct);
        Assert.Contains("Carla &lt;b&gt;Verdi&lt;/b&gt;", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<b>Verdi", html, StringComparison.Ordinal);

        using var letta = await anna.InviaModuloAsync(percorso, [], $"{percorso}?handler=Letta");
        Assert.True(SessioneProva.EReindirizzamento(letta));
        Assert.Contains("Letta", await (await anna.GetAsync(percorso)).Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        using var archiviata = await anna.InviaModuloAsync(percorso, [], $"{percorso}?handler=Archivia");
        Assert.True(SessioneProva.EReindirizzamento(archiviata));
        Assert.Contains("Archiviata", await (await anna.GetAsync(percorso)).Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);

        using var eliminata = await anna.InviaModuloAsync(percorso, [], $"{percorso}?handler=Elimina");
        Assert.Equal(Percorso(modulo), SessioneProva.Destinazione(eliminata));
        using var dopo = await anna.GetAsync(percorso);
        Assert.Equal(HttpStatusCode.NotFound, dopo.StatusCode);
        Assert.Equal(1, await AuditAsync(scenario, AzioniAudit.EliminazioneInvioModulo));
    }

    [Fact]
    public async Task UnaRichiestaDiUnAltroModuloONonEsisteNonSiTocca()
    {
        var (scenario, anna, modulo) = await PreparaAsync();
        await using var _ = scenario;
        var richiesta = await AggiungiAsync(scenario, scenario.A, modulo, valori: ("Nome", "Carla"));
        var altroModulo = await ModuloPubblicoWebTest.CreaModuloAsync(scenario, scenario.A, await IdAdminAsync(scenario), ModuloPubblicoWebTest.Dati(nome: "Altro"));

        using var sbagliato = await anna.GetAsync($"{Percorso(altroModulo)}/{richiesta:D}");
        using var eliminaSbagliata = await anna.InviaModuloAsync($"{Percorso(modulo)}/{richiesta:D}", [], $"{Percorso(altroModulo)}/{richiesta:D}?handler=Elimina");
        using var inesistente = await anna.GetAsync($"{Percorso(modulo)}/{Guid.NewGuid():D}");

        Assert.Equal(HttpStatusCode.NotFound, sbagliato.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, inesistente.StatusCode);
        using var ancora = await anna.GetAsync($"{Percorso(modulo)}/{richiesta:D}");
        Assert.Equal(HttpStatusCode.OK, ancora.StatusCode);
        Assert.NotEqual(Percorso(modulo), SessioneProva.Destinazione(eliminaSbagliata));
    }

    private static async Task<Guid> IdAdminAsync(ScenarioWeb scenario)
    {
        await using var ambito = scenario.Fabbrica.Services.CreateAsyncScope();
        return await ambito.ServiceProvider.GetRequiredService<ContestoDatabase>().Utenze.IgnoreQueryFilters()
            .Where(u => u.EmailNormalizzata == "anna@esempio.it").Select(u => u.Id).SingleAsync(Ct);
    }

    [Fact]
    public async Task LEsportazioneCsvHaLeColonneDeiCampiNeutralizzaLeFormuleEVaNellAuditSenzaDati()
    {
        var (scenario, anna, modulo) = await PreparaAsync();
        await using var _ = scenario;
        await AggiungiAsync(scenario, scenario.A, modulo, valori: [("Nome", "=SOMMA(A1)"), ("Messaggio", "ciao; \"mondo\"")]);
        await AggiungiAsync(scenario, scenario.A, modulo, StatoInvioModulo.Letto, valori: ("Nome", "Davide"));

        using var risposta = await anna.GetAsync($"{Percorso(modulo)}?handler=Esporta");
        var byteCsv = await risposta.Content.ReadAsByteArrayAsync(Ct);
        var csv = Encoding.UTF8.GetString(byteCsv);
        using var soloLette = await anna.GetAsync($"{Percorso(modulo)}?handler=Esporta&stato=Letto");
        var csvLette = Encoding.UTF8.GetString(await soloLette.Content.ReadAsByteArrayAsync(Ct));

        Assert.Equal("text/csv", risposta.Content.Headers.ContentType?.MediaType);
        Assert.Equal([0xEF, 0xBB, 0xBF], byteCsv[..3]);
        var righe = csv.TrimStart('﻿').Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("Id;DataUtc;Stato;Lingua;Nome;Messaggio", righe[0]);
        Assert.Equal(3, righe.Length);
        Assert.Contains("'=SOMMA(A1)", csv, StringComparison.Ordinal);
        Assert.Contains("\"ciao; \"\"mondo\"\"\"", csv, StringComparison.Ordinal);
        Assert.Equal(2, csvLette.TrimStart('﻿').Split("\r\n", StringSplitOptions.RemoveEmptyEntries).Length);
        Assert.DoesNotContain("Carla", csvLette, StringComparison.Ordinal);

        Assert.Equal(2, await AuditAsync(scenario, AzioniAudit.EsportazioneModulo));
        await using var ambito = scenario.Fabbrica.Services.CreateAsyncScope();
        var dettagli = await ambito.ServiceProvider.GetRequiredService<ContestoDatabase>().RegistroAudit.IgnoreQueryFilters()
            .Where(a => a.Azione == AzioniAudit.EsportazioneModulo).Select(a => a.Dettagli).ToListAsync(Ct);
        Assert.All(dettagli, d => Assert.DoesNotContain("Davide", d ?? string.Empty, StringComparison.Ordinal));
    }

    [Fact]
    public async Task LaManutenzioneCancellaLeRichiesteOltreLaConservazioneDelProprioModulo()
    {
        await using var scenario = await ScenarioWeb.CreaAsync();
        var admin = await scenario.CreaTenantAdminAsync("anna@esempio.it");
        var breve = await ModuloPubblicoWebTest.CreaModuloAsync(scenario, scenario.A, admin, ModuloPubblicoWebTest.Dati(nome: "Breve", impostazioni: new ImpostazioniModulo([], true, false, 30)));
        var lungo = await ModuloPubblicoWebTest.CreaModuloAsync(scenario, scenario.A, admin, ModuloPubblicoWebTest.Dati(nome: "Lungo", impostazioni: new ImpostazioniModulo([], true, false, 365)));
        var altro = await ModuloPubblicoWebTest.CreaModuloAsync(scenario, scenario.B, await IdAdminBAsync(scenario), ModuloPubblicoWebTest.Dati(nome: "Breve", impostazioni: new ImpostazioniModulo([], true, false, 30)));
        await AggiungiAsync(scenario, scenario.A, breve, giorniFa: 40, valori: ("Nome", "vecchia"));
        await AggiungiAsync(scenario, scenario.A, breve, giorniFa: 10, valori: ("Nome", "recente"));
        await AggiungiAsync(scenario, scenario.A, lungo, giorniFa: 40, valori: ("Nome", "ancora valida"));
        await AggiungiAsync(scenario, scenario.B, altro, giorniFa: 40, valori: ("Nome", "di B"));

        int cancellate;
        await using (var ambito = scenario.Fabbrica.Services.CreateAsyncScope())
        {
            var attivita = ambito.ServiceProvider.GetServices<IAttivitaManutenzione>().OfType<ManutenzioneModuli>().Single();
            cancellate = await attivita.EseguiAsync(Ct);
        }

        Assert.Equal(2, cancellate);
        await using var lettura = scenario.Fabbrica.Services.CreateAsyncScope();
        var db = lettura.ServiceProvider.GetRequiredService<ContestoDatabase>();
        var rimaste = (await db.InviiModulo.IgnoreQueryFilters().ToListAsync(Ct)).SelectMany(i => SerializzatoreModulo.LeggiContenuto(i.ContenutoJson)).SelectMany(v => v.Valori).Order().ToList();
        Assert.Equal(["ancora valida", "recente"], rimaste);
        Assert.Equal(2, await AuditAsync(scenario, AzioniAudit.ScadenzaInviiModulo));
    }

    private static async Task<Guid> IdAdminBAsync(ScenarioWeb scenario)
    {
        return await DatiProva.CreaUtenzaAsync(scenario.Fabbrica.Services, DvCms.Models.Accessi.RuoloUtenza.TenantAdmin, scenario.B.IdTenant, "bruno@esempio.it", annullamento: Ct);
    }
}
