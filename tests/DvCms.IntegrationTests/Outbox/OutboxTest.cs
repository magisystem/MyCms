using DvCms.Contracts.Cms;
using DvCms.Contracts.Outbox;
using DvCms.Core.Tenancy;
using DvCms.Data;
using DvCms.DTO.Cms;
using DvCms.DTO.Outbox;
using DvCms.DTO.Tenancy;
using DvCms.Infrastructure.Outbox;
using DvCms.Infrastructure.Persistenza;
using DvCms.IntegrationTests.Supporto;
using DvCms.Models.Accessi;
using DvCms.Models.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DvCms.IntegrationTests.Outbox;

/// <summary>Outbox transazionale e worker (C-007, ADR-0010) su SQL Server reale.</summary>
public class OutboxTest
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task IlMessaggioEScrittoConLaTransazioneDelChiamante()
    {
        await using var s = await Scenario.CreaAsync();

        await using (var ambito = s.Fornitore.CreateAsyncScope())
        {
            var contesto = ambito.ServiceProvider.GetRequiredService<ContestoDatabase>();
            await using var transazione = await contesto.Database.BeginTransactionAsync(Ct);
            ambito.ServiceProvider.GetRequiredService<IOutbox>().Accoda(s.IdTenant, "Prova", new { Valore = 1 });
            await contesto.SaveChangesAsync(Ct);
            await transazione.RollbackAsync(Ct);
        }

        Assert.Equal(0, await s.ContaAsync(m => true));

        await using (var ambito = s.Fornitore.CreateAsyncScope())
        {
            ambito.ServiceProvider.GetRequiredService<IOutbox>().Accoda(s.IdTenant, "Prova", new { Valore = 2 });
            await ambito.ServiceProvider.GetRequiredService<ContestoDatabase>().SaveChangesAsync(Ct);
        }

        var messaggio = await s.LeggiAsync();
        Assert.Equal("Prova", messaggio.Tipo);
        Assert.Equal("{\"Valore\":2}", messaggio.Payload);
        Assert.Equal(StatoMessaggioOutbox.Pendente, messaggio.Stato);
        Assert.Equal(0, messaggio.Tentativi);
    }

    [Fact]
    public async Task IlGestoreRiceveIlMessaggioEQuestoDiventaCompletato()
    {
        await using var s = await Scenario.CreaAsync();
        await s.AccodaAsync("Prova");

        var elaborati = await s.Elaboratore.ElaboraLottoAsync(Ct);

        Assert.Equal(1, elaborati);
        var gestito = Assert.Single(s.Gestore.Ricevuti);
        Assert.Equal(s.IdTenant, gestito.IdTenant);
        Assert.Equal(1, gestito.Tentativo);
        var messaggio = await s.LeggiAsync();
        Assert.Equal(StatoMessaggioOutbox.Completato, messaggio.Stato);
        Assert.Equal(1, messaggio.Tentativi);
        Assert.NotNull(messaggio.ElaboratoIlUtc);
        Assert.Null(messaggio.BloccatoDa);
        Assert.Equal(0, await s.Elaboratore.ElaboraLottoAsync(Ct));
    }

    [Fact]
    public async Task UnErroreFaRitentareConAttesaCrescenteFinoAlLimiteEPoiFallisce()
    {
        await using var s = await Scenario.CreaAsync(tentativiMassimi: 3);
        s.Gestore.Errore = new InvalidOperationException("server non raggiungibile");
        await s.AccodaAsync("Prova");

        await s.Elaboratore.ElaboraLottoAsync(Ct);
        var primo = await s.LeggiAsync();
        Assert.Equal(StatoMessaggioOutbox.Pendente, primo.Stato);
        Assert.Equal(1, primo.Tentativi);
        Assert.Contains("InvalidOperationException", primo.UltimoErrore, StringComparison.Ordinal);
        Assert.Equal(s.Orologio.GetUtcNow().UtcDateTime + TimeSpan.FromSeconds(10), primo.ProssimoTentativoIlUtc);

        // Prima della scadenza non viene ripreso.
        Assert.Equal(0, await s.Elaboratore.ElaboraLottoAsync(Ct));
        s.Orologio.Avanza(TimeSpan.FromSeconds(11));
        await s.Elaboratore.ElaboraLottoAsync(Ct);
        var secondo = await s.LeggiAsync();
        Assert.Equal(2, secondo.Tentativi);
        Assert.Equal(StatoMessaggioOutbox.Pendente, secondo.Stato);
        Assert.Equal(s.Orologio.GetUtcNow().UtcDateTime + TimeSpan.FromSeconds(20), secondo.ProssimoTentativoIlUtc);

        s.Orologio.Avanza(TimeSpan.FromSeconds(21));
        await s.Elaboratore.ElaboraLottoAsync(Ct);
        var terzo = await s.LeggiAsync();
        Assert.Equal(StatoMessaggioOutbox.Fallito, terzo.Stato);
        Assert.Equal(3, terzo.Tentativi);

        s.Orologio.Avanza(TimeSpan.FromHours(2));
        Assert.Equal(0, await s.Elaboratore.ElaboraLottoAsync(Ct));
        Assert.Equal(3, s.Gestore.Ricevuti.Count);
    }

    [Fact]
    public async Task DopoUnErroreIlMessaggioRiesceAlTentativoSuccessivo()
    {
        await using var s = await Scenario.CreaAsync();
        s.Gestore.Errore = new InvalidOperationException("temporaneo");
        await s.AccodaAsync("Prova");
        await s.Elaboratore.ElaboraLottoAsync(Ct);

        s.Gestore.Errore = null;
        s.Orologio.Avanza(TimeSpan.FromSeconds(11));
        await s.Elaboratore.ElaboraLottoAsync(Ct);

        var messaggio = await s.LeggiAsync();
        Assert.Equal(StatoMessaggioOutbox.Completato, messaggio.Stato);
        Assert.Equal(2, messaggio.Tentativi);
        Assert.Null(messaggio.UltimoErrore);
        Assert.Equal([1, 2], s.Gestore.Ricevuti.Select(r => r.Tentativo));
    }

    [Fact]
    public async Task UnTipoSenzaGestoreFallisceSubitoSenzaRitentare()
    {
        await using var s = await Scenario.CreaAsync();
        await s.AccodaAsync("TipoSconosciuto");

        await s.Elaboratore.ElaboraLottoAsync(Ct);

        var messaggio = await s.LeggiAsync();
        Assert.Equal(StatoMessaggioOutbox.Fallito, messaggio.Stato);
        Assert.Contains("TipoSconosciuto", messaggio.UltimoErrore, StringComparison.Ordinal);
        Assert.Empty(s.Gestore.Ricevuti);
    }

    [Fact]
    public async Task LaChiaveDiIdempotenzaImpedisceDoppioniPerLoStessoTenant()
    {
        await using var s = await Scenario.CreaAsync();
        await s.AccodaAsync("Prova", "evento-1");

        await Assert.ThrowsAsync<DbUpdateException>(() => s.AccodaAsync("Prova", "evento-1"));
        await s.AccodaAsync("Prova", "evento-2");

        Assert.Equal(2, await s.ContaAsync(m => true));
    }

    [Fact]
    public async Task DueWorkerInParalleloGestisconoOgniMessaggioUnaSolaVolta()
    {
        await using var s = await Scenario.CreaAsync(dimensioneLotto: 5);
        for (var i = 0; i < 12; i++)
        {
            await s.AccodaAsync("Prova");
        }

        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(async () =>
        {
            while (await s.Elaboratore.ElaboraLottoAsync(Ct) > 0)
            {
            }
        }, Ct)));

        Assert.Equal(12, s.Gestore.Ricevuti.Select(r => r.Id).Distinct().Count());
        Assert.Equal(12, s.Gestore.Ricevuti.Count);
        Assert.Equal(12, await s.ContaAsync(m => m.Stato == StatoMessaggioOutbox.Completato));
    }

    [Fact]
    public async Task UnaPrenotazioneScadutaPermetteDiRiprendereIlMessaggio()
    {
        await using var s = await Scenario.CreaAsync();
        var id = await s.AccodaAsync("Prova");
        await s.ImpostaPrenotazioneAsync(id, Guid.NewGuid(), s.Orologio.GetUtcNow().UtcDateTime.AddMinutes(1));

        Assert.Equal(0, await s.Elaboratore.ElaboraLottoAsync(Ct));
        s.Orologio.Avanza(TimeSpan.FromMinutes(2));
        Assert.Equal(1, await s.Elaboratore.ElaboraLottoAsync(Ct));
        Assert.Equal(StatoMessaggioOutbox.Completato, (await s.LeggiAsync()).Stato);
    }

    [Fact]
    public async Task IMessaggiHannoIlFiltroDiTenant()
    {
        await using var s = await Scenario.CreaAsync();
        var altro = await DatiProva.CreaTenantAsync(s.Fornitore, "Altro", "altro.esempio.it", annullamento: Ct);
        await s.AccodaAsync("Prova");
        await using (var ambito = s.Fornitore.CreateAsyncScope())
        {
            ambito.ServiceProvider.GetRequiredService<IOutbox>().Accoda(altro.IdTenant, "Prova", new { });
            await ambito.ServiceProvider.GetRequiredService<ContestoDatabase>().SaveChangesAsync(Ct);
        }

        await using var comeAltro = s.Fornitore.CreateAsyncScope();
        comeAltro.ServiceProvider.GetRequiredService<ContestoTenant>().Imposta(new DatiTenantRisolto(altro.IdTenant, altro.IdSito, altro.IdDominio, altro.Host, false));
        var contesto = comeAltro.ServiceProvider.GetRequiredService<ContestoDatabase>();

        Assert.Single(await contesto.MessaggiOutbox.ToListAsync(Ct));
        Assert.Equal(2, await contesto.MessaggiOutbox.IgnoreQueryFilters().CountAsync(Ct));
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(1, 10)]
    [InlineData(2, 20)]
    [InlineData(3, 40)]
    [InlineData(10, 3600)]
    [InlineData(50, 3600)]
    public void LAttesaCresceEHaUnTetto(int tentativi, int secondiAttesi)
    {
        Assert.Equal(TimeSpan.FromSeconds(secondiAttesi), ElaboratoreOutbox.AttesaPerTentativo(tentativi));
    }

    [Fact]
    public async Task PubblicareERitirareUnaPaginaScriveGliEventiNellaStessaTransazione()
    {
        await using var s = await Scenario.CreaAsync();
        var admin = await DatiProva.CreaUtenzaAsync(s.Fornitore, RuoloUtenza.TenantAdmin, s.IdTenant, "anna@esempio.it", annullamento: Ct);
        var dati = new DatiTenantRisolto(s.IdTenant, s.IdSito, s.IdDominio, "a.esempio.it", false);
        async Task<T> Come<T>(Func<IServizioPagine, Task<T>> operazione)
        {
            await using var ambito = s.Fornitore.CreateAsyncScope();
            ambito.ServiceProvider.GetRequiredService<ContestoTenant>().Imposta(dati);
            return await operazione(ambito.ServiceProvider.GetRequiredService<IServizioPagine>());
        }

        var creata = await Come(servizio => servizio.CreaAsync(admin, new DatiNuovaPagina("pagina", "Titolo", "Testo", "it", false), Ct));
        var dettaglio = (await Come(servizio => servizio.LeggiAsync(admin, creata.Id!.Value, Ct)))!;
        Assert.Equal(0, await s.ContaAsync(m => true));

        // Un tentativo con token vecchio fallisce e non lascia eventi.
        var conflitto = await Come(servizio => servizio.PubblicaAsync(admin, creata.Id!.Value, Convert.ToBase64String([1, 2, 3]), Ct));
        Assert.False(conflitto.Riuscito);
        Assert.Equal(0, await s.ContaAsync(m => true));

        Assert.True((await Come(servizio => servizio.PubblicaAsync(admin, creata.Id!.Value, dettaglio.TokenConcorrenza, Ct))).Riuscito);
        var dopo = (await Come(servizio => servizio.LeggiAsync(admin, creata.Id!.Value, Ct)))!;
        Assert.True((await Come(servizio => servizio.RitiraAsync(admin, creata.Id!.Value, dopo.TokenConcorrenza, Ct))).Riuscito);

        Assert.Equal(1, await s.ContaAsync(m => m.Tipo == TipiEventoPagina.Pubblicata && m.IdTenant == s.IdTenant && m.Payload.Contains("\"Slug\":\"pagina\"")));
        Assert.Equal(1, await s.ContaAsync(m => m.Tipo == TipiEventoPagina.Ritirata));

        // I gestori reali elaborano gli eventi (consegna ripetuta tollerata: rielaborare non fa danni).
        Assert.Equal(2, await s.Elaboratore.ElaboraLottoAsync(Ct));
        Assert.Equal(2, await s.ContaAsync(m => m.Stato == StatoMessaggioOutbox.Completato));
    }

    private sealed class GestoreDiProva : IGestoreOutbox
    {
        private readonly List<MessaggioDaElaborare> _ricevuti = [];

        public string Tipo => "Prova";

        public Exception? Errore { get; set; }

        public IReadOnlyList<MessaggioDaElaborare> Ricevuti
        {
            get
            {
                lock (_ricevuti)
                {
                    return [.. _ricevuti];
                }
            }
        }

        public Task GestisciAsync(MessaggioDaElaborare messaggio, CancellationToken annullamento)
        {
            lock (_ricevuti)
            {
                _ricevuti.Add(messaggio);
            }

            return Errore is null ? Task.CompletedTask : Task.FromException(Errore);
        }
    }

    private sealed class Scenario : IAsyncDisposable
    {
        private readonly DatabaseDiProva _database;

        private Scenario(DatabaseDiProva database, ServiceProvider fornitore, OrologioProva orologio, GestoreDiProva gestore, DatiProva.TenantCreato tenant)
        {
            _database = database;
            Fornitore = fornitore;
            Orologio = orologio;
            Gestore = gestore;
            IdTenant = tenant.IdTenant;
            IdSito = tenant.IdSito;
            IdDominio = tenant.IdDominio;
        }

        public ServiceProvider Fornitore { get; }

        public OrologioProva Orologio { get; }

        public GestoreDiProva Gestore { get; }

        public Guid IdTenant { get; }

        public Guid IdSito { get; }

        public Guid IdDominio { get; }

        public ElaboratoreOutbox Elaboratore => Fornitore.GetRequiredService<ElaboratoreOutbox>();

        public static async Task<Scenario> CreaAsync(int tentativiMassimi = 8, int dimensioneLotto = 20)
        {
            var database = await DatabaseDiProva.CreaAsync(Ct);
            var orologio = new OrologioProva();
            var gestore = new GestoreDiProva();
            var fornitore = await ServiziProva.CreaConMigrazioniAsync(
                database.Connessione,
                new Dictionary<string, string?>
                {
                    ["Outbox:TentativiMassimi"] = tentativiMassimi.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["Outbox:DimensioneLotto"] = dimensioneLotto.ToString(System.Globalization.CultureInfo.InvariantCulture),
                },
                orologio: orologio,
                configura: servizi => servizi.AddSingleton<IGestoreOutbox>(gestore),
                annullamento: Ct);
            var tenant = await DatiProva.CreaTenantAsync(fornitore, "A", "a.esempio.it", annullamento: Ct);
            return new Scenario(database, fornitore, orologio, gestore, tenant);
        }

        public async Task<Guid> AccodaAsync(string tipo, string? chiave = null)
        {
            await using var ambito = Fornitore.CreateAsyncScope();
            var id = ambito.ServiceProvider.GetRequiredService<IOutbox>().Accoda(IdTenant, tipo, new { Valore = 1 }, chiave);
            await ambito.ServiceProvider.GetRequiredService<ContestoDatabase>().SaveChangesAsync(Ct);
            return id;
        }

        public async Task<int> ContaAsync(System.Linq.Expressions.Expression<Func<MessaggioOutbox, bool>> condizione)
        {
            await using var ambito = Fornitore.CreateAsyncScope();
            return await ambito.ServiceProvider.GetRequiredService<ContestoDatabase>().MessaggiOutbox.IgnoreQueryFilters().CountAsync(condizione, Ct);
        }

        public async Task<MessaggioOutbox> LeggiAsync()
        {
            await using var ambito = Fornitore.CreateAsyncScope();
            return await ambito.ServiceProvider.GetRequiredService<ContestoDatabase>().MessaggiOutbox.IgnoreQueryFilters().AsNoTracking().SingleAsync(Ct);
        }

        public async Task ImpostaPrenotazioneAsync(Guid id, Guid worker, DateTime finoAl)
        {
            await using var ambito = Fornitore.CreateAsyncScope();
            await ambito.ServiceProvider.GetRequiredService<ContestoDatabase>().MessaggiOutbox.IgnoreQueryFilters()
                .Where(m => m.Id == id)
                .ExecuteUpdateAsync(a => a.SetProperty(m => m.BloccatoDa, worker).SetProperty(m => m.BloccatoFinoAlUtc, finoAl), Ct);
        }

        public async ValueTask DisposeAsync()
        {
            await Fornitore.DisposeAsync();
            await _database.DisposeAsync();
        }
    }
}
