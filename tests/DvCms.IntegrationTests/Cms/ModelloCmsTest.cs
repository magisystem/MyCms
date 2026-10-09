using DvCms.Domain.Accessi;
using DvCms.Domain.Cms;
using DvCms.Domain.Localizzazione;
using DvCms.Infrastructure.Persistenza;
using DvCms.IntegrationTests.Supporto;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DvCms.IntegrationTests.Cms;

/// <summary>Vincoli e relazioni di Pagina e RevisionePagina su SQL Server reale (C-001).</summary>
public class ModelloCmsTest
{
    private static readonly TimeProvider _orologio = TimeProvider.System;

    [Fact]
    public async Task PaginaERevisioneSiSalvanoEBozzaEPubblicatoSonoSeparati()
    {
        await using var s = await Scenario.CreaAsync();
        var (pagina, prima) = s.NuovaPagina(s.A, "chi-siamo");
        pagina.Pubblica(prima, _orologio);
        var seconda = pagina.AggiungiRevisione("Titolo 2", "Testo 2", "modifica", s.AutoreA, _orologio);
        await using (var scrittura = s.Ambito())
        {
            var contesto = scrittura.Contesto;
            var puntatorePubblicato = pagina.IdRevisionePubblicata;
            var puntatoreBozza = pagina.IdUltimaRevisioneBozza;
            pagina.IdRevisionePubblicata = null;
            pagina.IdUltimaRevisioneBozza = null;
            await contesto.SalvaNuovaPaginaAsync(pagina, prima, TestContext.Current.CancellationToken);
            contesto.RevisioniPagina.Add(seconda);
            pagina.IdRevisionePubblicata = puntatorePubblicato;
            pagina.IdUltimaRevisioneBozza = puntatoreBozza;
            await contesto.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var lettura = s.Ambito();
        var letta = await lettura.Contesto.Pagine.IgnoreQueryFilters().SingleAsync(TestContext.Current.CancellationToken);

        Assert.Equal(StatoPagina.Pubblicata, letta.Stato);
        Assert.Equal(prima.Id, letta.IdRevisionePubblicata);
        Assert.Equal(seconda.Id, letta.IdUltimaRevisioneBozza);
        Assert.Equal(2, letta.UltimoNumeroRevisione);
        Assert.NotEmpty(letta.TokenConcorrenza);
        Assert.Equal(DateTimeKind.Utc, letta.PubblicatoIlUtc!.Value.Kind);
        Assert.Equal(2, await lettura.Contesto.RevisioniPagina.IgnoreQueryFilters().CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(
            "Pubblicata",
            await s.Database.LeggiScalareAsync<string>("SELECT Stato FROM Pagine", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task LoSlugEUnicoPerSitoELinguaMaSiRipeteTraLingueESiti()
    {
        await using var s = await Scenario.CreaAsync();
        await s.SalvaAsync(s.NuovaPagina(s.A, "contatti"));

        // Altra lingua dello stesso sito: consentito. Altro tenant/sito: consentito.
        await s.SalvaAsync(s.NuovaPagina(s.A, "contatti", Lingua.IdInglese));
        await s.SalvaAsync(s.NuovaPagina(s.B, "contatti"));

        var duplicato = await Assert.ThrowsAsync<DbUpdateException>(() => s.SalvaAsync(s.NuovaPagina(s.A, "contatti")));
        Assert.True(duplicato.InnerException is SqlException { Number: 2601 or 2627 });
    }

    [Fact]
    public async Task UnaSolaPaginaInizialePerSitoELingua()
    {
        await using var s = await Scenario.CreaAsync();
        await s.SalvaAsync(s.NuovaPagina(s.A, "home", paginaIniziale: true));
        await s.SalvaAsync(s.NuovaPagina(s.A, "home-en", Lingua.IdInglese, paginaIniziale: true));
        await s.SalvaAsync(s.NuovaPagina(s.B, "home", paginaIniziale: true));
        await s.SalvaAsync(s.NuovaPagina(s.A, "altra"));

        var seconda = await Assert.ThrowsAsync<DbUpdateException>(() => s.SalvaAsync(s.NuovaPagina(s.A, "altra-home", paginaIniziale: true)));

        Assert.True(seconda.InnerException is SqlException { Number: 2601 or 2627 });
    }

    [Fact]
    public async Task LoSlugNonMinuscoloViolaIlVincoloSulDatabase()
    {
        await using var s = await Scenario.CreaAsync();
        var (pagina, revisione) = s.NuovaPagina(s.A, "minuscolo");
        pagina.Slug = "Maiuscolo";

        var eccezione = await Assert.ThrowsAsync<DbUpdateException>(() => s.SalvaAsync((pagina, revisione)));

        Assert.Contains("CK_Pagine_SlugMinuscolo", eccezione.InnerException?.Message ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnaPaginaNonPuoPuntareAlSitoDiUnAltroTenant()
    {
        await using var s = await Scenario.CreaAsync();
        var (pagina, revisione) = Pagina.Crea(s.A.IdTenant, s.B.IdSito, Lingua.IdItaliano, "incrociata", "T", "C", false, s.AutoreA, _orologio);

        var eccezione = await Assert.ThrowsAsync<DbUpdateException>(() => s.SalvaAsync((pagina, revisione)));

        Assert.True(eccezione.InnerException is SqlException { Number: 547 });
    }

    [Fact]
    public async Task UnaRevisioneNonPuoAppartenereAUnAltroTenantOSito()
    {
        await using var s = await Scenario.CreaAsync();
        var (pagina, revisione) = s.NuovaPagina(s.A, "pagina");
        await s.SalvaAsync((pagina, revisione));
        var estranea = pagina.AggiungiRevisione("T", "C", null, s.AutoreA, _orologio);
        estranea.IdTenant = s.B.IdTenant;

        await using var scrittura = s.Ambito();
        scrittura.Contesto.RevisioniPagina.Add(estranea);

        var eccezione = await Assert.ThrowsAsync<DbUpdateException>(() => scrittura.Contesto.SaveChangesAsync(TestContext.Current.CancellationToken));
        Assert.True(eccezione.InnerException is SqlException { Number: 547 });
    }

    [Fact]
    public async Task IlPuntatoreNonPuoIndicareLaRevisioneDiUnAltraPagina()
    {
        await using var s = await Scenario.CreaAsync();
        var (paginaUno, revUno) = s.NuovaPagina(s.A, "uno");
        var (paginaDue, revDue) = s.NuovaPagina(s.A, "due");
        await s.SalvaAsync((paginaUno, revUno));
        await s.SalvaAsync((paginaDue, revDue));

        await using var scrittura = s.Ambito();
        var da = await scrittura.Contesto.Pagine.IgnoreQueryFilters().SingleAsync(p => p.Id == paginaUno.Id, TestContext.Current.CancellationToken);
        da.IdRevisionePubblicata = revDue.Id;

        var eccezione = await Assert.ThrowsAsync<DbUpdateException>(() => scrittura.Contesto.SaveChangesAsync(TestContext.Current.CancellationToken));
        Assert.True(eccezione.InnerException is SqlException { Number: 547 });
    }

    [Fact]
    public async Task IlNumeroDiRevisioneEUnicoPerPagina()
    {
        await using var s = await Scenario.CreaAsync();
        var (pagina, revisione) = s.NuovaPagina(s.A, "pagina");
        await s.SalvaAsync((pagina, revisione));
        var doppia = pagina.AggiungiRevisione("T", "C", null, s.AutoreA, _orologio);
        doppia.NumeroRevisione = 1;

        await using var scrittura = s.Ambito();
        scrittura.Contesto.RevisioniPagina.Add(doppia);

        var eccezione = await Assert.ThrowsAsync<DbUpdateException>(() => scrittura.Contesto.SaveChangesAsync(TestContext.Current.CancellationToken));
        Assert.True(eccezione.InnerException is SqlException { Number: 2601 or 2627 });
    }

    [Fact]
    public async Task ModificheConcorrentiSullaStessaPaginaVengonoRilevate()
    {
        await using var s = await Scenario.CreaAsync();
        var (pagina, revisione) = s.NuovaPagina(s.A, "pagina");
        await s.SalvaAsync((pagina, revisione));

        await using var primo = s.Ambito();
        await using var secondo = s.Ambito();
        var daPrimo = await primo.Contesto.Pagine.IgnoreQueryFilters().SingleAsync(TestContext.Current.CancellationToken);
        var daSecondo = await secondo.Contesto.Pagine.IgnoreQueryFilters().SingleAsync(TestContext.Current.CancellationToken);
        daPrimo.PaginaIniziale = true;
        await primo.Contesto.SaveChangesAsync(TestContext.Current.CancellationToken);
        daSecondo.Slug = "altro";

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => secondo.Contesto.SaveChangesAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task IFiltriDiTenantNascondonoLePagineDegliAltri()
    {
        await using var s = await Scenario.CreaAsync();
        await s.SalvaAsync(s.NuovaPagina(s.A, "di-a"));
        await s.SalvaAsync(s.NuovaPagina(s.B, "di-b"));

        await using var comeA = s.Ambito(s.A.IdTenant);
        await using var senzaTenant = s.Ambito();

        Assert.Equal(["di-a"], await comeA.Contesto.Pagine.Select(p => p.Slug).ToListAsync(TestContext.Current.CancellationToken));
        Assert.Single(await comeA.Contesto.RevisioniPagina.ToListAsync(TestContext.Current.CancellationToken));
        Assert.Empty(await senzaTenant.Contesto.Pagine.ToListAsync(TestContext.Current.CancellationToken));
        Assert.Equal(2, await senzaTenant.Contesto.Pagine.IgnoreQueryFilters().CountAsync(TestContext.Current.CancellationToken));
    }

    private sealed class Ambito : IAsyncDisposable
    {
        private readonly AsyncServiceScope _ambito;

        public Ambito(AsyncServiceScope ambito)
        {
            _ambito = ambito;
        }

        public ContestoDatabase Contesto => _ambito.ServiceProvider.GetRequiredService<ContestoDatabase>();

        public ValueTask DisposeAsync() => _ambito.DisposeAsync();
    }

    private sealed class Scenario : IAsyncDisposable
    {
        private Scenario(DatabaseDiProva database, ServiceProvider fornitore, DatiProva.TenantCreato a, DatiProva.TenantCreato b, Guid autoreA)
        {
            Database = database;
            Fornitore = fornitore;
            A = a;
            B = b;
            AutoreA = autoreA;
        }

        public DatabaseDiProva Database { get; }

        public ServiceProvider Fornitore { get; }

        public DatiProva.TenantCreato A { get; }

        public DatiProva.TenantCreato B { get; }

        public Guid AutoreA { get; }

        public static async Task<Scenario> CreaAsync()
        {
            var database = await DatabaseDiProva.CreaAsync(TestContext.Current.CancellationToken);
            var fornitore = await ServiziProva.CreaConMigrazioniAsync(database.Connessione, annullamento: TestContext.Current.CancellationToken);
            var a = await DatiProva.CreaTenantAsync(fornitore, "A", "a.esempio.it", annullamento: TestContext.Current.CancellationToken);
            var b = await DatiProva.CreaTenantAsync(fornitore, "B", "b.esempio.it", annullamento: TestContext.Current.CancellationToken);
            var autore = await DatiProva.CreaUtenzaAsync(fornitore, RuoloUtenza.TenantAdmin, a.IdTenant, "anna@esempio.it", annullamento: TestContext.Current.CancellationToken);
            return new Scenario(database, fornitore, a, b, autore);
        }

        public (Pagina Pagina, RevisionePagina Revisione) NuovaPagina(DatiProva.TenantCreato tenant, string slug, Guid? idLingua = null, bool paginaIniziale = false) =>
            Pagina.Crea(tenant.IdTenant, tenant.IdSito, idLingua ?? Lingua.IdItaliano, slug, $"Titolo {slug}", "Contenuto", paginaIniziale, AutoreA, _orologio);

        public Ambito Ambito(Guid? idTenant = null)
        {
            var ambito = Fornitore.CreateAsyncScope();
            if (idTenant is { } id)
            {
                var dati = idTenant == A.IdTenant ? A : B;
                ambito.ServiceProvider.GetRequiredService<DvCms.Application.Tenancy.ContestoTenant>()
                    .Imposta(new DvCms.Application.Tenancy.DatiTenantRisolto(id, dati.IdSito, dati.IdDominio, dati.Host, false));
            }

            return new Ambito(ambito);
        }

        public async Task SalvaAsync((Pagina Pagina, RevisionePagina Revisione) coppia)
        {
            await using var ambito = Ambito();
            await ambito.Contesto.SalvaNuovaPaginaAsync(coppia.Pagina, coppia.Revisione, TestContext.Current.CancellationToken);
        }

        public async ValueTask DisposeAsync()
        {
            await Fornitore.DisposeAsync();
            await Database.DisposeAsync();
        }
    }
}
