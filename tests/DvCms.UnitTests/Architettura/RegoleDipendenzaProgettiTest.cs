using System.Reflection;
using DvCms.Application;
using DvCms.Domain;
using DvCms.Infrastructure;
using Xunit;

namespace DvCms.UnitTests.Architettura;

/// <summary>
/// Verifica le regole di dipendenza di <c>docs/architecture/overview.md</c>:
/// Domain non dipende da framework web, EF Core o altri progetti;
/// Application dipende solo da Domain; Infrastructure non dipende da Web.
/// </summary>
public class RegoleDipendenzaProgettiTest
{
    private static readonly string[] _prefissiFrameworkVietatiNelDominio =
    [
        "Microsoft.AspNetCore",
        "Microsoft.EntityFrameworkCore",
        "Microsoft.Data.SqlClient",
    ];

    private static IEnumerable<string> Riferimenti(Assembly assembly) =>
        assembly.GetReferencedAssemblies()
            .Select(riferimento => riferimento.Name ?? string.Empty);

    [Fact]
    public void DominioNonDipendeDaFrameworkWebDatabaseOAltriProgetti()
    {
        var riferimenti = Riferimenti(RiferimentoDominio.Assembly).ToList();

        Assert.DoesNotContain(riferimenti, nome =>
            _prefissiFrameworkVietatiNelDominio.Any(prefisso => nome.StartsWith(prefisso, StringComparison.Ordinal)));
        Assert.DoesNotContain(riferimenti, nome =>
            nome is "DvCms.Application" or "DvCms.Infrastructure" or "DvCms.Web");
    }

    [Fact]
    public void ApplicazioneDipendeSoloDalDominio()
    {
        var riferimenti = Riferimenti(RiferimentoApplicazione.Assembly).ToList();

        Assert.DoesNotContain(riferimenti, nome => nome is "DvCms.Infrastructure" or "DvCms.Web");
        Assert.DoesNotContain(riferimenti, nome =>
            nome.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal));
    }

    [Fact]
    public void InfrastrutturaNonDipendeDalWeb()
    {
        var riferimenti = Riferimenti(RiferimentoInfrastruttura.Assembly).ToList();

        Assert.DoesNotContain(riferimenti, nome => nome == "DvCms.Web");
    }
}
