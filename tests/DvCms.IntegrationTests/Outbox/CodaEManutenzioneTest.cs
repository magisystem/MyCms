using System.Diagnostics.Metrics;
using DvCms.Contracts.Outbox;
using DvCms.Core.Accessi;
using DvCms.Core.Outbox;
using DvCms.Data;
using DvCms.DTO.Outbox;
using DvCms.DTO.Piattaforma;
using DvCms.Infrastructure.Manutenzione;
using DvCms.Infrastructure.Outbox;
using DvCms.Infrastructure.Persistenza;
using DvCms.IntegrationTests.Supporto;
using DvCms.Models.Accessi;
using DvCms.Models.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DvCms.IntegrationTests.Outbox;

/// <summary>Coda consultabile, ripresa dei falliti, manutenzione dei dati scaduti e metriche (G-001).</summary>
public class CodaEManutenzioneTest
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task IlRiepilogoContaIMessaggiPerStatoEIlPiuVecchioInAttesa()
    {
        await using var s = await Scenario.CreaAsync();
        var vecchio = s.Orologio.GetUtcNow().UtcDateTime.AddHours(-3);
        await s.InserisciAsync(StatoMessaggioOutbox.Pendente, creato: vecchio.AddHours(1));
        await s.InserisciAsync(StatoMessaggioOutbox.Pendente, creato: vecchio);
        await s.InserisciAsync(StatoMessaggioOutbox.Fallito, errore: "Boom: dettaglio");
        await s.InserisciAsync(StatoMessaggioOutbox.Completato);

        var riepilogo = await s.ComeAsync(s.Platform, (coda, attore) => coda.RiepilogoAsync(attore, Ct));

        Assert.Equal((2, 1, 1), (riepilogo.Pendenti, riepilogo.Falliti, riepilogo.Completati));
        Assert.Equal(vecchio, riepilogo.PiuVecchioPendenteIlUtc);
    }

    [Fact]
    public async Task LElencoMostraStatoErroreETenantMaNonIlContenuto()
    {
        await using var s = await Scenario.CreaAsync();
        await s.InserisciAsync(StatoMessaggioOutbox.Fallito, tipo: "InvioEmail", errore: "SmtpException: server non raggiungibile", tentativi: 8, payload: "{\"Segreto\":\"CONTENUTO-RISERVATO\"}");
        await s.InserisciAsync(StatoMessaggioOutbox.Completato);

        var falliti = await s.ComeAsync(s.Platform, (coda, attore) => coda.ElencaAsync(attore, StatoMessaggioOutbox.Fallito, 50, Ct));
        var tutti = await s.ComeAsync(s.Platform, (coda, attore) => coda.ElencaAsync(attore, null, 50, Ct));

        var messaggio = Assert.Single(falliti);
        Assert.Equal(("InvioEmail", "A", 8, "SmtpException: server non raggiungibile"), (messaggio.Tipo, messaggio.NomeTenant, messaggio.Tentativi, messaggio.UltimoErrore));
        Assert.Equal(2, tutti.Count);
        Assert.DoesNotContain("CONTENUTO-RISERVATO", System.Text.Json.JsonSerializer.Serialize(tutti), StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnMessaggioFallitoSiRimetteInCodaEIlWorkerLoCompleta()
    {
        await using var s = await Scenario.CreaAsync();
        var id = await s.InserisciAsync(StatoMessaggioOutbox.Fallito, tipo: "Prova", errore: "Errore", tentativi: 8, prossimo: s.Orologio.GetUtcNow().UtcDateTime.AddHours(1));

        var esito = await s.ComeAsync(s.Platform, (coda, attore) => coda.RitentaAsync(attore, id, Ct));
        var elaborati = await s.Elaboratore.ElaboraLottoAsync(Ct);

        Assert.True(esito.Riuscito, esito.Codice.ToString());
        Assert.Equal(1, elaborati);
        var messaggio = await s.LeggiAsync(id);
        Assert.Equal(StatoMessaggioOutbox.Completato, messaggio.Stato);
        Assert.Equal(1, messaggio.Tentativi);
        Assert.Equal(1, await s.ContaAzioneAsync(AzioniAudit.RitentativoMessaggio));
    }

    [Fact]
    public async Task SoloIMessaggiFallitiSiRitentanoESiScartano()
    {
        await using var s = await Scenario.CreaAsync();
        var pendente = await s.InserisciAsync(StatoMessaggioOutbox.Pendente);
        var completato = await s.InserisciAsync(StatoMessaggioOutbox.Completato);
        var fallito = await s.InserisciAsync(StatoMessaggioOutbox.Fallito);

        var ritentaPendente = await s.ComeAsync(s.Platform, (c, a) => c.RitentaAsync(a, pendente, Ct));
        var scartaCompletato = await s.ComeAsync(s.Platform, (c, a) => c.ScartaAsync(a, completato, Ct));
        var inesistente = await s.ComeAsync(s.Platform, (c, a) => c.ScartaAsync(a, Guid.NewGuid(), Ct));
        var scartaFallito = await s.ComeAsync(s.Platform, (c, a) => c.ScartaAsync(a, fallito, Ct));
        var secondaVolta = await s.ComeAsync(s.Platform, (c, a) => c.ScartaAsync(a, fallito, Ct));

        Assert.Equal(CodiceEsito.NonConsentito, ritentaPendente.Codice);
        Assert.Equal(CodiceEsito.NonConsentito, scartaCompletato.Codice);
        Assert.Equal(CodiceEsito.NonTrovato, inesistente.Codice);
        Assert.True(scartaFallito.Riuscito);
        Assert.Equal(CodiceEsito.NonTrovato, secondaVolta.Codice);
        Assert.Equal(2, await s.ContaAsync(_ => true));
        Assert.Equal(StatoMessaggioOutbox.Pendente, (await s.LeggiAsync(pendente)).Stato);
    }

    [Fact]
    public async Task SoloUnPlatformAdminGestisceLaCoda()
    {
        await using var s = await Scenario.CreaAsync();
        var fallito = await s.InserisciAsync(StatoMessaggioOutbox.Fallito);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => s.ComeAsync(s.TenantAdmin, (c, a) => c.RiepilogoAsync(a, Ct)));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => s.ComeAsync(s.TenantAdmin, (c, a) => c.ElencaAsync(a, null, 10, Ct)));
        var ritenta = await s.ComeAsync(s.TenantAdmin, (c, a) => c.RitentaAsync(a, fallito, Ct));
        var scarta = await s.ComeAsync(s.TenantAdmin, (c, a) => c.ScartaAsync(a, fallito, Ct));
        var sconosciuto = await s.ComeAsync(Guid.NewGuid(), (c, a) => c.ScartaAsync(a, fallito, Ct));

        Assert.Equal(CodiceEsito.NonAutorizzato, ritenta.Codice);
        Assert.Equal(CodiceEsito.NonAutorizzato, scarta.Codice);
        Assert.Equal(CodiceEsito.NonAutorizzato, sconosciuto.Codice);
        Assert.Equal(StatoMessaggioOutbox.Fallito, (await s.LeggiAsync(fallito)).Stato);
        Assert.Equal(3, await s.ContaAzioneAsync(AzioniAudit.OperazioneNegata));
    }

    [Fact]
    public async Task LaManutenzioneEliminaSoloCioCheHaSuperatoLaConservazione()
    {
        await using var s = await Scenario.CreaAsync();
        var adesso = s.Orologio.GetUtcNow().UtcDateTime;
        var completatoVecchio = await s.InserisciAsync(StatoMessaggioOutbox.Completato, creato: adesso.AddDays(-9), elaborato: adesso.AddDays(-8));
        var completatoRecente = await s.InserisciAsync(StatoMessaggioOutbox.Completato, creato: adesso.AddDays(-7), elaborato: adesso.AddDays(-6));
        var falliatoVecchio = await s.InserisciAsync(StatoMessaggioOutbox.Fallito, creato: adesso.AddDays(-91));
        var falliatoRecente = await s.InserisciAsync(StatoMessaggioOutbox.Fallito, creato: adesso.AddDays(-89));
        var pendenteVecchissimo = await s.InserisciAsync(StatoMessaggioOutbox.Pendente, creato: adesso.AddDays(-400));

        var eliminati = await s.Manutenzione.EseguiAsync(Ct);

        Assert.Equal(2, eliminati);
        Assert.Equal(
            new[] { completatoRecente, falliatoRecente, pendenteVecchissimo }.Order(),
            (await s.IdMessaggiAsync()).Order());
        Assert.DoesNotContain(completatoVecchio, await s.IdMessaggiAsync());
        Assert.DoesNotContain(falliatoVecchio, await s.IdMessaggiAsync());
    }

    [Fact]
    public async Task LaManutenzioneRipulisceRecuperoPasswordEInvitiConclusiSenzaToccareQuelliAttivi()
    {
        await using var s = await Scenario.CreaAsync();
        var adesso = s.Orologio.GetUtcNow().UtcDateTime;
        await s.InserisciRecuperoAsync(adesso.AddDays(-31));
        await s.InserisciRecuperoAsync(adesso.AddDays(-1));
        await s.InserisciInvitoAsync(StatoInvito.Accettato, adesso.AddDays(-31), adesso.AddDays(-28));
        await s.InserisciInvitoAsync(StatoInvito.Annullato, adesso.AddDays(-5), adesso.AddDays(-2));
        await s.InserisciInvitoAsync(StatoInvito.Inviato, adesso.AddDays(-40), adesso.AddDays(-37));
        await s.InserisciInvitoAsync(StatoInvito.Inviato, adesso.AddDays(-1), adesso.AddDays(2));

        var eliminati = await s.Manutenzione.EseguiAsync(Ct);

        Assert.Equal(3, eliminati);
        Assert.Equal(1, await s.ContaRecuperiAsync());
        Assert.Equal(2, await s.ContaInvitiAsync());
    }

    [Fact]
    public async Task LaManutenzioneProcedeALottiEFinisceTuttoInUnPassaggio()
    {
        await using var s = await Scenario.CreaAsync(dimensioneLottoManutenzione: 2);
        var adesso = s.Orologio.GetUtcNow().UtcDateTime;
        for (var i = 0; i < 7; i++)
        {
            await s.InserisciAsync(StatoMessaggioOutbox.Completato, creato: adesso.AddDays(-20), elaborato: adesso.AddDays(-20));
        }

        var eliminati = await s.Manutenzione.EseguiAsync(Ct);
        var secondoPassaggio = await s.Manutenzione.EseguiAsync(Ct);

        Assert.Equal(7, eliminati);
        Assert.Equal(0, secondoPassaggio);
        Assert.Equal(0, await s.ContaAsync(_ => true));
    }

    [Fact]
    public async Task LeMetricheContanoGliEsitiPerTipo()
    {
        await using var s = await Scenario.CreaAsync(tentativiMassimi: 1);
        var registrate = new List<(string Tipo, string Esito, long Valore)>();
        using var ascoltatore = new MeterListener();
        ascoltatore.InstrumentPublished = (strumento, listener) =>
        {
            if (strumento.Meter.Name == MetricheOutbox.NomeMeter)
            {
                listener.EnableMeasurementEvents(strumento);
            }
        };
        ascoltatore.SetMeasurementEventCallback<long>((strumento, valore, tag, _) =>
        {
            if (strumento.Name == "dvcms.outbox.elaborati")
            {
                lock (registrate)
                {
                    registrate.Add((tag.ToArray().Single(t => t.Key == "tipo").Value?.ToString() ?? string.Empty, tag.ToArray().Single(t => t.Key == "esito").Value?.ToString() ?? string.Empty, valore));
                }
            }
        });
        ascoltatore.Start();
        await s.InserisciAsync(StatoMessaggioOutbox.Pendente, tipo: "Prova");
        await s.InserisciAsync(StatoMessaggioOutbox.Pendente, tipo: "TipoSenzaGestore");

        await s.Elaboratore.ElaboraLottoAsync(Ct);

        lock (registrate)
        {
            Assert.Contains(("Prova", "riuscito", 1L), registrate);
            Assert.Contains(("TipoSenzaGestore", "fallito", 1L), registrate);
        }
    }

    private sealed class GestoreProva : IGestoreOutbox
    {
        public string Tipo => "Prova";

        public Task GestisciAsync(MessaggioDaElaborare messaggio, CancellationToken annullamento) => Task.CompletedTask;
    }

    private sealed class Scenario : IAsyncDisposable
    {
        private readonly DatabaseDiProva _database;

        private Scenario(DatabaseDiProva database, ServiceProvider fornitore, OrologioProva orologio, DatiProva.TenantCreato tenant, Guid platform, Guid tenantAdmin)
        {
            _database = database;
            Fornitore = fornitore;
            Orologio = orologio;
            Tenant = tenant;
            Platform = platform;
            TenantAdmin = tenantAdmin;
        }

        public ServiceProvider Fornitore { get; }

        public OrologioProva Orologio { get; }

        public DatiProva.TenantCreato Tenant { get; }

        public Guid Platform { get; }

        public Guid TenantAdmin { get; }

        public ElaboratoreOutbox Elaboratore => Fornitore.GetRequiredService<ElaboratoreOutbox>();

        public ManutenzioneDati Manutenzione => Fornitore.GetRequiredService<ManutenzioneDati>();

        public static async Task<Scenario> CreaAsync(int tentativiMassimi = 8, int dimensioneLottoManutenzione = 1000)
        {
            var database = await DatabaseDiProva.CreaAsync(Ct);
            var orologio = new OrologioProva();
            var fornitore = await ServiziProva.CreaConMigrazioniAsync(
                database.Connessione,
                new Dictionary<string, string?>
                {
                    ["Outbox:TentativiMassimi"] = tentativiMassimi.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["Manutenzione:DimensioneLotto"] = dimensioneLottoManutenzione.ToString(System.Globalization.CultureInfo.InvariantCulture),
                },
                orologio: orologio,
                configura: servizi => servizi.AddSingleton<IGestoreOutbox, GestoreProva>(),
                annullamento: Ct);
            var tenant = await DatiProva.CreaTenantAsync(fornitore, "A", "a.esempio.it", annullamento: Ct);
            var platform = await DatiProva.CreaUtenzaAsync(fornitore, RuoloUtenza.PlatformAdmin, null, "root@esempio.it", annullamento: Ct);
            var tenantAdmin = await DatiProva.CreaUtenzaAsync(fornitore, RuoloUtenza.TenantAdmin, tenant.IdTenant, "anna@esempio.it", annullamento: Ct);
            return new Scenario(database, fornitore, orologio, tenant, platform, tenantAdmin);
        }

        public async Task<T> ComeAsync<T>(Guid attore, Func<IGestioneCodaOutbox, Guid, Task<T>> operazione)
        {
            await using var ambito = Fornitore.CreateAsyncScope();
            return await operazione(ambito.ServiceProvider.GetRequiredService<IGestioneCodaOutbox>(), attore);
        }

        public async Task<Guid> InserisciAsync(
            StatoMessaggioOutbox stato,
            string tipo = "Prova",
            string? errore = null,
            int tentativi = 0,
            string payload = "{}",
            DateTime? creato = null,
            DateTime? elaborato = null,
            DateTime? prossimo = null)
        {
            await using var ambito = Fornitore.CreateAsyncScope();
            var adesso = Orologio.GetUtcNow().UtcDateTime;
            var messaggio = new MessaggioOutbox
            {
                Id = Guid.NewGuid(),
                IdTenant = Tenant.IdTenant,
                Tipo = tipo,
                Payload = payload,
                Stato = stato,
                Tentativi = tentativi,
                UltimoErrore = errore,
                CreatoIlUtc = creato ?? adesso,
                ProssimoTentativoIlUtc = prossimo ?? creato ?? adesso,
                ElaboratoIlUtc = stato == StatoMessaggioOutbox.Completato ? elaborato ?? adesso : null,
            };
            var contesto = ambito.ServiceProvider.GetRequiredService<ContestoDatabase>();
            contesto.MessaggiOutbox.Add(messaggio);
            await contesto.SaveChangesAsync(Ct);
            return messaggio.Id;
        }

        public async Task InserisciRecuperoAsync(DateTime creato)
        {
            await using var ambito = Fornitore.CreateAsyncScope();
            var contesto = ambito.ServiceProvider.GetRequiredService<ContestoDatabase>();
            contesto.RichiesteRecuperoPassword.Add(new RichiestaRecuperoPassword
            {
                Id = Guid.NewGuid(),
                IdTenant = Tenant.IdTenant,
                EmailNormalizzata = "x@esempio.it",
                Stato = StatoRecuperoPassword.Ignorata,
                HashRiferimento = new byte[32],
                SaleCodice = new byte[16],
                HashCodice = new byte[32],
                ScadenzaCodiceIlUtc = creato.AddMinutes(10),
                CreatoIlUtc = creato,
            });
            await contesto.SaveChangesAsync(Ct);
        }

        public async Task InserisciInvitoAsync(StatoInvito stato, DateTime creato, DateTime scade)
        {
            await using var ambito = Fornitore.CreateAsyncScope();
            var contesto = ambito.ServiceProvider.GetRequiredService<ContestoDatabase>();
            contesto.InvitiAccount.Add(new InvitoAccount
            {
                Id = Guid.NewGuid(),
                IdTenant = Tenant.IdTenant,
                IdUtenza = TenantAdmin,
                HashToken = Guid.NewGuid().ToByteArray().Concat(Guid.NewGuid().ToByteArray()).ToArray(),
                Stato = stato,
                CreatoIlUtc = creato,
                ScadeIlUtc = scade,
            });
            await contesto.SaveChangesAsync(Ct);
        }

        public async Task<MessaggioOutbox> LeggiAsync(Guid id)
        {
            await using var ambito = Fornitore.CreateAsyncScope();
            return await ambito.ServiceProvider.GetRequiredService<ContestoDatabase>().MessaggiOutbox.IgnoreQueryFilters().AsNoTracking().SingleAsync(m => m.Id == id, Ct);
        }

        public async Task<List<Guid>> IdMessaggiAsync()
        {
            await using var ambito = Fornitore.CreateAsyncScope();
            return await ambito.ServiceProvider.GetRequiredService<ContestoDatabase>().MessaggiOutbox.IgnoreQueryFilters().Select(m => m.Id).ToListAsync(Ct);
        }

        public async Task<int> ContaAsync(System.Linq.Expressions.Expression<Func<MessaggioOutbox, bool>> condizione)
        {
            await using var ambito = Fornitore.CreateAsyncScope();
            return await ambito.ServiceProvider.GetRequiredService<ContestoDatabase>().MessaggiOutbox.IgnoreQueryFilters().CountAsync(condizione, Ct);
        }

        public async Task<int> ContaRecuperiAsync()
        {
            await using var ambito = Fornitore.CreateAsyncScope();
            return await ambito.ServiceProvider.GetRequiredService<ContestoDatabase>().RichiesteRecuperoPassword.IgnoreQueryFilters().CountAsync(Ct);
        }

        public async Task<int> ContaInvitiAsync()
        {
            await using var ambito = Fornitore.CreateAsyncScope();
            return await ambito.ServiceProvider.GetRequiredService<ContestoDatabase>().InvitiAccount.IgnoreQueryFilters().CountAsync(Ct);
        }

        public async Task<int> ContaAzioneAsync(string azione)
        {
            await using var ambito = Fornitore.CreateAsyncScope();
            return await ambito.ServiceProvider.GetRequiredService<ContestoDatabase>().RegistroAudit.IgnoreQueryFilters().CountAsync(r => r.Azione == azione, Ct);
        }

        public async ValueTask DisposeAsync()
        {
            await Fornitore.DisposeAsync();
            await _database.DisposeAsync();
        }
    }
}
