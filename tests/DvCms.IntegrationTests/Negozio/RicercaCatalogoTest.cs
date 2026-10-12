using DvCms.Application.Negozio;
using DvCms.Domain.Negozio;
using DvCms.Infrastructure.Persistenza;
using DvCms.IntegrationTests.Supporto;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DvCms.IntegrationTests.Negozio;

/// <summary>Ricerca nel catalogo (M7.24): indice di termini normalizzati, pertinenza, prefissi, radici, codici articolo, aggiornamento e ricostruzione.</summary>
public class RicercaCatalogoTest
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<Guid> CreaAsync(ContestoNegozio c, DatiProva.TenantCreato tenant, string nome, string? descrizione, string sku, StatoProdotto stato = StatoProdotto.Pubblicato, IReadOnlyList<Guid>? categorie = null)
    {
        var dati = new DatiProdotto(null, null, stato, 22m, [new TraduzioneProdottoDati("it", nome, descrizione)], [new VarianteDati(null, sku, 10m, null, true, null)], null, categorie);
        var esito = await c.ComeCatalogoAsync(tenant, s => s.SalvaAsync(c.Attore(tenant), dati, Ct));
        Assert.True(esito.Riuscito, $"{esito.Codice} {string.Join(',', esito.CampiNonValidi ?? [])}");
        return esito.Id!.Value;
    }

    private static async Task<List<string>> CercaAsync(ContestoNegozio c, string? testo, DatiProva.TenantCreato? tenant = null, Guid? categoria = null) =>
        [.. (await c.ComeVetrinaAsync(tenant ?? c.A, s => s.CercaAsync("it", testo, categoria, Ct))).Select(p => p.Nome)];

    private static async Task<int> RigheIndiceAsync(ContestoNegozio c, Guid? idProdotto = null)
    {
        await using var ambito = c.Web.Fabbrica.Services.CreateAsyncScope();
        var termini = ambito.ServiceProvider.GetRequiredService<ContestoDatabase>().TerminiRicercaProdotto.IgnoreQueryFilters();
        return await (idProdotto is { } id ? termini.Where(t => t.IdProdotto == id) : termini).CountAsync(Ct);
    }

    [Fact]
    public async Task ILNomePesaPiuDellaDescrizioneEILCodiceSiTrovaPerParti()
    {
        await using var c = await ContestoNegozio.CreaAsync();
        await CreaAsync(c, c.A, "Zaino da viaggio", "Comoda borsa con tasca per la maglietta di ricambio", "ZA-1");
        await CreaAsync(c, c.A, "Maglietta di cotone", "Taglia unica", "TS-M-001");
        await CreaAsync(c, c.A, "Cappello", "Cotone leggero, ideale con la maglietta", "CA-1");

        // Il nome pesa più della descrizione, a prescindere dall'ordine alfabetico.
        Assert.Equal(["Maglietta di cotone", "Cappello", "Zaino da viaggio"], await CercaAsync(c, "maglietta"));
        Assert.Equal(["Maglietta di cotone", "Cappello"], await CercaAsync(c, "cotone"));
        Assert.Equal(["Maglietta di cotone"], await CercaAsync(c, "TS-M-001"));
        Assert.Equal(["Maglietta di cotone"], await CercaAsync(c, "tsm001"));
        Assert.Equal(["Maglietta di cotone"], await CercaAsync(c, "ts 001"));
    }

    [Fact]
    public async Task SingolarePluraleAccentiMaiuscoleEPrefissoTrovanoLoStessoProdotto()
    {
        await using var c = await ContestoNegozio.CreaAsync();
        await CreaAsync(c, c.A, "Maglietta", "Un regalo perché ci tieni", "M-1");
        await CreaAsync(c, c.A, "Penna", null, "P-1");

        Assert.Equal(["Maglietta"], await CercaAsync(c, "Magliette"));
        Assert.Equal(["Maglietta"], await CercaAsync(c, "MAGLIETTA"));
        Assert.Equal(["Maglietta"], await CercaAsync(c, "magl"));
        Assert.Equal(["Maglietta"], await CercaAsync(c, "perche"));
        Assert.Equal(["Maglietta"], await CercaAsync(c, "PERCHÉ"));
        Assert.Empty(await CercaAsync(c, "gliett"));
        Assert.Empty(await CercaAsync(c, "zzz"));
    }

    [Fact]
    public async Task OgniParolaDeveTrovareUnaCorrispondenzaMaLeParoleVuoteNonContano()
    {
        await using var c = await ContestoNegozio.CreaAsync();
        await CreaAsync(c, c.A, "Maglietta rossa", "Cotone", "M-1");
        await CreaAsync(c, c.A, "Maglietta blu", "Lino", "M-2");

        Assert.Equal(["Maglietta rossa"], await CercaAsync(c, "maglietta rossa"));
        Assert.Equal(["Maglietta rossa"], await CercaAsync(c, "la maglietta di cotone"));
        Assert.Equal(["Maglietta blu", "Maglietta rossa"], await CercaAsync(c, "maglietta"));
        Assert.Empty(await CercaAsync(c, "maglietta rossa lino"));

        // Senza ricerca valida (vuota, un solo carattere) l'elenco resta intero.
        Assert.Equal(2, (await CercaAsync(c, "  ")).Count);
        Assert.Equal(2, (await CercaAsync(c, null)).Count);
        Assert.Equal(2, (await CercaAsync(c, "a")).Count);
    }

    [Fact]
    public async Task UnTermineDiDueCaratteriVaTrovatoEsattoSenzaRumore()
    {
        await using var c = await ContestoNegozio.CreaAsync();
        await CreaAsync(c, c.A, "Taglia XL", null, "X-1");
        await CreaAsync(c, c.A, "XLarge giacca", null, "X-2");

        Assert.Equal(["Taglia XL"], await CercaAsync(c, "xl"));
        Assert.Equal(["XLarge giacca"], await CercaAsync(c, "xlarge"));
    }

    [Fact]
    public async Task ModificareIlProdottoAggiornaLIndice()
    {
        await using var c = await ContestoNegozio.CreaAsync();
        var id = await CreaAsync(c, c.A, "Quaderno a righe", "Carta bianca", "QUA-100");
        Assert.Equal(["Quaderno a righe"], await CercaAsync(c, "bianca"));
        Assert.True(await RigheIndiceAsync(c, id) > 0);

        var dettaglio = (await c.ComeCatalogoAsync(c.A, s => s.LeggiAsync(c.AdminA, id, Ct)))!;
        var dati = new DatiProdotto(id, dettaglio.TokenConcorrenza, dettaglio.Stato, dettaglio.AliquotaIva, [new TraduzioneProdottoDati("it", "Block notes", "Carta gialla")], [new VarianteDati(dettaglio.Varianti[0].Id, "BLK-7", 10m, null, true, null)]);
        Assert.True((await c.ComeCatalogoAsync(c.A, s => s.SalvaAsync(c.AdminA, dati, Ct))).Riuscito);

        Assert.Empty(await CercaAsync(c, "quaderno"));
        Assert.Empty(await CercaAsync(c, "bianca"));
        Assert.Empty(await CercaAsync(c, "qua-100"));
        Assert.Equal(["Block notes"], await CercaAsync(c, "gialla"));
        Assert.Equal(["Block notes"], await CercaAsync(c, "blk-7"));
        await using var ambito = c.Web.Fabbrica.Services.CreateAsyncScope();
        var vecchi = await ambito.ServiceProvider.GetRequiredService<ContestoDatabase>().TerminiRicercaProdotto.IgnoreQueryFilters().CountAsync(t => t.IdProdotto == id && (t.Termine == "quaderno" || t.Termine == "qua100"), Ct);
        Assert.Equal(0, vecchi);
    }

    [Fact]
    public async Task IProdottiPrecedentiAllIndiceSiIndicizzanoAllaPrimaRicerca()
    {
        await using var c = await ContestoNegozio.CreaAsync();
        var id = await CreaAsync(c, c.A, "Libro antico", "Edizione rara", "LIB-1");
        await using (var ambito = c.Web.Fabbrica.Services.CreateAsyncScope())
        {
            await ambito.ServiceProvider.GetRequiredService<ContestoDatabase>().TerminiRicercaProdotto.IgnoreQueryFilters().ExecuteDeleteAsync(Ct);
        }

        Assert.Equal(0, await RigheIndiceAsync(c));
        Assert.Equal(["Libro antico"], await CercaAsync(c, "rara"));
        Assert.True(await RigheIndiceAsync(c, id) > 0);
        Assert.Equal(["Libro antico"], await CercaAsync(c, "lib-1"));
    }

    [Fact]
    public async Task NonTrovaBozzeNeProdottiDiUnAltroTenantNeCodiciDisattivati()
    {
        await using var c = await ContestoNegozio.CreaAsync();
        await CreaAsync(c, c.A, "Lampada pubblica", null, "LAM-1");
        await CreaAsync(c, c.A, "Lampada segreta", null, "LAM-2", StatoProdotto.Bozza);
        await CreaAsync(c, c.B, "Lampada di B", null, "LAM-B");

        Assert.Equal(["Lampada pubblica"], await CercaAsync(c, "lampada"));
        Assert.Equal(["Lampada di B"], await CercaAsync(c, "lampada", c.B));
        Assert.Empty(await CercaAsync(c, "lam-b"));
        Assert.Empty(await CercaAsync(c, "lam-2"));
    }

    [Fact]
    public async Task LaRicercaSiCombinaConLaCategoria()
    {
        await using var c = await ContestoNegozio.CreaAsync();
        var categoria = await c.ComeCategorieAsync(c.A, s => s.SalvaAsync(c.AdminA, new DatiCategoria(null, 0, [new NomeCategoriaDati("it", "Abbigliamento")]), Ct));
        Assert.True(categoria.Riuscito);
        await CreaAsync(c, c.A, "Maglietta estiva", null, "M-1", categorie: [categoria.Id!.Value]);
        await CreaAsync(c, c.A, "Maglietta tecnica", null, "M-2");

        Assert.Equal(["Maglietta estiva", "Maglietta tecnica"], await CercaAsync(c, "maglietta"));
        Assert.Equal(["Maglietta estiva"], await CercaAsync(c, "maglietta", categoria: categoria.Id));
    }
}
