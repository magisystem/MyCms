using System.Diagnostics.Metrics;
using System.Text.Json;
using DvCms.Contracts.Media;
using DvCms.IntegrationTests.Supporto;
using DvCms.Web.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DvCms.IntegrationTests.Avvio;

/// <summary>Sonde di salute e metriche delle richieste (M6).</summary>
public class SaluteEMetricheTest
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task LaSondaDiVitalitaRispondeAnchePerUnHostSconosciutoESenzaControlliEsterni()
    {
        // Database vuoto e nessun tenant: qualunque host sarebbe 404, ma le sonde non passano dalla risoluzione del tenant.
        await using var database = await DatabaseDiProva.CreaAsync(Ct);
        using var fabbrica = new FabbricaApplicazioneConConnessione(database.Connessione);
        using var client = fabbrica.CreateClient();
        using var richiesta = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        richiesta.Headers.Host = "dominio-sconosciuto.example";

        using var risposta = await client.SendAsync(richiesta, Ct);

        Assert.Equal(System.Net.HttpStatusCode.OK, risposta.StatusCode);
        Assert.Equal("no-store", risposta.Headers.CacheControl?.ToString());
        var json = JsonDocument.Parse(await risposta.Content.ReadAsStringAsync(Ct)).RootElement;
        Assert.Equal("sano", json.GetProperty("stato").GetString());
        Assert.Empty(json.GetProperty("controlli").EnumerateObject());
    }

    [Fact]
    public async Task LaProntezzaRichiedeDatabaseMigratoEArchivio()
    {
        await using var database = await DatabaseDiProva.CreaAsync(Ct);
        using var fabbrica = new FabbricaApplicazioneConConnessione(database.Connessione);

        // Database vuoto: raggiungibile ma senza migrazioni, quindi non pronto.
        using (var client = fabbrica.CreateClient())
        {
            using var nonPronta = await client.GetAsync("/health/ready", Ct);
            Assert.Equal(System.Net.HttpStatusCode.ServiceUnavailable, nonPronta.StatusCode);
            var json = JsonDocument.Parse(await nonPronta.Content.ReadAsStringAsync(Ct)).RootElement;
            Assert.Equal("non sano", json.GetProperty("stato").GetString());
            Assert.Equal("non sano", json.GetProperty("controlli").GetProperty("database").GetString());
            Assert.Equal("sano", json.GetProperty("controlli").GetProperty("archivio").GetString());
        }

        var esito = await DvCms.Web.Comandi.ComandoMigra.EseguiAsync(fabbrica.Services, Ct);
        Assert.Equal(0, esito);

        using var clientDopo = fabbrica.CreateClient();
        using var pronta = await clientDopo.GetAsync("/health/ready", Ct);
        Assert.Equal(System.Net.HttpStatusCode.OK, pronta.StatusCode);
        var jsonDopo = JsonDocument.Parse(await pronta.Content.ReadAsStringAsync(Ct)).RootElement;
        Assert.Equal("sano", jsonDopo.GetProperty("stato").GetString());
    }

    [Fact]
    public async Task LeRisposteDiSaluteNonRivelanoDettagliDiConnessioneNeErrori()
    {
        using var fabbrica = new FabbricaApplicazioneConConnessione(
            "Server=host-segreto.interno,1;Database=NonEsiste;User Id=utente-segreto;Password=password-segreta;TrustServerCertificate=True;Connect Timeout=2");
        using var client = fabbrica.CreateClient();

        using var risposta = await client.GetAsync("/health/ready", Ct);
        var corpo = await risposta.Content.ReadAsStringAsync(Ct);

        Assert.Equal(System.Net.HttpStatusCode.ServiceUnavailable, risposta.StatusCode);
        Assert.DoesNotContain("segret", corpo, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Exception", corpo, StringComparison.Ordinal);
        Assert.Equal("non sano", JsonDocument.Parse(corpo).RootElement.GetProperty("controlli").GetProperty("database").GetString());
    }

    [Fact]
    public async Task UnArchivioNonAccessibileRendeNonProntaLIstanza()
    {
        await using var database = await DatabaseDiProva.CreaAsync(Ct);
        using var fabbrica = new FabbricaConArchivioRotto(database.Connessione);
        var esito = await DvCms.Web.Comandi.ComandoMigra.EseguiAsync(fabbrica.Services, Ct);
        Assert.Equal(0, esito);
        using var client = fabbrica.CreateClient();

        using var risposta = await client.GetAsync("/health/ready", Ct);
        var corpo = await risposta.Content.ReadAsStringAsync(Ct);

        Assert.Equal(System.Net.HttpStatusCode.ServiceUnavailable, risposta.StatusCode);
        var json = JsonDocument.Parse(corpo).RootElement;
        Assert.Equal("sano", json.GetProperty("controlli").GetProperty("database").GetString());
        Assert.Equal("non sano", json.GetProperty("controlli").GetProperty("archivio").GetString());
        Assert.DoesNotContain("disco-guasto", corpo, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LeMetricheDelleRichiesteHannoTagABassaCardinalita()
    {
        var misurazioni = new List<(long Valore, Dictionary<string, object?> Tag)>();
        using var ascoltatore = new MeterListener();
        ascoltatore.InstrumentPublished = (strumento, listener) =>
        {
            if (strumento.Meter.Name == MiddlewareMetriche.NomeMeter && strumento.Name == "dvcms.http.richieste")
            {
                listener.EnableMeasurementEvents(strumento);
            }
        };
        ascoltatore.SetMeasurementEventCallback<long>((_, valore, tag, _) =>
        {
            lock (misurazioni)
            {
                misurazioni.Add((valore, tag.ToArray().ToDictionary(t => t.Key, t => t.Value)));
            }
        });
        ascoltatore.Start();

        using var fabbrica = new FabbricaApplicazioneSenzaDatabase();
        using var client = fabbrica.CreateClient();
        using var richiesta = new HttpRequestMessage(new HttpMethod("FOOBAR"), "/health/live?x=1");
        using var risposta = await client.SendAsync(richiesta, Ct);
        using var segmentoCasuale = await client.GetAsync("/health/live", Ct);

        ascoltatore.Dispose();
        Dictionary<string, object?>[] salute;
        lock (misurazioni)
        {
            salute = [.. misurazioni.Where(m => Equals(m.Tag.GetValueOrDefault("area"), "salute")).Select(m => m.Tag)];
        }

        Assert.NotEmpty(salute);
        Assert.All(salute, tag => Assert.Equal(["area", "classe", "metodo"], tag.Keys.Order(StringComparer.Ordinal)));
        Assert.Contains(salute, tag => Equals(tag["metodo"], "altro"));
        Assert.Contains(salute, tag => Equals(tag["metodo"], "GET") && Equals(tag["classe"], "2xx"));
    }

    [Theory]
    [InlineData("/admin/pagine/01996a00-0000-7000-8000-0000000000a1", "admin")]
    [InlineData("/media/01996a00-0000-7000-8000-0000000000a1", "media")]
    [InlineData("/sitemap.xml", "seo")]
    [InlineData("/robots.txt", "seo")]
    [InlineData("/css/componenti.css", "statico")]
    [InlineData("/health/ready", "salute")]
    [InlineData("/una-pagina-qualsiasi/con/segmenti", "pubblico")]
    [InlineData("/", "pubblico")]
    public void LAreaDipendeSoloDalPrimoSegmentoNoto(string percorso, string atteso) =>
        Assert.Equal(atteso, MiddlewareMetriche.Area(new PathString(percorso)));

    private sealed class FabbricaConArchivioRotto : FabbricaApplicazione
    {
        public FabbricaConArchivioRotto(string connessione)
            : base(connessione)
        {
        }

        protected override void ConfiguraServizi(IServiceCollection servizi)
        {
            servizi.AddSingleton<IArchivioOggetti, ArchivioRotto>();
        }
    }

    private sealed class ArchivioRotto : IArchivioOggetti
    {
        public Task ScriviAsync(string chiave, Stream contenuto, CancellationToken annullamento) => throw new IOException("disco-guasto");

        public Task<Stream?> ApriAsync(string chiave, CancellationToken annullamento) => throw new IOException("disco-guasto");

        public Task<bool> EliminaAsync(string chiave, CancellationToken annullamento) => throw new IOException("disco-guasto");

        public async IAsyncEnumerable<string> ElencaChiaviAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken annullamento)
        {
            await Task.CompletedTask;
            throw new IOException("disco-guasto");
#pragma warning disable CS0162 // Irraggiungibile: serve a dichiarare l'enumerabile asincrono.
            yield break;
#pragma warning restore CS0162
        }
    }
}
