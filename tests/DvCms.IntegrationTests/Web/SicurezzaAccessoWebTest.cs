using System.Net;
using DvCms.Contracts.Cms;
using DvCms.Core.Cache;
using DvCms.Core.Tenancy;
using DvCms.Data;
using DvCms.DTO.Cms;
using DvCms.DTO.Tenancy;
using DvCms.Infrastructure.Cache;
using DvCms.IntegrationTests.Supporto;
using DvCms.Models.Accessi;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DvCms.IntegrationTests.Web;

/// <summary>Esiti della revisione di sicurezza (G-005): blocco dell'account sotto richieste parallele, cache e autori.</summary>
public class SicurezzaAccessoWebTest
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TentativiParalleliSbagliatiVengonoTuttiContatiEBloccanoLAccount()
    {
        await using var s = await ScenarioWeb.CreaAsync();
        await s.CreaTenantAdminAsync("anna@esempio.it");

        // 20 tentativi insieme sullo stesso account: con un contatore salvato in modo ottimistico ne contava uno solo e il blocco non scattava.
        var risposte = await Task.WhenAll(Enumerable.Range(0, 20).Select(async i =>
        {
            using var risposta = await s.SessioneA().AccediAsync("anna@esempio.it", $"password-sbagliata-{i}");
            return risposta.StatusCode;
        }));

        Assert.All(risposte, stato => Assert.Equal(HttpStatusCode.OK, stato));
        await using var ambito = s.Fabbrica.Services.CreateAsyncScope();
        var utenza = await ambito.ServiceProvider.GetRequiredService<ContestoDatabase>().Utenze.IgnoreQueryFilters().AsNoTracking().SingleAsync(u => u.EmailNormalizzata == "anna@esempio.it", Ct);
        Assert.NotNull(utenza.BloccatoFinoAlUtc);
        Assert.True(utenza.ContatoreAccessiFalliti < 5);

        // Bloccato: anche la password giusta viene rifiutata.
        using var corretta = await s.SessioneA().AccediAsync("anna@esempio.it", DatiProva.PasswordProva);
        Assert.False(SessioneProva.EReindirizzamento(corretta));
    }

    [Fact]
    public async Task IlContatoreSiAzzeraConUnAccessoRiuscito()
    {
        await using var s = await ScenarioWeb.CreaAsync();
        await s.CreaTenantAdminAsync("anna@esempio.it");
        for (var i = 0; i < 3; i++)
        {
            using var _ = await s.SessioneA().AccediAsync("anna@esempio.it", "sbagliata-abbastanza-lunga");
        }

        using var riuscito = await s.SessioneA().AccediAsync("anna@esempio.it", DatiProva.PasswordProva);

        Assert.True(SessioneProva.EReindirizzamento(riuscito));
        await using var ambito = s.Fabbrica.Services.CreateAsyncScope();
        var utenza = await ambito.ServiceProvider.GetRequiredService<ContestoDatabase>().Utenze.IgnoreQueryFilters().AsNoTracking().SingleAsync(u => u.EmailNormalizzata == "anna@esempio.it", Ct);
        Assert.Equal(0, utenza.ContatoreAccessiFalliti);
        Assert.Null(utenza.BloccatoFinoAlUtc);
    }

    [Fact]
    public async Task VariantiEquivalentiDiUnoSlugCondividonoUnaSolaVoceDiCache()
    {
        await using var s = await ScenarioWeb.CreaAsync();
        var admin = await s.CreaTenantAdminAsync("anna@esempio.it");
        await CreaPubblicaAsync(s, admin, "about");
        var cache = (CachePubblica)s.Fabbrica.Services.GetRequiredService<ICachePubblica>();
        var prima = cache.Voci;

        foreach (var variante in new[] { "/about", "/About", "/ABOUT", "/%20about", "/%20%20about", "/about%09", "/%09about%20" })
        {
            using var risposta = await s.SessioneA().GetAsync(variante);
            Assert.Contains(risposta.StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.NotFound });
        }

        // La stessa pagina non genera una voce per ogni variante (al più quella della pagina, più il tema e le lingue del sito).
        Assert.True(cache.Voci - prima <= 4, $"Voci in più: {cache.Voci - prima}");
    }

    [Fact]
    public async Task LAutoreDiUnaRevisioneFattaDalPlatformAdminNonMostraLaSuaEmailAlTenant()
    {
        await using var s = await ScenarioWeb.CreaAsync();
        var platform = await s.CreaPlatformAdminAsync("root@esempio.it");
        var admin = await s.CreaTenantAdminAsync("anna@esempio.it");

        await using var ambito = s.Fabbrica.Services.CreateAsyncScope();
        ambito.ServiceProvider.GetRequiredService<ContestoTenant>().Imposta(new DatiTenantRisolto(s.A.IdTenant, s.A.IdSito, s.A.IdDominio, s.A.Host, false));
        var pagine = ambito.ServiceProvider.GetRequiredService<IServizioPagine>();
        var creata = await pagine.CreaAsync(platform, new DatiNuovaPagina("pagina", "Pagina", "x", "it", false), Ct);
        Assert.True(creata.Riuscito, creata.Codice.ToString());

        var dettaglio = (await pagine.LeggiAsync(admin, creata.Id!.Value, Ct))!;

        var revisione = Assert.Single(dettaglio.Revisioni);
        Assert.True(revisione.AutoreDiPiattaforma);
        Assert.Null(revisione.Autore);

        var sessione = s.SessioneA();
        await sessione.AccediAsync("anna@esempio.it", DatiProva.PasswordProva);
        var html = await (await sessione.GetAsync($"/admin/pagine/{creata.Id}")).Content.ReadAsStringAsync(Ct);
        Assert.DoesNotContain("root@esempio.it", html, StringComparison.Ordinal);
        Assert.Contains("Amministratore della piattaforma", html, StringComparison.Ordinal);
    }

    private static async Task CreaPubblicaAsync(ScenarioWeb s, Guid admin, string slug)
    {
        await using var ambito = s.Fabbrica.Services.CreateAsyncScope();
        ambito.ServiceProvider.GetRequiredService<ContestoTenant>().Imposta(new DatiTenantRisolto(s.A.IdTenant, s.A.IdSito, s.A.IdDominio, s.A.Host, false));
        var pagine = ambito.ServiceProvider.GetRequiredService<IServizioPagine>();
        var creata = await pagine.CreaAsync(admin, new DatiNuovaPagina(slug, "About", "x", "it", false), Ct);
        var dettaglio = (await pagine.LeggiAsync(admin, creata.Id!.Value, Ct))!;
        Assert.True((await pagine.PubblicaAsync(admin, creata.Id.Value, dettaglio.TokenConcorrenza, Ct)).Riuscito);
    }
}
