using System.Security.Cryptography;
using DvCms.Contracts.Accessi;
using DvCms.Contracts.Accessi.DueFattori;
using DvCms.Core.Accessi.DueFattori;
using DvCms.Core.Tenancy;
using DvCms.Data;
using DvCms.DTO.Accessi;
using DvCms.DTO.Accessi.DueFattori;
using DvCms.DTO.Tenancy;
using DvCms.Extender;
using DvCms.Infrastructure.Persistenza;
using DvCms.IntegrationTests.Supporto;
using DvCms.Models.Accessi;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DvCms.IntegrationTests.Accessi;

/// <summary>Secondo fattore a livello di servizio (G-006, ADR-0019): concorrenza, isolamento tra tenant, obbligo, configurazione.</summary>
public class ServizioDueFattoriTest
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task LoStessoCodiceUsatoInParalleloApreUnaSolaSessione()
    {
        await using var s = await Scenario.CreaAsync();
        var id = await s.CreaAmministratoreAsync();
        var (segreto, _) = await DatiProva.AttivaDueFattoriAsync(s.Fornitore, id, Ct);
        var timbro = (await s.LeggiAsync(id)).TimbroSicurezza;
        var codice = s.Codice(segreto);

        var risultati = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => s.CompletaAsync(s.A, id, timbro, codice)));

        Assert.Equal(1, risultati.Count(r => r.EsitoPositivo));
        Assert.All(risultati.Where(r => r.EsitoPositivo), r => Assert.NotNull(r.Identita));
    }

    [Fact]
    public async Task LoStessoCodiceDiRecuperoUsatoInParalleloValeUnaVolta()
    {
        await using var s = await Scenario.CreaAsync();
        var id = await s.CreaAmministratoreAsync();
        var (_, codici) = await DatiProva.AttivaDueFattoriAsync(s.Fornitore, id, Ct);
        var timbro = (await s.LeggiAsync(id)).TimbroSicurezza;

        var risultati = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => s.CompletaAsync(s.A, id, timbro, codici[3])));

        Assert.Equal(1, risultati.Count(r => r.EsitoPositivo));
        await using var ambito = s.Fornitore.CreateAsyncScope();
        var usati = await ambito.ServiceProvider.GetRequiredService<ContestoDatabase>().CodiciRecuperoUtenza.CountAsync(c => c.IdUtenza == id && c.UsatoIlUtc != null, Ct);
        Assert.Equal(1, usati);
    }

    [Fact]
    public async Task IlCodiceDelPassoSuccessivoValeDopoAverUsatoIlPrecedente()
    {
        await using var s = await Scenario.CreaAsync();
        var id = await s.CreaAmministratoreAsync();
        var (segreto, _) = await DatiProva.AttivaDueFattoriAsync(s.Fornitore, id, Ct);
        var timbro = (await s.LeggiAsync(id)).TimbroSicurezza;
        var codice = s.Codice(segreto);

        var primo = await s.CompletaAsync(s.A, id, timbro, codice);
        var riuso = await s.CompletaAsync(s.A, id, timbro, codice);
        s.Orologio.Avanza(TimeSpan.FromSeconds(Totp.SecondiPasso));
        var successivo = await s.CompletaAsync(s.A, id, timbro, s.Codice(segreto));
        var vecchio = await s.CompletaAsync(s.A, id, timbro, codice);

        Assert.True(primo.EsitoPositivo);
        Assert.False(riuso.EsitoPositivo);
        Assert.True(successivo.EsitoPositivo);
        Assert.False(vecchio.EsitoPositivo);
    }

    [Fact]
    public async Task IlSecondoFattoreNonSiCompletaDaUnAltroTenantNeConUnTimbroCambiatoNeConAccountDisabilitato()
    {
        await using var s = await Scenario.CreaAsync();
        var id = await s.CreaAmministratoreAsync();
        var (segreto, _) = await DatiProva.AttivaDueFattoriAsync(s.Fornitore, id, Ct);
        var timbro = (await s.LeggiAsync(id)).TimbroSicurezza;

        var altroTenant = await s.CompletaAsync(s.B, id, timbro, s.Codice(segreto));
        var timbroVecchio = await s.CompletaAsync(s.A, id, Guid.NewGuid(), s.Codice(segreto));
        await s.ImpostaStatoAsync(id, StatoUtenza.Disabilitata);
        var disabilitato = await s.CompletaAsync(s.A, id, timbro, s.Codice(segreto));
        await s.ImpostaStatoAsync(id, StatoUtenza.Attiva);
        var valido = await s.CompletaAsync(s.A, id, timbro, s.Codice(segreto));

        Assert.False(altroTenant.EsitoPositivo);
        Assert.False(timbroVecchio.EsitoPositivo);
        Assert.False(disabilitato.EsitoPositivo);
        Assert.True(valido.EsitoPositivo);
    }

    [Fact]
    public async Task ConLaDueFattoriAttivaLAccessoRichiedeLaSfidaEIlSegretoSbagliatoNonApre()
    {
        await using var s = await Scenario.CreaAsync();
        var id = await s.CreaAmministratoreAsync();
        var (segreto, _) = await DatiProva.AttivaDueFattoriAsync(s.Fornitore, id, Ct);

        var accesso = await s.AccediAsync(s.A);
        var conAltroSegreto = await s.CompletaAsync(s.A, id, accesso.Sfida!.TimbroSicurezza, s.Codice(Totp.GeneraSegreto()));
        var conSegreto = await s.CompletaAsync(s.A, id, accesso.Sfida.TimbroSicurezza, s.Codice(segreto));

        Assert.True(accesso.EsitoPositivo);
        Assert.Null(accesso.Identita);
        Assert.Equal(id, accesso.Sfida.IdUtenza);
        Assert.False(conAltroSegreto.EsitoPositivo);
        Assert.True(conSegreto.EsitoPositivo);
        Assert.True(conSegreto.Identita!.DueFattoriAttivi);
        Assert.False(conSegreto.Identita.DeveAttivareDueFattori);
    }

    [Fact]
    public async Task LObbligoDipendeDalRuoloEDalleOpzioni()
    {
        await using var tutti = await Scenario.CreaAsync(obbligo: "Amministratori");
        var tenantAdmin = await tutti.CreaAmministratoreAsync();
        await using var soloPlatform = await Scenario.CreaAsync(obbligo: "PlatformAdmin");
        await soloPlatform.CreaAmministratoreAsync();
        await using var nessuno = await Scenario.CreaAsync(obbligo: "Nessuno");
        await nessuno.CreaAmministratoreAsync();

        var conObbligo = await tutti.AccediAsync(tutti.A);
        var senzaPerTenantAdmin = await soloPlatform.AccediAsync(soloPlatform.A);
        var senzaObbligo = await nessuno.AccediAsync(nessuno.A);
        var platformDaTutti = await tutti.CreaPlatformAsync("platform@esempio.it");
        var platform = await tutti.AccediAsync(tutti.A, "platform@esempio.it");

        Assert.True(conObbligo.Identita!.DeveAttivareDueFattori);
        Assert.False(senzaPerTenantAdmin.Identita!.DeveAttivareDueFattori);
        Assert.False(senzaObbligo.Identita!.DeveAttivareDueFattori);
        Assert.NotEqual(Guid.Empty, platformDaTutti);
        Assert.True(platform.Identita!.DeveAttivareDueFattori);
        Assert.NotEqual(Guid.Empty, tenantAdmin);
    }

    [Fact]
    public async Task SenzaConfigurazioneLObbligoPredefinitoRiguardaGliAmministratoriEUnValoreNonValidoFallisce()
    {
        await using var predefinito = await Scenario.CreaAsync(obbligo: null);
        Assert.Equal(ObbligoDueFattori.Amministratori, predefinito.Fornitore.GetRequiredService<OpzioniDueFattori>().Obbligo);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await using var _ = await Scenario.CreaAsync(obbligo: "forse");
        });
        Assert.Contains("Sicurezza:DueFattori:Obbligo", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConfigurazioneConfermaDisattivazioneERigenerazioneAlLivelloDelServizio()
    {
        await using var s = await Scenario.CreaAsync(obbligo: "Nessuno");
        var id = await s.CreaAmministratoreAsync();

        var avvio = await s.EseguiAsync(s.A, d => d.AvviaConfigurazioneAsync(id, null, null, Ct));
        Assert.True(avvio.Riuscito);
        Assert.StartsWith("otpauth://totp/DvCms:", avvio.Valore!.Uri, StringComparison.Ordinal);
        Assert.True(Base32.TentaDecodifica(avvio.Valore.ChiaveTestuale, out var segreto));

        var inAttesa = await s.EseguiAsync(s.A, d => d.LeggiStatoAsync(id, Ct));
        Assert.False(inAttesa!.Attivi);
        Assert.True(inAttesa.ConfigurazioneInCorso);

        var errato = await s.EseguiAsync(s.A, d => d.ConfermaAttivazioneAsync(id, "000000", Ct));
        Assert.Equal(EsitoDueFattori.NonValido, errato.Esito);
        Assert.Equal(1, (await s.LeggiAsync(id)).ContatoreAccessiFalliti);

        var conferma = await s.EseguiAsync(s.A, d => d.ConfermaAttivazioneAsync(id, s.Codice(segreto), Ct));
        Assert.True(conferma.Riuscito);
        Assert.Equal(CodiciRecupero.Quantita, conferma.Valore!.CodiciRecupero.Count);
        Assert.True(conferma.Valore.Identita.DueFattoriAttivi);
        Assert.NotEqual(Guid.Empty, conferma.Valore.Identita.TimbroSicurezza);
        Assert.Equal(conferma.Valore.Identita.TimbroSicurezza, (await s.LeggiAsync(id)).TimbroSicurezza);

        // Senza password e codice non si cambia dispositivo né si rigenerano i codici.
        var cambioSenza = await s.EseguiAsync(s.A, d => d.AvviaConfigurazioneAsync(id, null, null, Ct));
        Assert.Equal(EsitoDueFattori.RichiedeVerifica, cambioSenza.Esito);
        s.Orologio.Avanza(TimeSpan.FromSeconds(Totp.SecondiPasso));
        var rigenerati = await s.EseguiAsync(s.A, d => d.RigeneraCodiciRecuperoAsync(id, DatiProva.PasswordProva, s.Codice(segreto), Ct));
        Assert.True(rigenerati.Riuscito);
        Assert.Empty(rigenerati.Valore!.CodiciRecupero.Intersect(conferma.Valore.CodiciRecupero));

        // Senza obbligo si disattiva con password e codice; con l'account di un altro tenant non è possibile.
        s.Orologio.Avanza(TimeSpan.FromSeconds(Totp.SecondiPasso));
        var altroTenant = await s.EseguiAsync(s.B, d => d.DisattivaAsync(id, DatiProva.PasswordProva, s.Codice(segreto), Ct));
        Assert.Equal(EsitoDueFattori.NonValido, altroTenant);
        var disattivata = await s.EseguiAsync(s.A, d => d.DisattivaAsync(id, DatiProva.PasswordProva, s.Codice(segreto), Ct));
        Assert.Equal(EsitoDueFattori.Riuscito, disattivata);
        var stato = await s.EseguiAsync(s.A, d => d.LeggiStatoAsync(id, Ct));
        Assert.False(stato!.Attivi);
        Assert.Equal(0, stato.CodiciRecuperoResidui);
    }

    [Fact]
    public async Task IlRuoloUtenteNonUsaLaDueFattoriENonSiOperaSuAccountAltrui()
    {
        await using var s = await Scenario.CreaAsync();
        var acquirente = await DatiProva.CreaUtenzaAsync(s.Fornitore, RuoloUtenza.Utente, s.A.IdTenant, "cliente@esempio.it", annullamento: Ct);
        var altroTenant = await DatiProva.CreaUtenzaAsync(s.Fornitore, RuoloUtenza.TenantAdmin, s.B.IdTenant, "b@esempio.it", annullamento: Ct);

        Assert.Null(await s.EseguiAsync(s.A, d => d.LeggiStatoAsync(acquirente, Ct)));
        Assert.Null(await s.EseguiAsync(s.A, d => d.LeggiStatoAsync(altroTenant, Ct)));
        var avvio = await s.EseguiAsync(s.A, d => d.AvviaConfigurazioneAsync(altroTenant, null, null, Ct));
        Assert.Equal(EsitoDueFattori.NonValido, avvio.Esito);
        Assert.Null((await s.LeggiAsync(altroTenant)).SegretoTotpInAttesaProtetto);
    }

    [Fact]
    public async Task ConUnaChiaveDeiSegretiDiversaLAccessoFallisceSenzaEccezioni()
    {
        await using var s = await Scenario.CreaAsync();
        var id = await s.CreaAmministratoreAsync();
        var (segreto, _) = await DatiProva.AttivaDueFattoriAsync(s.Fornitore, id, Ct);
        var timbro = (await s.LeggiAsync(id)).TimbroSicurezza;

        await using var altraChiave = await ServiziProva.CreaSenzaMigrazioniAsync(
            s.Database.Connessione,
            new Dictionary<string, string?> { ["Sicurezza:ChiaveSegreti"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) },
            orologio: s.Orologio);
        await using var ambito = altraChiave.CreateAsyncScope();
        ambito.ServiceProvider.GetRequiredService<ContestoTenant>().Imposta(
            new DatiTenantRisolto(s.A.IdTenant, s.A.IdSito, s.A.IdDominio, s.A.Host, TenantPrimario: false));
        var esito = await ambito.ServiceProvider.GetRequiredService<IServizioDueFattori>().CompletaAccessoAsync(id, timbro, s.Codice(segreto), Ct);

        Assert.False(esito.EsitoPositivo);
    }

    private sealed class Scenario : IAsyncDisposable
    {
        private Scenario(DatabaseDiProva database, ServiceProvider fornitore, OrologioProva orologio, DatiProva.TenantCreato a, DatiProva.TenantCreato b)
        {
            Database = database;
            Fornitore = fornitore;
            Orologio = orologio;
            A = a;
            B = b;
        }

        public DatabaseDiProva Database { get; }

        public ServiceProvider Fornitore { get; }

        public OrologioProva Orologio { get; }

        public DatiProva.TenantCreato A { get; }

        public DatiProva.TenantCreato B { get; }

        public static async Task<Scenario> CreaAsync(string? obbligo = "Nessuno")
        {
            var database = await DatabaseDiProva.CreaAsync(Ct);
            var orologio = new OrologioProva();
            var fornitore = await ServiziProva.CreaConMigrazioniAsync(
                database.Connessione,
                new Dictionary<string, string?>
                {
                    ["Sicurezza:ChiaveSegreti"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
                    ["Sicurezza:DueFattori:Obbligo"] = obbligo,
                },
                orologio: orologio,
                annullamento: Ct);
            var a = await DatiProva.CreaTenantAsync(fornitore, "A", "a.esempio.it", annullamento: Ct);
            var b = await DatiProva.CreaTenantAsync(fornitore, "B", "b.esempio.it", annullamento: Ct);
            return new Scenario(database, fornitore, orologio, a, b);
        }

        public Task<Guid> CreaAmministratoreAsync() =>
            DatiProva.CreaUtenzaAsync(Fornitore, RuoloUtenza.TenantAdmin, A.IdTenant, "anna@esempio.it", annullamento: Ct);

        public Task<Guid> CreaPlatformAsync(string email) =>
            DatiProva.CreaUtenzaAsync(Fornitore, RuoloUtenza.PlatformAdmin, null, email, annullamento: Ct);

        public string Codice(byte[] segreto) =>
            Totp.CalcolaCodice(segreto, Totp.PassoDi(Orologio.GetUtcNow()));

        public async Task<RisultatoAccesso> AccediAsync(DatiProva.TenantCreato tenant, string email = "anna@esempio.it")
        {
            await using var ambito = Ambito(tenant);
            return await ambito.ServiceProvider.GetRequiredService<IServizioAccesso>().AccediAsync(email, DatiProva.PasswordProva, Ct);
        }

        public Task<RisultatoAccesso> CompletaAsync(DatiProva.TenantCreato tenant, Guid idUtenza, Guid timbro, string codice) =>
            EseguiAsync(tenant, d => d.CompletaAccessoAsync(idUtenza, timbro, codice, Ct));

        public async Task<T> EseguiAsync<T>(DatiProva.TenantCreato tenant, Func<IServizioDueFattori, Task<T>> operazione)
        {
            await using var ambito = Ambito(tenant);
            return await operazione(ambito.ServiceProvider.GetRequiredService<IServizioDueFattori>());
        }

        public async Task<Utenza> LeggiAsync(Guid id)
        {
            await using var ambito = Fornitore.CreateAsyncScope();
            return await ambito.ServiceProvider.GetRequiredService<ContestoDatabase>().Utenze.IgnoreQueryFilters().AsNoTracking().SingleAsync(u => u.Id == id, Ct);
        }

        public async Task ImpostaStatoAsync(Guid id, StatoUtenza stato)
        {
            await using var ambito = Fornitore.CreateAsyncScope();
            await ambito.ServiceProvider.GetRequiredService<ContestoDatabase>().Utenze.IgnoreQueryFilters().Where(u => u.Id == id)
                .ExecuteUpdateAsync(u => u.SetProperty(x => x.Stato, stato), Ct);
        }

        public async ValueTask DisposeAsync()
        {
            await Fornitore.DisposeAsync();
            await Database.DisposeAsync();
        }

        private AsyncServiceScope Ambito(DatiProva.TenantCreato tenant)
        {
            var ambito = Fornitore.CreateAsyncScope();
            ambito.ServiceProvider.GetRequiredService<ContestoTenant>().Imposta(
                new DatiTenantRisolto(tenant.IdTenant, tenant.IdSito, tenant.IdDominio, tenant.Host, TenantPrimario: false));
            return ambito;
        }
    }
}
