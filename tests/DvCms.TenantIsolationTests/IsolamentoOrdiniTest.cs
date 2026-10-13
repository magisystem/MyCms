using DvCms.Contracts.Negozio;
using DvCms.Contracts.Piattaforma;
using DvCms.Core.Tenancy;
using DvCms.Data;
using DvCms.DTO.Negozio;
using DvCms.DTO.Piattaforma;
using DvCms.DTO.Tenancy;
using DvCms.IntegrationTests.Supporto;
using DvCms.Models.Accessi;
using DvCms.Models.Negozio;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DvCms.TenantIsolationTests;

/// <summary>Matrice di isolamento (B-004) sugli ordini (F-003): servizio, query filter e vincoli del database.</summary>
public class IsolamentoOrdiniTest
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task GliOrdiniDiANonSiVedonoNeSiToccanoDaBEIVincoliDelDatabaseTengono()
    {
        await using var d = await DatiIsolamento.CreaAsync();
        var root = await d.Web.CreaPlatformAdminAsync("root@esempio.it");
        foreach (var tenant in new[] { d.A, d.B })
        {
            await using var ambito = d.Web.Fabbrica.Services.CreateAsyncScope();
            Assert.True((await ambito.ServiceProvider.GetRequiredService<IServizioPiattaforma>().ImpostaCommercioAsync(root, tenant.IdTenant, tenant.IdSito, true, Ct)).Riuscito);
        }

        var clienteA = await DatiProva.CreaUtenzaAsync(d.Web.Fabbrica.Services, RuoloUtenza.Utente, d.A.IdTenant, "cliente@esempio.it", annullamento: Ct);
        var clienteB = await DatiProva.CreaUtenzaAsync(d.Web.Fabbrica.Services, RuoloUtenza.Utente, d.B.IdTenant, "cliente@esempio.it", annullamento: Ct);
        var prodotto = await ComeAsync<IServizioCatalogo, EsitoOperazione>(d, d.A, c => c.SalvaAsync(d.AdminA, new DatiProdotto(null, null, StatoProdotto.Pubblicato, 22m, [new("it", "Di A", null)], [new(null, "SKU-A", 10m, null, true, null)]), Ct));
        var varianteA = (await ComeAsync<IServizioCatalogo, DettaglioProdotto?>(d, d.A, c => c.LeggiAsync(d.AdminA, prodotto.Id!.Value, Ct)))!.Varianti.Single().Id;
        var metodoA = (await ComeAsync<IServizioSpedizioni, EsitoOperazione>(d, d.A, s => s.SalvaAsync(d.AdminA, new DatiMetodoSpedizione(null, TipoMetodoSpedizione.Ritiro, null, "Ritiro", 0m, true), Ct))).Id!.Value;
        Assert.True((await ComeAsync<IServizioCarrello, EsitoOperazione>(d, d.A, s => s.AggiungiAsync(clienteA, varianteA, 1, Ct))).Riuscito);
        var ordine = await ComeAsync<IServizioOrdini, EsitoOperazione>(d, d.A, s => s.CreaDaCarrelloAsync(clienteA, "it", new DatiOrdine(metodoA, null, null, null, null, null, null, 10m), Ct));
        Assert.True(ordine.Riuscito, ordine.Codice.ToString());

        // Né il cliente né l'amministratore di B, né lo stesso account usato dall'host sbagliato, raggiungono l'ordine di A.
        Assert.Null(await ComeAsync<IServizioOrdini, OrdineVista?>(d, d.B, s => s.LeggiMioAsync(clienteB, ordine.Id!.Value, Ct)));
        Assert.Null(await ComeAsync<IServizioOrdini, OrdineVista?>(d, d.B, s => s.LeggiAsync(d.AdminB, ordine.Id!.Value, Ct)));
        Assert.Empty(await ComeAsync<IServizioOrdini, IReadOnlyList<RiepilogoOrdine>>(d, d.B, s => s.ElencaAsync(d.AdminB, Ct)));
        Assert.Equal(CodiceEsito.NonAutorizzato, (await ComeAsync<IServizioOrdini, EsitoOperazione>(d, d.B, s => s.AnnullaMioAsync(clienteA, ordine.Id!.Value, Ct))).Codice);
        Assert.Equal(CodiceEsito.NonTrovato, (await ComeAsync<IServizioOrdini, EsitoOperazione>(d, d.B, s => s.AnnullaMioAsync(clienteB, ordine.Id!.Value, Ct))).Codice);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => ComeAsync<IServizioOrdini, IReadOnlyList<RiepilogoOrdine>>(d, d.B, s => s.ElencaAsync(d.AdminA, Ct)));
        Assert.Equal(StatoOrdine.InAttesaDiPagamento, (await ComeAsync<IServizioOrdini, OrdineVista?>(d, d.A, s => s.LeggiMioAsync(clienteA, ordine.Id!.Value, Ct)))!.Stato);

        // Il database rifiuta anche ciò che il servizio non produrrebbe: riga di un altro tenant o totali incoerenti.
        await using var scrittura = d.Web.Fabbrica.Services.CreateAsyncScope();
        var db = scrittura.ServiceProvider.GetRequiredService<ContestoDatabase>();
        db.RigheOrdine.Add(new RigaOrdine { Id = Guid.NewGuid(), IdTenant = d.B.IdTenant, IdSito = d.B.IdSito, IdOrdine = ordine.Id!.Value, Posizione = 9, IdVariante = Guid.NewGuid(), Sku = "X", NomeProdotto = "X", Quantita = 1, PrezzoUnitarioLordo = 1m, AliquotaIva = 22m, Lordo = 1m, Imponibile = 0.82m, Iva = 0.18m });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(Ct));
        db.ChangeTracker.Clear();

        var corrotto = await db.Ordini.IgnoreQueryFilters().SingleAsync(o => o.Id == ordine.Id, Ct);
        corrotto.TotaleIva += 1m;
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(Ct));
    }

    private static async Task<T> ComeAsync<TServizio, T>(DatiIsolamento d, DatiProva.TenantCreato tenant, Func<TServizio, Task<T>> operazione)
        where TServizio : notnull
    {
        await using var ambito = d.Web.Fabbrica.Services.CreateAsyncScope();
        ambito.ServiceProvider.GetRequiredService<ContestoTenant>().Imposta(new DatiTenantRisolto(tenant.IdTenant, tenant.IdSito, tenant.IdDominio, tenant.Host, false));
        return await operazione(ambito.ServiceProvider.GetRequiredService<TServizio>());
    }
}
