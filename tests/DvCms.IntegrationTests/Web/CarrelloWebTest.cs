using System.Net;
using DvCms.Application.Negozio;
using DvCms.Application.Piattaforma;
using DvCms.Application.Tenancy;
using DvCms.Infrastructure.Persistenza;
using DvCms.IntegrationTests.Supporto;
using DvCms.Models.Accessi;
using DvCms.Models.Negozio;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DvCms.IntegrationTests.Web;

/// <summary>Carrello dal modulo web (F-002): solo identificativo e quantità, valori sempre dal server, testo codificato.</summary>
public class CarrelloWebTest
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<string> Testo(HttpResponseMessage risposta) => WebUtility.HtmlDecode(await risposta.Content.ReadAsStringAsync(Ct));

    [Fact]
    public async Task SenzaSessioneClienteIlCarrelloRimandaAllAccessoEConUnAdminNonSiApre()
    {
        await using var c = await Contesto.CreaAsync();
        var anonimo = c.Web.SessioneA();
        using var senza = await anonimo.GetAsync("/account/carrello");
        Assert.True(SessioneProva.EReindirizzamento(senza));
        Assert.StartsWith("/account/accesso", SessioneProva.Destinazione(senza), StringComparison.Ordinal);

        var admin = c.Web.SessioneA();
        await admin.AccediAsync("anna@esempio.it", DatiProva.PasswordProva);
        using var comeAdmin = await admin.GetAsync("/account/carrello");
        Assert.NotEqual(HttpStatusCode.OK, comeAdmin.StatusCode);
    }

    [Fact]
    public async Task SiAggiungeSiAggiornaSiRimuoveESiVedonoSoloTotaliCalcolatiDalServer()
    {
        await using var c = await Contesto.CreaAsync();
        var idVariante = await c.CreaProdottoAsync("Maglietta <b>rossa</b>", 22m, "TS-M", 19.90m);
        var sessione = await c.AccediClienteAsync();

        // Un prezzo e un totale manomessi nel modulo vengono ignorati: il server legge solo idVariante e quantita.
        using var aggiunta = await sessione.InviaModuloAsync(
            "/account",
            [new("idVariante", idVariante.ToString()), new("quantita", "2"), new("prezzo", "0.01"), new("totale", "0.01"), new("aliquota", "0")],
            "/account/carrello?handler=Aggiungi");
        Assert.True(SessioneProva.EReindirizzamento(aggiunta), $"{aggiunta.StatusCode}");

        using var pagina = await sessione.GetAsync("/account/carrello");
        var html = await pagina.Content.ReadAsStringAsync(Ct);
        var testo = WebUtility.HtmlDecode(html);
        Assert.Equal(HttpStatusCode.OK, pagina.StatusCode);
        Assert.Contains("Maglietta &lt;b&gt;rossa&lt;/b&gt;", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<b>rossa</b>", html, StringComparison.Ordinal);
        Assert.Contains("19,90 EUR", testo, StringComparison.Ordinal);
        Assert.Contains("39,80 EUR", testo, StringComparison.Ordinal);
        Assert.Contains("32,62 EUR", testo, StringComparison.Ordinal); // imponibile 39,80 / 1,22
        Assert.Contains("7,18 EUR", testo, StringComparison.Ordinal);
        Assert.Equal(2, await c.QuantitaAsync(idVariante));

        using var aggiorna = await sessione.InviaModuloAsync("/account", [new("idVariante", idVariante.ToString()), new("quantita", "5")], "/account/carrello?handler=Quantita");
        Assert.True(SessioneProva.EReindirizzamento(aggiorna));
        Assert.Equal(5, await c.QuantitaAsync(idVariante));
        Assert.Contains("99,50 EUR", await Testo(await sessione.GetAsync("/account/carrello")), StringComparison.Ordinal);

        // Quantità fuori limite: errore visibile e nulla cambia.
        using var troppe = await sessione.InviaModuloAsync("/account", [new("idVariante", idVariante.ToString()), new("quantita", "500")], "/account/carrello?handler=Quantita");
        Assert.True(SessioneProva.EReindirizzamento(troppe));
        var conErrore = await Testo(await sessione.GetAsync("/account/carrello"));
        Assert.Contains("role=\"alert\"", conErrore, StringComparison.Ordinal);
        Assert.Equal(5, await c.QuantitaAsync(idVariante));

        using var rimuovi = await sessione.InviaModuloAsync("/account", [new("idVariante", idVariante.ToString())], "/account/carrello?handler=Rimuovi");
        Assert.True(SessioneProva.EReindirizzamento(rimuovi));
        Assert.Contains("carrello è vuoto", await Testo(await sessione.GetAsync("/account/carrello")), StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnaVarianteInventataOAltruiNonEntraNelCarrelloESvuotaLoSvuota()
    {
        await using var c = await Contesto.CreaAsync();
        var propria = await c.CreaProdottoAsync("Mio", 22m, "MIO-1", 10m);
        var sessione = await c.AccediClienteAsync();

        foreach (var id in new[] { Guid.NewGuid(), Guid.Empty })
        {
            using var rifiutata = await sessione.InviaModuloAsync("/account", [new("idVariante", id.ToString()), new("quantita", "1")], "/account/carrello?handler=Aggiungi");
            Assert.True(SessioneProva.EReindirizzamento(rifiutata));
        }

        Assert.Equal(0, await c.ContaRigheAsync());
        Assert.Contains("role=\"alert\"", await Testo(await sessione.GetAsync("/account/carrello")), StringComparison.Ordinal);

        await sessione.InviaModuloAsync("/account", [new("idVariante", propria.ToString()), new("quantita", "3")], "/account/carrello?handler=Aggiungi");
        Assert.Equal(1, await c.ContaRigheAsync());
        using var svuota = await sessione.InviaModuloAsync("/account", [], "/account/carrello?handler=Svuota");
        Assert.True(SessioneProva.EReindirizzamento(svuota));
        Assert.Equal(0, await c.ContaRigheAsync());
    }

    private sealed class Contesto : IAsyncDisposable
    {
        private Contesto(ScenarioWeb web) => Web = web;

        public ScenarioWeb Web { get; }

        public Guid Platform { get; private set; }

        public Guid AdminA { get; private set; }

        public static async Task<Contesto> CreaAsync()
        {
            var c = new Contesto(await ScenarioWeb.CreaAsync());
            c.Platform = await c.Web.CreaPlatformAdminAsync("root@esempio.it");
            c.AdminA = await c.Web.CreaTenantAdminAsync("anna@esempio.it");
            await DatiProva.CreaUtenzaAsync(c.Web.Fabbrica.Services, RuoloUtenza.Utente, c.Web.A.IdTenant, "cliente@esempio.it", annullamento: Ct);
            await using var ambito = c.Web.Fabbrica.Services.CreateAsyncScope();
            var esito = await ambito.ServiceProvider.GetRequiredService<IServizioPiattaforma>().ImpostaCommercioAsync(c.Platform, c.Web.A.IdTenant, c.Web.A.IdSito, true, Ct);
            Assert.True(esito.Riuscito, esito.Codice.ToString());
            return c;
        }

        public async Task<SessioneProva> AccediClienteAsync()
        {
            var sessione = Web.SessioneA();
            using var risposta = await sessione.InviaModuloAsync("/account/accesso", [new("Email", "cliente@esempio.it"), new("Password", DatiProva.PasswordProva)]);
            Assert.True(SessioneProva.EReindirizzamento(risposta));
            return sessione;
        }

        public async Task<Guid> CreaProdottoAsync(string nome, decimal iva, string sku, decimal prezzo)
        {
            await using var ambito = Web.Fabbrica.Services.CreateAsyncScope();
            ambito.ServiceProvider.GetRequiredService<ContestoTenant>().Imposta(new DatiTenantRisolto(Web.A.IdTenant, Web.A.IdSito, Web.A.IdDominio, Web.A.Host, false));
            var catalogo = ambito.ServiceProvider.GetRequiredService<IServizioCatalogo>();
            var esito = await catalogo.SalvaAsync(AdminA, new DatiProdotto(null, null, StatoProdotto.Pubblicato, iva, [new("it", nome, null)], [new(null, sku, prezzo, null, true, null)]), Ct);
            Assert.True(esito.Riuscito, esito.Codice.ToString());
            return (await catalogo.LeggiAsync(AdminA, esito.Id!.Value, Ct))!.Varianti.Single().Id;
        }

        public async Task<int> QuantitaAsync(Guid idVariante)
        {
            await using var ambito = Web.Fabbrica.Services.CreateAsyncScope();
            return await ambito.ServiceProvider.GetRequiredService<ContestoDatabase>().RigheCarrello.IgnoreQueryFilters().Where(r => r.IdVariante == idVariante).Select(r => r.Quantita).SingleOrDefaultAsync(Ct);
        }

        public async Task<int> ContaRigheAsync()
        {
            await using var ambito = Web.Fabbrica.Services.CreateAsyncScope();
            return await ambito.ServiceProvider.GetRequiredService<ContestoDatabase>().RigheCarrello.IgnoreQueryFilters().CountAsync(Ct);
        }

        public ValueTask DisposeAsync() => Web.DisposeAsync();
    }
}
