using DvCms.Contracts.Negozio;
using DvCms.Contracts.Piattaforma;
using DvCms.Core.Tenancy;
using DvCms.DTO.Negozio;
using DvCms.DTO.Piattaforma;
using DvCms.DTO.Tenancy;
using DvCms.Infrastructure.Persistenza;
using DvCms.IntegrationTests.Supporto;
using DvCms.IntegrationTests.Web;
using DvCms.Models.Accessi;
using DvCms.Models.Negozio;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DvCms.IntegrationTests.Negozio;

/// <summary>Carrello (F-002): solo identificativi e quantità dal cliente, totali sempre dal catalogo, isolamento fra account e tenant.</summary>
public class ServizioCarrelloTest
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task IlCarrelloSommaLeRigheConIvaScorporataEUsaIPrezziDelCatalogo()
    {
        await using var c = await Contesto.CreaAsync();
        var maglietta = await c.CreaProdottoAsync(c.A, "Maglietta <b>", 22m, ("TS-M", 19.90m, "Taglia M"), ("TS-L", 21.00m, "Taglia L"));
        var libro = await c.CreaProdottoAsync(c.A, "Libro", 4m, ("LIB-1", 12.00m, null));

        Assert.True((await c.ComeAsync(c.A, s => s.AggiungiAsync(c.Cliente, maglietta[0], 2, Ct))).Riuscito);
        Assert.True((await c.ComeAsync(c.A, s => s.AggiungiAsync(c.Cliente, libro[0], 1, Ct))).Riuscito);
        Assert.True((await c.ComeAsync(c.A, s => s.AggiungiAsync(c.Cliente, maglietta[0], 1, Ct))).Riuscito);
        var vista = await c.ComeAsync(c.A, s => s.LeggiAsync(c.Cliente, "it", Ct));

        Assert.Equal(["TS-M", "LIB-1"], vista.Righe.Select(r => r.Sku));
        Assert.Equal([3, 1], vista.Righe.Select(r => r.Quantita));
        Assert.Equal("Maglietta <b>", vista.Righe[0].NomeProdotto);
        Assert.Equal("Taglia M", vista.Righe[0].EtichettaVariante);
        Assert.Null(vista.Righe[1].EtichettaVariante);
        // 3 x 19,90 = 59,70 al 22% (imponibile 48,93; IVA 10,77) più 12,00 al 4% (imponibile 11,54; IVA 0,46).
        Assert.Equal([59.70m, 12.00m], vista.Righe.Select(r => r.Lordo));
        Assert.Equal([48.93m, 11.54m], vista.Righe.Select(r => r.Imponibile));
        Assert.Equal((71.70m, 60.47m, 11.23m), (vista.TotaleLordo, vista.TotaleImponibile, vista.TotaleIva));
        Assert.Equal(vista.TotaleLordo, vista.TotaleImponibile + vista.TotaleIva);

        // Il prezzo cambia nel catalogo: il carrello lo segue subito (nessun prezzo salvato nel carrello).
        await c.CambiaPrezzoAsync(c.A, "TS-M", 25.00m);
        var dopo = await c.ComeAsync(c.A, s => s.LeggiAsync(c.Cliente, "it", Ct));
        Assert.Equal(75.00m, dopo.Righe[0].Lordo);
        Assert.Equal(87.00m, dopo.TotaleLordo);
    }

    [Fact]
    public async Task QuantitaLimitiERigheSonoControllatiDalServer()
    {
        await using var c = await Contesto.CreaAsync();
        var varianti = await c.CreaProdottoAsync(c.A, "Tanti", 22m, [.. Enumerable.Range(0, 51).Select(i => ($"V{i}", 1.00m, (string?)$"Variante {i}"))]);

        foreach (var quantita in new[] { 0, -1, 100 })
        {
            var rifiutata = await c.ComeAsync(c.A, s => s.AggiungiAsync(c.Cliente, varianti[0], quantita, Ct));
            Assert.Equal(CodiceEsito.DatiNonValidi, rifiutata.Codice);
        }

        Assert.True((await c.ComeAsync(c.A, s => s.AggiungiAsync(c.Cliente, varianti[0], 99, Ct))).Riuscito);
        Assert.Equal(CodiceEsito.DatiNonValidi, (await c.ComeAsync(c.A, s => s.AggiungiAsync(c.Cliente, varianti[0], 1, Ct))).Codice);

        for (var i = 1; i < Carrello.RigheMassime; i++)
        {
            Assert.True((await c.ComeAsync(c.A, s => s.AggiungiAsync(c.Cliente, varianti[i], 1, Ct))).Riuscito);
        }

        var cinquantunesima = await c.ComeAsync(c.A, s => s.AggiungiAsync(c.Cliente, varianti[50], 1, Ct));
        Assert.Equal(CodiceEsito.DatiNonValidi, cinquantunesima.Codice);
        Assert.Contains("Righe", cinquantunesima.CampiNonValidi!);
        Assert.Equal(Carrello.RigheMassime, (await c.ComeAsync(c.A, s => s.LeggiAsync(c.Cliente, "it", Ct))).Righe.Count);
    }

    [Fact]
    public async Task SiAcquistaSoloCioChePubblicatoEAttivoNelProprioSito()
    {
        await using var c = await Contesto.CreaAsync();
        var bozza = await c.CreaProdottoAsync(c.A, "Bozza", 22m, StatoProdotto.Bozza, ("BOZZA-1", 5m, null));
        var attivo = await c.CreaProdottoAsync(c.A, "Pubblico", 22m, ("OK-1", 5m, "A"), ("OK-2", 6m, "B"));
        var altroTenant = await c.CreaProdottoAsync(c.B, "Di B", 22m, ("B-1", 5m, null));
        await c.DisattivaVarianteAsync(c.A, "OK-2");

        var esiti = new[]
        {
            await c.ComeAsync(c.A, s => s.AggiungiAsync(c.Cliente, bozza[0], 1, Ct)),
            await c.ComeAsync(c.A, s => s.AggiungiAsync(c.Cliente, attivo[1], 1, Ct)),
            await c.ComeAsync(c.A, s => s.AggiungiAsync(c.Cliente, altroTenant[0], 1, Ct)),
            await c.ComeAsync(c.A, s => s.AggiungiAsync(c.Cliente, Guid.NewGuid(), 1, Ct)),
        };

        Assert.All(esiti, e => Assert.Equal(CodiceEsito.NonTrovato, e.Codice));
        Assert.Empty((await c.ComeAsync(c.A, s => s.LeggiAsync(c.Cliente, "it", Ct))).Righe);
    }

    [Fact]
    public async Task UnArticoloChePerdeDisponibilitaRestaVisibileMaFuoriDaiTotaliEPuoEssereTolto()
    {
        await using var c = await Contesto.CreaAsync();
        var p = await c.CreaProdottoAsync(c.A, "Prodotto", 22m, ("P-1", 10m, "Uno"), ("P-2", 20m, "Due"));
        await c.ComeAsync(c.A, s => s.AggiungiAsync(c.Cliente, p[0], 1, Ct));
        await c.ComeAsync(c.A, s => s.AggiungiAsync(c.Cliente, p[1], 1, Ct));

        await c.DisattivaVarianteAsync(c.A, "P-1");
        var vista = await c.ComeAsync(c.A, s => s.LeggiAsync(c.Cliente, "it", Ct));
        Assert.Equal([p[0]], vista.NonDisponibili);
        Assert.Equal(["P-2"], vista.Righe.Select(r => r.Sku));
        Assert.Equal(20m, vista.TotaleLordo);

        // Non si può più aumentarne la quantità, ma si può togliere.
        Assert.Equal(CodiceEsito.NonTrovato, (await c.ComeAsync(c.A, s => s.ImpostaQuantitaAsync(c.Cliente, p[0], 3, Ct))).Codice);
        Assert.True((await c.ComeAsync(c.A, s => s.ImpostaQuantitaAsync(c.Cliente, p[0], 0, Ct))).Riuscito);
        Assert.Empty((await c.ComeAsync(c.A, s => s.LeggiAsync(c.Cliente, "it", Ct))).NonDisponibili);

        // Se il prodotto viene archiviato, anche l'ultima riga esce dai totali.
        await c.ArchiviaAsync(c.A, p[1]);
        var archiviato = await c.ComeAsync(c.A, s => s.LeggiAsync(c.Cliente, "it", Ct));
        Assert.Equal((0m, 1), (archiviato.TotaleLordo, archiviato.NonDisponibili.Count));
    }

    [Fact]
    public async Task EliminareUnaVarianteDalCatalogoLaTogliAncheDaiCarrelli()
    {
        await using var c = await Contesto.CreaAsync();
        var p = await c.CreaProdottoAsync(c.A, "Prodotto", 22m, ("P-1", 10m, "Uno"), ("P-2", 20m, "Due"));
        await c.ComeAsync(c.A, s => s.AggiungiAsync(c.Cliente, p[0], 1, Ct));

        await c.TogliVarianteAsync(c.A, "P-1");

        var vista = await c.ComeAsync(c.A, s => s.LeggiAsync(c.Cliente, "it", Ct));
        Assert.Empty(vista.Righe);
        Assert.Empty(vista.NonDisponibili);
        Assert.Equal(0, await c.ContaRigheAsync());
    }

    [Fact]
    public async Task IlCarrelloEPersonaleEIsolatoFraAccountETenant()
    {
        await using var c = await Contesto.CreaAsync();
        var p = await c.CreaProdottoAsync(c.A, "Prodotto", 22m, ("P-1", 10m, null));
        await c.ComeAsync(c.A, s => s.AggiungiAsync(c.Cliente, p[0], 2, Ct));

        // Un altro acquirente dello stesso tenant ha il suo carrello vuoto e non vede quello altrui.
        Assert.Empty((await c.ComeAsync(c.A, s => s.LeggiAsync(c.AltroCliente, "it", Ct))).Righe);
        // Lo stesso account sull'host di un altro tenant non è valido (e uno di B non lo è su A).
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => c.ComeAsync(c.B, s => s.LeggiAsync(c.Cliente, "it", Ct)));
        Assert.Equal(CodiceEsito.NonAutorizzato, (await c.ComeAsync(c.B, s => s.AggiungiAsync(c.Cliente, p[0], 1, Ct))).Codice);
        Assert.Equal(CodiceEsito.NonAutorizzato, (await c.ComeAsync(c.A, s => s.AggiungiAsync(c.ClienteDiB, p[0], 1, Ct))).Codice);
        Assert.Equal(2, (await c.ComeAsync(c.A, s => s.LeggiAsync(c.Cliente, "it", Ct))).Righe.Single().Quantita);

        // Modifica e svuotamento riguardano solo il proprio carrello.
        Assert.Equal(CodiceEsito.NonTrovato, (await c.ComeAsync(c.A, s => s.ImpostaQuantitaAsync(c.AltroCliente, p[0], 5, Ct))).Codice);
        Assert.True((await c.ComeAsync(c.A, s => s.SvuotaAsync(c.AltroCliente, Ct))).Riuscito);
        Assert.Equal(2, (await c.ComeAsync(c.A, s => s.LeggiAsync(c.Cliente, "it", Ct))).Righe.Single().Quantita);
    }

    [Fact]
    public async Task UnAccountDisabilitatoONonClienteNonPuoOperare()
    {
        await using var c = await Contesto.CreaAsync();
        var p = await c.CreaProdottoAsync(c.A, "Prodotto", 22m, ("P-1", 10m, null));
        await c.DisabilitaAsync(c.AltroCliente);

        Assert.Equal(CodiceEsito.NonAutorizzato, (await c.ComeAsync(c.A, s => s.AggiungiAsync(c.AltroCliente, p[0], 1, Ct))).Codice);
        Assert.Equal(CodiceEsito.NonAutorizzato, (await c.ComeAsync(c.A, s => s.AggiungiAsync(Guid.NewGuid(), p[0], 1, Ct))).Codice);
        // Il TenantAdmin del tenant può ordinare (ADR-0011).
        Assert.True((await c.ComeAsync(c.A, s => s.AggiungiAsync(c.AdminA, p[0], 1, Ct))).Riuscito);
        // Il negozio spento blocca le scritture e svuota la vista.
        await c.SpegniNegozioAsync(c.A);
        Assert.Equal(CodiceEsito.NonConsentito, (await c.ComeAsync(c.A, s => s.AggiungiAsync(c.AdminA, p[0], 1, Ct))).Codice);
        Assert.Empty((await c.ComeAsync(c.A, s => s.LeggiAsync(c.AdminA, "it", Ct))).Righe);
    }

    [Fact]
    public async Task NomiEdEtichetteSeguonoLaLinguaRichiestaConRipiegoSullaPredefinita()
    {
        await using var c = await Contesto.CreaAsync();
        var p = await c.CreaProdottoAsync(c.A, "Maglietta", 22m, new[] { new TraduzioneProdottoDati("it", "Maglietta", null), new TraduzioneProdottoDati("en", "T-shirt", null) }, StatoProdotto.Pubblicato, ("TS", 10m, "Rossa"));
        await c.ComeAsync(c.A, s => s.AggiungiAsync(c.Cliente, p[0], 1, Ct));

        var inglese = (await c.ComeAsync(c.A, s => s.LeggiAsync(c.Cliente, "en", Ct))).Righe.Single();
        var tedesco = (await c.ComeAsync(c.A, s => s.LeggiAsync(c.Cliente, "de", Ct))).Righe.Single();

        Assert.Equal(("T-shirt", "Rossa"), (inglese.NomeProdotto, inglese.EtichettaVariante));
        Assert.Equal(("Maglietta", "Rossa"), (tedesco.NomeProdotto, tedesco.EtichettaVariante));
    }

    private sealed class Contesto : IAsyncDisposable
    {
        private Contesto(ScenarioWeb web) => Web = web;

        public ScenarioWeb Web { get; }

        public DatiProva.TenantCreato A => Web.A;

        public DatiProva.TenantCreato B => Web.B;

        public Guid Platform { get; private set; }

        public Guid AdminA { get; private set; }

        public Guid Cliente { get; private set; }

        public Guid AltroCliente { get; private set; }

        public Guid ClienteDiB { get; private set; }

        public static async Task<Contesto> CreaAsync()
        {
            var c = new Contesto(await ScenarioWeb.CreaAsync());
            c.Platform = await c.Web.CreaPlatformAdminAsync("root@esempio.it");
            c.AdminA = await c.Web.CreaTenantAdminAsync("anna@esempio.it");
            c.Cliente = await DatiProva.CreaUtenzaAsync(c.Web.Fabbrica.Services, RuoloUtenza.Utente, c.A.IdTenant, "cliente@esempio.it", annullamento: Ct);
            c.AltroCliente = await DatiProva.CreaUtenzaAsync(c.Web.Fabbrica.Services, RuoloUtenza.Utente, c.A.IdTenant, "altro@esempio.it", annullamento: Ct);
            c.ClienteDiB = await DatiProva.CreaUtenzaAsync(c.Web.Fabbrica.Services, RuoloUtenza.Utente, c.B.IdTenant, "cliente@esempio.it", annullamento: Ct);
            foreach (var tenant in new[] { c.A, c.B })
            {
                await using var ambito = c.Web.Fabbrica.Services.CreateAsyncScope();
                var esito = await ambito.ServiceProvider.GetRequiredService<IServizioPiattaforma>().ImpostaCommercioAsync(c.Platform, tenant.IdTenant, tenant.IdSito, true, Ct);
                Assert.True(esito.Riuscito, esito.Codice.ToString());
            }

            return c;
        }

        public async Task<T> ComeAsync<T>(DatiProva.TenantCreato tenant, Func<IServizioCarrello, Task<T>> operazione)
        {
            await using var ambito = Web.Fabbrica.Services.CreateAsyncScope();
            ambito.ServiceProvider.GetRequiredService<ContestoTenant>().Imposta(new DatiTenantRisolto(tenant.IdTenant, tenant.IdSito, tenant.IdDominio, tenant.Host, false));
            return await operazione(ambito.ServiceProvider.GetRequiredService<IServizioCarrello>());
        }

        private async Task<T> ComeCatalogoAsync<T>(DatiProva.TenantCreato tenant, Func<IServizioCatalogo, Guid, Task<T>> operazione)
        {
            await using var ambito = Web.Fabbrica.Services.CreateAsyncScope();
            ambito.ServiceProvider.GetRequiredService<ContestoTenant>().Imposta(new DatiTenantRisolto(tenant.IdTenant, tenant.IdSito, tenant.IdDominio, tenant.Host, false));
            var attore = tenant.IdTenant == A.IdTenant ? AdminA : Platform;
            return await operazione(ambito.ServiceProvider.GetRequiredService<IServizioCatalogo>(), attore);
        }

        /// <summary>Crea un prodotto pubblicato con le varianti indicate (codice, prezzo, etichetta) e restituisce gli identificativi delle varianti.</summary>
        public Task<IReadOnlyList<Guid>> CreaProdottoAsync(DatiProva.TenantCreato tenant, string nome, decimal iva, params (string Sku, decimal Prezzo, string? Etichetta)[] varianti) =>
            CreaProdottoAsync(tenant, nome, iva, StatoProdotto.Pubblicato, varianti);

        public Task<IReadOnlyList<Guid>> CreaProdottoAsync(DatiProva.TenantCreato tenant, string nome, decimal iva, StatoProdotto stato, params (string Sku, decimal Prezzo, string? Etichetta)[] varianti) =>
            CreaProdottoAsync(tenant, nome, iva, [new TraduzioneProdottoDati("it", nome, null)], stato, varianti);

        public async Task<IReadOnlyList<Guid>> CreaProdottoAsync(DatiProva.TenantCreato tenant, string nome, decimal iva, IReadOnlyList<TraduzioneProdottoDati> traduzioni, StatoProdotto stato, params (string Sku, decimal Prezzo, string? Etichetta)[] varianti)
        {
            var dati = new DatiProdotto(
                null,
                null,
                stato,
                iva,
                traduzioni,
                [.. varianti.Select(v => new VarianteDati(null, v.Sku, v.Prezzo, null, true, v.Etichetta is null ? null : new Dictionary<string, string> { ["it"] = v.Etichetta, ["en"] = v.Etichetta }))]);
            var esito = await ComeCatalogoAsync(tenant, (s, attore) => s.SalvaAsync(attore, dati, Ct));
            Assert.True(esito.Riuscito, $"{esito.Codice} {string.Join(',', esito.CampiNonValidi ?? [])}");
            var dettaglio = (await ComeCatalogoAsync(tenant, (s, attore) => s.LeggiAsync(attore, esito.Id!.Value, Ct)))!;
            return [.. dettaglio.Varianti.Select(v => v.Id)];
        }

        public Task CambiaPrezzoAsync(DatiProva.TenantCreato tenant, string sku, decimal prezzo) =>
            ModificaAsync(tenant, sku, (d, v) => d with { Varianti = [.. d.Varianti.Select(x => x with { PrezzoLordo = x.Sku == sku ? prezzo : x.PrezzoLordo })] });

        public Task DisattivaVarianteAsync(DatiProva.TenantCreato tenant, string sku) =>
            ModificaAsync(tenant, sku, (d, v) => d with { Varianti = [.. d.Varianti.Select(x => x with { Attiva = x.Sku != sku && x.Attiva })] });

        public Task TogliVarianteAsync(DatiProva.TenantCreato tenant, string sku) =>
            ModificaAsync(tenant, sku, (d, v) => d with { Varianti = [.. d.Varianti.Where(x => x.Sku != sku)] });

        public async Task ArchiviaAsync(DatiProva.TenantCreato tenant, Guid idVariante)
        {
            var idProdotto = await ProdottoDellaVarianteAsync(idVariante);
            var dettaglio = (await ComeCatalogoAsync(tenant, (s, attore) => s.LeggiAsync(attore, idProdotto, Ct)))!;
            var dati = new DatiProdotto(dettaglio.Id, dettaglio.TokenConcorrenza, StatoProdotto.Archiviato, dettaglio.AliquotaIva, dettaglio.Traduzioni, [.. dettaglio.Varianti.Select(v => new VarianteDati(v.Id, v.Sku, v.PrezzoLordo, v.Valuta, v.Attiva, v.Etichette))]);
            var esito = await ComeCatalogoAsync(tenant, (s, attore) => s.SalvaAsync(attore, dati, Ct));
            Assert.True(esito.Riuscito, esito.Codice.ToString());
        }

        private async Task ModificaAsync(DatiProva.TenantCreato tenant, string sku, Func<DatiProdotto, Guid, DatiProdotto> trasforma)
        {
            await using var ambito = Web.Fabbrica.Services.CreateAsyncScope();
            var idProdotto = await ambito.ServiceProvider.GetRequiredService<ContestoDatabase>().VariantiProdotto.IgnoreQueryFilters()
                .Where(v => v.IdSito == tenant.IdSito && v.Sku == sku).Select(v => v.IdProdotto).SingleAsync(Ct);
            var dettaglio = (await ComeCatalogoAsync(tenant, (s, attore) => s.LeggiAsync(attore, idProdotto, Ct)))!;
            var dati = new DatiProdotto(
                dettaglio.Id,
                dettaglio.TokenConcorrenza,
                dettaglio.Stato,
                dettaglio.AliquotaIva,
                dettaglio.Traduzioni,
                [.. dettaglio.Varianti.Select(v => new VarianteDati(v.Id, v.Sku, v.PrezzoLordo, v.Valuta, v.Attiva, v.Etichette))]);
            var modificati = trasforma(dati, idProdotto);
            var esito = await ComeCatalogoAsync(tenant, (s, attore) => s.SalvaAsync(attore, modificati, Ct));
            Assert.True(esito.Riuscito, $"{esito.Codice} {string.Join(',', esito.CampiNonValidi ?? [])}");
        }

        private async Task<Guid> ProdottoDellaVarianteAsync(Guid idVariante)
        {
            await using var ambito = Web.Fabbrica.Services.CreateAsyncScope();
            return await ambito.ServiceProvider.GetRequiredService<ContestoDatabase>().VariantiProdotto.IgnoreQueryFilters().Where(v => v.Id == idVariante).Select(v => v.IdProdotto).SingleAsync(Ct);
        }

        public async Task DisabilitaAsync(Guid idAccount)
        {
            await using var ambito = Web.Fabbrica.Services.CreateAsyncScope();
            await ambito.ServiceProvider.GetRequiredService<ContestoDatabase>().Utenze.IgnoreQueryFilters().Where(u => u.Id == idAccount)
                .ExecuteUpdateAsync(x => x.SetProperty(u => u.Stato, StatoUtenza.Disabilitata), Ct);
        }

        public async Task SpegniNegozioAsync(DatiProva.TenantCreato tenant)
        {
            await using var ambito = Web.Fabbrica.Services.CreateAsyncScope();
            var esito = await ambito.ServiceProvider.GetRequiredService<IServizioPiattaforma>().ImpostaCommercioAsync(Platform, tenant.IdTenant, tenant.IdSito, false, Ct);
            Assert.True(esito.Riuscito);
        }

        public async Task<int> ContaRigheAsync()
        {
            await using var ambito = Web.Fabbrica.Services.CreateAsyncScope();
            return await ambito.ServiceProvider.GetRequiredService<ContestoDatabase>().RigheCarrello.IgnoreQueryFilters().CountAsync(Ct);
        }

        public ValueTask DisposeAsync() => Web.DisposeAsync();
    }
}
