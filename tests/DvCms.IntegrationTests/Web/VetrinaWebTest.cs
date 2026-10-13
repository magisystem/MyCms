using System.Net;
using System.Text.RegularExpressions;
using DvCms.Contracts.Cms;
using DvCms.Core.Tenancy;
using DvCms.DTO.Cms;
using DvCms.DTO.Tenancy;
using DvCms.Infrastructure.Cache;
using DvCms.IntegrationTests.Negozio;
using DvCms.IntegrationTests.Supporto;
using DvCms.Models.Negozio;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DvCms.IntegrationTests.Web;

/// <summary>Vetrina pubblica (F-001): visibilità dei prodotti, disponibilità, aggiunta al carrello da acquirente e isolamento fra tenant.</summary>
public class VetrinaWebTest
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<SessioneProva> ClienteAsync(ContestoNegozio c, bool tenantB = false)
    {
        var sessione = c.Web.SessioneSu(tenantB ? c.B.Host : c.A.Host);
        using var accesso = await sessione.InviaModuloAsync("/account/accesso", [new("Email", "cliente@esempio.it"), new("Password", DatiProva.PasswordProva)]);
        Assert.True(SessioneProva.EReindirizzamento(accesso));
        return sessione;
    }

    [Fact]
    public async Task LeVociDelNegozioCompaionoSullePagineDelSitoSoloConIlNegozioAttivo()
    {
        await using var c = await ContestoNegozio.CreaAsync();
        await using (var ambito = c.Web.Fabbrica.Services.CreateAsyncScope())
        {
            ambito.ServiceProvider.GetRequiredService<ContestoTenant>().Imposta(new DatiTenantRisolto(c.A.IdTenant, c.A.IdSito, c.A.IdDominio, c.A.Host, false));
            var pagine = ambito.ServiceProvider.GetRequiredService<IServizioPagine>();
            var creata = await pagine.CreaAsync(c.AdminA, new DatiNuovaPagina("benvenuto", "Benvenuto", "ciao", "it", false), Ct);
            var dettaglio = (await pagine.LeggiAsync(c.AdminA, creata.Id!.Value, Ct))!;
            Assert.True((await pagine.PubblicaAsync(c.AdminA, creata.Id.Value, dettaglio.TokenConcorrenza, Ct)).Riuscito);
        }

        var anonimo = c.Web.SessioneSu(c.A.Host);
        var acceso = await (await anonimo.GetAsync("/benvenuto")).Content.ReadAsStringAsync(Ct);
        await c.SpegniNegozioAsync(c.A);
        var spento = await (await anonimo.GetAsync("/benvenuto")).Content.ReadAsStringAsync(Ct);

        Assert.Contains("class=\"dv-barra-negozio\"", acceso, StringComparison.Ordinal);
        Assert.Contains("href=\"/negozio\"", acceso, StringComparison.Ordinal);
        Assert.DoesNotContain("dv-barra-negozio", spento, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LaRicercaTrovaPerNomeECodiceSenzaDistinguereLeMaiuscoleELimitaAiVisibiliEAlTenant()
    {
        await using var c = await ContestoNegozio.CreaAsync();
        await c.CreaProdottoAsync(c.A, "Quaderno a righe", 22m, ("QUA-100", 5m, null));
        await c.CreaProdottoAsync(c.A, "Penna blu", 22m, ("PEN-200", 2m, null));
        await c.CreaProdottoAsync(c.A, "Quaderno segreto", 22m, StatoProdotto.Bozza, ("QUA-999", 5m, null));
        await c.CreaProdottoAsync(c.B, "Quaderno di B", 22m, ("QUB-1", 5m, null));
        var anonimo = c.Web.SessioneSu(c.A.Host);

        var perNome = await (await anonimo.GetAsync("/negozio?q=quaderno")).Content.ReadAsStringAsync(Ct);
        var perCodice = await (await anonimo.GetAsync("/negozio?q=pen-2")).Content.ReadAsStringAsync(Ct);
        var piuParole = await (await anonimo.GetAsync("/negozio?q=quaderno%20righe")).Content.ReadAsStringAsync(Ct);
        var nessuno = await (await anonimo.GetAsync("/negozio?q=zzz")).Content.ReadAsStringAsync(Ct);
        var vuota = await (await anonimo.GetAsync("/negozio?q=%20%20")).Content.ReadAsStringAsync(Ct);
        var xss = await (await anonimo.GetAsync("/negozio?q=%3Cscript%3Ealert(1)%3C%2Fscript%3E")).Content.ReadAsStringAsync(Ct);

        Assert.Contains("Quaderno a righe", perNome, StringComparison.Ordinal);
        Assert.DoesNotContain("Penna blu", perNome, StringComparison.Ordinal);
        Assert.DoesNotContain("Quaderno segreto", perNome, StringComparison.Ordinal);
        Assert.DoesNotContain("Quaderno di B", perNome, StringComparison.Ordinal);
        Assert.Contains("Penna blu", perCodice, StringComparison.Ordinal);
        Assert.DoesNotContain("Quaderno a righe", perCodice, StringComparison.Ordinal);
        Assert.Contains("Quaderno a righe", piuParole, StringComparison.Ordinal);
        Assert.Contains("Nessun prodotto corrisponde alla ricerca.", nessuno, StringComparison.Ordinal);
        Assert.Contains("Quaderno a righe", vuota, StringComparison.Ordinal);
        Assert.Contains("Penna blu", vuota, StringComparison.Ordinal);
        Assert.DoesNotContain("<script>alert", xss, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SenzaNegozioAttivoLaVetrinaNonEsiste()
    {
        await using var c = await ContestoNegozio.CreaAsync();
        var varianti = await c.CreaProdottoAsync(c.A, "Libro", 4m, ("LIB-1", 10m, null));
        await c.SpegniNegozioAsync(c.A);
        var anonimo = c.Web.SessioneSu(c.A.Host);

        using var elenco = await anonimo.GetAsync("/negozio");
        var idProdotto = await c.IdProdottoAsync(varianti[0]);
        using var scheda = await anonimo.GetAsync($"/negozio/{idProdotto}");

        Assert.Equal(HttpStatusCode.NotFound, elenco.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, scheda.StatusCode);
    }

    [Fact]
    public async Task SiVedonoSoloProdottiPubblicatiConVariantiAttiveConTestoCodificato()
    {
        await using var c = await ContestoNegozio.CreaAsync();
        await c.CreaProdottoAsync(c.A, "Maglietta <b>rossa</b>", 22m, ("TS-1", 19.90m, "Taglia M"), ("TS-2", 25m, "Taglia L"));
        await c.CreaProdottoAsync(c.A, "Bozza segreta", 22m, StatoProdotto.Bozza, ("BOZ-1", 5m, null));
        var archiviata = await c.CreaProdottoAsync(c.A, "Archiviata", 22m, ("ARC-1", 5m, null));
        await c.ArchiviaAsync(c.A, archiviata[0]);
        await c.CreaProdottoAsync(c.B, "Solo di B", 22m, ("B-1", 5m, null));
        var anonimo = c.Web.SessioneSu(c.A.Host);

        using var elenco = await anonimo.GetAsync("/negozio");

        var html = await elenco.Content.ReadAsStringAsync(Ct);
        Assert.Equal(HttpStatusCode.OK, elenco.StatusCode);
        Assert.Contains("Maglietta &lt;b&gt;rossa&lt;/b&gt;", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<b>rossa</b>", html, StringComparison.Ordinal);
        Assert.Contains("19,90 EUR", WebUtility.HtmlDecode(html), StringComparison.Ordinal);
        Assert.DoesNotContain("Bozza segreta", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Archiviata", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Solo di B", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnProdottoNonPubblicatoODiUnAltroTenantDa404()
    {
        await using var c = await ContestoNegozio.CreaAsync();
        var bozza = await c.CreaProdottoAsync(c.A, "Bozza", 22m, StatoProdotto.Bozza, ("BOZ-1", 5m, null));
        var diB = await c.CreaProdottoAsync(c.B, "Di B", 22m, ("B-1", 5m, null));
        var anonimo = c.Web.SessioneSu(c.A.Host);

        using var rispostaBozza = await anonimo.GetAsync($"/negozio/{await c.IdProdottoAsync(bozza[0])}");
        using var rispostaAltroTenant = await anonimo.GetAsync($"/negozio/{await c.IdProdottoAsync(diB[0])}");
        using var inesistente = await anonimo.GetAsync($"/negozio/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, rispostaBozza.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, rispostaAltroTenant.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, inesistente.StatusCode);
    }

    [Fact]
    public async Task LAnonimoVedeLaSchedaConIlLinkPerAccedereENonIlModuloPerAcquistare()
    {
        await using var c = await ContestoNegozio.CreaAsync();
        var varianti = await c.CreaProdottoAsync(c.A, "Libro", 4m, ("LIB-1", 10m, null));
        var id = await c.IdProdottoAsync(varianti[0]);
        var anonimo = c.Web.SessioneSu(c.A.Host);

        using var scheda = await anonimo.GetAsync($"/negozio/{id}");

        var html = await scheda.Content.ReadAsStringAsync(Ct);
        Assert.Equal(HttpStatusCode.OK, scheda.StatusCode);
        Assert.Contains($"/account/accesso?returnUrl=%2Fnegozio%2F{id}", html, StringComparison.Ordinal);
        Assert.DoesNotContain("handler=Aggiungi", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LAcquirenteAggiungeAlCarrelloDallaSchedaESoloConIdentificativoEQuantita()
    {
        await using var c = await ContestoNegozio.CreaAsync();
        var varianti = await c.CreaProdottoAsync(c.A, "Libro", 4m, ("LIB-1", 10m, null));
        var id = await c.IdProdottoAsync(varianti[0]);
        var cliente = await ClienteAsync(c);

        using var scheda = await cliente.GetAsync($"/negozio/{id}");
        var html = await scheda.Content.ReadAsStringAsync(Ct);
        var token = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        Assert.Contains("handler=Aggiungi", html, StringComparison.Ordinal);
        Assert.NotEmpty(token);
        using var aggiunta = await cliente.InviaAsync(new HttpRequestMessage(HttpMethod.Post, "/account/carrello?handler=Aggiungi")
        {
            Content = new FormUrlEncodedContent([new("idVariante", varianti[0].ToString()), new("quantita", "2"), new("prezzo", "0.01"), new("__RequestVerificationToken", WebUtility.HtmlDecode(token))]),
        });

        Assert.True(SessioneProva.EReindirizzamento(aggiunta), $"{aggiunta.StatusCode}");
        var carrello = await c.ComeCarrelloAsync(c.A, s => s.LeggiAsync(c.Cliente, "it", Ct));
        Assert.Equal((2, 10m), (carrello.Righe.Single().Quantita, carrello.Righe.Single().PrezzoUnitarioLordo));
    }

    [Fact]
    public async Task LaDisponibilitaDipendeDallaGiacenzaLibera()
    {
        await using var c = await ContestoNegozio.CreaAsync();
        var varianti = await c.CreaProdottoAsync(c.A, "Libro", 4m, ("LIB-1", 10m, "Base"), ("LIB-2", 12m, "Extra"), ("LIB-3", 15m, "Libero"));
        await c.ImpostaGiacenzaAsync(c.A, varianti[0], 3);
        await c.ImpostaGiacenzaAsync(c.A, varianti[1], 0);
        var id = await c.IdProdottoAsync(varianti[0]);
        var cliente = await ClienteAsync(c);
        var anonimo = c.Web.SessioneSu(c.A.Host);

        using var scheda = await cliente.GetAsync($"/negozio/{id}");
        using var elenco = await anonimo.GetAsync("/negozio");

        var html = await scheda.Content.ReadAsStringAsync(Ct);
        Assert.Contains("max=\"3\"", html, StringComparison.Ordinal);
        Assert.Contains("max=\"99\"", html, StringComparison.Ordinal);
        Assert.Contains("Esaurito", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Esaurito", await elenco.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
    }
}
