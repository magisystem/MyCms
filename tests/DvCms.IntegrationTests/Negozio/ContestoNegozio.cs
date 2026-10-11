using DvCms.Application.Negozio;
using DvCms.Application.Piattaforma;
using DvCms.Application.Tenancy;
using DvCms.Domain.Accessi;
using DvCms.Domain.Negozio;
using DvCms.Infrastructure.Persistenza;
using DvCms.IntegrationTests.Supporto;
using DvCms.IntegrationTests.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DvCms.IntegrationTests.Negozio;

/// <summary>Scenario condiviso dai test del negozio a livello di servizio: due tenant, amministratori, clienti, catalogo, spedizioni, ordini.</summary>
internal sealed class ContestoNegozio : IAsyncDisposable
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private ContestoNegozio(ScenarioWeb web) => Web = web;

    public ScenarioWeb Web { get; }

    public DatiProva.TenantCreato A => Web.A;

    public DatiProva.TenantCreato B => Web.B;

    public Guid Platform { get; private set; }

    public Guid AdminA { get; private set; }

    public Guid AdminB { get; private set; }

    public Guid Cliente { get; private set; }

    public Guid AltroCliente { get; private set; }

    public Guid ClienteDiB { get; private set; }

    public static async Task<ContestoNegozio> CreaAsync()
    {
        var c = new ContestoNegozio(await ScenarioWeb.CreaAsync());
        c.Platform = await c.Web.CreaPlatformAdminAsync("root@esempio.it");
        c.AdminA = await c.Web.CreaTenantAdminAsync("anna@esempio.it");
        c.AdminB = await DatiProva.CreaUtenzaAsync(c.Web.Fabbrica.Services, RuoloUtenza.TenantAdmin, c.B.IdTenant, "bruno@esempio.it", annullamento: Ct);
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

    public Task<T> ComeAsync<T>(DatiProva.TenantCreato tenant, Func<IServizioOrdini, Task<T>> operazione) => InAsync(tenant, operazione);

    public Task<T> ComeCarrelloAsync<T>(DatiProva.TenantCreato tenant, Func<IServizioCarrello, Task<T>> operazione) => InAsync(tenant, operazione);

    private async Task<T> InAsync<TServizio, T>(DatiProva.TenantCreato tenant, Func<TServizio, Task<T>> operazione)
        where TServizio : notnull
    {
        await using var ambito = Web.Fabbrica.Services.CreateAsyncScope();
        ambito.ServiceProvider.GetRequiredService<ContestoTenant>().Imposta(new DatiTenantRisolto(tenant.IdTenant, tenant.IdSito, tenant.IdDominio, tenant.Host, false));
        return await operazione(ambito.ServiceProvider.GetRequiredService<TServizio>());
    }

    private Task<T> InAsync<T>(DatiProva.TenantCreato tenant, Func<IServizioOrdini, Task<T>> operazione) => InAsync<IServizioOrdini, T>(tenant, operazione);

    public Guid Attore(DatiProva.TenantCreato tenant) => tenant.IdTenant == A.IdTenant ? AdminA : AdminB;

    public async Task AggiungiAsync(Guid cliente, Guid idVariante, int quantita, DatiProva.TenantCreato? tenant = null)
    {
        var esito = await InAsync<IServizioCarrello, EsitoOperazione>(tenant ?? A, s => s.AggiungiAsync(cliente, idVariante, quantita, Ct));
        Assert.True(esito.Riuscito, esito.Codice.ToString());
    }

    public async Task<IReadOnlyList<Guid>> CreaProdottoAsync(DatiProva.TenantCreato tenant, string nome, decimal iva, params (string Sku, decimal Prezzo, string? Etichetta)[] varianti)
    {
        var dati = new DatiProdotto(null, null, StatoProdotto.Pubblicato, iva, [new TraduzioneProdottoDati("it", nome, null)],
            [.. varianti.Select(v => new VarianteDati(null, v.Sku, v.Prezzo, null, true, v.Etichetta is null ? null : new Dictionary<string, string> { ["it"] = v.Etichetta }))]);
        var esito = await InAsync<IServizioCatalogo, EsitoOperazione>(tenant, s => s.SalvaAsync(Attore(tenant), dati, Ct));
        Assert.True(esito.Riuscito, $"{esito.Codice} {string.Join(',', esito.CampiNonValidi ?? [])}");
        var dettaglio = (await InAsync<IServizioCatalogo, DettaglioProdotto?>(tenant, s => s.LeggiAsync(Attore(tenant), esito.Id!.Value, Ct)))!;
        return [.. dettaglio.Varianti.Select(v => v.Id)];
    }

    public async Task<Guid> CreaMetodoAsync(DatiProva.TenantCreato tenant, string nome, decimal tariffa, AreaSpedizione area, bool attivo = true)
    {
        var esito = await InAsync<IServizioSpedizioni, EsitoOperazione>(tenant, s => s.SalvaAsync(Attore(tenant), new(null, TipoMetodoSpedizione.Zona, area, nome, tariffa, attivo), Ct));
        Assert.True(esito.Riuscito, esito.Codice.ToString());
        return esito.Id!.Value;
    }

    public async Task<Guid> CreaRitiroAsync(DatiProva.TenantCreato tenant)
    {
        var esito = await InAsync<IServizioSpedizioni, EsitoOperazione>(tenant, s => s.SalvaAsync(Attore(tenant), new(null, TipoMetodoSpedizione.Ritiro, null, "Ritiro in negozio", 0m, true), Ct));
        Assert.True(esito.Riuscito, esito.Codice.ToString());
        return esito.Id!.Value;
    }

    public async Task EliminaMetodoAsync(DatiProva.TenantCreato tenant, Guid id) =>
        Assert.True((await InAsync<IServizioSpedizioni, EsitoOperazione>(tenant, s => s.EliminaAsync(Attore(tenant), id, Ct))).Riuscito);

    public Task CambiaPrezzoAsync(DatiProva.TenantCreato tenant, string sku, decimal prezzo) =>
        ModificaAsync(tenant, sku, d => d with { Varianti = [.. d.Varianti.Select(x => x with { PrezzoLordo = x.Sku == sku ? prezzo : x.PrezzoLordo })] });

    public Task DisattivaVarianteAsync(DatiProva.TenantCreato tenant, string sku) =>
        ModificaAsync(tenant, sku, d => d with { Varianti = [.. d.Varianti.Select(x => x with { Attiva = x.Sku != sku && x.Attiva }), new VarianteDati(null, "ALTRA", 1m, null, true, new Dictionary<string, string> { ["it"] = "Altra" })] });

    public Task TogliVarianteAsync(DatiProva.TenantCreato tenant, string sku) =>
        ModificaAsync(tenant, sku, d => d with { Varianti = [.. d.Varianti.Where(x => x.Sku != sku), new VarianteDati(null, "ALTRA", 1m, null, true, new Dictionary<string, string> { ["it"] = "Altra" })] });

    private async Task ModificaAsync(DatiProva.TenantCreato tenant, string sku, Func<DatiProdotto, DatiProdotto> trasforma)
    {
        await using var ambito = Web.Fabbrica.Services.CreateAsyncScope();
        var idProdotto = await ambito.ServiceProvider.GetRequiredService<ContestoDatabase>().VariantiProdotto.IgnoreQueryFilters()
            .Where(v => v.IdSito == tenant.IdSito && v.Sku == sku).Select(v => v.IdProdotto).SingleAsync(Ct);
        var dettaglio = (await InAsync<IServizioCatalogo, DettaglioProdotto?>(tenant, s => s.LeggiAsync(Attore(tenant), idProdotto, Ct)))!;
        var dati = new DatiProdotto(dettaglio.Id, dettaglio.TokenConcorrenza, dettaglio.Stato, dettaglio.AliquotaIva, dettaglio.Traduzioni,
            [.. dettaglio.Varianti.Select(v => new VarianteDati(v.Id, v.Sku, v.PrezzoLordo, v.Valuta, v.Attiva, v.Etichette))]);
        var modificati = trasforma(dati);
        var esito = await InAsync<IServizioCatalogo, EsitoOperazione>(tenant, s => s.SalvaAsync(Attore(tenant), modificati, Ct));
        Assert.True(esito.Riuscito, $"{esito.Codice} {string.Join(',', esito.CampiNonValidi ?? [])}");
    }

    /// <summary>Come <see cref="TogliVarianteAsync"/> ma senza pretendere che riesca: restituisce l'esito del catalogo.</summary>
    public async Task<EsitoOperazione> TentaTogliereVarianteAsync(DatiProva.TenantCreato tenant, string sku)
    {
        await using var ambito = Web.Fabbrica.Services.CreateAsyncScope();
        var idProdotto = await ambito.ServiceProvider.GetRequiredService<ContestoDatabase>().VariantiProdotto.IgnoreQueryFilters()
            .Where(v => v.IdSito == tenant.IdSito && v.Sku == sku).Select(v => v.IdProdotto).SingleAsync(Ct);
        var dettaglio = (await InAsync<IServizioCatalogo, DettaglioProdotto?>(tenant, s => s.LeggiAsync(Attore(tenant), idProdotto, Ct)))!;
        var dati = new DatiProdotto(dettaglio.Id, dettaglio.TokenConcorrenza, dettaglio.Stato, dettaglio.AliquotaIva, dettaglio.Traduzioni,
            [.. dettaglio.Varianti.Where(v => v.Sku != sku).Select(v => new VarianteDati(v.Id, v.Sku, v.PrezzoLordo, v.Valuta, v.Attiva, v.Etichette))]);
        return await InAsync<IServizioCatalogo, EsitoOperazione>(tenant, s => s.SalvaAsync(Attore(tenant), dati, Ct));
    }

    public async Task ImpostaStatoAsync(Guid idOrdine, StatoOrdine stato)
    {
        await using var ambito = Web.Fabbrica.Services.CreateAsyncScope();
        await ambito.ServiceProvider.GetRequiredService<ContestoDatabase>().Ordini.IgnoreQueryFilters().Where(o => o.Id == idOrdine)
            .ExecuteUpdateAsync(x => x.SetProperty(o => o.Stato, stato), Ct);
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
        Assert.True((await ambito.ServiceProvider.GetRequiredService<IServizioPiattaforma>().ImpostaCommercioAsync(Platform, tenant.IdTenant, tenant.IdSito, false, Ct)).Riuscito);
    }

    public async Task<int> ContaOrdiniAsync()
    {
        await using var ambito = Web.Fabbrica.Services.CreateAsyncScope();
        return await ambito.ServiceProvider.GetRequiredService<ContestoDatabase>().Ordini.IgnoreQueryFilters().CountAsync(Ct);
    }


    public Task<T> ComeGiacenzeAsync<T>(DatiProva.TenantCreato tenant, Func<IServizioGiacenze, Task<T>> operazione) => InAsync<IServizioGiacenze, T>(tenant, operazione);

    public async Task ImpostaGiacenzaAsync(DatiProva.TenantCreato tenant, Guid idVariante, int quantita)
    {
        var esito = await ComeGiacenzeAsync(tenant, s => s.ImpostaAsync(Attore(tenant), idVariante, quantita, Ct));
        Assert.True(esito.Riuscito, esito.Codice.ToString());
    }

    public async Task<GiacenzaVariante> GiacenzaAsync(Guid idVariante)
    {
        await using var ambito = Web.Fabbrica.Services.CreateAsyncScope();
        return await ambito.ServiceProvider.GetRequiredService<ContestoDatabase>().GiacenzeVarianti.IgnoreQueryFilters().AsNoTracking().SingleAsync(g => g.IdVariante == idVariante, Ct);
    }

    public async Task<List<PrenotazioneGiacenza>> PrenotazioniAsync(Guid idOrdine)
    {
        await using var ambito = Web.Fabbrica.Services.CreateAsyncScope();
        return await ambito.ServiceProvider.GetRequiredService<ContestoDatabase>().PrenotazioniGiacenza.IgnoreQueryFilters().AsNoTracking().Where(p => p.IdOrdine == idOrdine).ToListAsync(Ct);
    }

    public async Task<List<MovimentoGiacenza>> MovimentiAsync(Guid idVariante)
    {
        await using var ambito = Web.Fabbrica.Services.CreateAsyncScope();
        return await ambito.ServiceProvider.GetRequiredService<ContestoDatabase>().MovimentiGiacenza.IgnoreQueryFilters().AsNoTracking().Where(m => m.IdVariante == idVariante).OrderBy(m => m.CreatoIlUtc).ToListAsync(Ct);
    }

    /// <summary>Fa scadere le prenotazioni di un ordine portando la loro scadenza nel passato (i test non possono far correre il tempo).</summary>
    public async Task ScadiPrenotazioniAsync(Guid idOrdine)
    {
        await using var ambito = Web.Fabbrica.Services.CreateAsyncScope();
        var passato = DateTime.UtcNow.AddMinutes(-1);
        await ambito.ServiceProvider.GetRequiredService<ContestoDatabase>().PrenotazioniGiacenza.IgnoreQueryFilters().Where(p => p.IdOrdine == idOrdine)
            .ExecuteUpdateAsync(x => x.SetProperty(p => p.ScadeIlUtc, passato), Ct);
    }

    public async Task<StatoOrdine> StatoOrdineAsync(Guid idOrdine)
    {
        await using var ambito = Web.Fabbrica.Services.CreateAsyncScope();
        return await ambito.ServiceProvider.GetRequiredService<ContestoDatabase>().Ordini.IgnoreQueryFilters().AsNoTracking().Where(o => o.Id == idOrdine).Select(o => o.Stato).SingleAsync(Ct);
    }

    public ValueTask DisposeAsync() => Web.DisposeAsync();
}
