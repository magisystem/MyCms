using DvCms.Application.Negozio;
using DvCms.Application.Piattaforma;
using DvCms.Application.Tenancy;
using DvCms.Domain.Accessi;
using DvCms.Domain.Negozio;
using DvCms.Infrastructure.Persistenza;
using DvCms.IntegrationTests.Supporto;
using DvCms.IntegrationTests.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DvCms.IntegrationTests.Negozio;

/// <summary>Metodi di spedizione (F-002, ADR-0020): validazione, permessi, visibilità per gli acquirenti e isolamento fra tenant.</summary>
public class ServizioSpedizioniTest
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static DatiMetodoSpedizione Italia(string nome = "Corriere Italia", decimal tariffa = 6.90m, bool attivo = true, Guid? id = null) =>
        new(id, TipoMetodoSpedizione.Zona, AreaSpedizione.Italia, nome, tariffa, attivo);

    private static DatiMetodoSpedizione Ritiro(string nome = "Ritiro in negozio", bool attivo = true) =>
        new(null, TipoMetodoSpedizione.Ritiro, null, nome, 0m, attivo);

    [Fact]
    public async Task SenzaNegozioAttivoNonSiGestisconoMetodi()
    {
        await using var c = await Contesto.CreaAsync(commercio: false);

        var esito = await c.ComeAsync(c.A, s => s.SalvaAsync(c.AdminA, Italia(), Ct));
        var attivi = await c.ComeAsync(c.A, s => s.ElencaAttiviAsync(c.Cliente, Ct));

        Assert.Equal(CodiceEsito.NonConsentito, esito.Codice);
        Assert.Empty(attivi);
    }

    [Fact]
    public async Task CreaEModificaMetodiEGliAcquirentiVedonoSoloQuelliAttiviNellOrdineDiPresentazione()
    {
        await using var c = await Contesto.CreaAsync();
        var italia = await c.ComeAsync(c.A, s => s.SalvaAsync(c.AdminA, Italia(), Ct));
        var europa = await c.ComeAsync(c.A, s => s.SalvaAsync(c.AdminA, new(null, TipoMetodoSpedizione.Zona, AreaSpedizione.UnioneEuropea, "Corriere UE", 14.50m, true), Ct));
        var ritiro = await c.ComeAsync(c.A, s => s.SalvaAsync(c.AdminA, Ritiro(), Ct));
        var spento = await c.ComeAsync(c.A, s => s.SalvaAsync(c.AdminA, Italia("Express", 12m, attivo: false), Ct));
        Assert.All(new[] { italia, europa, ritiro, spento }, e => Assert.True(e.Riuscito, $"{e.Codice} {string.Join(',', e.CampiNonValidi ?? [])}"));

        var modifica = await c.ComeAsync(c.A, s => s.SalvaAsync(c.AdminA, Italia("  Corriere Italia 24h ", 7.50m, id: italia.Id), Ct));

        Assert.True(modifica.Riuscito);
        var tutti = await c.ComeAsync(c.A, s => s.ElencaAsync(c.AdminA, Ct));
        Assert.Equal(["Corriere Italia 24h", "Corriere UE", "Ritiro in negozio", "Express"], tutti.Select(m => m.Nome));
        Assert.Equal(7.50m, tutti[0].TariffaLorda);
        var attivi = await c.ComeAsync(c.A, s => s.ElencaAttiviAsync(c.Cliente, Ct));
        Assert.Equal(["Corriere Italia 24h", "Corriere UE", "Ritiro in negozio"], attivi.Select(m => m.Nome));
    }

    [Theory]
    [InlineData(1, "", 5, "Nome")]
    [InlineData(1, "   ", 5, "Nome")]
    [InlineData(1, "Nome\u0007", 5, "Nome")]
    [InlineData(1, "Corriere", -1, "TariffaLorda")]
    [InlineData(1, "Corriere", 10000.01, "TariffaLorda")]
    [InlineData(1, "Corriere", 5.005, "TariffaLorda")]
    public async Task IDatiNonValidiSonoRifiutatiIndicandoIlCampo(int tipo, string nome, double tariffa, string campo)
    {
        await using var c = await Contesto.CreaAsync();

        var esito = await c.ComeAsync(c.A, s => s.SalvaAsync(c.AdminA, new(null, (TipoMetodoSpedizione)tipo, AreaSpedizione.Italia, nome, (decimal)tariffa, true), Ct));

        Assert.Equal(CodiceEsito.DatiNonValidi, esito.Codice);
        Assert.Contains(campo, esito.CampiNonValidi!);
        Assert.Empty(await c.ComeAsync(c.A, s => s.ElencaAsync(c.AdminA, Ct)));
    }

    [Fact]
    public async Task ZonaRichiedeUnAreaERitiroNonLaAmmetteENonHaTariffa()
    {
        await using var c = await Contesto.CreaAsync();

        var senzaArea = await c.ComeAsync(c.A, s => s.SalvaAsync(c.AdminA, new(null, TipoMetodoSpedizione.Zona, null, "Corriere", 5m, true), Ct));
        var areaInventata = await c.ComeAsync(c.A, s => s.SalvaAsync(c.AdminA, new(null, TipoMetodoSpedizione.Zona, (AreaSpedizione)99, "Corriere", 5m, true), Ct));
        var ritiroConArea = await c.ComeAsync(c.A, s => s.SalvaAsync(c.AdminA, new(null, TipoMetodoSpedizione.Ritiro, AreaSpedizione.Italia, "Ritiro", 0m, true), Ct));
        var tipoInventato = await c.ComeAsync(c.A, s => s.SalvaAsync(c.AdminA, new(null, (TipoMetodoSpedizione)99, null, "Strano", 0m, true), Ct));
        var ritiroConTariffa = await c.ComeAsync(c.A, s => s.SalvaAsync(c.AdminA, new(null, TipoMetodoSpedizione.Ritiro, null, "Ritiro", 9m, true), Ct));

        Assert.Equal(CodiceEsito.DatiNonValidi, senzaArea.Codice);
        Assert.Equal(CodiceEsito.DatiNonValidi, areaInventata.Codice);
        Assert.Equal(CodiceEsito.DatiNonValidi, ritiroConArea.Codice);
        Assert.Equal(CodiceEsito.DatiNonValidi, tipoInventato.Codice);
        // Il ritiro non ha costo: una tariffa ricevuta dal cliente viene ignorata, mai salvata.
        Assert.True(ritiroConTariffa.Riuscito);
        Assert.Equal(0m, (await c.ComeAsync(c.A, s => s.ElencaAsync(c.AdminA, Ct))).Single().TariffaLorda);
    }

    [Fact]
    public async Task UnaSpedizioneGratuitaEAmmessaEIlNumeroDiMetodiELimitato()
    {
        await using var c = await Contesto.CreaAsync();
        Assert.True((await c.ComeAsync(c.A, s => s.SalvaAsync(c.AdminA, Italia("Gratis", 0m), Ct))).Riuscito);

        for (var i = 1; i < MetodoSpedizione.NumeroMassimo; i++)
        {
            var n = i;
            Assert.True((await c.ComeAsync(c.A, s => s.SalvaAsync(c.AdminA, Italia($"Metodo {n}", 1m), Ct))).Riuscito);
        }

        var troppi = await c.ComeAsync(c.A, s => s.SalvaAsync(c.AdminA, Italia("Uno di troppo", 1m), Ct));
        var altroTenant = await c.ComeAsync(c.B, s => s.SalvaAsync(c.AdminB, Italia(), Ct));

        Assert.Equal(CodiceEsito.NonConsentito, troppi.Codice);
        Assert.True(altroTenant.Riuscito);
    }

    [Fact]
    public async Task EliminaUnMetodoESoloDelProprioSito()
    {
        await using var c = await Contesto.CreaAsync();
        var mio = await c.ComeAsync(c.A, s => s.SalvaAsync(c.AdminA, Italia(), Ct));
        var suo = await c.ComeAsync(c.B, s => s.SalvaAsync(c.AdminB, Italia("Di B"), Ct));

        var daAltroTenant = await c.ComeAsync(c.A, s => s.EliminaAsync(c.AdminA, suo.Id!.Value, Ct));
        var modificaAltrui = await c.ComeAsync(c.A, s => s.SalvaAsync(c.AdminA, Italia("Rubato", id: suo.Id), Ct));
        var elimina = await c.ComeAsync(c.A, s => s.EliminaAsync(c.AdminA, mio.Id!.Value, Ct));
        var ancora = await c.ComeAsync(c.A, s => s.EliminaAsync(c.AdminA, mio.Id!.Value, Ct));

        Assert.Equal(CodiceEsito.NonTrovato, daAltroTenant.Codice);
        Assert.Equal(CodiceEsito.NonTrovato, modificaAltrui.Codice);
        Assert.True(elimina.Riuscito);
        Assert.Equal(CodiceEsito.NonTrovato, ancora.Codice);
        Assert.Equal("Di B", (await c.ComeAsync(c.B, s => s.ElencaAsync(c.AdminB, Ct))).Single().Nome);
    }

    [Fact]
    public async Task SoloGliAmministratoriDelTenantGestisconoEGliAcquirentiNonPossonoElencareTutto()
    {
        await using var c = await Contesto.CreaAsync();
        var creato = await c.ComeAsync(c.A, s => s.SalvaAsync(c.AdminA, Italia(), Ct));

        var comeCliente = await c.ComeAsync(c.A, s => s.SalvaAsync(c.Cliente, Italia("Intruso"), Ct));
        var comeAdminDiB = await c.ComeAsync(c.A, s => s.SalvaAsync(c.AdminB, Italia("Intruso"), Ct));
        var eliminaCliente = await c.ComeAsync(c.A, s => s.EliminaAsync(c.Cliente, creato.Id!.Value, Ct));
        var comePlatform = await c.ComeAsync(c.A, s => s.SalvaAsync(c.Platform, Italia("Dalla piattaforma"), Ct));

        Assert.Equal(CodiceEsito.NonAutorizzato, comeCliente.Codice);
        Assert.Equal(CodiceEsito.NonAutorizzato, comeAdminDiB.Codice);
        Assert.Equal(CodiceEsito.NonAutorizzato, eliminaCliente.Codice);
        Assert.True(comePlatform.Riuscito);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => c.ComeAsync(c.A, s => s.ElencaAsync(c.Cliente, Ct)));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => c.ComeAsync(c.A, s => s.ElencaAttiviAsync(Guid.NewGuid(), Ct)));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => c.ComeAsync(c.A, s => s.ElencaAttiviAsync(c.ClienteDiB, Ct)));
        Assert.Equal(2, (await c.ComeAsync(c.A, s => s.ElencaAsync(c.AdminA, Ct))).Count);
    }

    [Fact]
    public async Task IMetodiDiUnTenantNonSiVedonoDaUnAltro()
    {
        await using var c = await Contesto.CreaAsync();
        await c.ComeAsync(c.A, s => s.SalvaAsync(c.AdminA, Italia("Solo A"), Ct));

        Assert.Empty(await c.ComeAsync(c.B, s => s.ElencaAsync(c.AdminB, Ct)));
        Assert.Empty(await c.ComeAsync(c.B, s => s.ElencaAttiviAsync(c.ClienteDiB, Ct)));
    }

    private sealed class Contesto : IAsyncDisposable
    {
        private Contesto(ScenarioWeb web) => Web = web;

        public ScenarioWeb Web { get; }

        public DatiProva.TenantCreato A => Web.A;

        public DatiProva.TenantCreato B => Web.B;

        public Guid Platform { get; private set; }

        public Guid AdminA { get; private set; }

        public Guid AdminB { get; private set; }

        public Guid Cliente { get; private set; }

        public Guid ClienteDiB { get; private set; }

        public static async Task<Contesto> CreaAsync(bool commercio = true)
        {
            var c = new Contesto(await ScenarioWeb.CreaAsync());
            c.Platform = await c.Web.CreaPlatformAdminAsync("root@esempio.it");
            c.AdminA = await c.Web.CreaTenantAdminAsync("anna@esempio.it");
            c.AdminB = await DatiProva.CreaUtenzaAsync(c.Web.Fabbrica.Services, RuoloUtenza.TenantAdmin, c.B.IdTenant, "bruno@esempio.it", annullamento: Ct);
            c.Cliente = await DatiProva.CreaUtenzaAsync(c.Web.Fabbrica.Services, RuoloUtenza.Utente, c.A.IdTenant, "cliente@esempio.it", annullamento: Ct);
            c.ClienteDiB = await DatiProva.CreaUtenzaAsync(c.Web.Fabbrica.Services, RuoloUtenza.Utente, c.B.IdTenant, "cliente@esempio.it", annullamento: Ct);
            if (commercio)
            {
                foreach (var tenant in new[] { c.A, c.B })
                {
                    await using var ambito = c.Web.Fabbrica.Services.CreateAsyncScope();
                    var esito = await ambito.ServiceProvider.GetRequiredService<IServizioPiattaforma>().ImpostaCommercioAsync(c.Platform, tenant.IdTenant, tenant.IdSito, true, Ct);
                    Assert.True(esito.Riuscito, esito.Codice.ToString());
                }
            }

            return c;
        }

        public async Task<T> ComeAsync<T>(DatiProva.TenantCreato tenant, Func<IServizioSpedizioni, Task<T>> operazione)
        {
            await using var ambito = Web.Fabbrica.Services.CreateAsyncScope();
            ambito.ServiceProvider.GetRequiredService<ContestoTenant>().Imposta(new DatiTenantRisolto(tenant.IdTenant, tenant.IdSito, tenant.IdDominio, tenant.Host, false));
            return await operazione(ambito.ServiceProvider.GetRequiredService<IServizioSpedizioni>());
        }

        public ValueTask DisposeAsync() => Web.DisposeAsync();
    }
}
