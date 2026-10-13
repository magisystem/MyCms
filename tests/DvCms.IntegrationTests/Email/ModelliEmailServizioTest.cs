using DvCms.Contracts.Email;
using DvCms.Core.Accessi;
using DvCms.Core.Tenancy;
using DvCms.Data;
using DvCms.DTO.Email;
using DvCms.DTO.Piattaforma;
using DvCms.DTO.Tenancy;
using DvCms.Emails;
using DvCms.IntegrationTests.Supporto;
using DvCms.Models.Accessi;
using DvCms.Models.Email;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DvCms.IntegrationTests.Email;

/// <summary>Modelli email nel database (ADR-0025): gestione riservata al TenantAdmin, ripiego a cascata, validazione e isolamento fra tenant.</summary>
public class ModelliEmailServizioTest
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly ContenutoModelloEmail _codicePersonalizzato = new(
        "Il tuo codice: {{ codice }}",
        "<p>Codice <b>{{ codice }}</b> per {{ nome }}</p>",
        "Codice {{ codice }} per {{ nome }}");

    [Fact]
    public async Task SoloIlTenantAdminDelTenantGestisceIModelli()
    {
        await using var s = await Scenario.CreaAsync();

        // PlatformAdmin, TenantAdmin di un altro tenant, acquirente e account inesistente: né lettura né scrittura.
        foreach (var attore in new[] { s.Platform, s.AdminB, s.Cliente, Guid.NewGuid() })
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => s.ComeAsync(s.A, p => p.ElencaAsync(attore, Ct)));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => s.ComeAsync(s.A, p => p.LeggiAsync(attore, CodiciModelloEmail.RecuperoCodice, "it", Ct)));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => s.ComeAsync(s.A, p => p.AnteprimaAsync(attore, CodiciModelloEmail.RecuperoCodice, _codicePersonalizzato, Ct)));
            Assert.Equal(CodiceEsito.NonAutorizzato, (await s.ComeAsync(s.A, p => p.SalvaAsync(attore, CodiciModelloEmail.RecuperoCodice, "it", _codicePersonalizzato, null, Ct))).Codice);
            Assert.Equal(CodiceEsito.NonAutorizzato, (await s.ComeAsync(s.A, p => p.RipristinaAsync(attore, CodiciModelloEmail.RecuperoCodice, "it", Ct))).Codice);
        }

        Assert.Empty(await s.RigheAsync());
        await using var ambito = s.Fornitore.CreateAsyncScope();
        var negati = await ambito.ServiceProvider.GetRequiredService<ContestoDatabase>().RegistroAudit.IgnoreQueryFilters().CountAsync(a => a.Azione == AzioniAudit.OperazioneNegata, Ct);
        Assert.Equal(8, negati);
    }

    [Fact]
    public async Task LElencoMostraTuttoIlCatalogoELeLingueConUnModelloDelTenant()
    {
        await using var s = await Scenario.CreaAsync();
        Assert.True((await s.ComeAsync(s.A, p => p.SalvaAsync(s.AdminA, CodiciModelloEmail.RecuperoCodice, "it", _codicePersonalizzato, null, Ct))).Codice == CodiceEsito.Riuscito);

        var elenco = await s.ComeAsync(s.A, p => p.ElencaAsync(s.AdminA, Ct));

        Assert.Equal(CatalogoModelliEmail.Definizioni.Count, elenco.Count);
        Assert.Equal(["it"], elenco.Single(v => v.Definizione.Codice == CodiciModelloEmail.RecuperoCodice).LinguePersonalizzate);
        Assert.Empty(elenco.Single(v => v.Definizione.Codice == CodiciModelloEmail.RecuperoLink).LinguePersonalizzate);
        var altro = await s.ComeAsync(s.B, p => p.ElencaAsync(s.AdminB, Ct));
        Assert.All(altro, v => Assert.Empty(v.LinguePersonalizzate));
    }

    [Fact]
    public async Task LeggeIlModelloDiFabbricaESalvaQuelloDelTenantConAudit()
    {
        await using var s = await Scenario.CreaAsync();

        var iniziale = (await s.ComeAsync(s.A, p => p.LeggiAsync(s.AdminA, CodiciModelloEmail.RecuperoCodice, "it", Ct)))!;
        Assert.False(iniziale.Personalizzato);
        Assert.Equal(OrigineModelloEmail.FabbricaLingua, iniziale.Origine);
        Assert.Null(iniziale.TokenConcorrenza);
        Assert.Equal(["it", "en"], iniziale.LingueDisponibili);

        var esito = await s.ComeAsync(s.A, p => p.SalvaAsync(s.AdminA, CodiciModelloEmail.RecuperoCodice, "it", _codicePersonalizzato, null, Ct));

        Assert.Equal(CodiceEsito.Riuscito, esito.Codice);
        var salvato = (await s.ComeAsync(s.A, p => p.LeggiAsync(s.AdminA, CodiciModelloEmail.RecuperoCodice, "it", Ct)))!;
        Assert.True(salvato.Personalizzato);
        Assert.Equal(OrigineModelloEmail.TenantLingua, salvato.Origine);
        Assert.Equal(_codicePersonalizzato, salvato.Contenuto);
        var riga = Assert.Single(await s.RigheAsync());
        Assert.Equal(s.AdminA, riga.AggiornatoDaIdUtenza);
        Assert.Equal(s.A.IdTenant, riga.IdTenant);
        await using var ambito = s.Fornitore.CreateAsyncScope();
        var audit = await ambito.ServiceProvider.GetRequiredService<ContestoDatabase>().RegistroAudit.IgnoreQueryFilters().SingleAsync(a => a.Azione == AzioniAudit.ModificaModelloEmail, Ct);
        Assert.Equal(s.AdminA, audit.IdUtenzaAttore);
        Assert.Contains("recupero-codice (it)", audit.Dettagli, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnModelloNonValidoNonSiSalva()
    {
        await using var s = await Scenario.CreaAsync();

        var senzaCodice = await s.ComeAsync(s.A, p => p.SalvaAsync(s.AdminA, CodiciModelloEmail.RecuperoCodice, "it", new ContenutoModelloEmail("Codice", "<p>Ciao</p>", "Ciao"), null, Ct));
        var sintassi = await s.ComeAsync(s.A, p => p.SalvaAsync(s.AdminA, CodiciModelloEmail.RecuperoCodice, "it", new ContenutoModelloEmail("Codice {{ codice", "<p>{{ codice }}</p>", "{{ codice }}"), null, Ct));
        var attivo = await s.ComeAsync(s.A, p => p.SalvaAsync(s.AdminA, CodiciModelloEmail.RecuperoCodice, "it", new ContenutoModelloEmail("Codice", "<script>x</script>{{ codice }}", "{{ codice }}"), null, Ct));

        foreach (var esito in new[] { senzaCodice, sintassi, attivo })
        {
            Assert.Equal(CodiceEsito.DatiNonValidi, esito.Codice);
            Assert.NotEmpty(esito.Validazione.Errori);
        }

        Assert.Empty(await s.RigheAsync());
    }

    [Fact]
    public async Task CodiceOLinguaSconosciutiNonSiTrovanoNeSiScrivono()
    {
        await using var s = await Scenario.CreaAsync();

        Assert.Null(await s.ComeAsync(s.A, p => p.LeggiAsync(s.AdminA, "non-esiste", "it", Ct)));
        Assert.Null(await s.ComeAsync(s.A, p => p.LeggiAsync(s.AdminA, CodiciModelloEmail.RecuperoCodice, "fr", Ct)));
        Assert.Equal(CodiceEsito.NonTrovato, (await s.ComeAsync(s.A, p => p.SalvaAsync(s.AdminA, "non-esiste", "it", _codicePersonalizzato, null, Ct))).Codice);
        Assert.Equal(CodiceEsito.NonTrovato, (await s.ComeAsync(s.A, p => p.SalvaAsync(s.AdminA, CodiciModelloEmail.RecuperoCodice, "fr", _codicePersonalizzato, null, Ct))).Codice);
        Assert.Equal(CodiceEsito.NonTrovato, (await s.ComeAsync(s.A, p => p.RipristinaAsync(s.AdminA, CodiciModelloEmail.RecuperoCodice, "it", Ct))).Codice);
        Assert.Empty(await s.RigheAsync());
    }

    [Fact]
    public async Task DueModificheConcorrentiNonSiSovrascrivonoInSilenzio()
    {
        await using var s = await Scenario.CreaAsync();
        await s.ComeAsync(s.A, p => p.SalvaAsync(s.AdminA, CodiciModelloEmail.RecuperoCodice, "it", _codicePersonalizzato, null, Ct));
        var lettura = (await s.ComeAsync(s.A, p => p.LeggiAsync(s.AdminA, CodiciModelloEmail.RecuperoCodice, "it", Ct)))!;
        var primo = _codicePersonalizzato with { Oggetto = "Primo {{ codice }}" };
        var secondo = _codicePersonalizzato with { Oggetto = "Secondo {{ codice }}" };

        var riuscita = await s.ComeAsync(s.A, p => p.SalvaAsync(s.AdminA, CodiciModelloEmail.RecuperoCodice, "it", primo, lettura.TokenConcorrenza, Ct));
        var superata = await s.ComeAsync(s.A, p => p.SalvaAsync(s.AdminA, CodiciModelloEmail.RecuperoCodice, "it", secondo, lettura.TokenConcorrenza, Ct));

        Assert.Equal(CodiceEsito.Riuscito, riuscita.Codice);
        Assert.Equal(CodiceEsito.Conflitto, superata.Codice);
        Assert.Equal("Primo {{ codice }}", Assert.Single(await s.RigheAsync()).Oggetto);
    }

    [Fact]
    public async Task RipristinaIlModelloDiFabbrica()
    {
        await using var s = await Scenario.CreaAsync();
        await s.ComeAsync(s.A, p => p.SalvaAsync(s.AdminA, CodiciModelloEmail.RecuperoCodice, "it", _codicePersonalizzato, null, Ct));

        var esito = await s.ComeAsync(s.A, p => p.RipristinaAsync(s.AdminA, CodiciModelloEmail.RecuperoCodice, "it", Ct));

        Assert.Equal(CodiceEsito.Riuscito, esito.Codice);
        Assert.Empty(await s.RigheAsync());
        var scheda = (await s.ComeAsync(s.A, p => p.LeggiAsync(s.AdminA, CodiciModelloEmail.RecuperoCodice, "it", Ct)))!;
        Assert.False(scheda.Personalizzato);
        await using var ambito = s.Fornitore.CreateAsyncScope();
        Assert.Equal(1, await ambito.ServiceProvider.GetRequiredService<ContestoDatabase>().RegistroAudit.IgnoreQueryFilters().CountAsync(a => a.Azione == AzioniAudit.RipristinoModelloEmail, Ct));
    }

    [Fact]
    public async Task LAnteprimaRendeIlContenutoSenzaSalvarlo()
    {
        await using var s = await Scenario.CreaAsync();

        var anteprima = (await s.ComeAsync(s.A, p => p.AnteprimaAsync(s.AdminA, CodiciModelloEmail.RecuperoCodice, _codicePersonalizzato, Ct)))!;

        Assert.True(anteprima.Validazione.Valido);
        Assert.Equal("Il tuo codice: 483920", anteprima.Oggetto);
        Assert.Contains("<b>483920</b>", anteprima.CorpoHtml, StringComparison.Ordinal);
        Assert.Equal("Codice 483920 per Mario Rossi", anteprima.CorpoTesto);
        Assert.Empty(await s.RigheAsync());
    }

    [Fact]
    public async Task LaComposizioneRipiegaATenantLinguaPoiLinguaPredefinitaPoiFabbricaPoiItaliano()
    {
        await using var s = await Scenario.CreaAsync();

        // Nessun modello del tenant: fabbrica nella lingua richiesta, e italiano dove la lingua non ha un modello di fabbrica.
        var fabbricaEn = await s.ComponiAsync(s.A, CodiciModelloEmail.RecuperoCodice, "en");
        var fabbricaFr = await s.ComponiAsync(s.A, CodiciModelloEmail.RecuperoCodice, "fr");
        Assert.Contains("Your verification code is 111222", fabbricaEn.CorpoTesto, StringComparison.Ordinal);
        Assert.Contains("Il tuo codice di verifica è 111222", fabbricaFr.CorpoTesto, StringComparison.Ordinal);
        Assert.Equal("Codice per reimpostare la password", fabbricaFr.Oggetto);

        // Un modello del tenant in italiano (la sua lingua predefinita) vale anche per le lingue senza modello proprio.
        await s.ComeAsync(s.A, p => p.SalvaAsync(s.AdminA, CodiciModelloEmail.RecuperoCodice, "it", _codicePersonalizzato, null, Ct));
        var ripiegoPredefinita = await s.ComponiAsync(s.A, CodiciModelloEmail.RecuperoCodice, "en");
        Assert.Equal("Il tuo codice: 111222", ripiegoPredefinita.Oggetto);

        // Un modello del tenant nella lingua richiesta ha la precedenza.
        var inglese = new ContenutoModelloEmail("Your code: {{ codice }}", "<p>{{ codice }}</p>", "{{ codice }}");
        Assert.Equal(CodiceEsito.Riuscito, (await s.ComeAsync(s.A, p => p.SalvaAsync(s.AdminA, CodiciModelloEmail.RecuperoCodice, "en", inglese, null, Ct))).Codice);
        Assert.Equal("Your code: 111222", (await s.ComponiAsync(s.A, CodiciModelloEmail.RecuperoCodice, "en")).Oggetto);
        Assert.Equal("Il tuo codice: 111222", (await s.ComponiAsync(s.A, CodiciModelloEmail.RecuperoCodice, "it")).Oggetto);

        // L'altro tenant non è toccato.
        Assert.Equal("Codice per reimpostare la password", (await s.ComponiAsync(s.B, CodiciModelloEmail.RecuperoCodice, "it")).Oggetto);
    }

    [Fact]
    public async Task UnModelloDelTenantCheNonSiLasciaRenderePortaAlModelloDiFabbricaSenzaFermareLInvio()
    {
        await using var s = await Scenario.CreaAsync();
        await using (var ambito = s.Fornitore.CreateAsyncScope())
        {
            var contesto = ambito.ServiceProvider.GetRequiredService<ContestoDatabase>();
            contesto.ModelliEmail.Add(new ModelloEmail
            {
                Id = Guid.NewGuid(),
                IdTenant = s.A.IdTenant,
                Codice = CodiciModelloEmail.RecuperoCodice,
                Lingua = "it",
                Oggetto = "Rotto {% if %}",
                CorpoHtml = "{% for i in (1..1000000) %}x{% endfor %}",
                CorpoTesto = "{{ codice }}",
                AggiornatoIlUtc = DateTime.UtcNow,
            });
            await contesto.SaveChangesAsync(Ct);
        }

        var messaggio = await s.ComponiAsync(s.A, CodiciModelloEmail.RecuperoCodice, "it");

        Assert.Contains("Il tuo codice di verifica è 111222", messaggio.CorpoTesto, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LaComposizioneCodificaIValoriNellHtmlENonNelTesto()
    {
        await using var s = await Scenario.CreaAsync();

        var messaggio = await s.ComeAsync(s.A, p => p.GetRequiredService<IComponitoreEmail>().ComponiAsync(
            s.A.IdTenant,
            CodiciModelloEmail.RecuperoNotifica,
            "it",
            "anna@esempio.it",
            "Anna <b>",
            new Dictionary<string, object?> { ["nome"] = "Anna <b>" },
            Ct));

        Assert.Equal("anna@esempio.it", messaggio.Destinatario);
        Assert.DoesNotContain("<b>", messaggio.CorpoHtml!, StringComparison.Ordinal);
        Assert.Contains("La password del tuo account è stata appena cambiata.", messaggio.CorpoTesto, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnCodiceSconosciutoNonSiComponeEUnTenantNonVedeIModelliDiUnAltro()
    {
        await using var s = await Scenario.CreaAsync();
        await s.ComeAsync(s.A, p => p.SalvaAsync(s.AdminA, CodiciModelloEmail.RecuperoCodice, "it", _codicePersonalizzato, null, Ct));

        await Assert.ThrowsAsync<ArgumentException>(() => s.ComponiAsync(s.A, "non-esiste", "it"));

        // Il filtro globale: nel contesto del tenant B le righe di A non compaiono nemmeno con una query diretta.
        var visibiliDaB = await s.ComeAsync(s.B, p => p.GetRequiredService<ContestoDatabase>().ModelliEmail.CountAsync(Ct));
        Assert.Equal(0, visibiliDaB);
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

        public async Task<T> ComeAsync<T>(DatiProva.TenantCreato tenant, Func<IServiceProvider, Task<T>> operazione)
        {
            await using var ambito = Fornitore.CreateAsyncScope();
            ambito.ServiceProvider.GetRequiredService<ContestoTenant>().Imposta(new DatiTenantRisolto(tenant.IdTenant, tenant.IdSito, tenant.IdDominio, tenant.Host, false));
            return await operazione(ambito.ServiceProvider);
        }

        public Task<T> ComeAsync<T>(DatiProva.TenantCreato tenant, Func<IServizioModelliEmail, Task<T>> operazione) =>
            ComeAsync(tenant, p => operazione(p.GetRequiredService<IServizioModelliEmail>()));

        public Task<MessaggioEmail> ComponiAsync(DatiProva.TenantCreato tenant, string codice, string lingua) =>
            ComeAsync(tenant, p => p.GetRequiredService<IComponitoreEmail>().ComponiAsync(
                tenant.IdTenant,
                codice,
                lingua,
                "anna@esempio.it",
                "Anna",
                new Dictionary<string, object?> { ["nome"] = "Anna", ["codice"] = "111222", ["minuti_validita"] = 15 },
                Ct));

        public async Task<IReadOnlyList<ModelloEmail>> RigheAsync()
        {
            await using var ambito = Fornitore.CreateAsyncScope();
            return await ambito.ServiceProvider.GetRequiredService<ContestoDatabase>().ModelliEmail.IgnoreQueryFilters().AsNoTracking().ToListAsync(Ct);
        }

        public async ValueTask DisposeAsync()
        {
            await Fornitore.DisposeAsync();
            await _database.DisposeAsync();
        }
    }
}
