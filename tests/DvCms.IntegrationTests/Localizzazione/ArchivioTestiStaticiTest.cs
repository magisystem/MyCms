using DvCms.Application.Localizzazione;
using DvCms.Domain.Localizzazione;
using DvCms.Infrastructure.Persistenza;
using DvCms.IntegrationTests.Supporto;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DvCms.IntegrationTests.Localizzazione;

public class ArchivioTestiStaticiTest
{
    [Fact]
    public async Task MigrazioneInserisceLeLingueIniziali()
    {
        await using var database = await DatabaseDiProva.CreaAsync(TestContext.Current.CancellationToken);
        await using var fornitore = await ServiziProva.CreaConMigrazioniAsync(database.Connessione, annullamento: TestContext.Current.CancellationToken);
        var archivio = fornitore.GetRequiredService<IArchivioTestiStatici>();

        var lingue = await archivio.CaricaLingueAsync(TestContext.Current.CancellationToken);

        Assert.Collection(
            lingue.OrderBy(lingua => lingua.Codice),
            inglese =>
            {
                Assert.Equal("en", inglese.Codice);
                Assert.Equal("en-GB", inglese.Cultura);
                Assert.False(inglese.Predefinita);
                Assert.True(inglese.Attiva);
            },
            italiano =>
            {
                Assert.Equal("it", italiano.Codice);
                Assert.Equal("it-IT", italiano.Cultura);
                Assert.True(italiano.Predefinita);
                Assert.True(italiano.Attiva);
            });
    }

    [Fact]
    public async Task RegistraChiaveCreaUnaRigaPerOgniLinguaAttiva()
    {
        await using var database = await DatabaseDiProva.CreaAsync(TestContext.Current.CancellationToken);
        await using var fornitore = await ServiziProva.CreaConMigrazioniAsync(database.Connessione, annullamento: TestContext.Current.CancellationToken);
        var archivio = fornitore.GetRequiredService<IArchivioTestiStatici>();

        var righe = await archivio.RegistraChiaveAsync("Salva le modifiche", TestContext.Current.CancellationToken);

        Assert.Equal(2, righe.Count);
        var italiano = Assert.Single(righe, riga => riga.CodiceLingua == "it");
        var inglese = Assert.Single(righe, riga => riga.CodiceLingua == "en");
        Assert.Equal("Salva le modifiche", italiano.Testo);
        Assert.False(italiano.DaTradurre);
        Assert.Equal("Salva le modifiche", inglese.Testo);
        Assert.True(inglese.DaTradurre);
    }

    [Fact]
    public async Task RegistraChiaveNonSovrascriveTestiGiaTradotti()
    {
        await using var database = await DatabaseDiProva.CreaAsync(TestContext.Current.CancellationToken);
        await using var fornitore = await ServiziProva.CreaConMigrazioniAsync(database.Connessione, annullamento: TestContext.Current.CancellationToken);
        var archivio = fornitore.GetRequiredService<IArchivioTestiStatici>();
        await archivio.RegistraChiaveAsync("Elimina", TestContext.Current.CancellationToken);

        await using (var ambito = fornitore.CreateAsyncScope())
        {
            var contesto = ambito.ServiceProvider.GetRequiredService<ContestoDatabase>();
            var inglese = await contesto.TestiStatici.SingleAsync(
                testo => testo.IdLingua == Lingua.IdInglese,
                TestContext.Current.CancellationToken);
            inglese.Testo = "Delete";
            inglese.DaTradurre = false;
            await contesto.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var righe = await archivio.RegistraChiaveAsync("Elimina", TestContext.Current.CancellationToken);

        Assert.Equal("Delete", Assert.Single(righe, riga => riga.CodiceLingua == "en").Testo);
        Assert.Equal(2, await database.LeggiScalareAsync<int>("SELECT COUNT(*) FROM TestiStatici", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RegistrazioneConcorrenteDellaStessaChiaveNonGeneraErroriNeDuplicati()
    {
        await using var database = await DatabaseDiProva.CreaAsync(TestContext.Current.CancellationToken);
        await using var fornitore = await ServiziProva.CreaConMigrazioniAsync(database.Connessione, annullamento: TestContext.Current.CancellationToken);
        var archivio = fornitore.GetRequiredService<IArchivioTestiStatici>();

        var attivita = Enumerable.Range(0, 12)
            .Select(_ => Task.Run(() => archivio.RegistraChiaveAsync("Chiave concorrente", TestContext.Current.CancellationToken), TestContext.Current.CancellationToken))
            .ToArray();
        var risultati = await Task.WhenAll(attivita);

        Assert.All(risultati, righe => Assert.Equal(2, righe.Count));
        Assert.Equal(2, await database.LeggiScalareAsync<int>("SELECT COUNT(*) FROM TestiStatici", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task IndiceUnivocoImpedisceRigheDuplicate()
    {
        await using var database = await DatabaseDiProva.CreaAsync(TestContext.Current.CancellationToken);
        await using var fornitore = await ServiziProva.CreaConMigrazioniAsync(database.Connessione, annullamento: TestContext.Current.CancellationToken);
        var archivio = fornitore.GetRequiredService<IArchivioTestiStatici>();
        await archivio.RegistraChiaveAsync("Duplicato", TestContext.Current.CancellationToken);

        await using var ambito = fornitore.CreateAsyncScope();
        var contesto = ambito.ServiceProvider.GetRequiredService<ContestoDatabase>();
        contesto.TestiStatici.Add(new TestoStatico
        {
            Id = Guid.NewGuid(),
            IdLingua = Lingua.IdItaliano,
            Chiave = "Duplicato",
            HashChiave = Infrastructure.Localizzazione.ArchivioTestiStatici.CalcolaHash("Duplicato"),
            Testo = "Duplicato",
            AggiornatoIlUtc = DateTime.UtcNow,
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => contesto.SaveChangesAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ElaboratoreScriveLeChiaviAccodateDalLocalizzatore()
    {
        await using var database = await DatabaseDiProva.CreaAsync(TestContext.Current.CancellationToken);
        await using var fornitore = await ServiziProva.CreaConMigrazioniAsync(database.Connessione, annullamento: TestContext.Current.CancellationToken);
        var elaboratore = fornitore.GetRequiredService<ElaboratoreTestiMancanti>();
        await elaboratore.CaricaCacheAsync(TestContext.Current.CancellationToken);
        await using var ambito = fornitore.CreateAsyncScope();
        var localizzatore = ambito.ServiceProvider.GetRequiredService<ILocalizzatore>();

        var testoImmediato = localizzatore.TraduciIn("en", "Pagina non trovata");
        await elaboratore.ElaboraAsync(TestContext.Current.CancellationToken);

        Assert.Equal("Pagina non trovata", testoImmediato);
        Assert.Equal(2, await database.LeggiScalareAsync<int>("SELECT COUNT(*) FROM TestiStatici", TestContext.Current.CancellationToken));
        Assert.Equal(
            1,
            await database.LeggiScalareAsync<int>(
                "SELECT COUNT(*) FROM TestiStatici WHERE DaTradurre = 1",
                TestContext.Current.CancellationToken));
        Assert.Equal("Pagina non trovata", localizzatore.TraduciIn("en", "Pagina non trovata"));
    }
}
