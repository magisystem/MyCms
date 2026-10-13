using System.Text.RegularExpressions;
using Xunit;

namespace DvCms.UnitTests.Convenzioni;

/// <summary>
/// Controlli sul codice sorgente delle convenzioni di <c>docs/development/convenzioni-codice.md</c>.
/// </summary>
public partial class ConvenzioniCodiceTest
{
    [Fact]
    public void IlCodiceNonUsaLOrologioDiSistemaDirettamente()
    {
        var radice = TrovaRadiceRepository();
        var violazioni = new List<string>();

        foreach (var file in Directory.EnumerateFiles(Path.Combine(radice, "src"), "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                continue;
            }

            var righe = File.ReadAllLines(file);
            for (var indice = 0; indice < righe.Length; indice++)
            {
                if (OrologioDiSistema().IsMatch(righe[indice]))
                {
                    violazioni.Add($"{Path.GetRelativePath(radice, file)}:{indice + 1}");
                }
            }
        }

        Assert.True(
            violazioni.Count == 0,
            "Usare TimeProvider (GetUtcNow) invece di DateTime/DateTimeOffset Now, UtcNow e Today. Occorrenze: "
            + string.Join(", ", violazioni));
    }

    [Fact]
    public void IlLocalizzatoreRiceveSoloTestiLetterali()
    {
        var radice = TrovaRadiceRepository();
        var violazioni = new List<string>();
        var separatore = Path.DirectorySeparatorChar;

        foreach (var file in Directory.EnumerateFiles(Path.Combine(radice, "src"), "*.*", SearchOption.AllDirectories)
            .Where(percorso => percorso.EndsWith(".cs", StringComparison.Ordinal) || percorso.EndsWith(".cshtml", StringComparison.Ordinal)))
        {
            var relativo = Path.GetRelativePath(radice, file);
            if (relativo.Contains($"{separatore}obj{separatore}", StringComparison.Ordinal)
                || relativo.Contains($"{separatore}bin{separatore}", StringComparison.Ordinal)
                || relativo.Contains($"{separatore}wwwroot{separatore}", StringComparison.Ordinal)
                || relativo.Contains($"DvCms.Core{separatore}Localizzazione", StringComparison.Ordinal)
                || relativo.Contains($"DvCms.Contracts{separatore}Localizzazione", StringComparison.Ordinal))
            {
                continue;
            }

            var righe = File.ReadAllLines(file);
            for (var indice = 0; indice < righe.Length; indice++)
            {
                if (ChiamataNonLetterale().IsMatch(righe[indice]))
                {
                    violazioni.Add($"{relativo}:{indice + 1}");
                }
            }
        }

        Assert.True(
            violazioni.Count == 0,
            "Al localizzatore (_L[...], Traduci, TraduciIn) si passa solo un testo letterale tra virgolette, mai una variabile o una stringa interpolata. Occorrenze: "
            + string.Join(", ", violazioni));
    }

    [Fact]
    public void SottoWwwrootLibCiSonoSoloTablerTablerIconsEIlBundleDelPageBuilder()
    {
        var radice = TrovaRadiceRepository();
        var cartellaLib = Path.Combine(radice, "src", "DvCms.Web", "wwwroot", "lib");

        var presenti = Directory.EnumerateDirectories(cartellaLib)
            .Select(cartella => Path.GetFileName(cartella))
            .Order(StringComparer.Ordinal)
            .ToList();

        // ADR-0013: tabler-emails, tabler-illustrations e tabler-avatars non vanno in wwwroot.
        // ADR-0007: page-builder è il bundle compilato dell'editor, caricato solo dalla sua pagina (sorgenti in frontend/page-builder).
        Assert.Equal(["page-builder", "tabler", "tabler-icons"], presenti);
    }

    private static string TrovaRadiceRepository()
    {
        var cartella = new DirectoryInfo(AppContext.BaseDirectory);
        while (cartella is not null && !File.Exists(Path.Combine(cartella.FullName, "DvCms.sln")))
        {
            cartella = cartella.Parent;
        }

        return cartella?.FullName ?? throw new InvalidOperationException("Radice del repository (DvCms.sln) non trovata.");
    }

    // Chiamate al localizzatore il cui testo non è un letterale tra virgolette.
    [GeneratedRegex("""(_L\[(?>\s*)(?!")|\.Traduci\((?>\s*)(?!")|\.TraduciIn\((?>\s*)[^,()]+,(?>\s*)(?!"))""", RegexOptions.CultureInvariant)]
    private static partial Regex ChiamataNonLetterale();

    [GeneratedRegex(@"\bDateTime(Offset)?\.(Now|UtcNow|Today)\b", RegexOptions.CultureInvariant)]
    private static partial Regex OrologioDiSistema();
}
