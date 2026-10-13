using DvCms.Application.Negozio;
using DvCms.Application.Piattaforma;
using DvCms.Application.Tenancy;
using DvCms.Infrastructure.Persistenza;
using DvCms.IntegrationTests.Supporto;
using DvCms.Models.Negozio;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DvCms.TenantIsolationTests;

/// <summary>Matrice di isolamento (B-004) sulla giacenza (F-005): servizio e vincoli del database.</summary>
public class IsolamentoGiacenzeTest
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task LaGiacenzaDiANonSiVedeNeSiImpostaDaBEIlDatabaseRifiutaDiVendereOltreIlMagazzino()
    {
        await using var d = await DatiIsolamento.CreaAsync();
        var root = await d.Web.CreaPlatformAdminAsync("root@esempio.it");
        foreach (var tenant in new[] { d.A, d.B })
        {
            await using var ambito = d.Web.Fabbrica.Services.CreateAsyncScope();
            Assert.True((await ambito.ServiceProvider.GetRequiredService<IServizioPiattaforma>().ImpostaCommercioAsync(root, tenant.IdTenant, tenant.IdSito, true, Ct)).Riuscito);
        }

        var prodotto = await ComeAsync<IServizioCatalogo, EsitoOperazione>(d, d.A, c => c.SalvaAsync(d.AdminA, new DatiProdotto(null, null, StatoProdotto.Pubblicato, 22m, [new("it", "Di A", null)], [new(null, "SKU-A", 10m, null, true, null)]), Ct));
        var variante = (await ComeAsync<IServizioCatalogo, DettaglioProdotto?>(d, d.A, c => c.LeggiAsync(d.AdminA, prodotto.Id!.Value, Ct)))!.Varianti.Single().Id;
        Assert.True((await ComeAsync<IServizioGiacenze, EsitoOperazione>(d, d.A, s => s.ImpostaAsync(d.AdminA, variante, 5, Ct))).Riuscito);

        // L'amministratore di B non vede né modifica la variante di A; quello di A non la imposta dall'host di B.
        Assert.Equal(CodiceEsito.NonTrovato, (await ComeAsync<IServizioGiacenze, EsitoOperazione>(d, d.B, s => s.ImpostaAsync(d.AdminB, variante, 99, Ct))).Codice);
        Assert.Equal(CodiceEsito.NonAutorizzato, (await ComeAsync<IServizioGiacenze, EsitoOperazione>(d, d.B, s => s.ImpostaAsync(d.AdminA, variante, 99, Ct))).Codice);
        Assert.DoesNotContain(await ComeAsync<IServizioGiacenze, IReadOnlyList<GiacenzaVista>>(d, d.B, s => s.ElencaAsync(d.AdminB, Ct)), g => g.IdVariante == variante);
        Assert.Equal(5, (await ComeAsync<IServizioGiacenze, IReadOnlyList<GiacenzaVista>>(d, d.A, s => s.ElencaAsync(d.AdminA, Ct))).Single(g => g.IdVariante == variante).Disponibile);

        await using var scrittura = d.Web.Fabbrica.Services.CreateAsyncScope();
        var db = scrittura.ServiceProvider.GetRequiredService<ContestoDatabase>();

        // Più riservato che disponibile: il vincolo del database lo impedisce anche aggirando il servizio.
        var riga = await db.GiacenzeVarianti.IgnoreQueryFilters().SingleAsync(g => g.IdVariante == variante, Ct);
        riga.Riservata = 6;
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(Ct));
        db.ChangeTracker.Clear();

        // Una giacenza non può puntare a una variante con un'altra coppia tenant/sito.
        db.GiacenzeVarianti.RemoveRange(db.GiacenzeVarianti.Local);
        db.GiacenzeVarianti.Add(new GiacenzaVariante { IdTenant = d.B.IdTenant, IdSito = d.B.IdSito, IdVariante = variante, Disponibile = 1 });
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
