using System.Net;
using DvCms.Application.Accessi;
using DvCms.Application.Piattaforma;
using DvCms.Domain.Accessi;
using DvCms.Infrastructure.Persistenza;
using DvCms.IntegrationTests.Supporto;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DvCms.TenantIsolationTests;

/// <summary>Matrice di isolamento (B-004) su account, domini e operazioni di piattaforma.</summary>
public class IsolamentoAccountEDominiTest
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task IlLoginIncrociatoFallisceConLoStessoEsitoDelleCredenzialiErrate()
    {
        await using var d = await DatiIsolamento.CreaAsync();

        using var annaSuB = await d.Web.SessioneB().AccediAsync("anna@esempio.it", DatiProva.PasswordProva);
        using var brunoSuA = await d.Web.SessioneA().AccediAsync("bruno@esempio.it", DatiProva.PasswordProva);
        using var passwordErrata = await d.Web.SessioneA().AccediAsync("anna@esempio.it", "password-sbagliata-123");

        var incrociato = await annaSuB.Content.ReadAsStringAsync(Ct);
        Assert.Equal(HttpStatusCode.OK, annaSuB.StatusCode);
        Assert.Contains("alert-danger", incrociato, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, brunoSuA.StatusCode);
        Assert.Equal(Normalizza(await passwordErrata.Content.ReadAsStringAsync(Ct)), Normalizza(incrociato));
        Assert.DoesNotContain(SessioneProva.IntestazioniSetCookie(annaSuB), c => c.StartsWith(".DvCms.Admin=", StringComparison.Ordinal));
        Assert.DoesNotContain(SessioneProva.IntestazioniSetCookie(brunoSuA), c => c.StartsWith(".DvCms.Admin=", StringComparison.Ordinal));
    }

    [Fact]
    public async Task IlCookieDiUnTenantNonVaSuLHostDellAltro()
    {
        await using var d = await DatiIsolamento.CreaAsync();
        var anna = d.Web.SessioneA();
        var bruno = d.Web.SessioneB();
        await anna.AccediAsync("anna@esempio.it", DatiProva.PasswordProva);
        await bruno.AccediAsync("bruno@esempio.it", DatiProva.PasswordProva);

        foreach (var (cookie, destinazione) in new[] { (anna.CookieAdmin, d.Web.SessioneB()), (bruno.CookieAdmin, d.Web.SessioneA()) })
        {
            using var richiesta = new HttpRequestMessage(HttpMethod.Get, "/admin/pagine");
            richiesta.Headers.TryAddWithoutValidation("Cookie", $".DvCms.Admin={cookie}");
            using var risposta = await destinazione.InviaAsync(richiesta);

            Assert.True(SessioneProva.EReindirizzamento(risposta));
            Assert.StartsWith("/admin/accesso", SessioneProva.Destinazione(risposta), StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task UnTenantAdminNonPuoOperareSuAccountEDominiDiAltriTenant()
    {
        await using var d = await DatiIsolamento.CreaAsync();
        var anna = d.Web.SessioneA();
        await anna.AccediAsync("anna@esempio.it", DatiProva.PasswordProva);
        var dettaglioB = $"/admin/platform/tenant/{d.B.IdTenant}";

        var tentativi = new (string Percorso, KeyValuePair<string, string>[] Campi)[]
        {
            ($"{dettaglioB}?handler=NuovoAmministratore", [new("email", "complice@esempio.it"), new("nomeVisualizzato", "Complice")]),
            ($"{dettaglioB}?handler=DominioAttivo", [new("idDominio", d.B.IdDominio.ToString()), new("attivo", "false")]),
            ($"{dettaglioB}?handler=Sospendi", []),
            ($"{dettaglioB}?handler=AccountAttivo", [new("idUtenza", d.AdminB.ToString()), new("attiva", "false")]),
            ($"{dettaglioB}?handler=Promuovi", [new("idUtenza", d.AdminB.ToString())]),
            ($"/admin/platform/tenant/{d.A.IdTenant}?handler=NuovoAmministratore", [new("email", "complice@esempio.it"), new("nomeVisualizzato", "Complice")]),
        };

        foreach (var (percorso, campi) in tentativi)
        {
            using var risposta = await anna.InviaModuloAsync("/admin", campi, percorso);
            Assert.Equal(HttpStatusCode.Forbidden, risposta.StatusCode);
        }

        using var lettura = await anna.GetAsync(dettaglioB);
        Assert.Equal(HttpStatusCode.Forbidden, lettura.StatusCode);
        await using var ambito = d.Web.Fabbrica.Services.CreateAsyncScope();
        var contesto = ambito.ServiceProvider.GetRequiredService<ContestoDatabase>();
        Assert.True((await contesto.DominiSito.IgnoreQueryFilters().SingleAsync(x => x.Id == d.B.IdDominio, Ct)).Attivo);
        Assert.Equal(StatoUtenza.Attiva, (await contesto.Utenze.IgnoreQueryFilters().SingleAsync(u => u.Id == d.AdminB, Ct)).Stato);
        Assert.Equal(2, await contesto.Utenze.IgnoreQueryFilters().CountAsync(Ct));
        Assert.Equal(DvCms.Domain.Tenancy.StatoTenant.Attivo, (await contesto.Tenant.IgnoreQueryFilters().SingleAsync(t => t.Id == d.B.IdTenant, Ct)).Stato);
    }

    [Fact]
    public async Task UnTenantAdminNonPuoAutoElevarsiNeCrearePlatformAdmin()
    {
        await using var d = await DatiIsolamento.CreaAsync();
        var anna = d.Web.SessioneA();
        await anna.AccediAsync("anna@esempio.it", DatiProva.PasswordProva);

        // Richieste manipolate: campi di ruolo e di tenant aggiuntivi nel corpo, su ogni schermata di scrittura.
        var ruolo = new KeyValuePair<string, string>[] { new("ruolo", "PlatformAdmin"), new("Ruolo", "PlatformAdmin"), new("idUtenza", d.AdminA.ToString()), new("email", "anna2@esempio.it"), new("nomeVisualizzato", "Anna") };
        foreach (var percorso in new[]
        {
            $"/admin/platform/tenant/{d.A.IdTenant}?handler=Promuovi",
            $"/admin/platform/tenant/{d.A.IdTenant}?handler=NuovoAmministratore",
            "/admin/platform/tenant/nuovo",
        })
        {
            using var risposta = await anna.InviaModuloAsync("/admin", ruolo, percorso);
            Assert.Equal(HttpStatusCode.Forbidden, risposta.StatusCode);
        }

        await using var ambito = d.Web.Fabbrica.Services.CreateAsyncScope();
        var contesto = ambito.ServiceProvider.GetRequiredService<ContestoDatabase>();
        Assert.False(await contesto.Utenze.IgnoreQueryFilters().AnyAsync(u => u.Ruolo == RuoloUtenza.PlatformAdmin, Ct));
        Assert.Equal(RuoloUtenza.TenantAdmin, (await contesto.Utenze.IgnoreQueryFilters().SingleAsync(u => u.Id == d.AdminA, Ct)).Ruolo);
    }

    [Fact]
    public async Task LeOperazioniDiPiattaformaConTenantERisorsaIncoerentiNonHannoEffetto()
    {
        await using var d = await DatiIsolamento.CreaAsync();
        var platform = await DatiProva.CreaUtenzaAsync(d.Web.Fabbrica.Services, RuoloUtenza.PlatformAdmin, null, "root@esempio.it", annullamento: Ct);
        var utenteDiB = await DatiProva.CreaUtenzaAsync(d.Web.Fabbrica.Services, RuoloUtenza.Utente, d.B.IdTenant, "cliente@esempio.it", annullamento: Ct);

        // Tenant A dichiarato, ma la risorsa è di B: la risorsa deve risultare inesistente.
        var esiti = new List<EsitoOperazione>();
        await using (var ambito = d.Web.Fabbrica.Services.CreateAsyncScope())
        {
            var s = ambito.ServiceProvider.GetRequiredService<IServizioPiattaforma>();
            esiti.Add(await s.VerificaDominioAsync(platform, d.A.IdTenant, d.B.IdDominio, Ct));
            esiti.Add(await s.ImpostaDominioAttivoAsync(platform, d.A.IdTenant, d.B.IdDominio, false, Ct));
            esiti.Add(await s.AggiungiDominioAsync(platform, d.A.IdTenant, d.B.IdSito, "furto.esempio.it", true, Ct));
            esiti.Add(await s.ModificaSitoAsync(platform, d.A.IdTenant, d.B.IdSito, "Furto", null, false, Ct));
            esiti.Add(await s.ElevaATenantAdminAsync(platform, d.A.IdTenant, utenteDiB, Ct));
            esiti.Add(await s.ImpostaUtenzaAttivaAsync(platform, d.A.IdTenant, d.AdminB, false, Ct));
        }

        Assert.All(esiti, esito => Assert.Equal(CodiceEsito.NonTrovato, esito.Codice));
        await using var lettura = d.Web.Fabbrica.Services.CreateAsyncScope();
        var contesto = lettura.ServiceProvider.GetRequiredService<ContestoDatabase>();
        Assert.True((await contesto.DominiSito.IgnoreQueryFilters().SingleAsync(x => x.Id == d.B.IdDominio, Ct)).Attivo);
        Assert.Equal("Sito di Tenant B", (await contesto.Siti.IgnoreQueryFilters().SingleAsync(x => x.Id == d.B.IdSito, Ct)).Nome);
        Assert.Equal(RuoloUtenza.Utente, (await contesto.Utenze.IgnoreQueryFilters().SingleAsync(u => u.Id == utenteDiB, Ct)).Ruolo);
        Assert.Equal(StatoUtenza.Attiva, (await contesto.Utenze.IgnoreQueryFilters().SingleAsync(u => u.Id == d.AdminB, Ct)).Stato);
        Assert.False(await contesto.DominiSito.IgnoreQueryFilters().AnyAsync(x => x.Host == "furto.esempio.it", Ct));
    }

    [Fact]
    public async Task LAuditDiUnTenantNonSiVedeDaUnAltro()
    {
        await using var d = await DatiIsolamento.CreaAsync();
        var platform = await DatiProva.CreaUtenzaAsync(d.Web.Fabbrica.Services, RuoloUtenza.PlatformAdmin, null, "root@esempio.it", annullamento: Ct);
        await using (var ambito = d.Web.Fabbrica.Services.CreateAsyncScope())
        {
            await ambito.ServiceProvider.GetRequiredService<IServizioPiattaforma>().RinominaTenantAsync(platform, d.B.IdTenant, "B rinominato", Ct);
        }

        await using var comeA = d.Web.Fabbrica.Services.CreateAsyncScope();
        comeA.ServiceProvider.GetRequiredService<DvCms.Application.Tenancy.ContestoTenant>()
            .Imposta(new DvCms.Application.Tenancy.DatiTenantRisolto(d.A.IdTenant, d.A.IdSito, d.A.IdDominio, d.A.Host, false));
        var contesto = comeA.ServiceProvider.GetRequiredService<ContestoDatabase>();

        Assert.Empty(await contesto.RegistroAudit.ToListAsync(Ct));
        Assert.Single(await contesto.RegistroAudit.IgnoreQueryFilters().Where(voce => voce.IdTenant == d.B.IdTenant).ToListAsync(Ct));
    }

    private static string Normalizza(string html) =>
        System.Text.RegularExpressions.Regex.Replace(html, "(name=\"__RequestVerificationToken\"[^>]*value=\")[^\"]+", "$1", System.Text.RegularExpressions.RegexOptions.CultureInvariant);
}
