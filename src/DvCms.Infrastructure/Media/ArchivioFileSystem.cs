using System.Text.RegularExpressions;
using DvCms.Application.Media;

namespace DvCms.Infrastructure.Media;

/// <summary>
/// Archivio su file system locale (sviluppo e installazioni semplici). La chiave ha il formato fisso
/// <c>segmento/segmento/…</c> con segmenti di lettere, cifre, <c>-</c> e <c>_</c>: niente punti, quindi niente <c>..</c>, e il
/// percorso risolto deve comunque restare dentro la radice. Le scritture sono atomiche (file temporaneo e spostamento).
/// </summary>
public sealed partial class ArchivioFileSystem : IArchivioOggetti
{
    private readonly string _radice;

    public ArchivioFileSystem(string radice)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(radice);
        _radice = Path.TrimEndingDirectorySeparator(Path.GetFullPath(radice));
    }

    public async Task ScriviAsync(string chiave, Stream contenuto, CancellationToken annullamento)
    {
        ArgumentNullException.ThrowIfNull(contenuto);
        var percorso = Percorso(chiave);
        Directory.CreateDirectory(Path.GetDirectoryName(percorso)!);
        if (File.Exists(percorso))
        {
            throw new InvalidOperationException("Un oggetto con questa chiave esiste già.");
        }

        var temporaneo = percorso + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var uscita = new FileStream(temporaneo, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                await contenuto.CopyToAsync(uscita, annullamento);
            }

            // Senza sovrascrittura: se nel frattempo qualcuno ha scritto la stessa chiave, l'errore è esplicito.
            File.Move(temporaneo, percorso, overwrite: false);
        }
        catch (IOException) when (File.Exists(percorso))
        {
            throw new InvalidOperationException("Un oggetto con questa chiave esiste già.");
        }
        finally
        {
            if (File.Exists(temporaneo))
            {
                File.Delete(temporaneo);
            }
        }
    }

    public Task<Stream?> ApriAsync(string chiave, CancellationToken annullamento)
    {
        var percorso = Percorso(chiave);
        Stream? flusso = File.Exists(percorso)
            ? new FileStream(percorso, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true)
            : null;
        return Task.FromResult(flusso);
    }

    public Task<bool> EliminaAsync(string chiave, CancellationToken annullamento)
    {
        var percorso = Percorso(chiave);
        if (!File.Exists(percorso))
        {
            return Task.FromResult(false);
        }

        File.Delete(percorso);
        return Task.FromResult(true);
    }

    public async IAsyncEnumerable<string> ElencaChiaviAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken annullamento)
    {
        if (!Directory.Exists(_radice))
        {
            yield break;
        }

        foreach (var file in Directory.EnumerateFiles(_radice, "*", SearchOption.AllDirectories))
        {
            annullamento.ThrowIfCancellationRequested();
            var chiave = Path.GetRelativePath(_radice, file).Replace(Path.DirectorySeparatorChar, '/');

            // I file temporanei di una scrittura interrotta non sono oggetti.
            if (!chiave.EndsWith(".tmp", StringComparison.Ordinal) && FormatoChiave().IsMatch(chiave))
            {
                yield return chiave;
            }
        }

        await Task.CompletedTask;
    }

    private string Percorso(string chiave)
    {
        if (string.IsNullOrEmpty(chiave) || chiave.Length > 200 || !FormatoChiave().IsMatch(chiave))
        {
            throw new ArgumentException("Chiave dell'oggetto non valida.", nameof(chiave));
        }

        var percorso = Path.GetFullPath(Path.Combine(_radice, chiave.Replace('/', Path.DirectorySeparatorChar)));
        if (!percorso.StartsWith(_radice + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new ArgumentException("Chiave dell'oggetto non valida.", nameof(chiave));
        }

        return percorso;
    }

    [GeneratedRegex("^[A-Za-z0-9_-]{1,64}(/[A-Za-z0-9_-]{1,64}){0,4}$", RegexOptions.CultureInvariant)]
    private static partial Regex FormatoChiave();
}
