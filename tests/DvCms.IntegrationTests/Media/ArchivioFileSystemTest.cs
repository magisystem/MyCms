using DvCms.Core.Media;
using DvCms.Infrastructure.Media;
using Xunit;

namespace DvCms.IntegrationTests.Media;

/// <summary>Archivio su file system (D-003): chiavi validate, nessuna uscita dalla radice, oggetti immutabili.</summary>
public sealed class ArchivioFileSystemTest : IDisposable
{
    private readonly string _radice = Path.Combine(Path.GetTempPath(), "dvcms-archivio-" + Guid.NewGuid().ToString("N"));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ScriveLeggeEdEliminaUnOggetto()
    {
        var archivio = new ArchivioFileSystem(_radice);

        await archivio.ScriviAsync("tenant1/sito1/risorsa1", new MemoryStream([1, 2, 3, 4]), Ct);
        await using (var flusso = await archivio.ApriAsync("tenant1/sito1/risorsa1", Ct))
        {
            Assert.NotNull(flusso);
            using var memoria = new MemoryStream();
            await flusso.CopyToAsync(memoria, Ct);
            Assert.Equal([1, 2, 3, 4], memoria.ToArray());
        }

        Assert.True(await archivio.EliminaAsync("tenant1/sito1/risorsa1", Ct));
        Assert.Null(await archivio.ApriAsync("tenant1/sito1/risorsa1", Ct));
        Assert.False(await archivio.EliminaAsync("tenant1/sito1/risorsa1", Ct));
    }

    [Fact]
    public async Task UnOggettoNonSiSovrascriveENonLasciaFileTemporanei()
    {
        var archivio = new ArchivioFileSystem(_radice);
        await archivio.ScriviAsync("a/b/c", new MemoryStream([1]), Ct);

        await Assert.ThrowsAsync<InvalidOperationException>(() => archivio.ScriviAsync("a/b/c", new MemoryStream([2]), Ct));

        await using var flusso = await archivio.ApriAsync("a/b/c", Ct);
        Assert.Equal(1, flusso!.ReadByte());
        Assert.Empty(Directory.EnumerateFiles(_radice, "*.tmp", SearchOption.AllDirectories));
    }

    [Theory]
    [InlineData("")]
    [InlineData("../fuori")]
    [InlineData("a/../../fuori")]
    [InlineData("a/./b")]
    [InlineData("/assoluto")]
    [InlineData("C:/windows")]
    [InlineData("a\\b")]
    [InlineData("a//b")]
    [InlineData("a/b/")]
    [InlineData("nome.con.punti")]
    [InlineData("a/b%2e%2e/c")]
    [InlineData("a/b c")]
    [InlineData("a/b\0c")]
    [InlineData("1/2/3/4/5/6")]
    public async Task LeChiaviFuoriFormatoSonoRifiutateInOgniOperazione(string chiave)
    {
        var archivio = new ArchivioFileSystem(_radice);

        await Assert.ThrowsAsync<ArgumentException>(() => archivio.ScriviAsync(chiave, new MemoryStream([1]), Ct));
        await Assert.ThrowsAsync<ArgumentException>(() => archivio.ApriAsync(chiave, Ct));
        await Assert.ThrowsAsync<ArgumentException>(() => archivio.EliminaAsync(chiave, Ct));
        Assert.False(File.Exists(Path.Combine(Path.GetTempPath(), "fuori")));
    }

    [Fact]
    public async Task LaChiaveTroppoLungaERifiutata()
    {
        var archivio = new ArchivioFileSystem(_radice);

        await Assert.ThrowsAsync<ArgumentException>(() => archivio.ApriAsync(new string('a', 65), Ct));
        await Assert.ThrowsAsync<ArgumentException>(() => archivio.ApriAsync(string.Join('/', Enumerable.Repeat(new string('a', 64), 4)) + "/" + new string('b', 30), Ct));
    }

    public void Dispose()
    {
        if (Directory.Exists(_radice))
        {
            Directory.Delete(_radice, recursive: true);
        }
    }
}
