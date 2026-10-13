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

    public ServerSmtpFinto? Server { get; private set; }

    public Guid Platform { get; private set; }

    public Guid AdminA { get; private set; }

    public Guid AdminB { get; private set; }

    public Guid Cliente { get; private set; }

    public Guid AltroCliente { get; private set; }

    public Guid ClienteDiB { get; private set; }

    public static async Task<ContestoNegozio> CreaAsync(PayPalFinto? payPal = null, bool produzioneAbilitata = false, bool conEmail = false, bool avvisoAmministratori = true, NexiFinto? nexi = null)
    {
        var impostazioni = new Dictionary<string, string?>
        {
            ["Sicurezza:ChiaveSegreti"] = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)),
            ["Negozio:CheckoutProduzione"] = produzioneAbilitata ? "true" : "false",
            ["Negozio:AvvisoNuovoOrdineAmministratori"] = avvisoAmministratori ? "true" : "false",
        };
        var c = new ContestoNegozio(await ScenarioWeb.CreaAsync(impostazioni: impostazioni, payPal: payPal, nexi: nexi));
        if (conEmail)
        {
            // Il canale email del tenant A punta a un server SMTP finto in locale: nessuna email reale parte dai test.
            c.Server = new ServerSmtpFinto();
            await DatiProva.ConfiguraSmtpAsync(c.Web.Fabbrica.Services, c.A.IdTenant, c.Server.Porta, Ct);
        }

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

    public Task<IReadOnlyList<Guid>> CreaProdottoAsync(DatiProva.TenantCreato tenant, string nome, decimal iva, params (string Sku, decimal Prezzo, string? Etichetta)[] varianti) =>
        CreaProdottoAsync(tenant, nome, iva, StatoProdotto.Pubblicato, varianti);

    public async Task<IReadOnlyList<Guid>> CreaProdottoAsync(DatiProva.TenantCreato tenant, string nome, decimal iva, StatoProdotto stato, params (string Sku, decimal Prezzo, string? Etichetta)[] varianti)
    {
        var dati = new DatiProdotto(null, null, stato, iva, [new TraduzioneProdottoDati("it", nome, null)],
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

    /// <summary>Disabilita un account qualsiasi (anche un amministratore), come farebbe il PlatformAdmin.</summary>
    public async Task DisabilitaAccountAsync(Guid idAccount) => await DisabilitaAsync(idAccount);

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


    public const string IbanProva = "IT60X0542811101000000123456";

    public Task<T> ComeOfflineAsync<T>(DatiProva.TenantCreato tenant, Func<IServizioPagamentiOffline, Task<T>> operazione) => InAsync<IServizioPagamentiOffline, T>(tenant, operazione);

    /// <summary>Impostazioni valide con bonifico (7 giorni) e contrassegno (4,00 fissi) attivi; le variazioni si passano con <c>with</c>.</summary>
    public static ImpostazioniPagamentoOffline ImpostazioniOffline { get; } = new(
        true, "Negozio Srl", IbanProva, "BCITITMM", "Banca di Prova", 7, "Indica l'ordine nella causale.",
        true, 4.00m, 0m, null, null, null, "Tieni pronto il contante.");

    /// <summary>Salva le impostazioni dei pagamenti manuali come amministratore del tenant e verifica che siano accettate.</summary>
    public async Task ConfiguraOfflineAsync(DatiProva.TenantCreato tenant, ImpostazioniPagamentoOffline? dati = null)
    {
        var scheda = await InAsync<IServizioPagamentiOffline, SchedaPagamentoOffline>(tenant, s => s.LeggiAsync(Attore(tenant), Ct));
        var esito = await InAsync<IServizioPagamentiOffline, EsitoOperazione>(tenant, s => s.SalvaAsync(Attore(tenant), dati ?? ImpostazioniOffline, scheda.TokenConcorrenza, Ct));
        Assert.True(esito.Riuscito, $"{esito.Codice} {string.Join(',', esito.CampiNonValidi ?? [])}");
    }

    public async Task<List<DvCms.Domain.Accessi.RegistroAudit>> AuditAsync(string azione)
    {
        await using var ambito = Web.Fabbrica.Services.CreateAsyncScope();
        return await ambito.ServiceProvider.GetRequiredService<ContestoDatabase>().RegistroAudit.IgnoreQueryFilters().AsNoTracking().Where(v => v.Azione == azione).ToListAsync(Ct);
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

    public async Task ArchiviaAsync(DatiProva.TenantCreato tenant, Guid idVariante)
    {
        var idProdotto = await IdProdottoAsync(idVariante);
        var dettaglio = (await InAsync<IServizioCatalogo, DettaglioProdotto?>(tenant, s => s.LeggiAsync(Attore(tenant), idProdotto, Ct)))!;
        var dati = new DatiProdotto(dettaglio.Id, dettaglio.TokenConcorrenza, StatoProdotto.Archiviato, dettaglio.AliquotaIva, dettaglio.Traduzioni,
            [.. dettaglio.Varianti.Select(v => new VarianteDati(v.Id, v.Sku, v.PrezzoLordo, v.Valuta, v.Attiva, v.Etichette))]);
        var esito = await InAsync<IServizioCatalogo, EsitoOperazione>(tenant, s => s.SalvaAsync(Attore(tenant), dati, Ct));
        Assert.True(esito.Riuscito, esito.Codice.ToString());
    }

    public async Task<Guid> IdProdottoAsync(Guid idVariante)
    {
        await using var ambito = Web.Fabbrica.Services.CreateAsyncScope();
        return await ambito.ServiceProvider.GetRequiredService<ContestoDatabase>().VariantiProdotto.IgnoreQueryFilters().Where(v => v.Id == idVariante).Select(v => v.IdProdotto).SingleAsync(Ct);
    }

    public async Task<StatoOrdine> StatoOrdineAsync(Guid idOrdine)
    {
        await using var ambito = Web.Fabbrica.Services.CreateAsyncScope();
        return await ambito.ServiceProvider.GetRequiredService<ContestoDatabase>().Ordini.IgnoreQueryFilters().AsNoTracking().Where(o => o.Id == idOrdine).Select(o => o.Stato).SingleAsync(Ct);
    }

    /// <summary>Carica un'immagine PNG nella libreria media del tenant (con le credenziali del suo amministratore).</summary>
    public async Task<Guid> CaricaImmagineAsync(DatiProva.TenantCreato tenant, DvCms.Domain.Media.VisibilitaMedia visibilita = DvCms.Domain.Media.VisibilitaMedia.Pubblica)
    {
        return await InAsync<DvCms.Application.Media.IServizioMedia, Guid>(tenant, async media =>
        {
            var esito = await media.CaricaAsync(Attore(tenant), new DvCms.Application.Media.FileDaCaricare(new MemoryStream(ImmaginiProva.Png(8, 8)), "foto.png", "image/png"), Ct);
            Assert.True(esito.Riuscito, esito.Codice.ToString());
            if (visibilita != DvCms.Domain.Media.VisibilitaMedia.Pubblica)
            {
                Assert.True((await media.ImpostaVisibilitaAsync(Attore(tenant), esito.Id!.Value, visibilita, Ct)).Riuscito);
            }

            return esito.Id!.Value;
        });
    }

    public Task<EsitoOperazione> EliminaMediaAsync(DatiProva.TenantCreato tenant, Guid idRisorsa) =>
        InAsync<DvCms.Application.Media.IServizioMedia, EsitoOperazione>(tenant, media => media.EliminaAsync(Attore(tenant), idRisorsa, Ct));

    public Task<T> ComeEsportazioneAsync<T>(DatiProva.TenantCreato tenant, Func<IServizioEsportazioneOrdini, Task<T>> operazione) => InAsync<IServizioEsportazioneOrdini, T>(tenant, operazione);

    public Task<T> ComeVetrinaAsync<T>(DatiProva.TenantCreato tenant, Func<IServizioVetrina, Task<T>> operazione) => InAsync<IServizioVetrina, T>(tenant, operazione);

    public Task<T> ComeCategorieAsync<T>(DatiProva.TenantCreato tenant, Func<IServizioCategorie, Task<T>> operazione) => InAsync<IServizioCategorie, T>(tenant, operazione);

    public Task<T> ComeCatalogoAsync<T>(DatiProva.TenantCreato tenant, Func<IServizioCatalogo, Task<T>> operazione) => InAsync<IServizioCatalogo, T>(tenant, operazione);

    public Task<T> ComeDatiPersonaliAsync<T>(DatiProva.TenantCreato tenant, Func<IServizioDatiPersonali, Task<T>> operazione) => InAsync<IServizioDatiPersonali, T>(tenant, operazione);

    public async Task InvecchiaOrdineAsync(Guid idOrdine, DateTime aggiornatoIlUtc)
    {
        await using var ambito = Web.Fabbrica.Services.CreateAsyncScope();
        await ambito.ServiceProvider.GetRequiredService<ContestoDatabase>().Ordini.IgnoreQueryFilters().Where(o => o.Id == idOrdine)
            .ExecuteUpdateAsync(x => x.SetProperty(o => o.AggiornatoIlUtc, aggiornatoIlUtc), Ct);
    }

    /// <summary>Porta indietro nel tempo i tentativi di pagamento di un ordine (i test non possono far correre il tempo).</summary>
    public async Task InvecchiaTentativiAsync(Guid idOrdine, TimeSpan eta)
    {
        await using var ambito = Web.Fabbrica.Services.CreateAsyncScope();
        var passato = DateTime.UtcNow - eta;
        await ambito.ServiceProvider.GetRequiredService<ContestoDatabase>().TentativiPagamento.IgnoreQueryFilters().Where(t => t.IdOrdine == idOrdine)
            .ExecuteUpdateAsync(x => x.SetProperty(t => t.CreatoIlUtc, passato), Ct);
    }

    public async Task<int> RiconciliaAsync()
    {
        await using var ambito = Web.Fabbrica.Services.CreateAsyncScope();
        return await ambito.ServiceProvider.GetRequiredService<DvCms.Infrastructure.Pagamenti.RiconciliazionePagamenti>().EseguiAsync(Ct);
    }

    public async Task<Ordine> OrdineAsync(Guid idOrdine)
    {
        await using var ambito = Web.Fabbrica.Services.CreateAsyncScope();
        return await ambito.ServiceProvider.GetRequiredService<ContestoDatabase>().Ordini.IgnoreQueryFilters().AsNoTracking().SingleAsync(o => o.Id == idOrdine, Ct);
    }

    public async Task<Utenza> UtenzaAsync(Guid idAccount)
    {
        await using var ambito = Web.Fabbrica.Services.CreateAsyncScope();
        return await ambito.ServiceProvider.GetRequiredService<ContestoDatabase>().Utenze.IgnoreQueryFilters().AsNoTracking().SingleAsync(u => u.Id == idAccount, Ct);
    }

    public Task<T> ComeRimborsiAsync<T>(DatiProva.TenantCreato tenant, Func<IServizioRimborsi, Task<T>> operazione) => InAsync<IServizioRimborsi, T>(tenant, operazione);

    public Task<T> ComePagamentiAsync<T>(DatiProva.TenantCreato tenant, Func<IServizioPagamenti, Task<T>> operazione) => InAsync<IServizioPagamenti, T>(tenant, operazione);

    /// <summary>Configura le credenziali PayPal del sito come le avrebbe inserite il PlatformAdmin (devono coincidere con quelle del PayPal finto).</summary>
    public async Task ConfiguraPagamentoAsync(DatiProva.TenantCreato tenant, AmbientePagamento ambiente = AmbientePagamento.Sandbox)
    {
        await using var ambito = Web.Fabbrica.Services.CreateAsyncScope();
        var esito = await ambito.ServiceProvider.GetRequiredService<IServizioImpostazioniPagamento>()
            .SalvaAsync(Platform, tenant.IdTenant, tenant.IdSito, ambiente, "client-di-prova", "segreto-di-prova", "webhook-di-prova", null, Ct);
        Assert.True(esito.Riuscito, $"{esito.Codice} {string.Join(',', esito.CampiNonValidi ?? [])}");
    }

    public async Task ConfiguraNexiAsync(DatiProva.TenantCreato tenant, AmbientePagamento ambiente = AmbientePagamento.Sandbox)
    {
        await using var ambito = Web.Fabbrica.Services.CreateAsyncScope();
        var esito = await ambito.ServiceProvider.GetRequiredService<IServizioImpostazioniNexi>()
            .SalvaAsync(Platform, tenant.IdTenant, tenant.IdSito, ambiente, NexiFinto.ChiaveValida, null, Ct);
        Assert.True(esito.Riuscito, $"{esito.Codice} {string.Join(',', esito.CampiNonValidi ?? [])}");
    }

    public async Task<List<TentativoPagamento>> TentativiAsync(Guid idOrdine)
    {
        await using var ambito = Web.Fabbrica.Services.CreateAsyncScope();
        return await ambito.ServiceProvider.GetRequiredService<ContestoDatabase>().TentativiPagamento.IgnoreQueryFilters().AsNoTracking().Where(t => t.IdOrdine == idOrdine).OrderBy(t => t.CreatoIlUtc).ToListAsync(Ct);
    }

    public async Task<List<EventoWebhook>> EventiAsync()
    {
        await using var ambito = Web.Fabbrica.Services.CreateAsyncScope();
        return await ambito.ServiceProvider.GetRequiredService<ContestoDatabase>().EventiWebhook.IgnoreQueryFilters().AsNoTracking().ToListAsync(Ct);
    }

    public static DatiLinkInvito Link => new("https", null);

    /// <summary>Invia le email in coda e restituisce quelle nuove arrivate al server SMTP finto.</summary>
    public async Task<IReadOnlyList<EmailRicevuta>> ElaboraEmailAsync()
    {
        var prima = Server!.Ricevute.Count;
        while (await Web.Fabbrica.Services.GetRequiredService<DvCms.Infrastructure.Outbox.ElaboratoreOutbox>().ElaboraLottoAsync(Ct) > 0)
        {
        }

        return [.. Server.Ricevute.Skip(prima)];
    }

    public async Task<int> ContaMessaggiAsync()
    {
        await using var ambito = Web.Fabbrica.Services.CreateAsyncScope();
        return await ambito.ServiceProvider.GetRequiredService<ContestoDatabase>().MessaggiOutbox.IgnoreQueryFilters().CountAsync(Ct);
    }

    public async ValueTask DisposeAsync()
    {
        await Web.DisposeAsync();
        if (Server is not null)
        {
            await Server.DisposeAsync();
        }
    }
}
