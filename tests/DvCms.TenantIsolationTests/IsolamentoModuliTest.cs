using DvCms.Contracts.Moduli;
using DvCms.Core.Tenancy;
using DvCms.Data;
using DvCms.DTO.Moduli;
using DvCms.DTO.Piattaforma;
using DvCms.DTO.Tenancy;
using DvCms.IntegrationTests.Supporto;
using DvCms.Models.Moduli;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DvCms.TenantIsolationTests;

/// <summary>Matrice di isolamento (B-004) sui moduli di contatto (ADR-0027): servizio, filtri globali e vincoli del database.</summary>
public class IsolamentoModuliTest
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static DatiModulo Dati(string nome) =>
        new(
            nome,
            StatoModulo.Bozza,
            new DefinizioneModulo(
                [new CampoModulo("c1", TipoCampoModulo.TestoBreve, true, 200, new Dictionary<string, string> { ["it"] = "Nome" }, new Dictionary<string, string>(), [])],
                new Dictionary<string, string> { ["it"] = "Invia" },
                new Dictionary<string, string> { ["it"] = "Grazie" },
                null,
                null),
            ImpostazioniModulo.Predefinite);

    [Fact]
    public async Task IModuliDiANonSiVedonoNeSiToccanoDaB()
    {
        await using var d = await DatiIsolamento.CreaAsync();
        var idA = (await ComeAsync(d, d.A, s => s.CreaAsync(d.AdminA, Dati("Contatti di A"), Ct))).Id!.Value;

        // B non legge, non modifica, non pubblica e non elimina il modulo di A; un amministratore di A non agisce nel contesto di B.
        Assert.Null(await ComeAsync(d, d.B, s => s.LeggiAsync(d.AdminB, idA, Ct)));
        Assert.Equal(CodiceEsito.NonTrovato, (await ComeAsync(d, d.B, s => s.AggiornaAsync(d.AdminB, idA, Dati("Rubato"), null, Ct))).Codice);
        Assert.Equal(CodiceEsito.NonTrovato, (await ComeAsync(d, d.B, s => s.ImpostaStatoAsync(d.AdminB, idA, StatoModulo.Attivo, null, Ct))).Codice);
        Assert.Equal(CodiceEsito.NonTrovato, (await ComeAsync(d, d.B, s => s.EliminaAsync(d.AdminB, idA, Ct))).Codice);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => ComeAsync(d, d.B, s => s.LeggiAsync(d.AdminA, idA, Ct)));
        Assert.Equal(CodiceEsito.NonAutorizzato, (await ComeAsync(d, d.B, s => s.CreaAsync(d.AdminA, Dati("Intruso"), Ct))).Codice);
        Assert.Empty(await ComeAsync(d, d.B, s => s.ElencaAsync(d.AdminB, Ct)));

        // Il modulo di A è intatto e B può avere uno con lo stesso nome.
        Assert.Equal("Contatti di A", (await ComeAsync(d, d.A, s => s.LeggiAsync(d.AdminA, idA, Ct)))!.Nome);
        Assert.Equal(CodiceEsito.Riuscito, (await ComeAsync(d, d.B, s => s.CreaAsync(d.AdminB, Dati("Contatti di A"), Ct))).Codice);
        Assert.Equal(1, await ComeAsync(d, d.A, p => p.GetRequiredService<ContestoDatabase>().Moduli.CountAsync(Ct)));
        Assert.Equal(1, await ComeAsync(d, d.B, p => p.GetRequiredService<ContestoDatabase>().Moduli.CountAsync(Ct)));
    }

    [Fact]
    public async Task IlDatabaseRifiutaCoppieIncoerentiENomiDoppi()
    {
        await using var d = await DatiIsolamento.CreaAsync();
        var idA = (await ComeAsync(d, d.A, s => s.CreaAsync(d.AdminA, Dati("Contatti"), Ct))).Id!.Value;

        // Un modulo non può puntare a un sito di un altro tenant, né ripetere il nome nello stesso sito.
        await using (var scrittura = d.Web.Fabbrica.Services.CreateAsyncScope())
        {
            var db = scrittura.ServiceProvider.GetRequiredService<ContestoDatabase>();
            db.Moduli.Add(Riga(d.B.IdTenant, d.A.IdSito, "Incoerente"));
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(Ct));
        }

        await using (var scrittura = d.Web.Fabbrica.Services.CreateAsyncScope())
        {
            var db = scrittura.ServiceProvider.GetRequiredService<ContestoDatabase>();
            db.Moduli.Add(Riga(d.A.IdTenant, d.A.IdSito, "Contatti"));
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(Ct));
        }

        // Una richiesta non può appartenere a un modulo di un altro tenant o di un altro sito.
        await using (var scrittura = d.Web.Fabbrica.Services.CreateAsyncScope())
        {
            var db = scrittura.ServiceProvider.GetRequiredService<ContestoDatabase>();
            db.InviiModulo.Add(new InvioModulo { Id = Guid.NewGuid(), IdTenant = d.B.IdTenant, IdSito = d.B.IdSito, IdModulo = idA, CodiceLingua = "it", Stato = StatoInvioModulo.Nuovo, CreatoIlUtc = DateTime.UtcNow });
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(Ct));
        }

        // I giorni di conservazione fuori dai limiti sono rifiutati dal database.
        await using (var scrittura = d.Web.Fabbrica.Services.CreateAsyncScope())
        {
            var db = scrittura.ServiceProvider.GetRequiredService<ContestoDatabase>();
            var fuori = Riga(d.A.IdTenant, d.A.IdSito, "Troppo a lungo");
            fuori.GiorniConservazione = 5000;
            db.Moduli.Add(fuori);
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(Ct));
        }
    }

    private static Modulo Riga(Guid idTenant, Guid idSito, string nome) => new()
    {
        Id = Guid.NewGuid(),
        IdTenant = idTenant,
        IdSito = idSito,
        Nome = nome,
        Stato = StatoModulo.Bozza,
        CreatoIlUtc = DateTime.UtcNow,
        AggiornatoIlUtc = DateTime.UtcNow,
    };

    private static async Task<T> ComeAsync<T>(DatiIsolamento d, DatiProva.TenantCreato tenant, Func<IServiceProvider, Task<T>> operazione)
    {
        await using var ambito = d.Web.Fabbrica.Services.CreateAsyncScope();
        ambito.ServiceProvider.GetRequiredService<ContestoTenant>().Imposta(new DatiTenantRisolto(tenant.IdTenant, tenant.IdSito, tenant.IdDominio, tenant.Host, false));
        return await operazione(ambito.ServiceProvider);
    }

    private static Task<T> ComeAsync<T>(DatiIsolamento d, DatiProva.TenantCreato tenant, Func<IServizioModuli, Task<T>> operazione) =>
        ComeAsync(d, tenant, p => operazione(p.GetRequiredService<IServizioModuli>()));
}
