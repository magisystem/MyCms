using System.Net;
using DvCms.Core.Tenancy;
using DvCms.DTO.Cms;
using DvCms.DTO.Piattaforma;
using DvCms.DTO.Tenancy;
using DvCms.Infrastructure.Persistenza;
using DvCms.IntegrationTests.Supporto;
using DvCms.Models.Cms;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DvCms.TenantIsolationTests;

/// <summary>
/// Matrice di isolamento (B-004) sulle pagine: l'amministratore del tenant A non legge, modifica, pubblica, ritira né
/// cancella le pagine del tenant B, e nulla di ciò che il client invia può cambiare tenant o sito.
/// Le aree che arrivano dopo (media, catalogo, ordini) aggiungono qui le loro righe.
/// </summary>
public class IsolamentoPagineTest
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task LaPaginaDiBNonSiLeggeDaA()
    {
        await using var d = await DatiIsolamento.CreaAsync();
        var anna = d.Web.SessioneA();
        await anna.AccediAsync("anna@esempio.it", DatiProva.PasswordProva);

        using var modifica = await anna.GetAsync($"/admin/pagine/{d.PaginaB}");
        using var elenco = await anna.GetAsync("/admin/pagine");
        using var pubblica = await d.Web.SessioneA().GetAsync("/contatti");

        Assert.Equal(HttpStatusCode.NotFound, modifica.StatusCode);
        var htmlElenco = await elenco.Content.ReadAsStringAsync(Ct);
        Assert.Contains("Contatti di A", htmlElenco, StringComparison.Ordinal);
        Assert.DoesNotContain("Contatti di B", htmlElenco, StringComparison.Ordinal);
        Assert.DoesNotContain("Riservato a B", await pubblica.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ModificaPubblicazioneERitiroDiUnaPaginaDiBDaAVengonoNegati()
    {
        await using var d = await DatiIsolamento.CreaAsync();
        var token = Convert.ToBase64String([1, 2, 3, 4]);

        var salva = await d.ComeAsync(d.A, (s, a) => s.SalvaAsync(a, d.PaginaB, new DatiModificaPagina(null, "Hacked", "Hacked", null, false, token), Ct));
        var pubblica = await d.ComeAsync(d.A, (s, a) => s.PubblicaAsync(a, d.PaginaB, token, Ct));
        var ritira = await d.ComeAsync(d.A, (s, a) => s.RitiraAsync(a, d.PaginaB, token, Ct));
        var leggi = await d.ComeAsync(d.A, (s, a) => s.LeggiAsync(a, d.PaginaB, Ct));

        Assert.Equal(CodiceEsito.NonTrovato, salva.Codice);
        Assert.Equal(CodiceEsito.NonTrovato, pubblica.Codice);
        Assert.Equal(CodiceEsito.NonTrovato, ritira.Codice);
        Assert.Null(leggi);
        var dopo = (await d.ComeAsync(d.B, (s, a) => s.LeggiAsync(a, d.PaginaB, Ct)))!;
        Assert.Equal("Contatti di B", dopo.Titolo);
        Assert.Equal(StatoPagina.Pubblicata, dopo.Stato);
        Assert.Single(dopo.Revisioni);
    }

    [Fact]
    public async Task LeScrittureWebSuUnaPaginaDiBDaAHannoEffettoNullo()
    {
        await using var d = await DatiIsolamento.CreaAsync();
        var anna = d.Web.SessioneA();
        await anna.AccediAsync("anna@esempio.it", DatiProva.PasswordProva);
        var percorso = $"/admin/pagine/{d.PaginaB}";
        var token = (await d.ComeAsync(d.B, (s, a) => s.LeggiAsync(a, d.PaginaB, Ct)))!.TokenConcorrenza;

        using var salva = await anna.InviaModuloAsync("/admin", [new("Titolo", "Hacked"), new("Contenuto", "Hacked"), new("TokenConcorrenza", token)], $"{percorso}?handler=Salva");
        using var ritira = await anna.InviaModuloAsync("/admin", [new("TokenConcorrenza", token)], $"{percorso}?handler=Ritira");

        Assert.True(SessioneProva.EReindirizzamento(salva));
        Assert.True(SessioneProva.EReindirizzamento(ritira));
        var dopo = (await d.ComeAsync(d.B, (s, a) => s.LeggiAsync(a, d.PaginaB, Ct)))!;
        Assert.Equal("Contatti di B", dopo.Titolo);
        Assert.Equal(StatoPagina.Pubblicata, dopo.Stato);
    }

    [Fact]
    public async Task IdentificativiDiTenantESitoNelCorpoDellaRichiestaSonoIgnorati()
    {
        await using var d = await DatiIsolamento.CreaAsync();
        var anna = d.Web.SessioneA();
        await anna.AccediAsync("anna@esempio.it", DatiProva.PasswordProva);

        using var creazione = await anna.InviaModuloAsync(
            "/admin/pagine/nuova",
            [
                new("Titolo", "Tentativo"),
                new("Slug", "tentativo"),
                new("CodiceLingua", "it"),
                new("Contenuto", "x"),
                new("IdTenant", d.B.IdTenant.ToString()),
                new("IdSito", d.B.IdSito.ToString()),
                new("idTenant", d.B.IdTenant.ToString()),
            ]);

        Assert.True(SessioneProva.EReindirizzamento(creazione));
        await using var ambito = d.Web.Fabbrica.Services.CreateAsyncScope();
        var contesto = ambito.ServiceProvider.GetRequiredService<ContestoDatabase>();
        var pagina = await contesto.Pagine.IgnoreQueryFilters().SingleAsync(p => p.Slug == "tentativo", Ct);
        Assert.Equal(d.A.IdTenant, pagina.IdTenant);
        Assert.Equal(d.A.IdSito, pagina.IdSito);
    }

    [Fact]
    public async Task IFiltriDelContestoNeganoLetturaModificaECancellazione()
    {
        await using var d = await DatiIsolamento.CreaAsync();
        await using var ambito = d.Web.Fabbrica.Services.CreateAsyncScope();
        ambito.ServiceProvider.GetRequiredService<DvCms.Core.Tenancy.ContestoTenant>()
            .Imposta(new DvCms.DTO.Tenancy.DatiTenantRisolto(d.A.IdTenant, d.A.IdSito, d.A.IdDominio, d.A.Host, false));
        var contesto = ambito.ServiceProvider.GetRequiredService<ContestoDatabase>();

        // Lettura.
        Assert.Equal(["Contatti di A"], await contesto.RevisioniPagina.Select(r => r.Titolo).ToListAsync(Ct));
        Assert.Equal([d.PaginaA], await contesto.Pagine.Select(p => p.Id).ToListAsync(Ct));
        Assert.Equal([d.A.IdSito], await contesto.Siti.Select(s => s.Id).ToListAsync(Ct));
        Assert.Equal([d.A.IdDominio], await contesto.DominiSito.Select(x => x.Id).ToListAsync(Ct));
        Assert.DoesNotContain(await contesto.Utenze.Select(u => u.Email).ToListAsync(Ct), email => email == "bruno@esempio.it");

        // Modifica e cancellazione di massa: non toccano le righe del tenant B.
        var modificate = await contesto.Pagine.Where(p => p.Id == d.PaginaB).ExecuteUpdateAsync(c => c.SetProperty(p => p.Slug, "hacked"), Ct);
        var cancellate = await contesto.Pagine.Where(p => p.Id == d.PaginaB).ExecuteDeleteAsync(Ct);
        var domini = await contesto.DominiSito.Where(x => x.Id == d.B.IdDominio).ExecuteDeleteAsync(Ct);
        var utenze = await contesto.Utenze.Where(u => u.Id == d.AdminB).ExecuteDeleteAsync(Ct);
        Assert.Equal(0, modificate + cancellate + domini + utenze);

        var pagineB = await contesto.Pagine.IgnoreQueryFilters().Where(p => p.IdTenant == d.B.IdTenant).ToListAsync(Ct);
        Assert.Equal("contatti", Assert.Single(pagineB).Slug);
        Assert.True(await contesto.Utenze.IgnoreQueryFilters().AnyAsync(u => u.Id == d.AdminB, Ct));
    }

    [Fact]
    public async Task SenzaTenantRisoltoNonSiVedeNulla()
    {
        await using var d = await DatiIsolamento.CreaAsync();
        await using var ambito = d.Web.Fabbrica.Services.CreateAsyncScope();
        var contesto = ambito.ServiceProvider.GetRequiredService<ContestoDatabase>();

        Assert.Empty(await contesto.Pagine.ToListAsync(Ct));
        Assert.Empty(await contesto.RevisioniPagina.ToListAsync(Ct));
        Assert.Empty(await contesto.Siti.ToListAsync(Ct));
        Assert.Empty(await contesto.DominiSito.ToListAsync(Ct));
        Assert.Empty(await contesto.Utenze.ToListAsync(Ct));
        Assert.Empty(await contesto.RegistroAudit.ToListAsync(Ct));
        Assert.Empty(await contesto.ProfiliGuest.ToListAsync(Ct));
    }

    [Fact]
    public async Task StessoSlugSuTenantDiversiNonEntraInConflitto()
    {
        await using var d = await DatiIsolamento.CreaAsync();

        // Entrambi i tenant hanno già "contatti" (creato dallo scenario): ognuno vede il proprio contenuto.
        var daA = await (await d.Web.SessioneA().GetAsync("/contatti")).Content.ReadAsStringAsync(Ct);
        var daB = await (await d.Web.SessioneB().GetAsync("/contatti")).Content.ReadAsStringAsync(Ct);

        Assert.Contains("Contatti di A", daA, StringComparison.Ordinal);
        Assert.Contains("Contatti di B", daB, StringComparison.Ordinal);
    }

    [Fact]
    public async Task IReindirizzamentiDiBNonValgonoNeSiEliminanoDaA()
    {
        await using var d = await DatiIsolamento.CreaAsync();
        var lettaB = (await d.ComeAsync(d.B, (s, a) => s.LeggiAsync(a, d.PaginaB, Ct)))!;
        var rinomina = await d.ComeAsync(d.B, (s, a) => s.SalvaAsync(a, d.PaginaB, new DatiModificaPagina("contatti-b", lettaB.Titolo, lettaB.Contenuto, null, lettaB.PaginaIniziale, lettaB.TokenConcorrenza), Ct));
        Assert.True(rinomina.Riuscito, rinomina.Codice.ToString());
        var idRedirect = (await d.ComeAsync(d.B, (s, a) => s.LeggiAsync(a, d.PaginaB, Ct)))!.Reindirizzamenti![0].Id;

        using var daB = await d.Web.SessioneB().GetAsync("/contatti");
        using var daA = await d.Web.SessioneA().GetAsync("/contatti");
        var eliminaDaA = await d.ComeAsync(d.A, (s, a) => s.EliminaReindirizzamentoAsync(a, d.PaginaB, idRedirect, Ct));

        Assert.Equal(HttpStatusCode.MovedPermanently, daB.StatusCode);
        Assert.Equal("/contatti-b", SessioneProva.Destinazione(daB));
        Assert.Equal(HttpStatusCode.OK, daA.StatusCode);
        Assert.Contains("Contatti di A", await daA.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        Assert.Equal(CodiceEsito.NonTrovato, eliminaDaA.Codice);
        Assert.Single((await d.ComeAsync(d.B, (s, a) => s.LeggiAsync(a, d.PaginaB, Ct)))!.Reindirizzamenti!);
    }

    [Fact]
    public async Task LaSitemapEIlRobotsDiUnDominioNonContengonoNullaDiAltri()
    {
        await using var d = await DatiIsolamento.CreaAsync();

        var sitemapA = await (await d.Web.SessioneA().GetAsync("/sitemap.xml")).Content.ReadAsStringAsync(Ct);
        var sitemapB = await (await d.Web.SessioneB().GetAsync("/sitemap.xml")).Content.ReadAsStringAsync(Ct);

        Assert.Contains($"http://{d.Web.A.Host}/contatti", sitemapA, StringComparison.Ordinal);
        Assert.DoesNotContain(d.Web.B.Host, sitemapA, StringComparison.Ordinal);
        Assert.Contains($"http://{d.Web.B.Host}/contatti", sitemapB, StringComparison.Ordinal);
        Assert.DoesNotContain(d.Web.A.Host, sitemapB, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LaCacheDeiContenutiPubbliciNonMescolaITenantNemmenoDopoUnaInvalidazione()
    {
        await using var d = await DatiIsolamento.CreaAsync();
        var sessioneA = d.Web.SessioneA();
        var sessioneB = d.Web.SessioneB();

        // Si riempie la cache di entrambi i tenant con lo stesso slug, poi si invalida solo A (nuova pubblicazione).
        await (await sessioneA.GetAsync("/contatti")).Content.ReadAsStringAsync(Ct);
        await (await sessioneB.GetAsync("/contatti")).Content.ReadAsStringAsync(Ct);
        var dettaglio = (await d.ComeAsync(d.A, (s, a) => s.LeggiAsync(a, d.PaginaA, Ct)))!;
        await d.ComeAsync(d.A, (s, a) => s.SalvaAsync(a, d.PaginaA, new DatiModificaPagina(null, "Contatti di A, aggiornati", dettaglio.Contenuto, null, false, dettaglio.TokenConcorrenza), Ct));
        var aggiornata = (await d.ComeAsync(d.A, (s, a) => s.LeggiAsync(a, d.PaginaA, Ct)))!;
        await d.ComeAsync(d.A, (s, a) => s.PubblicaAsync(a, d.PaginaA, aggiornata.TokenConcorrenza, Ct));

        var daA = await (await sessioneA.GetAsync("/contatti")).Content.ReadAsStringAsync(Ct);
        var daB = await (await sessioneB.GetAsync("/contatti")).Content.ReadAsStringAsync(Ct);

        Assert.Contains("Contatti di A, aggiornati", daA, StringComparison.Ordinal);
        Assert.DoesNotContain("Contatti di B", daA, StringComparison.Ordinal);
        Assert.Contains("Contatti di B", daB, StringComparison.Ordinal);
        Assert.DoesNotContain("Contatti di A", daB, StringComparison.Ordinal);
    }
}
