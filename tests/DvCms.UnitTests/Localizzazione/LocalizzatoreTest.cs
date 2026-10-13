using DvCms.Contracts.Localizzazione;
using DvCms.Core.Localizzazione;
using DvCms.DTO.Localizzazione;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DvCms.UnitTests.Localizzazione;

public class LocalizzatoreTest
{
    private readonly CacheTestiStatici _cache = new();
    private readonly CodaTestiMancanti _coda = new();

    public LocalizzatoreTest()
    {
        _cache.ImpostaLingue(
        [
            new InfoLingua("it", "it-IT", Predefinita: true, Attiva: true),
            new InfoLingua("en", "en-GB", Predefinita: false, Attiva: true),
        ]);
    }

    private Localizzatore Crea(string codiceLingua = "it") =>
        new(_cache, _coda, new ContestoLinguaFisso(codiceLingua));

    [Fact]
    public void RestituisceIlTestoDellaLinguaSePresente()
    {
        _cache.Imposta("en", "Salva", "Save");

        Assert.Equal("Save", Crea("en").Traduci("Salva"));
        Assert.Equal(0, _coda.InAttesa);
    }

    [Fact]
    public void IndicizzatoreEquivaleATraduci()
    {
        _cache.Imposta("en", "Salva", "Save");

        Assert.Equal("Save", Crea("en")["Salva"]);
    }

    [Fact]
    public void TestoMancanteRestituisceItalianoEAccodaLaChiave()
    {
        var testo = Crea("en").Traduci("Elimina pagina");

        Assert.Equal("Elimina pagina", testo);
        Assert.Equal(1, _coda.InAttesa);
    }

    [Fact]
    public void ChiaveMancanteSiAccodaUnaSolaVolta()
    {
        var localizzatore = Crea("en");

        localizzatore.Traduci("Elimina pagina");
        localizzatore.Traduci("Elimina pagina");
        Crea("it").Traduci("Elimina pagina");

        Assert.Equal(1, _coda.InAttesa);
    }

    [Fact]
    public void LinguaMancanteUsaItalianoModificatoSePresente()
    {
        _cache.Imposta("it", "Salva", "Salva modifiche");

        Assert.Equal("Salva modifiche", Crea("en").Traduci("Salva"));
    }

    [Fact]
    public void ApplicaGliArgomentiConLaCulturaDellaLingua()
    {
        _cache.Imposta("it", "Totale {0}", "Totale {0}");
        _cache.Imposta("en", "Totale {0}", "Total {0}");

        Assert.Equal("Total 1234.5", Crea("en").Traduci("Totale {0}", 1234.5m));
        Assert.Equal("Totale 1234,5", Crea("it").Traduci("Totale {0}", 1234.5m));
    }

    [Fact]
    public void ArgomentiSuTestoMancanteVengonoApplicatiAllaChiave()
    {
        Assert.Equal("Ciao Anna", Crea("en").Traduci("Ciao {0}", "Anna"));
    }

    [Fact]
    public void TraduzioneConSegnapostoErratiNonRompeLaPagina()
    {
        _cache.Imposta("en", "Ciao {0}", "Hello {0} {1} {");

        var testo = Crea("en").Traduci("Ciao {0}", "Anna");

        Assert.Equal("Hello {0} {1} {", testo);
    }

    [Fact]
    public void TraduciInUsaLaLinguaEsplicita()
    {
        _cache.Imposta("en", "Salva", "Save");

        Assert.Equal("Save", Crea("it").TraduciIn("en", "Salva"));
        Assert.Equal("Salva", Crea("it").TraduciIn("it", "Salva"));
    }

    [Fact]
    public void ChiaviTroppoLungheNonVengonoAccodate()
    {
        var chiave = new string('x', 1001);

        Assert.Equal(chiave, Crea("en").Traduci(chiave));
        Assert.Equal(0, _coda.InAttesa);
    }

    [Fact]
    public async Task ElaboratoreScriveLeChiaviMancantiEAggiornaLaCache()
    {
        var archivio = new ArchivioFinto();
        var elaboratore = new ElaboratoreTestiMancanti(_coda, _cache, archivio, NullLogger<ElaboratoreTestiMancanti>.Instance);
        var localizzatore = Crea("en");

        localizzatore.Traduci("Nuova pagina");
        var scritte = await elaboratore.ElaboraAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, scritte);
        Assert.Equal(["Nuova pagina"], archivio.ChiaviRegistrate);
        Assert.Equal(0, _coda.InAttesa);
        Assert.True(_cache.TentaLeggi("en", "Nuova pagina", out var testo));
        Assert.Equal("Nuova pagina", testo);
    }

    [Fact]
    public async Task ErroreDiScritturaNonBloccaLeAltreChiaviEPermetteDiRiprovare()
    {
        var archivio = new ArchivioFinto { FalliscePer = "Rotta" };
        var elaboratore = new ElaboratoreTestiMancanti(_coda, _cache, archivio, NullLogger<ElaboratoreTestiMancanti>.Instance);
        var localizzatore = Crea("en");

        localizzatore.Traduci("Rotta");
        localizzatore.Traduci("Buona");
        var scritte = await elaboratore.ElaboraAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, scritte);
        Assert.Equal(0, _coda.InAttesa);

        // La chiave fallita si può riaccodare alla prossima richiesta.
        localizzatore.Traduci("Rotta");
        Assert.Equal(1, _coda.InAttesa);
    }

    private sealed class ArchivioFinto : IArchivioTestiStatici
    {
        public List<string> ChiaviRegistrate { get; } = [];

        public string? FalliscePer { get; set; }

        public Task<IReadOnlyList<InfoLingua>> CaricaLingueAsync(CancellationToken annullamento) =>
            Task.FromResult<IReadOnlyList<InfoLingua>>([]);

        public Task<IReadOnlyList<RigaTestoStatico>> CaricaTuttiAsync(CancellationToken annullamento) =>
            Task.FromResult<IReadOnlyList<RigaTestoStatico>>([]);

        public Task<IReadOnlyList<RigaTestoStatico>> RegistraChiaveAsync(string chiave, CancellationToken annullamento)
        {
            if (chiave == FalliscePer)
            {
                throw new InvalidOperationException("Errore simulato");
            }

            ChiaviRegistrate.Add(chiave);
            IReadOnlyList<RigaTestoStatico> righe =
            [
                new RigaTestoStatico("it", chiave, chiave, DaTradurre: false),
                new RigaTestoStatico("en", chiave, chiave, DaTradurre: true),
            ];
            return Task.FromResult(righe);
        }
    }
}
