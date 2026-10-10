using DvCms.Infrastructure.Cache;
using Xunit;

namespace DvCms.UnitTests.Cache;

public class CachePubblicaTest
{
    private static readonly Guid _sitoA = Guid.NewGuid();
    private static readonly Guid _sitoB = Guid.NewGuid();

    private static CachePubblica Crea(OrologioFinto? orologio = null, int voci = 100, bool abilitata = true) =>
        new(new OpzioniCache(abilitata, TimeSpan.FromMinutes(5), voci, TimeSpan.FromSeconds(2)), orologio ?? new OrologioFinto());

    [Fact]
    public async Task IlSecondoAccessoNonRicarica()
    {
        var cache = Crea();
        var caricamenti = 0;

        var primo = await cache.OttieniAsync(_sitoA, "k", () => Task.FromResult<string?>($"valore{++caricamenti}"));
        var secondo = await cache.OttieniAsync(_sitoA, "k", () => Task.FromResult<string?>($"valore{++caricamenti}"));

        Assert.Equal("valore1", primo);
        Assert.Equal("valore1", secondo);
        Assert.Equal(1, caricamenti);
    }

    [Fact]
    public async Task LeVociDiSitiEChiaviDiverseSonoSeparate()
    {
        var cache = Crea();

        var a = await cache.OttieniAsync(_sitoA, "k", () => Task.FromResult<string?>("A"));
        var b = await cache.OttieniAsync(_sitoB, "k", () => Task.FromResult<string?>("B"));
        var altraChiave = await cache.OttieniAsync(_sitoA, "altra", () => Task.FromResult<string?>("A2"));

        Assert.Equal(("A", "B", "A2"), (a, b, altraChiave));
    }

    [Fact]
    public async Task InvalidareUnSitoRicaricaSoloLuiENonGliAltri()
    {
        var cache = Crea();
        await cache.OttieniAsync(_sitoA, "k", () => Task.FromResult<string?>("A1"));
        await cache.OttieniAsync(_sitoB, "k", () => Task.FromResult<string?>("B1"));

        cache.Invalida(_sitoA);

        Assert.Equal("A2", await cache.OttieniAsync(_sitoA, "k", () => Task.FromResult<string?>("A2")));
        Assert.Equal("B1", await cache.OttieniAsync(_sitoB, "k", () => Task.FromResult<string?>("B2")));
    }

    [Fact]
    public async Task UnRisultatoNulloNonSiMemorizza()
    {
        var cache = Crea();
        var chiamate = 0;

        await cache.OttieniAsync<string>(_sitoA, "k", () => { chiamate++; return Task.FromResult<string?>(null); });
        await cache.OttieniAsync<string>(_sitoA, "k", () => { chiamate++; return Task.FromResult<string?>(null); });

        Assert.Equal(2, chiamate);
        Assert.Equal(0, cache.Voci);
    }

    [Fact]
    public async Task LaVoceScadeDopoLaDurata()
    {
        var orologio = new OrologioFinto();
        var cache = Crea(orologio);
        await cache.OttieniAsync(_sitoA, "k", () => Task.FromResult<string?>("vecchio"));

        orologio.Avanza(TimeSpan.FromMinutes(4));
        Assert.Equal("vecchio", await cache.OttieniAsync(_sitoA, "k", () => Task.FromResult<string?>("nuovo")));
        orologio.Avanza(TimeSpan.FromMinutes(2));

        Assert.Equal("nuovo", await cache.OttieniAsync(_sitoA, "k", () => Task.FromResult<string?>("nuovo")));
    }

    [Fact]
    public async Task SeLaCacheEDisabilitataSiCaricaSempre()
    {
        var cache = Crea(abilitata: false);
        var caricamenti = 0;

        await cache.OttieniAsync(_sitoA, "k", () => Task.FromResult<string?>($"v{++caricamenti}"));
        await cache.OttieniAsync(_sitoA, "k", () => Task.FromResult<string?>($"v{++caricamenti}"));

        Assert.Equal(2, caricamenti);
        Assert.Equal(0, cache.Voci);
    }

    [Fact]
    public async Task UnaInvalidazioneDuranteIlCaricamentoNonLasciaUnaVoceUtilizzabile()
    {
        var cache = Crea();

        // Il caricamento "vede" il contenuto vecchio, ma il sito viene invalidato prima che finisca.
        var primo = await cache.OttieniAsync(_sitoA, "k", () =>
        {
            cache.Invalida(_sitoA);
            return Task.FromResult<string?>("letto-prima-della-modifica");
        });
        var dopo = await cache.OttieniAsync(_sitoA, "k", () => Task.FromResult<string?>("letto-dopo"));

        Assert.Equal("letto-prima-della-modifica", primo);
        Assert.Equal("letto-dopo", dopo);
    }

    [Fact]
    public async Task LaMemoriaEntroIlLimiteDiVoci()
    {
        var cache = Crea(voci: 20);

        for (var i = 0; i < 200; i++)
        {
            await cache.OttieniAsync(_sitoA, $"k{i}", () => Task.FromResult<string?>($"v{i}"));
        }

        Assert.InRange(cache.Voci, 1, 20);
    }

    [Fact]
    public async Task LeGenerazioniVecchieSiSfoltisconoPerPrime()
    {
        var cache = Crea(voci: 10);
        for (var i = 0; i < 10; i++)
        {
            await cache.OttieniAsync(_sitoA, $"k{i}", () => Task.FromResult<string?>("x"));
        }

        cache.Invalida(_sitoA);
        await cache.OttieniAsync(_sitoB, "nuova", () => Task.FromResult<string?>("y"));

        // Le dieci voci della generazione superata sono state eliminate, resta quella nuova.
        Assert.Equal(1, cache.Voci);
    }

    [Fact]
    public async Task AccessiConcorrentiNonRompononulla()
    {
        var cache = Crea(voci: 50);

        await Parallel.ForAsync(0, 400, TestContext.Current.CancellationToken, async (i, _) =>
        {
            if (i % 17 == 0)
            {
                cache.Invalida(_sitoA);
            }

            var valore = await cache.OttieniAsync(_sitoA, $"k{i % 30}", () => Task.FromResult<string?>($"v{i % 30}"));
            Assert.Equal($"v{i % 30}", valore);
        });
    }

    private sealed class OrologioFinto : TimeProvider
    {
        private DateTimeOffset _adesso = new(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);

        public void Avanza(TimeSpan intervallo) => _adesso += intervallo;

        public override DateTimeOffset GetUtcNow() => _adesso;
    }
}
