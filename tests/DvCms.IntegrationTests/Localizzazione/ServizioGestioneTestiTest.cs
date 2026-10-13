using DvCms.Contracts.Localizzazione;
using DvCms.Core.Accessi;
using DvCms.Core.Localizzazione;
using DvCms.Data;
using DvCms.DTO.Localizzazione;
using DvCms.DTO.Piattaforma;
using DvCms.Infrastructure.Persistenza;
using DvCms.IntegrationTests.Supporto;
using DvCms.Models.Accessi;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DvCms.IntegrationTests.Localizzazione;

/// <summary>Gestione dei testi statici riservata al PlatformAdmin (B-010).</summary>
public class ServizioGestioneTestiTest
{
    [Fact]
    public async Task ElencaFiltraPerRicercaLinguaEDaTradurre()
    {
        await using var s = await Scenario.CreaAsync();
        await s.RegistraAsync("Benvenuto", "Arrivederci");
        var ct = TestContext.Current.CancellationToken;

        var tutti = await s.ChiamaAsync(servizio => servizio.ElencaAsync(s.IdPlatformAdmin, new FiltroTesti(null, null, false, 1), ct));
        var ricerca = await s.ChiamaAsync(servizio => servizio.ElencaAsync(s.IdPlatformAdmin, new FiltroTesti("Benv", null, false, 1), ct));
        var inglese = await s.ChiamaAsync(servizio => servizio.ElencaAsync(s.IdPlatformAdmin, new FiltroTesti(null, "EN", true, 1), ct));
        var italianoDaTradurre = await s.ChiamaAsync(servizio => servizio.ElencaAsync(s.IdPlatformAdmin, new FiltroTesti(null, "it", true, 1), ct));

        Assert.Equal(4, tutti.Totale);
        Assert.Equal(2, ricerca.Totale);
        Assert.All(ricerca.Testi, t => Assert.Equal("Benvenuto", t.Chiave));
        Assert.Equal(2, inglese.Totale);
        Assert.All(inglese.Testi, t => Assert.True(t.DaTradurre && t.CodiceLingua == "en"));
        Assert.Equal(0, italianoDaTradurre.Totale);
    }

    [Fact]
    public async Task LaPaginaFuoriIntervalloVieneRiportataAiLimiti()
    {
        await using var s = await Scenario.CreaAsync();
        await s.RegistraAsync("Uno");

        var pagina = await s.ChiamaAsync(servizio => servizio.ElencaAsync(s.IdPlatformAdmin, new FiltroTesti(null, null, false, 99), TestContext.Current.CancellationToken));

        Assert.Equal(1, pagina.Pagina);
        Assert.Equal(1, pagina.TotalePagine);
    }

    [Fact]
    public async Task ModificaSalvaAzzeraDaTradurreAggiornaLaCacheEScriveLAudit()
    {
        await using var s = await Scenario.CreaAsync();
        await s.RegistraAsync("Benvenuto");
        var ct = TestContext.Current.CancellationToken;
        var inglese = (await s.ChiamaAsync(servizio => servizio.ElencaAsync(s.IdPlatformAdmin, new FiltroTesti(null, "en", false, 1), ct))).Testi.Single();
        Assert.True(inglese.DaTradurre);

        var esito = await s.ChiamaAsync(servizio => servizio.ModificaAsync(s.IdPlatformAdmin, inglese.Id, "  Welcome  ", ct));

        Assert.True(esito.Riuscito);
        var dopo = (await s.ChiamaAsync(servizio => servizio.ElencaAsync(s.IdPlatformAdmin, new FiltroTesti(null, "en", false, 1), ct))).Testi.Single();
        Assert.Equal("Welcome", dopo.Testo);
        Assert.False(dopo.DaTradurre);
        Assert.True(s.Fornitore.GetRequiredService<CacheTestiStatici>().TentaLeggi("en", "Benvenuto", out var daCache));
        Assert.Equal("Welcome", daCache);
        await using var ambito = s.Fornitore.CreateAsyncScope();
        var contesto = ambito.ServiceProvider.GetRequiredService<ContestoDatabase>();
        var riga = await contesto.TestiStatici.SingleAsync(t => t.Id == inglese.Id, ct);
        Assert.Equal(s.IdPlatformAdmin, riga.AggiornatoDaIdUtenza);
        var voce = await contesto.RegistroAudit.IgnoreQueryFilters().SingleAsync(a => a.Azione == AzioniAudit.ModificaTestoStatico, ct);
        Assert.Equal(s.IdPlatformAdmin, voce.IdUtenzaAttore);
        Assert.DoesNotContain("Welcome", voce.Dettagli ?? string.Empty, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("testo\u0007con controllo")]
    public async Task RifiutaTestiNonValidi(string? testo)
    {
        await using var s = await Scenario.CreaAsync();
        await s.RegistraAsync("Benvenuto");
        var ct = TestContext.Current.CancellationToken;
        var riga = (await s.ChiamaAsync(servizio => servizio.ElencaAsync(s.IdPlatformAdmin, new FiltroTesti(null, "en", false, 1), ct))).Testi.Single();

        var esito = await s.ChiamaAsync(servizio => servizio.ModificaAsync(s.IdPlatformAdmin, riga.Id, testo, ct));

        Assert.Equal(CodiceEsito.DatiNonValidi, esito.Codice);
    }

    [Fact]
    public async Task RifiutaUnTestoTroppoLungoEAccettaIlLimite()
    {
        await using var s = await Scenario.CreaAsync();
        await s.RegistraAsync("Benvenuto");
        var ct = TestContext.Current.CancellationToken;
        var riga = (await s.ChiamaAsync(servizio => servizio.ElencaAsync(s.IdPlatformAdmin, new FiltroTesti(null, "en", false, 1), ct))).Testi.Single();

        var troppo = await s.ChiamaAsync(servizio => servizio.ModificaAsync(s.IdPlatformAdmin, riga.Id, new string('a', 4001), ct));
        var limite = await s.ChiamaAsync(servizio => servizio.ModificaAsync(s.IdPlatformAdmin, riga.Id, new string('a', 4000), ct));

        Assert.Equal(CodiceEsito.DatiNonValidi, troppo.Codice);
        Assert.True(limite.Riuscito);
    }

    [Fact]
    public async Task UnTestoInesistenteNonVieneTrovato()
    {
        await using var s = await Scenario.CreaAsync();

        var esito = await s.ChiamaAsync(servizio => servizio.ModificaAsync(s.IdPlatformAdmin, Guid.NewGuid(), "x", TestContext.Current.CancellationToken));

        Assert.Equal(CodiceEsito.NonTrovato, esito.Codice);
    }

    [Fact]
    public async Task UnTenantAdminNonPuoLeggereNeModificareEIlTentativoEInAudit()
    {
        await using var s = await Scenario.CreaAsync();
        await s.RegistraAsync("Benvenuto");
        var ct = TestContext.Current.CancellationToken;
        var idAdmin = await DatiProva.CreaUtenzaAsync(s.Fornitore, RuoloUtenza.TenantAdmin, s.IdTenant, "anna@esempio.it", annullamento: ct);
        var riga = (await s.ChiamaAsync(servizio => servizio.ElencaAsync(s.IdPlatformAdmin, new FiltroTesti(null, "en", false, 1), ct))).Testi.Single();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => s.ChiamaAsync(servizio => servizio.ElencaAsync(idAdmin, new FiltroTesti(null, null, false, 1), ct)));
        var esito = await s.ChiamaAsync(servizio => servizio.ModificaAsync(idAdmin, riga.Id, "Hacked", ct));

        Assert.Equal(CodiceEsito.NonAutorizzato, esito.Codice);
        var invariato = (await s.ChiamaAsync(servizio => servizio.ElencaAsync(s.IdPlatformAdmin, new FiltroTesti(null, "en", false, 1), ct))).Testi.Single();
        Assert.Equal("Benvenuto", invariato.Testo);
        Assert.True(invariato.DaTradurre);
        await using var ambito = s.Fornitore.CreateAsyncScope();
        var contesto = ambito.ServiceProvider.GetRequiredService<ContestoDatabase>();
        Assert.Equal(1, await contesto.RegistroAudit.IgnoreQueryFilters().CountAsync(a => a.Azione == AzioniAudit.OperazioneNegata && a.IdUtenzaAttore == idAdmin, ct));
    }

    private sealed class Scenario : IAsyncDisposable
    {
        private readonly DatabaseDiProva _database;

        private Scenario(DatabaseDiProva database, ServiceProvider fornitore, Guid idPlatformAdmin, Guid idTenant)
        {
            _database = database;
            Fornitore = fornitore;
            IdPlatformAdmin = idPlatformAdmin;
            IdTenant = idTenant;
        }

        public ServiceProvider Fornitore { get; }

        public Guid IdPlatformAdmin { get; }

        public Guid IdTenant { get; }

        public static async Task<Scenario> CreaAsync()
        {
            var database = await DatabaseDiProva.CreaAsync(TestContext.Current.CancellationToken);
            var fornitore = await ServiziProva.CreaConMigrazioniAsync(database.Connessione, annullamento: TestContext.Current.CancellationToken);
            var tenant = await DatiProva.CreaTenantAsync(fornitore, "A", "a.esempio.it", annullamento: TestContext.Current.CancellationToken);
            var admin = await DatiProva.CreaUtenzaAsync(fornitore, RuoloUtenza.PlatformAdmin, null, "platform@esempio.it", annullamento: TestContext.Current.CancellationToken);
            return new Scenario(database, fornitore, admin, tenant.IdTenant);
        }

        public async Task RegistraAsync(params string[] chiavi)
        {
            var archivio = Fornitore.GetRequiredService<IArchivioTestiStatici>();
            foreach (var chiave in chiavi)
            {
                await archivio.RegistraChiaveAsync(chiave, TestContext.Current.CancellationToken);
            }
        }

        public async Task<T> ChiamaAsync<T>(Func<IServizioGestioneTesti, Task<T>> operazione)
        {
            await using var ambito = Fornitore.CreateAsyncScope();
            return await operazione(ambito.ServiceProvider.GetRequiredService<IServizioGestioneTesti>());
        }

        public async ValueTask DisposeAsync()
        {
            await Fornitore.DisposeAsync();
            await _database.DisposeAsync();
        }
    }
}
