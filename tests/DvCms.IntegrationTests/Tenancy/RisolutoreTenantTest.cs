using DvCms.Application.Tenancy;
using DvCms.IntegrationTests.Supporto;
using DvCms.Models.Tenancy;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DvCms.IntegrationTests.Tenancy;

/// <summary>Risoluzione dell'host nel tenant (B-002): solo host registrati, verificati e attivi.</summary>
public class RisolutoreTenantTest
{
    [Fact]
    public async Task HostRegistratoRisolveTenantESito()
    {
        await using var contestoProva = await CreaAsync();
        var creato = await DatiProva.CreaTenantAsync(contestoProva.Fornitore, "Uno", "uno.esempio.it", annullamento: TestContext.Current.CancellationToken);

        var risultato = await Risolutore(contestoProva).RisolviAsync("uno.esempio.it", TestContext.Current.CancellationToken);

        Assert.Equal(EsitoRisoluzioneTenant.Risolto, risultato.Esito);
        Assert.Equal(creato.IdTenant, risultato.Dati!.IdTenant);
        Assert.Equal(creato.IdSito, risultato.Dati.IdSito);
        Assert.Equal(creato.IdDominio, risultato.Dati.IdDominio);
        Assert.False(risultato.Dati.TenantPrimario);
    }

    [Theory]
    [InlineData("UNO.esempio.IT")]
    [InlineData("uno.esempio.it:8080")]
    [InlineData("uno.esempio.it.")]
    public async Task MaiuscoleMinuscolePortaEPuntoFinaleSonoIgnorati(string richiesto)
    {
        await using var contestoProva = await CreaAsync();
        await DatiProva.CreaTenantAsync(contestoProva.Fornitore, "Uno", "uno.esempio.it", annullamento: TestContext.Current.CancellationToken);

        var risultato = await Risolutore(contestoProva).RisolviAsync(richiesto, TestContext.Current.CancellationToken);

        Assert.Equal(EsitoRisoluzioneTenant.Risolto, risultato.Esito);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("sconosciuto.esempio.it")]
    [InlineData("esempio.it")]
    [InlineData("sotto.uno.esempio.it")]
    [InlineData("127.0.0.1")]
    [InlineData("uno.esempio.it\r\nHost: altro")]
    public async Task HostSconosciutoOInvalidoNonRisolve(string? richiesto)
    {
        await using var contestoProva = await CreaAsync();
        await DatiProva.CreaTenantAsync(contestoProva.Fornitore, "Uno", "uno.esempio.it", annullamento: TestContext.Current.CancellationToken);

        var risultato = await Risolutore(contestoProva).RisolviAsync(richiesto, TestContext.Current.CancellationToken);

        Assert.Equal(EsitoRisoluzioneTenant.NonRegistrato, risultato.Esito);
        Assert.Null(risultato.Dati);
    }

    [Fact]
    public async Task DominioNonVerificatoODisattivatoNonRisolve()
    {
        await using var contestoProva = await CreaAsync();
        await DatiProva.CreaTenantAsync(contestoProva.Fornitore, "NonVerificato", "nonverificato.esempio.it", verificato: false, annullamento: TestContext.Current.CancellationToken);
        await DatiProva.CreaTenantAsync(contestoProva.Fornitore, "Disattivo", "disattivo.esempio.it", dominioAttivo: false, annullamento: TestContext.Current.CancellationToken);
        var risolutore = Risolutore(contestoProva);

        Assert.Equal(EsitoRisoluzioneTenant.NonRegistrato, (await risolutore.RisolviAsync("nonverificato.esempio.it", TestContext.Current.CancellationToken)).Esito);
        Assert.Equal(EsitoRisoluzioneTenant.NonRegistrato, (await risolutore.RisolviAsync("disattivo.esempio.it", TestContext.Current.CancellationToken)).Esito);
    }

    [Fact]
    public async Task TenantOSitoSospesiRisultanoNonDisponibili()
    {
        await using var contestoProva = await CreaAsync();
        await DatiProva.CreaTenantAsync(contestoProva.Fornitore, "Sospeso", "sospeso.esempio.it", statoTenant: StatoTenant.Sospeso, annullamento: TestContext.Current.CancellationToken);
        await DatiProva.CreaTenantAsync(contestoProva.Fornitore, "SitoOff", "sitooff.esempio.it", statoSito: StatoSito.Disattivato, annullamento: TestContext.Current.CancellationToken);
        var risolutore = Risolutore(contestoProva);

        Assert.Equal(EsitoRisoluzioneTenant.NonDisponibile, (await risolutore.RisolviAsync("sospeso.esempio.it", TestContext.Current.CancellationToken)).Esito);
        Assert.Equal(EsitoRisoluzioneTenant.NonDisponibile, (await risolutore.RisolviAsync("sitooff.esempio.it", TestContext.Current.CancellationToken)).Esito);
    }

    [Theory]
    [InlineData("Development", EsitoRisoluzioneTenant.Risolto)]
    [InlineData("Production", EsitoRisoluzioneTenant.NonRegistrato)]
    [InlineData("Staging", EsitoRisoluzioneTenant.NonRegistrato)]
    public async Task DominioSoloSviluppoRisolveSoloInSviluppo(string ambiente, EsitoRisoluzioneTenant atteso)
    {
        await using var contestoProva = await CreaAsync(ambiente);
        await DatiProva.CreaTenantAsync(contestoProva.Fornitore, "Gestione", "localhost", primario: true, soloSviluppo: true, annullamento: TestContext.Current.CancellationToken);

        var risultato = await Risolutore(contestoProva).RisolviAsync("localhost:5123", TestContext.Current.CancellationToken);

        Assert.Equal(atteso, risultato.Esito);
        if (atteso == EsitoRisoluzioneTenant.Risolto)
        {
            Assert.True(risultato.Dati!.TenantPrimario);
        }
    }

    private static IRisolutoreTenant Risolutore(ContestoProvaDatabase contesto) =>
        contesto.Fornitore.GetRequiredService<IRisolutoreTenant>();

    private static async Task<ContestoProvaDatabase> CreaAsync(string ambiente = "Development")
    {
        var database = await DatabaseDiProva.CreaAsync(TestContext.Current.CancellationToken);
        var fornitore = await ServiziProva.CreaConMigrazioniAsync(
            database.Connessione,
            ambiente: ambiente,
            annullamento: TestContext.Current.CancellationToken);
        return new ContestoProvaDatabase(database, fornitore);
    }

    private sealed class ContestoProvaDatabase : IAsyncDisposable
    {
        private readonly DatabaseDiProva _database;

        public ContestoProvaDatabase(DatabaseDiProva database, ServiceProvider fornitore)
        {
            _database = database;
            Fornitore = fornitore;
        }

        public ServiceProvider Fornitore { get; }

        public async ValueTask DisposeAsync()
        {
            await Fornitore.DisposeAsync();
            await _database.DisposeAsync();
        }
    }
}
