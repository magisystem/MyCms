using DvCms.Application.Negozio;
using DvCms.Application.Piattaforma;
using DvCms.Application.Tenancy;
using DvCms.Infrastructure.Persistenza;
using DvCms.IntegrationTests.Supporto;
using DvCms.IntegrationTests.Web;
using DvCms.Models.Accessi;
using DvCms.Models.Negozio;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DvCms.IntegrationTests.Negozio;

/// <summary>Catalogo del negozio (F-001, F-006): regole di validazione, codici articolo, concorrenza, permessi e isolamento.</summary>
public class ServizioCatalogoTest
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static VarianteDati Variante(string sku = "TSHIRT-M", decimal prezzo = 19.90m, Dictionary<string, string>? etichette = null, Guid? id = null, bool attiva = true, string? valuta = null) =>
        new(id, sku, prezzo, valuta, attiva, etichette);

    private static DatiProdotto Prodotto(
        string nome = "Maglietta",
        VarianteDati[]? varianti = null,
        decimal iva = 22m,
        StatoProdotto stato = StatoProdotto.Bozza,
        Guid? id = null,
        string? token = null,
        TraduzioneProdottoDati[]? traduzioni = null) =>
        new(id, token, stato, iva, traduzioni ?? [new("it", nome, "Cotone <b>100%</b>")], varianti ?? [Variante()]);

    [Fact]
    public async Task SenzaNegozioAttivoNonSiScriveNeSiLegge()
    {
        await using var c = await Contesto.CreaAsync();

        var esito = await c.ComeAsync(c.A, c.AdminA, s => s.SalvaAsync(c.AdminA, Prodotto(), Ct));
        var elenco = await c.ComeAsync(c.A, c.AdminA, s => s.ElencaAsync(c.AdminA, Ct));

        Assert.Equal(CodiceEsito.NonConsentito, esito.Codice);
        Assert.Empty(elenco);
        Assert.False(await c.ComeAsync(c.A, c.AdminA, s => s.CommercioAttivoAsync(Ct)));
    }

    [Fact]
    public async Task CreaLeggeEdElencaUnProdottoConVariantiETraduzioni()
    {
        await using var c = await Contesto.CreaAsync();
        await c.AttivaCommercioAsync(c.A);
        var dati = Prodotto(
            varianti:
            [
                Variante("tshirt-m", 19.90m, new() { ["it"] = "Taglia M", ["en"] = "Size M" }),
                Variante("TSHIRT-L", 21.00m, new() { ["it"] = "Taglia L" }),
                Variante("TSHIRT-XL", 5.00m, new() { ["it"] = "Taglia XL" }, attiva: false),
            ],
            traduzioni: [new("it", "Maglietta", "Cotone <b>100%</b>"), new("en", "T-shirt", null)],
            stato: StatoProdotto.Pubblicato);

        var esito = await c.ComeAsync(c.A, c.AdminA, s => s.SalvaAsync(c.AdminA, dati, Ct));
        Assert.True(esito.Riuscito, $"{esito.Codice} {string.Join(',', esito.CampiNonValidi ?? [])}");

        var dettaglio = (await c.ComeAsync(c.A, c.AdminA, s => s.LeggiAsync(c.AdminA, esito.Id!.Value, Ct)))!;
        Assert.Equal(StatoProdotto.Pubblicato, dettaglio.Stato);
        Assert.Equal(22m, dettaglio.AliquotaIva);
        Assert.Equal(["TSHIRT-M", "TSHIRT-L", "TSHIRT-XL"], dettaglio.Varianti.Select(v => v.Sku));
        Assert.Equal([19.90m, 21.00m, 5.00m], dettaglio.Varianti.Select(v => v.PrezzoLordo));
        Assert.All(dettaglio.Varianti, v => Assert.Equal("EUR", v.Valuta));
        Assert.Equal("Size M", dettaglio.Varianti[0].Etichette["en"]);
        Assert.Equal("Cotone <b>100%</b>", dettaglio.Traduzioni.Single(t => t.CodiceLingua == "it").Descrizione);
        Assert.Equal("T-shirt", dettaglio.Traduzioni.Single(t => t.CodiceLingua == "en").Nome);

        // Il "prezzo da" considera solo le varianti attive.
        var elenco = await c.ComeAsync(c.A, c.AdminA, s => s.ElencaAsync(c.AdminA, Ct));
        var riga = Assert.Single(elenco);
        Assert.Equal(("Maglietta", 3, 19.90m), (riga.Nome, riga.Varianti, riga.PrezzoDa));
    }

    public static TheoryData<string, Func<DatiProdotto>> NonValidi() => new()
    {
        { "AliquotaIva", () => Prodotto(iva: 21m) },
        { "Prezzo", () => Prodotto(varianti: [Variante(prezzo: 0m)]) },
        { "Prezzo", () => Prodotto(varianti: [Variante(prezzo: -1m)]) },
        { "Prezzo", () => Prodotto(varianti: [Variante(prezzo: 10.005m)]) },
        { "Prezzo", () => Prodotto(varianti: [Variante(prezzo: 1_000_000.01m)]) },
        { "Valuta", () => Prodotto(varianti: [Variante(valuta: "USD")]) },
        { "Valuta", () => Prodotto(varianti: [Variante(valuta: "eur")]) },
        { "Sku", () => Prodotto(varianti: [Variante(sku: "con spazi")]) },
        { "Sku", () => Prodotto(varianti: [Variante(sku: "")]) },
        { "Sku", () => Prodotto(varianti: [Variante(sku: "-iniziale")]) },
        { "Sku", () => Prodotto(varianti: [Variante(sku: new string('A', 51))]) },
        { "Sku", () => Prodotto(varianti: [Variante("UGUALE", etichette: new() { ["it"] = "a" }), Variante("uguale", etichette: new() { ["it"] = "b" })]) },
        { "Varianti", () => Prodotto(varianti: []) },
        { "Varianti", () => Prodotto(stato: StatoProdotto.Pubblicato, varianti: [Variante(attiva: false)]) },
        { "Nome", () => Prodotto(nome: "   ") },
        { "Nome", () => Prodotto(nome: new string('x', 201)) },
        { "Nome", () => Prodotto(nome: "con\u0007controllo") },
        { "Nome", () => Prodotto(traduzioni: [new("en", "Solo inglese", null)]) },
        { "Traduzioni", () => Prodotto(traduzioni: [new("it", "A", null), new("it", "B", null)]) },
        { "Traduzioni", () => Prodotto(traduzioni: [new("it", "A", null), new("fr", "B", null)]) },
        { "Descrizione", () => Prodotto(traduzioni: [new("it", "A", new string('d', 5001))]) },
        { "Stato", () => Prodotto(stato: StatoProdotto.Archiviato) },
        { "Etichetta", () => Prodotto(varianti: [Variante("A"), Variante("B")]) },
        { "Etichetta", () => Prodotto(varianti: [Variante(etichette: new() { ["fr"] = "x" })]) },
    };

    [Theory]
    [MemberData(nameof(NonValidi))]
    public async Task UnProdottoNonValidoERifiutatoConIlNomeDelCampoENonSalvato(string campo, Func<DatiProdotto> dati)
    {
        await using var c = await Contesto.CreaAsync();
        await c.AttivaCommercioAsync(c.A);

        var esito = await c.ComeAsync(c.A, c.AdminA, s => s.SalvaAsync(c.AdminA, dati(), Ct));

        Assert.Equal(CodiceEsito.DatiNonValidi, esito.Codice);
        Assert.Contains(campo, esito.CampiNonValidi!);
        Assert.Equal(0, await c.ContaAsync(db => db.Prodotti.IgnoreQueryFilters().CountAsync(Ct)));
        Assert.Equal(0, await c.ContaAsync(db => db.VariantiProdotto.IgnoreQueryFilters().CountAsync(Ct)));
    }

    [Fact]
    public async Task IlCodiceArticoloEUnicoNelSitoMaPuoRipetersiInUnAltroTenant()
    {
        await using var c = await Contesto.CreaAsync();
        await c.AttivaCommercioAsync(c.A);
        await c.AttivaCommercioAsync(c.B);
        var primo = await c.ComeAsync(c.A, c.AdminA, s => s.SalvaAsync(c.AdminA, Prodotto(varianti: [Variante("CODICE-1")]), Ct));
        Assert.True(primo.Riuscito);

        var duplicato = await c.ComeAsync(c.A, c.AdminA, s => s.SalvaAsync(c.AdminA, Prodotto("Altro", [Variante("codice-1")]), Ct));
        var altroTenant = await c.ComeAsync(c.B, c.AdminB, s => s.SalvaAsync(c.AdminB, Prodotto("Altro", [Variante("CODICE-1")]), Ct));

        Assert.Equal(CodiceEsito.GiaPresente, duplicato.Codice);
        Assert.Contains("Sku", duplicato.CampiNonValidi!);
        Assert.True(altroTenant.Riuscito, altroTenant.Codice.ToString());
        Assert.Equal(1, await c.ContaAsync(db => db.Prodotti.IgnoreQueryFilters().CountAsync(p => p.IdTenant == c.A.IdTenant, Ct)));
    }

    [Fact]
    public async Task LaModificaSostituisceVariantiETraduzioniELaConcorrenzaEControllata()
    {
        await using var c = await Contesto.CreaAsync();
        await c.AttivaCommercioAsync(c.A);
        var creato = await c.ComeAsync(c.A, c.AdminA, s => s.SalvaAsync(c.AdminA, Prodotto(
            traduzioni: [new("it", "Maglietta", null), new("en", "T-shirt", null)],
            varianti: [Variante("A", 10m, new() { ["it"] = "Rossa" }), Variante("B", 12m, new() { ["it"] = "Blu" })]), Ct));
        var dettaglio = (await c.ComeAsync(c.A, c.AdminA, s => s.LeggiAsync(c.AdminA, creato.Id!.Value, Ct)))!;
        var idA = dettaglio.Varianti.Single(v => v.Sku == "A").Id;

        // Si tiene A (prezzo nuovo), si toglie B, si aggiunge C; si toglie la traduzione inglese.
        var modifica = Prodotto(
            id: dettaglio.Id,
            token: dettaglio.TokenConcorrenza,
            iva: 10m,
            stato: StatoProdotto.Pubblicato,
            traduzioni: [new("it", "Maglietta nuova", "Descrizione")],
            varianti: [Variante("A", 11.50m, new() { ["it"] = "Rossa" }, id: idA), Variante("C", 13m, new() { ["it"] = "Verde" })]);
        var esito = await c.ComeAsync(c.A, c.AdminA, s => s.SalvaAsync(c.AdminA, modifica, Ct));
        Assert.True(esito.Riuscito, $"{esito.Codice} {string.Join(',', esito.CampiNonValidi ?? [])}");

        var dopo = (await c.ComeAsync(c.A, c.AdminA, s => s.LeggiAsync(c.AdminA, dettaglio.Id, Ct)))!;
        Assert.Equal(["A", "C"], dopo.Varianti.Select(v => v.Sku));
        Assert.Equal(idA, dopo.Varianti[0].Id);
        Assert.Equal(11.50m, dopo.Varianti[0].PrezzoLordo);
        Assert.Equal(10m, dopo.AliquotaIva);
        Assert.Equal(["it"], dopo.Traduzioni.Select(t => t.CodiceLingua));
        Assert.Equal(1, await c.ContaAsync(db => db.TraduzioniProdotto.IgnoreQueryFilters().CountAsync(Ct)));
        Assert.Equal(2, await c.ContaAsync(db => db.TraduzioniVarianteProdotto.IgnoreQueryFilters().CountAsync(Ct)));

        // Con il token vecchio la modifica è rifiutata e non cambia nulla.
        var vecchio = await c.ComeAsync(c.A, c.AdminA, s => s.SalvaAsync(c.AdminA, modifica with { Traduzioni = [new("it", "Troppo tardi", null)] }, Ct));
        Assert.Equal(CodiceEsito.Conflitto, vecchio.Codice);
        var invariato = (await c.ComeAsync(c.A, c.AdminA, s => s.LeggiAsync(c.AdminA, dettaglio.Id, Ct)))!;
        Assert.Equal("Maglietta nuova", invariato.Traduzioni.Single().Nome);

        // Una variante di un altro prodotto non si può agganciare a questo.
        var altro = await c.ComeAsync(c.A, c.AdminA, s => s.SalvaAsync(c.AdminA, Prodotto("Altro", [Variante("Z")]), Ct));
        var dettaglioAltro = (await c.ComeAsync(c.A, c.AdminA, s => s.LeggiAsync(c.AdminA, altro.Id!.Value, Ct)))!;
        var rubata = Prodotto(id: dopo.Id, token: invariato.TokenConcorrenza, varianti: [Variante("Z", id: dettaglioAltro.Varianti[0].Id)]);
        var tentativo = await c.ComeAsync(c.A, c.AdminA, s => s.SalvaAsync(c.AdminA, rubata, Ct));
        Assert.Equal(CodiceEsito.DatiNonValidi, tentativo.Codice);
        Assert.Contains("Varianti", tentativo.CampiNonValidi!);
    }

    [Fact]
    public async Task SoloGliAmministratoriDelTenantOperano()
    {
        await using var c = await Contesto.CreaAsync();
        await c.AttivaCommercioAsync(c.A);
        var acquirente = await DatiProva.CreaUtenzaAsync(c.Web.Fabbrica.Services, RuoloUtenza.Utente, c.A.IdTenant, "cliente@esempio.it", annullamento: Ct);

        var comeAcquirente = await c.ComeAsync(c.A, acquirente, s => s.SalvaAsync(acquirente, Prodotto(), Ct));
        var comeAltroTenant = await c.ComeAsync(c.A, c.AdminB, s => s.SalvaAsync(c.AdminB, Prodotto(), Ct));

        Assert.Equal(CodiceEsito.NonAutorizzato, comeAcquirente.Codice);
        Assert.Equal(CodiceEsito.NonAutorizzato, comeAltroTenant.Codice);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => c.ComeAsync(c.A, acquirente, s => s.ElencaAsync(acquirente, Ct)));
        Assert.Equal(0, await c.ContaAsync(db => db.Prodotti.IgnoreQueryFilters().CountAsync(Ct)));
    }

    [Fact]
    public async Task DisattivareIlNegozioSpegneLaRegistrazioneDegliAcquirentiENonCancellaIlCatalogo()
    {
        await using var c = await Contesto.CreaAsync();
        await c.AttivaCommercioAsync(c.A);
        await c.ComeAsync(c.A, c.AdminA, s => s.SalvaAsync(c.AdminA, Prodotto(), Ct));
        await c.ContaAsync(async db =>
        {
            await db.Siti.IgnoreQueryFilters().Where(s => s.Id == c.A.IdSito).ExecuteUpdateAsync(x => x.SetProperty(s => s.RegistrazioneAcquirentiAbilitata, true), Ct);
            return 0;
        });

        var spento = await c.ComePlatformAsync(p => p.ImpostaCommercioAsync(c.Platform, c.A.IdTenant, c.A.IdSito, false, Ct));
        var sito = await c.ContaAsync(db => db.Siti.IgnoreQueryFilters().Where(s => s.Id == c.A.IdSito).Select(s => s.RegistrazioneAcquirentiAbilitata || s.CommercioAttivo ? 1 : 0).SingleAsync(Ct));
        var comeTenantAdmin = await c.ComePlatformAsync(p => p.ImpostaCommercioAsync(c.AdminA, c.A.IdTenant, c.A.IdSito, true, Ct));

        Assert.True(spento.Riuscito);
        Assert.Equal(0, sito);
        Assert.Equal(CodiceEsito.NonAutorizzato, comeTenantAdmin.Codice);
        Assert.Equal(1, await c.ContaAsync(db => db.Prodotti.IgnoreQueryFilters().CountAsync(Ct)));
    }

    [Fact]
    public async Task IlDatabaseRifiutaUnaRegistrazioneAcquirentiSenzaNegozio()
    {
        await using var c = await Contesto.CreaAsync();

        await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(() => c.ContaAsync(async db =>
        {
            await db.Siti.IgnoreQueryFilters().Where(s => s.Id == c.A.IdSito).ExecuteUpdateAsync(x => x.SetProperty(s => s.RegistrazioneAcquirentiAbilitata, true), Ct);
            return 0;
        }));
    }

    private sealed class Contesto : IAsyncDisposable
    {
        private Contesto(ScenarioWeb web, Guid adminA, Guid adminB, Guid platform)
        {
            Web = web;
            AdminA = adminA;
            AdminB = adminB;
            Platform = platform;
        }

        public ScenarioWeb Web { get; }

        public DatiProva.TenantCreato A => Web.A;

        public DatiProva.TenantCreato B => Web.B;

        public Guid AdminA { get; }

        public Guid AdminB { get; }

        public Guid Platform { get; }

        public static async Task<Contesto> CreaAsync()
        {
            var web = await ScenarioWeb.CreaAsync();
            var adminA = await web.CreaTenantAdminAsync("anna@esempio.it");
            var adminB = await DatiProva.CreaUtenzaAsync(web.Fabbrica.Services, RuoloUtenza.TenantAdmin, web.B.IdTenant, "bruno@esempio.it", annullamento: Ct);
            var platform = await web.CreaPlatformAdminAsync("root@esempio.it");
            return new Contesto(web, adminA, adminB, platform);
        }

        public async Task<T> ComeAsync<T>(DatiProva.TenantCreato tenant, Guid attore, Func<IServizioCatalogo, Task<T>> operazione)
        {
            await using var ambito = Web.Fabbrica.Services.CreateAsyncScope();
            ambito.ServiceProvider.GetRequiredService<ContestoTenant>().Imposta(new DatiTenantRisolto(tenant.IdTenant, tenant.IdSito, tenant.IdDominio, tenant.Host, false));
            return await operazione(ambito.ServiceProvider.GetRequiredService<IServizioCatalogo>());
        }

        public async Task<T> ComePlatformAsync<T>(Func<IServizioPiattaforma, Task<T>> operazione)
        {
            await using var ambito = Web.Fabbrica.Services.CreateAsyncScope();
            return await operazione(ambito.ServiceProvider.GetRequiredService<IServizioPiattaforma>());
        }

        public async Task<int> ContaAsync(Func<ContestoDatabase, Task<int>> lettura)
        {
            await using var ambito = Web.Fabbrica.Services.CreateAsyncScope();
            return await lettura(ambito.ServiceProvider.GetRequiredService<ContestoDatabase>());
        }

        public async Task AttivaCommercioAsync(DatiProva.TenantCreato tenant)
        {
            var esito = await ComePlatformAsync(p => p.ImpostaCommercioAsync(Platform, tenant.IdTenant, tenant.IdSito, true, Ct));
            Assert.True(esito.Riuscito, esito.Codice.ToString());
        }

        public ValueTask DisposeAsync() => Web.DisposeAsync();
    }
}
