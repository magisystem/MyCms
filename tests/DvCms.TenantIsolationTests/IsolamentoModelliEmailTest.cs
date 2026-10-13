using DvCms.Contracts.Email;
using DvCms.Core.Tenancy;
using DvCms.Data;
using DvCms.DTO.Email;
using DvCms.DTO.Piattaforma;
using DvCms.DTO.Tenancy;
using DvCms.IntegrationTests.Supporto;
using DvCms.Models.Email;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DvCms.TenantIsolationTests;

/// <summary>Matrice di isolamento (B-004) sui modelli email (ADR-0025): servizio, composizione e vincoli del database.</summary>
public class IsolamentoModelliEmailTest
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly ContenutoModelloEmail _soloDiA = new("Solo di A {{ codice }}", "<p>{{ codice }}</p>", "{{ codice }}");

    [Fact]
    public async Task IModelliDiANonSiVedonoNeSiUsanoNeSiToccanoDaB()
    {
        await using var d = await DatiIsolamento.CreaAsync();
        Assert.Equal(CodiceEsito.Riuscito, (await ComeAsync(d, d.A, s => s.SalvaAsync(d.AdminA, CodiciModelloEmail.RecuperoCodice, "it", _soloDiA, null, Ct))).Codice);

        // B non legge, non sovrascrive e non ripristina quelli di A, e un amministratore di A non agisce nel contesto di B.
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => ComeAsync(d, d.B, s => s.LeggiAsync(d.AdminA, CodiciModelloEmail.RecuperoCodice, "it", Ct)));
        Assert.Equal(CodiceEsito.NonAutorizzato, (await ComeAsync(d, d.B, s => s.SalvaAsync(d.AdminA, CodiciModelloEmail.RecuperoCodice, "it", _soloDiA, null, Ct))).Codice);
        Assert.Equal(CodiceEsito.NonAutorizzato, (await ComeAsync(d, d.B, s => s.RipristinaAsync(d.AdminA, CodiciModelloEmail.RecuperoCodice, "it", Ct))).Codice);
        var daB = (await ComeAsync(d, d.B, s => s.LeggiAsync(d.AdminB, CodiciModelloEmail.RecuperoCodice, "it", Ct)))!;
        Assert.False(daB.Personalizzato);
        Assert.DoesNotContain("Solo di A", daB.Contenuto.Oggetto, StringComparison.Ordinal);
        Assert.All(await ComeAsync(d, d.B, s => s.ElencaAsync(d.AdminB, Ct)), voce => Assert.Empty(voce.LinguePersonalizzate));

        // La composizione per B ignora il modello di A, anche se B lo chiede esplicitamente per nome di tenant sbagliato.
        var messaggioB = await ComponiAsync(d, d.B, d.B.IdTenant);
        Assert.Equal("Codice per reimpostare la password", messaggioB.Oggetto);
        Assert.Equal("Solo di A 111222", (await ComponiAsync(d, d.A, d.A.IdTenant)).Oggetto);

        // Il salvataggio di B non tocca la riga di A.
        Assert.Equal(CodiceEsito.Riuscito, (await ComeAsync(d, d.B, s => s.SalvaAsync(d.AdminB, CodiciModelloEmail.RecuperoCodice, "it", _soloDiA with { Oggetto = "Solo di B {{ codice }}" }, null, Ct))).Codice);
        Assert.Equal("Solo di A 111222", (await ComponiAsync(d, d.A, d.A.IdTenant)).Oggetto);
        Assert.Equal("Solo di B 111222", (await ComponiAsync(d, d.B, d.B.IdTenant)).Oggetto);

        // Il filtro globale: ognuno vede solo la propria riga.
        Assert.Equal(1, await ComeAsync(d, d.A, p => p.GetRequiredService<ContestoDatabase>().ModelliEmail.CountAsync(Ct)));
        Assert.Equal(1, await ComeAsync(d, d.B, p => p.GetRequiredService<ContestoDatabase>().ModelliEmail.CountAsync(Ct)));
    }

    [Fact]
    public async Task IlDatabaseRifiutaDuplicatiETenantInesistenti()
    {
        await using var d = await DatiIsolamento.CreaAsync();
        await ComeAsync(d, d.A, s => s.SalvaAsync(d.AdminA, CodiciModelloEmail.RecuperoCodice, "it", _soloDiA, null, Ct));

        await using var scrittura = d.Web.Fabbrica.Services.CreateAsyncScope();
        var db = scrittura.ServiceProvider.GetRequiredService<ContestoDatabase>();
        db.ModelliEmail.Add(Riga(d.A.IdTenant));
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(Ct));

        await using var altra = d.Web.Fabbrica.Services.CreateAsyncScope();
        var db2 = altra.ServiceProvider.GetRequiredService<ContestoDatabase>();
        db2.ModelliEmail.Add(Riga(Guid.NewGuid()));
        await Assert.ThrowsAsync<DbUpdateException>(() => db2.SaveChangesAsync(Ct));
    }

    private static ModelloEmail Riga(Guid idTenant) => new()
    {
        Id = Guid.NewGuid(),
        IdTenant = idTenant,
        Codice = CodiciModelloEmail.RecuperoCodice,
        Lingua = "it",
        Oggetto = "x",
        CorpoHtml = "x",
        CorpoTesto = "x",
        AggiornatoIlUtc = DateTime.UtcNow,
    };

    private static async Task<T> ComeAsync<T>(DatiIsolamento d, DatiProva.TenantCreato tenant, Func<IServiceProvider, Task<T>> operazione)
    {
        await using var ambito = d.Web.Fabbrica.Services.CreateAsyncScope();
        ambito.ServiceProvider.GetRequiredService<ContestoTenant>().Imposta(new DatiTenantRisolto(tenant.IdTenant, tenant.IdSito, tenant.IdDominio, tenant.Host, false));
        return await operazione(ambito.ServiceProvider);
    }

    private static Task<T> ComeAsync<T>(DatiIsolamento d, DatiProva.TenantCreato tenant, Func<IServizioModelliEmail, Task<T>> operazione) =>
        ComeAsync(d, tenant, p => operazione(p.GetRequiredService<IServizioModelliEmail>()));

    private static Task<MessaggioEmail> ComponiAsync(DatiIsolamento d, DatiProva.TenantCreato tenant, Guid idTenant) =>
        ComeAsync(d, tenant, p => p.GetRequiredService<IComponitoreEmail>().ComponiAsync(
            idTenant,
            CodiciModelloEmail.RecuperoCodice,
            "it",
            "x@esempio.it",
            null,
            new Dictionary<string, object?> { ["nome"] = "X", ["codice"] = "111222", ["minuti_validita"] = 15 },
            Ct));
}
