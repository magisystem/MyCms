using System.Xml.Linq;
using DvCms.Extender;
using DvCms.Models;
using Xunit;

namespace DvCms.UnitTests.Architettura;

/// <summary>
/// Fa rispettare le regole di dipendenza dell'ADR-0024: un progetto referenzia solo quelli "sotto" di lui, i tre progetti di servizi non
/// si referenziano fra loro, il livello dei modelli non usa framework web né EF Core. Si legge dai file di progetto, non dagli assembly,
/// perché il compilatore toglie i riferimenti non usati.
/// </summary>
public class RegoleDipendenzaProgettiTest
{
    private static readonly string[] _basso = ["DvCms.Extender", "DvCms.Models", "DvCms.DTO", "DvCms.Contracts", "DvCms.Core"];

    private static readonly string[] _prefissiFrameworkVietati =
    [
        "Microsoft.AspNetCore",
        "Microsoft.EntityFrameworkCore",
        "Microsoft.Data.SqlClient",
        "Npgsql",
    ];

    /// <summary>Riferimenti diretti consentiti per ogni progetto (oltre a sé stesso). <c>Web</c> e i test non hanno limiti.</summary>
    private static Dictionary<string, string[]> Consentiti { get; } = new()
    {
        ["DvCms.Extender"] = [],
        ["DvCms.Models"] = ["DvCms.Extender"],
        ["DvCms.DTO"] = ["DvCms.Extender", "DvCms.Models"],
        ["DvCms.Contracts"] = ["DvCms.Extender", "DvCms.Models", "DvCms.DTO"],
        ["DvCms.Core"] = ["DvCms.Extender", "DvCms.Models", "DvCms.DTO", "DvCms.Contracts"],
        ["DvCms.Data"] = [.. _basso],
        ["DvCms.SqlServer"] = [.. _basso, "DvCms.Data"],
        ["DvCms.PostgreSql"] = [],
        ["DvCms.Emails"] = [.. _basso, "DvCms.Data"],
        ["DvCms.Services"] = [.. _basso, "DvCms.Data"],
        ["DvCms.Services.Cms"] = [.. _basso, "DvCms.Data"],
        ["DvCms.Services.Negozio"] = [.. _basso, "DvCms.Data"],
    };

    private static string Radice
    {
        get
        {
            var cartella = new DirectoryInfo(AppContext.BaseDirectory);
            while (cartella is not null && !File.Exists(Path.Combine(cartella.FullName, "DvCms.sln")))
            {
                cartella = cartella.Parent;
            }

            return cartella?.FullName ?? throw new InvalidOperationException("Radice del repository non trovata.");
        }
    }

    private static IEnumerable<(string Nome, XDocument Progetto)> ProgettiSorgente() =>
        Directory.EnumerateFiles(Path.Combine(Radice, "src"), "*.csproj", SearchOption.AllDirectories)
            .Where(percorso => !percorso.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(percorso => (Path.GetFileNameWithoutExtension(percorso), XDocument.Load(percorso)));

    private static IEnumerable<string> RiferimentiDiProgetto(XDocument progetto) =>
        progetto.Descendants("ProjectReference")
            .Select(riferimento => Path.GetFileNameWithoutExtension((string?)riferimento.Attribute("Include") ?? string.Empty).Replace('\\', '/'))
            .Select(nome => nome[(nome.LastIndexOf('/') + 1)..]);

    [Fact]
    public void OgniProgettoReferenziaSoloQuelliConsentiti()
    {
        foreach (var (nome, progetto) in ProgettiSorgente().Where(p => Consentiti.ContainsKey(p.Nome)))
        {
            var vietati = RiferimentiDiProgetto(progetto).Where(r => !Consentiti[nome].Contains(r)).ToList();
            Assert.True(vietati.Count == 0, $"{nome} non può referenziare: {string.Join(", ", vietati)}");
        }
    }

    [Fact]
    public void ITreProgettiDiServiziNonSiReferenzianoFraLoroNeReferenzianoLaUiOIProvider()
    {
        foreach (var (nome, progetto) in ProgettiSorgente().Where(p => p.Nome.StartsWith("DvCms.Services", StringComparison.Ordinal)))
        {
            var vietati = RiferimentiDiProgetto(progetto)
                .Where(r => r.StartsWith("DvCms.Services", StringComparison.Ordinal) || r is "DvCms.Web" or "DvCms.SqlServer" or "DvCms.PostgreSql" or "DvCms.Emails")
                .ToList();
            Assert.True(vietati.Count == 0, $"{nome} non può referenziare: {string.Join(", ", vietati)}");
        }
    }

    [Fact]
    public void ILivelliBassiNonUsanoFrameworkWebDatabaseOProvider()
    {
        foreach (var (nome, progetto) in ProgettiSorgente().Where(p => p.Nome is "DvCms.Extender" or "DvCms.Models" or "DvCms.DTO" or "DvCms.Contracts"))
        {
            var pacchetti = progetto.Descendants("PackageReference").Select(p => (string?)p.Attribute("Include") ?? string.Empty);
            Assert.DoesNotContain(pacchetti, pacchetto => _prefissiFrameworkVietati.Any(prefisso => pacchetto.StartsWith(prefisso, StringComparison.Ordinal)));
        }
    }

    [Fact]
    public void ModelliEExtenderNonDipendonoDaFrameworkWebDatabaseOAltriProgetti()
    {
        foreach (var assembly in new[] { RiferimentoModelli.Assembly, typeof(Identificativi).Assembly })
        {
            var riferimenti = assembly.GetReferencedAssemblies().Select(r => r.Name ?? string.Empty).ToList();

            Assert.DoesNotContain(riferimenti, nome => _prefissiFrameworkVietati.Any(prefisso => nome.StartsWith(prefisso, StringComparison.Ordinal)));
            Assert.DoesNotContain(riferimenti, nome => nome.StartsWith("DvCms.", StringComparison.Ordinal) && nome != "DvCms.Extender");
        }
    }

    [Fact]
    public void IlSegnapostoPostgreSqlRestaVuoto()
    {
        var cartella = Path.Combine(Radice, "src", "DvCms.PostgreSql");
        if (!Directory.Exists(cartella))
        {
            return;
        }

        var codice = Directory.EnumerateFiles(cartella, "*.cs", SearchOption.AllDirectories).Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal));
        Assert.Empty(codice);
        var progetto = XDocument.Load(Path.Combine(cartella, "DvCms.PostgreSql.csproj"));
        Assert.Empty(progetto.Descendants("PackageReference"));
    }
}
