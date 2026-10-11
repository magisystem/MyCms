using DvCms.IntegrationTests.Supporto;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.RazorPages.Infrastructure;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DvCms.IntegrationTests.Web;

/// <summary>
/// Controllo strutturale (G-005) su TUTTE le pagine dell'applicazione, comprese quelle che arriveranno: ogni pagina con un gestore
/// che modifica lo stato (POST) deve convalidare l'antiforgery e, salvo le poche pagine pubbliche dell'elenco esplicito, richiedere
/// un utente autenticato (e per l'area Platform il ruolo di piattaforma). Una pagina nuova che non rispetta la regola fa fallire il test.
/// </summary>
public class MatriceSicurezzaPagineTest
{
    /// <summary>Pagine che si usano senza sessione: ognuna ha un motivo e un limite di richieste (accesso, recupero, invito).</summary>
    private static readonly string[] _pagineAnonime =
    [
        "/Pages/Admin/Accesso.cshtml",
        "/Pages/Admin/Verifica.cshtml",
        "/Pages/Admin/Recupero/Richiesta.cshtml",
        "/Pages/Admin/Recupero/Codice.cshtml",
        "/Pages/Admin/Recupero/Reimposta.cshtml",
        "/Pages/Admin/Invito/Index.cshtml",
        "/Pages/Account/Registrazione.cshtml",
        "/Pages/Account/Conferma.cshtml",
        "/Pages/Account/Accesso.cshtml",
    ];

    [Fact]
    public async Task OgniPaginaChePuoModificareLoStatoConvalidaLAntiforgery()
    {
        await using var s = await ScenarioWeb.CreaAsync();

        var pagine = await PagineConPostAsync(s);

        Assert.True(pagine.Count > 10, "Il controllo deve vedere le pagine dell'applicazione.");
        foreach (var pagina in pagine)
        {
            var filtri = pagina.FilterDescriptors.Select(f => f.Filter).ToList();
            Assert.DoesNotContain(filtri, f => f is IgnoreAntiforgeryTokenAttribute);
            Assert.Contains(filtri, f => f is AutoValidateAntiforgeryTokenAttribute);
        }
    }

    [Fact]
    public async Task OgniPaginaDiAdminCheModificaLoStatoRichiedeLAutenticazioneSalvoLElencoEsplicito()
    {
        await using var s = await ScenarioWeb.CreaAsync();

        foreach (var pagina in await PagineConPostAsync(s))
        {
            var filtri = pagina.FilterDescriptors.Select(f => f.Filter).ToList();

            // Con il routing degli endpoint le convenzioni (AuthorizeFolder, AllowAnonymousToPage) diventano metadati dell'endpoint.
            var anonima = pagina.EndpointMetadata.OfType<IAllowAnonymous>().Any();
            var protetta = pagina.EndpointMetadata.OfType<IAuthorizeData>().Any();

            if (_pagineAnonime.Contains(pagina.RelativePath, StringComparer.Ordinal))
            {
                Assert.True(anonima, $"{pagina.RelativePath} dovrebbe essere raggiungibile senza sessione");
            }
            else
            {
                Assert.False(anonima, $"{pagina.RelativePath} non deve essere anonima");
                Assert.True(protetta, $"{pagina.RelativePath} modifica lo stato ma non richiede autenticazione");
            }
        }
    }

    [Fact]
    public async Task LeSchermateDellAreaPlatformRichiedonoIlRuoloDiPiattaforma()
    {
        await using var s = await ScenarioWeb.CreaAsync();

        var pagine = (await PagineConPostAsync(s)).Where(p => p.AreaName == "Platform").ToList();

        Assert.NotEmpty(pagine);
        foreach (var pagina in pagine)
        {
            var autorizzazioni = pagina.EndpointMetadata.OfType<IAuthorizeData>().ToList();
            Assert.Contains(autorizzazioni, a => a.Policy == "AmministratorePiattaforma");
        }

        // Il controllo del ruolo vero e proprio (403 al TenantAdmin) è provato dai test dell'area Platform: qui basta che nessuna
        // pagina dell'area sia sfuggita alla convenzione della politica di piattaforma.
        var autorizzazione = s.Fabbrica.Services.GetRequiredService<IAuthorizationPolicyProvider>();
        Assert.NotNull(await autorizzazione.GetPolicyAsync("AmministratorePiattaforma"));
    }

    private static async Task<List<CompiledPageActionDescriptor>> PagineConPostAsync(ScenarioWeb s)
    {
        var elenco = s.Fabbrica.Services.GetRequiredService<IActionDescriptorCollectionProvider>();
        var caricatore = s.Fabbrica.Services.GetRequiredService<PageLoader>();
        var compilate = new List<CompiledPageActionDescriptor>();
        foreach (var descrittore in elenco.ActionDescriptors.Items.OfType<PageActionDescriptor>())
        {
            var compilata = await caricatore.LoadAsync(descrittore, EndpointMetadataCollection.Empty);
            if (compilata.HandlerMethods.Any(m => string.Equals(m.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase)))
            {
                compilate.Add(compilata);
            }
        }

        return compilate;
    }
}
