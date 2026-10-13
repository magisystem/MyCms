using System.Net;
using DvCms.Contracts.Negozio;
using DvCms.Contracts.Piattaforma;
using DvCms.Core.Tenancy;
using DvCms.Data;
using DvCms.DTO.Negozio;
using DvCms.DTO.Tenancy;
using DvCms.IntegrationTests.Supporto;
using DvCms.Models.Accessi;
using DvCms.Models.Negozio;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DvCms.IntegrationTests.Web;

/// <summary>Cassa e ordini dal modulo web (F-003): totali del server, indirizzo, proprietà dell'ordine, testo codificato e isolamento.</summary>
public class OrdiniWebTest
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static List<KeyValuePair<string, string>> Cassa(Guid metodo, string atteso, string destinatario = "Mario Rossi", string paese = "IT")
    {
        return
        [
            new("Modulo.IdMetodo", metodo.ToString()),
            new($"Modulo.Atteso[{metodo}]", atteso),
            new("Modulo.Destinatario", destinatario),
            new("Modulo.IndirizzoRiga1", "Via Roma 1"),
            new("Modulo.Cap", "20100"),
            new("Modulo.Citta", "Milano"),
            new("Modulo.Paese", paese),
        ];
    }

    [Fact]
    public async Task SenzaSessioneClienteLaCassaELeSchermateOrdiniRimandanoAllAccesso()
    {
        await using var c = await Contesto.CreaAsync();
        var anonimo = c.Web.SessioneA();

        foreach (var percorso in new[] { "/account/checkout", "/account/ordini", $"/account/ordini/{Guid.NewGuid()}" })
        {
            using var risposta = await anonimo.GetAsync(percorso);
            Assert.True(SessioneProva.EReindirizzamento(risposta), percorso);
            Assert.StartsWith("/account/accesso", SessioneProva.Destinazione(risposta), StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task CarrelloVuotoPortaAlCarrelloEIlCarrelloPienoOffreLaCassa()
    {
        await using var c = await Contesto.CreaAsync();
        var sessione = await c.AccediAsync(c.Web.A, "cliente@esempio.it");

        using var vuoto = await sessione.GetAsync("/account/checkout");
        Assert.True(SessioneProva.EReindirizzamento(vuoto));
        Assert.Equal("/account/carrello", SessioneProva.Destinazione(vuoto));

        await Contesto.AggiungiAsync(sessione, await c.CreaProdottoAsync(c.Web.A, "Libro", 4m, "LIB-1", 12.00m));
        using var carrello = await sessione.GetAsync("/account/carrello");
        Assert.Contains("/account/checkout", await carrello.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task LOrdineSiCreaDallaCassaConTotaliDelServerEIlTestoEcodificato()
    {
        await using var c = await Contesto.CreaAsync();
        var metodo = await c.CreaMetodoAsync(c.Web.A, "Corriere <i>veloce</i>", 6.90m);
        var sessione = await c.AccediAsync(c.Web.A, "cliente@esempio.it");
        await Contesto.AggiungiAsync(sessione, await c.CreaProdottoAsync(c.Web.A, "Maglietta <b>rossa</b>", 22m, "TS-M", 19.90m), quantita: 2);

        using var cassa = await sessione.GetAsync("/account/checkout");
        var htmlCassa = await cassa.Content.ReadAsStringAsync(Ct);
        Assert.Contains("Corriere &lt;i&gt;veloce&lt;/i&gt;", htmlCassa, StringComparison.Ordinal);
        Assert.Contains("46,70 EUR", WebUtility.HtmlDecode(htmlCassa), StringComparison.Ordinal); // 39,80 + 6,90
        Assert.Contains("value=\"46.70\"", htmlCassa, StringComparison.Ordinal);

        using var risposta = await sessione.InviaModuloAsync("/account/checkout", Cassa(metodo, "46.70", destinatario: "Mario <script>alert(1)</script>"));

        Assert.True(SessioneProva.EReindirizzamento(risposta), WebUtility.HtmlDecode(await risposta.Content.ReadAsStringAsync(Ct)));
        Assert.Matches("^/account/ordini/[0-9a-f-]{36}$", SessioneProva.Destinazione(risposta));
        using var dettaglio = await sessione.GetAsync(SessioneProva.Destinazione(risposta)!);
        var html = await dettaglio.Content.ReadAsStringAsync(Ct);
        var testo = WebUtility.HtmlDecode(html);
        Assert.Equal(HttpStatusCode.OK, dettaglio.StatusCode);
        Assert.Contains("Maglietta &lt;b&gt;rossa&lt;/b&gt;", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<script>alert(1)</script>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<b>rossa</b>", html, StringComparison.Ordinal);
        Assert.Contains("46,70 EUR", testo, StringComparison.Ordinal);
        Assert.Contains("In attesa di pagamento", testo, StringComparison.Ordinal);
        using var elenco = await sessione.GetAsync("/account/ordini");
        Assert.Contains(">#1<", await elenco.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        using var carrello = await sessione.GetAsync("/account/carrello");
        Assert.Contains("Il carrello è vuoto", WebUtility.HtmlDecode(await carrello.Content.ReadAsStringAsync(Ct)), StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnTotaleManomessoOUnIndirizzoNonValidoNonCreanoOrdiniENonPerdonoIDati()
    {
        await using var c = await Contesto.CreaAsync();
        var metodo = await c.CreaMetodoAsync(c.Web.A, "Corriere", 6.90m);
        var sessione = await c.AccediAsync(c.Web.A, "cliente@esempio.it");
        await Contesto.AggiungiAsync(sessione, await c.CreaProdottoAsync(c.Web.A, "Libro", 4m, "LIB-1", 12.00m));

        using var totale = await sessione.InviaModuloAsync("/account/checkout", Cassa(metodo, "0.01", destinatario: "Da non perdere"));
        using var paese = await sessione.InviaModuloAsync("/account/checkout", Cassa(metodo, "18.90", destinatario: "Da non perdere", paese: "FR"));
        using var senzaMetodo = await sessione.InviaModuloAsync("/account/checkout", [new("Modulo.Destinatario", "Nessuno")]);

        foreach (var risposta in new[] { totale, paese, senzaMetodo })
        {
            Assert.Equal(HttpStatusCode.OK, risposta.StatusCode);
            Assert.Contains("role=\"alert\"", await risposta.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        }

        Assert.Contains("Da non perdere", await paese.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        Assert.Equal(0, await c.ContaOrdiniAsync());
    }

    [Fact]
    public async Task SiAnnullaUnOrdineNonPagatoELAmministrazioneLoVedeInSolaLettura()
    {
        await using var c = await Contesto.CreaAsync();
        var metodo = await c.CreaMetodoAsync(c.Web.A, "Corriere", 6.90m);
        var cliente = await c.AccediAsync(c.Web.A, "cliente@esempio.it");
        await Contesto.AggiungiAsync(cliente, await c.CreaProdottoAsync(c.Web.A, "Libro", 4m, "LIB-1", 12.00m));
        using var creato = await cliente.InviaModuloAsync("/account/checkout", Cassa(metodo, "18.90"));
        var percorso = SessioneProva.Destinazione(creato)!;
        var anna = await c.AccediAsync(c.Web.A, "anna@esempio.it");

        using var elencoAdmin = await anna.GetAsync("/admin/negozio/ordini");
        using var dettaglioAdmin = await anna.GetAsync(percorso.Replace("/account/ordini/", "/admin/negozio/ordini/", StringComparison.Ordinal));
        using var annulla = await cliente.InviaModuloAsync(percorso, [], $"{percorso}?handler=Annulla");
        using var dopo = await cliente.GetAsync(percorso);

        Assert.Contains("cliente@esempio.it", await elencoAdmin.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        var testoAdmin = WebUtility.HtmlDecode(await dettaglioAdmin.Content.ReadAsStringAsync(Ct));
        Assert.Contains("Via Roma 1", testoAdmin, StringComparison.Ordinal);
        Assert.Contains("18,90 EUR", testoAdmin, StringComparison.Ordinal);
        Assert.True(SessioneProva.EReindirizzamento(annulla));
        Assert.Contains("Annullato", WebUtility.HtmlDecode(await dopo.Content.ReadAsStringAsync(Ct)), StringComparison.Ordinal);
        Assert.DoesNotContain("Annulla l'ordine", WebUtility.HtmlDecode(await dopo.Content.ReadAsStringAsync(Ct)), StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnOrdineAltruiOdiUnAltroTenantNonSiVedeNeSiAnnulla()
    {
        await using var c = await Contesto.CreaAsync();
        var metodo = await c.CreaMetodoAsync(c.Web.A, "Corriere", 6.90m);
        var cliente = await c.AccediAsync(c.Web.A, "cliente@esempio.it");
        var altro = await c.AccediAsync(c.Web.A, "altro@esempio.it");
        var bruno = await c.AccediAsync(c.Web.B, "bruno@esempio.it");
        var clienteDiB = await c.AccediAsync(c.Web.B, "cliente@esempio.it");
        await Contesto.AggiungiAsync(cliente, await c.CreaProdottoAsync(c.Web.A, "Libro", 4m, "LIB-1", 12.00m));
        using var creato = await cliente.InviaModuloAsync("/account/checkout", Cassa(metodo, "18.90"));
        var percorso = SessioneProva.Destinazione(creato)!;

        using var daAltro = await altro.GetAsync(percorso);
        using var annullaDaAltro = await altro.InviaModuloAsync("/account", [], $"{percorso}?handler=Annulla");
        using var elencoAltro = await altro.GetAsync("/account/ordini");
        using var daClienteDiB = await clienteDiB.GetAsync(percorso);
        using var adminDiB = await bruno.GetAsync(percorso.Replace("/account/ordini/", "/admin/negozio/ordini/", StringComparison.Ordinal));
        using var elencoDiB = await bruno.GetAsync("/admin/negozio/ordini");

        Assert.Equal(HttpStatusCode.NotFound, daAltro.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, annullaDaAltro.StatusCode);
        Assert.DoesNotContain(percorso, await elencoAltro.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NotFound, daClienteDiB.StatusCode);
        Assert.NotEqual(HttpStatusCode.OK, adminDiB.StatusCode);
        Assert.DoesNotContain("cliente@esempio.it", await elencoDiB.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        using var ancora = await cliente.GetAsync(percorso);
        Assert.Contains("In attesa di pagamento", WebUtility.HtmlDecode(await ancora.Content.ReadAsStringAsync(Ct)), StringComparison.Ordinal);
    }

    private sealed class Contesto : IAsyncDisposable
    {
        private Contesto(ScenarioWeb web) => Web = web;

        public ScenarioWeb Web { get; }

        public Guid Platform { get; private set; }

        public Guid AdminA { get; private set; }

        public Guid AdminB { get; private set; }

        public static async Task<Contesto> CreaAsync()
        {
            var c = new Contesto(await ScenarioWeb.CreaAsync());
            c.Platform = await c.Web.CreaPlatformAdminAsync("root@esempio.it");
            c.AdminA = await c.Web.CreaTenantAdminAsync("anna@esempio.it");
            c.AdminB = await DatiProva.CreaUtenzaAsync(c.Web.Fabbrica.Services, RuoloUtenza.TenantAdmin, c.Web.B.IdTenant, "bruno@esempio.it", annullamento: Ct);
            await DatiProva.CreaUtenzaAsync(c.Web.Fabbrica.Services, RuoloUtenza.Utente, c.Web.A.IdTenant, "cliente@esempio.it", annullamento: Ct);
            await DatiProva.CreaUtenzaAsync(c.Web.Fabbrica.Services, RuoloUtenza.Utente, c.Web.A.IdTenant, "altro@esempio.it", annullamento: Ct);
            await DatiProva.CreaUtenzaAsync(c.Web.Fabbrica.Services, RuoloUtenza.Utente, c.Web.B.IdTenant, "cliente@esempio.it", annullamento: Ct);
            foreach (var tenant in new[] { c.Web.A, c.Web.B })
            {
                await using var ambito = c.Web.Fabbrica.Services.CreateAsyncScope();
                var esito = await ambito.ServiceProvider.GetRequiredService<IServizioPiattaforma>().ImpostaCommercioAsync(c.Platform, tenant.IdTenant, tenant.IdSito, true, Ct);
                Assert.True(esito.Riuscito, esito.Codice.ToString());
            }

            return c;
        }

        /// <summary>Apre una sessione: i clienti dalla pagina di accesso dell'area cliente, gli amministratori da quella dell'admin.</summary>
        public async Task<SessioneProva> AccediAsync(DatiProva.TenantCreato tenant, string email)
        {
            var sessione = Web.SessioneSu(tenant.Host);
            if (email.StartsWith("anna", StringComparison.Ordinal) || email.StartsWith("bruno", StringComparison.Ordinal))
            {
                await sessione.AccediAsync(email, DatiProva.PasswordProva);
            }
            else
            {
                using var risposta = await sessione.InviaModuloAsync("/account/accesso", [new("Email", email), new("Password", DatiProva.PasswordProva)]);
                Assert.True(SessioneProva.EReindirizzamento(risposta), email);
            }

            return sessione;
        }

        public static async Task AggiungiAsync(SessioneProva sessione, Guid idVariante, int quantita = 1)
        {
            using var risposta = await sessione.InviaModuloAsync(
                "/account",
                [new("idVariante", idVariante.ToString()), new("quantita", quantita.ToString(System.Globalization.CultureInfo.InvariantCulture))],
                "/account/carrello?handler=Aggiungi");
            Assert.True(SessioneProva.EReindirizzamento(risposta), $"{risposta.StatusCode}");
        }

        public async Task<Guid> CreaProdottoAsync(DatiProva.TenantCreato tenant, string nome, decimal iva, string sku, decimal prezzo)
        {
            await using var ambito = Web.Fabbrica.Services.CreateAsyncScope();
            ambito.ServiceProvider.GetRequiredService<ContestoTenant>().Imposta(new DatiTenantRisolto(tenant.IdTenant, tenant.IdSito, tenant.IdDominio, tenant.Host, false));
            var catalogo = ambito.ServiceProvider.GetRequiredService<IServizioCatalogo>();
            var attore = tenant.IdTenant == Web.A.IdTenant ? AdminA : AdminB;
            var esito = await catalogo.SalvaAsync(attore, new DatiProdotto(null, null, StatoProdotto.Pubblicato, iva, [new("it", nome, null)], [new(null, sku, prezzo, null, true, null)]), Ct);
            Assert.True(esito.Riuscito, esito.Codice.ToString());
            return (await catalogo.LeggiAsync(attore, esito.Id!.Value, Ct))!.Varianti.Single().Id;
        }

        public async Task<Guid> CreaMetodoAsync(DatiProva.TenantCreato tenant, string nome, decimal tariffa)
        {
            await using var ambito = Web.Fabbrica.Services.CreateAsyncScope();
            ambito.ServiceProvider.GetRequiredService<ContestoTenant>().Imposta(new DatiTenantRisolto(tenant.IdTenant, tenant.IdSito, tenant.IdDominio, tenant.Host, false));
            var attore = tenant.IdTenant == Web.A.IdTenant ? AdminA : AdminB;
            var esito = await ambito.ServiceProvider.GetRequiredService<IServizioSpedizioni>().SalvaAsync(attore, new(null, TipoMetodoSpedizione.Zona, AreaSpedizione.Italia, nome, tariffa, true), Ct);
            Assert.True(esito.Riuscito, esito.Codice.ToString());
            return esito.Id!.Value;
        }

        public async Task<int> ContaOrdiniAsync()
        {
            await using var ambito = Web.Fabbrica.Services.CreateAsyncScope();
            return await ambito.ServiceProvider.GetRequiredService<ContestoDatabase>().Ordini.IgnoreQueryFilters().CountAsync(Ct);
        }

        public ValueTask DisposeAsync() => Web.DisposeAsync();
    }
}
