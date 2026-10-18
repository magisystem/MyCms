using DvCms.Contracts.Moduli;
using DvCms.Core.Accessi;
using DvCms.Core.Tenancy;
using DvCms.Data;
using DvCms.DTO.Moduli;
using DvCms.DTO.Piattaforma;
using DvCms.DTO.Tenancy;
using DvCms.IntegrationTests.Supporto;
using DvCms.Models.Accessi;
using DvCms.Models.Moduli;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DvCms.IntegrationTests.Moduli;

/// <summary>Moduli di contatto (ADR-0027): gestione riservata al TenantAdmin, validazione, stati, concorrenza, eliminazione e audit.</summary>
public class ModuliServizioTest
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Dictionary<string, string> It(string testo) => new() { ["it"] = testo };

    private static DatiModulo Dati(string nome = "Contatti", StatoModulo stato = StatoModulo.Bozza, string campo = "Il tuo nome") =>
        new(
            nome,
            stato,
            new DefinizioneModulo(
                [
                    new CampoModulo("c1", TipoCampoModulo.TestoBreve, true, 200, It(campo), new Dictionary<string, string>(), []),
                    new CampoModulo("c2", TipoCampoModulo.Email, true, 254, It("Email"), new Dictionary<string, string>(), []),
                ],
                It("Invia"),
                It("Grazie, ti risponderemo presto."),
                null,
                null),
            ImpostazioniModulo.Predefinite);

    [Fact]
    public async Task SoloIlTenantAdminDelTenantGestisceIModuli()
    {
        await using var s = await Scenario.CreaAsync();
        var esistente = (await s.ComeAsync(s.A, p => p.CreaAsync(s.AdminA, Dati(), Ct))).Id!.Value;

        // PlatformAdmin, TenantAdmin di un altro tenant, acquirente e account inesistente: nessuna lettura e nessuna scrittura.
        foreach (var attore in new[] { s.Platform, s.AdminB, s.Cliente, Guid.NewGuid() })
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => s.ComeAsync(s.A, p => p.ElencaAsync(attore, Ct)));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => s.ComeAsync(s.A, p => p.LeggiAsync(attore, esistente, Ct)));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => s.ComeAsync(s.A, p => p.LingueAsync(attore, Ct)));
            Assert.Equal(CodiceEsito.NonAutorizzato, (await s.ComeAsync(s.A, p => p.CreaAsync(attore, Dati("Altro"), Ct))).Codice);
            Assert.Equal(CodiceEsito.NonAutorizzato, (await s.ComeAsync(s.A, p => p.AggiornaAsync(attore, esistente, Dati("Altro"), null, Ct))).Codice);
            Assert.Equal(CodiceEsito.NonAutorizzato, (await s.ComeAsync(s.A, p => p.ImpostaStatoAsync(attore, esistente, StatoModulo.Attivo, null, Ct))).Codice);
            Assert.Equal(CodiceEsito.NonAutorizzato, (await s.ComeAsync(s.A, p => p.EliminaAsync(attore, esistente, Ct))).Codice);
        }

        Assert.Single(await s.RigheAsync());
        await using var ambito = s.Fornitore.CreateAsyncScope();
        var negati = await ambito.ServiceProvider.GetRequiredService<ContestoDatabase>().RegistroAudit.IgnoreQueryFilters().CountAsync(a => a.Azione == AzioniAudit.OperazioneNegata, Ct);
        Assert.Equal(16, negati);
    }

    [Fact]
    public async Task CreaLeggeEAggiornaConAudit()
    {
        await using var s = await Scenario.CreaAsync();

        var creato = await s.ComeAsync(s.A, p => p.CreaAsync(s.AdminA, Dati("  Contatti  "), Ct));

        Assert.Equal(CodiceEsito.Riuscito, creato.Codice);
        var vista = (await s.ComeAsync(s.A, p => p.LeggiAsync(s.AdminA, creato.Id!.Value, Ct)))!;
        Assert.Equal("Contatti", vista.Nome);
        Assert.Equal(StatoModulo.Bozza, vista.Stato);
        Assert.Equal(["c1", "c2"], vista.Definizione.Campi.Select(c => c.Id));
        Assert.Equal("Il tuo nome", vista.Definizione.Campi[0].Etichette["it"]);
        Assert.Equal(365, vista.Impostazioni.GiorniConservazione);
        Assert.True(vista.Impostazioni.TuttiGliAmministratori);
        Assert.Equal((0, 0), (vista.RichiesteNuove, vista.RichiesteTotali));

        var aggiornato = await s.ComeAsync(s.A, p => p.AggiornaAsync(s.AdminA, creato.Id!.Value, Dati("Contatti", campo: "Nome e cognome"), vista.TokenConcorrenza, Ct));

        Assert.Equal(CodiceEsito.Riuscito, aggiornato.Codice);
        Assert.Equal("Nome e cognome", (await s.ComeAsync(s.A, p => p.LeggiAsync(s.AdminA, creato.Id!.Value, Ct)))!.Definizione.Campi[0].Etichette["it"]);
        var riga = Assert.Single(await s.RigheAsync());
        Assert.Equal(s.AdminA, riga.AggiornatoDaIdUtenza);
        Assert.Equal(s.A.IdSito, riga.IdSito);
        await using var ambito = s.Fornitore.CreateAsyncScope();
        var audit = await ambito.ServiceProvider.GetRequiredService<ContestoDatabase>().RegistroAudit.IgnoreQueryFilters()
            .Where(a => a.Azione == AzioniAudit.CreazioneModulo || a.Azione == AzioniAudit.ModificaModulo).Select(a => a.Azione).ToListAsync(Ct);
        Assert.Equal([AzioniAudit.CreazioneModulo, AzioniAudit.ModificaModulo], audit.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task LeLinguePerIlModuloSonoQuelleDelSito()
    {
        await using var s = await Scenario.CreaAsync();

        var lingue = await s.ComeAsync(s.A, p => p.LingueAsync(s.AdminA, Ct));

        Assert.Equal(["it", "en"], lingue.Select(l => l.Codice));
        Assert.True(lingue[0].Predefinita);
    }

    [Fact]
    public async Task UnModuloNonValidoNonSiSalvaEGliErroriSpiegano()
    {
        await using var s = await Scenario.CreaAsync();

        var senzaCampi = await s.ComeAsync(s.A, p => p.CreaAsync(s.AdminA, Dati(stato: StatoModulo.Attivo) with { Definizione = Dati().Definizione with { Campi = [] } }, Ct));
        var senzaNome = await s.ComeAsync(s.A, p => p.CreaAsync(s.AdminA, Dati(" "), Ct));
        var linguaSconosciuta = await s.ComeAsync(s.A, p => p.CreaAsync(s.AdminA, Dati() with { Definizione = Dati().Definizione with { Pulsante = new Dictionary<string, string> { ["it"] = "Invia", ["fr"] = "Envoyer" } } }, Ct));

        foreach (var esito in new[] { senzaCampi, senzaNome, linguaSconosciuta })
        {
            Assert.Equal(CodiceEsito.DatiNonValidi, esito.Codice);
            Assert.NotEmpty(esito.Errori);
        }

        Assert.Empty(await s.RigheAsync());
    }

    [Fact]
    public async Task IlNomeEUnicoNelSito()
    {
        await using var s = await Scenario.CreaAsync();
        Assert.Equal(CodiceEsito.Riuscito, (await s.ComeAsync(s.A, p => p.CreaAsync(s.AdminA, Dati("Contatti"), Ct))).Codice);
        var altro = (await s.ComeAsync(s.A, p => p.CreaAsync(s.AdminA, Dati("Preventivi"), Ct))).Id!.Value;

        var doppio = await s.ComeAsync(s.A, p => p.CreaAsync(s.AdminA, Dati("Contatti"), Ct));
        var rinomina = await s.ComeAsync(s.A, p => p.AggiornaAsync(s.AdminA, altro, Dati("Contatti"), null, Ct));

        Assert.Equal(CodiceEsito.GiaPresente, doppio.Codice);
        Assert.Equal(CodiceEsito.GiaPresente, rinomina.Codice);
        // Lo stesso nome in un altro tenant è lecito.
        Assert.Equal(CodiceEsito.Riuscito, (await s.ComeAsync(s.B, p => p.CreaAsync(s.AdminB, Dati("Contatti"), Ct))).Codice);
    }

    [Fact]
    public async Task UnSitoHaAlPiuCinquantaModuli()
    {
        await using var s = await Scenario.CreaAsync();
        await using (var ambito = s.Fornitore.CreateAsyncScope())
        {
            var contesto = ambito.ServiceProvider.GetRequiredService<ContestoDatabase>();
            for (var n = 0; n < Modulo.NumeroMassimoPerSito; n++)
            {
                contesto.Moduli.Add(new Modulo
                {
                    Id = Guid.NewGuid(),
                    IdTenant = s.A.IdTenant,
                    IdSito = s.A.IdSito,
                    Nome = "Modulo " + n,
                    Stato = StatoModulo.Bozza,
                    CreatoIlUtc = DateTime.UtcNow,
                    AggiornatoIlUtc = DateTime.UtcNow,
                });
            }

            await contesto.SaveChangesAsync(Ct);
        }

        var oltre = await s.ComeAsync(s.A, p => p.CreaAsync(s.AdminA, Dati("Uno in più"), Ct));

        Assert.Equal(CodiceEsito.NonConsentito, oltre.Codice);
    }

    [Fact]
    public async Task PubblicareRichiedeUnModuloCompletoEIlCambioDiStatoVaInAudit()
    {
        await using var s = await Scenario.CreaAsync();
        var incompleto = Dati() with { Definizione = Dati().Definizione with { Campi = [], Conferma = new Dictionary<string, string>() } };
        var id = (await s.ComeAsync(s.A, p => p.CreaAsync(s.AdminA, incompleto, Ct))).Id!.Value;

        var rifiutato = await s.ComeAsync(s.A, p => p.ImpostaStatoAsync(s.AdminA, id, StatoModulo.Attivo, null, Ct));
        Assert.Equal(CodiceEsito.DatiNonValidi, rifiutato.Codice);
        Assert.NotEmpty(rifiutato.Errori);
        Assert.Equal(StatoModulo.Bozza, Assert.Single(await s.RigheAsync()).Stato);

        var completo = (await s.ComeAsync(s.A, p => p.AggiornaAsync(s.AdminA, id, Dati(), null, Ct)));
        Assert.Equal(CodiceEsito.Riuscito, completo.Codice);
        Assert.Equal(CodiceEsito.Riuscito, (await s.ComeAsync(s.A, p => p.ImpostaStatoAsync(s.AdminA, id, StatoModulo.Attivo, null, Ct))).Codice);
        Assert.Equal(CodiceEsito.Riuscito, (await s.ComeAsync(s.A, p => p.ImpostaStatoAsync(s.AdminA, id, StatoModulo.Archiviato, null, Ct))).Codice);

        Assert.Equal(StatoModulo.Archiviato, Assert.Single(await s.RigheAsync()).Stato);
        await using var ambito = s.Fornitore.CreateAsyncScope();
        Assert.Equal(2, await ambito.ServiceProvider.GetRequiredService<ContestoDatabase>().RegistroAudit.IgnoreQueryFilters().CountAsync(a => a.Azione == AzioniAudit.CambioStatoModulo, Ct));
    }

    [Fact]
    public async Task DueModificheConcorrentiNonSiSovrascrivonoInSilenzio()
    {
        await using var s = await Scenario.CreaAsync();
        var id = (await s.ComeAsync(s.A, p => p.CreaAsync(s.AdminA, Dati(), Ct))).Id!.Value;
        var lettura = (await s.ComeAsync(s.A, p => p.LeggiAsync(s.AdminA, id, Ct)))!;

        var prima = await s.ComeAsync(s.A, p => p.AggiornaAsync(s.AdminA, id, Dati(campo: "Prima"), lettura.TokenConcorrenza, Ct));
        var superata = await s.ComeAsync(s.A, p => p.AggiornaAsync(s.AdminA, id, Dati(campo: "Seconda"), lettura.TokenConcorrenza, Ct));

        Assert.Equal(CodiceEsito.Riuscito, prima.Codice);
        Assert.Equal(CodiceEsito.Conflitto, superata.Codice);
        Assert.Equal("Prima", (await s.ComeAsync(s.A, p => p.LeggiAsync(s.AdminA, id, Ct)))!.Definizione.Campi[0].Etichette["it"]);
    }

    [Fact]
    public async Task UnModuloConRichiesteSiArchiviaNonSiElimina()
    {
        await using var s = await Scenario.CreaAsync();
        var vuoto = (await s.ComeAsync(s.A, p => p.CreaAsync(s.AdminA, Dati("Vuoto"), Ct))).Id!.Value;
        var pieno = (await s.ComeAsync(s.A, p => p.CreaAsync(s.AdminA, Dati("Pieno"), Ct))).Id!.Value;
        await using (var ambito = s.Fornitore.CreateAsyncScope())
        {
            var contesto = ambito.ServiceProvider.GetRequiredService<ContestoDatabase>();
            contesto.InviiModulo.Add(new InvioModulo { Id = Guid.NewGuid(), IdTenant = s.A.IdTenant, IdSito = s.A.IdSito, IdModulo = pieno, CodiceLingua = "it", Stato = StatoInvioModulo.Nuovo, CreatoIlUtc = DateTime.UtcNow });
            await contesto.SaveChangesAsync(Ct);
        }

        Assert.Equal(CodiceEsito.NonConsentito, (await s.ComeAsync(s.A, p => p.EliminaAsync(s.AdminA, pieno, Ct))).Codice);
        Assert.Equal(CodiceEsito.Riuscito, (await s.ComeAsync(s.A, p => p.EliminaAsync(s.AdminA, vuoto, Ct))).Codice);
        Assert.Equal(CodiceEsito.NonTrovato, (await s.ComeAsync(s.A, p => p.EliminaAsync(s.AdminA, vuoto, Ct))).Codice);

        var elenco = await s.ComeAsync(s.A, p => p.ElencaAsync(s.AdminA, Ct));
        var riga = Assert.Single(elenco);
        Assert.Equal((pieno, 1, 1), (riga.Id, riga.RichiesteNuove, riga.RichiesteTotali));
        Assert.Equal(2, riga.NumeroCampi);
    }

    private sealed class Scenario : IAsyncDisposable
    {
        private readonly DatabaseDiProva _database;

        private Scenario(DatabaseDiProva database, ServiceProvider fornitore, DatiProva.TenantCreato a, DatiProva.TenantCreato b)
        {
            _database = database;
            Fornitore = fornitore;
            A = a;
            B = b;
        }

        public ServiceProvider Fornitore { get; }

        public DatiProva.TenantCreato A { get; }

        public DatiProva.TenantCreato B { get; }

        public Guid Platform { get; private set; }

        public Guid AdminA { get; private set; }

        public Guid AdminB { get; private set; }

        public Guid Cliente { get; private set; }

        public static async Task<Scenario> CreaAsync()
        {
            var database = await DatabaseDiProva.CreaAsync(Ct);
            var fornitore = await ServiziProva.CreaConMigrazioniAsync(database.Connessione, annullamento: Ct);
            var a = await DatiProva.CreaTenantAsync(fornitore, "A", "a.esempio.it", annullamento: Ct);
            var b = await DatiProva.CreaTenantAsync(fornitore, "B", "b.esempio.it", annullamento: Ct);
            return new Scenario(database, fornitore, a, b)
            {
                Platform = await DatiProva.CreaUtenzaAsync(fornitore, RuoloUtenza.PlatformAdmin, null, "root@esempio.it", annullamento: Ct),
                AdminA = await DatiProva.CreaUtenzaAsync(fornitore, RuoloUtenza.TenantAdmin, a.IdTenant, "anna@esempio.it", annullamento: Ct),
                AdminB = await DatiProva.CreaUtenzaAsync(fornitore, RuoloUtenza.TenantAdmin, b.IdTenant, "bruno@esempio.it", annullamento: Ct),
                Cliente = await DatiProva.CreaUtenzaAsync(fornitore, RuoloUtenza.Utente, a.IdTenant, "cliente@esempio.it", annullamento: Ct),
            };
        }

        public async Task<T> ComeAsync<T>(DatiProva.TenantCreato tenant, Func<IServizioModuli, Task<T>> operazione)
        {
            await using var ambito = Fornitore.CreateAsyncScope();
            ambito.ServiceProvider.GetRequiredService<ContestoTenant>().Imposta(new DatiTenantRisolto(tenant.IdTenant, tenant.IdSito, tenant.IdDominio, tenant.Host, false));
            return await operazione(ambito.ServiceProvider.GetRequiredService<IServizioModuli>());
        }

        public async Task<IReadOnlyList<Modulo>> RigheAsync()
        {
            await using var ambito = Fornitore.CreateAsyncScope();
            return await ambito.ServiceProvider.GetRequiredService<ContestoDatabase>().Moduli.IgnoreQueryFilters().AsNoTracking().ToListAsync(Ct);
        }

        public async ValueTask DisposeAsync()
        {
            await Fornitore.DisposeAsync();
            await _database.DisposeAsync();
        }
    }
}
