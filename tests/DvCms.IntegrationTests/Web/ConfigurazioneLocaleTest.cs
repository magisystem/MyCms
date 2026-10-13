using DvCms.Web.Configurazione;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace DvCms.IntegrationTests.Web;

/// <summary>Il file <c>appsettings.Local.json</c> (non versionato) completa la configurazione senza battere variabili d'ambiente e riga di comando.</summary>
public class ConfigurazioneLocaleTest : IDisposable
{
    private readonly string _cartella = Path.Combine(Path.GetTempPath(), "dvcms-config-" + Guid.NewGuid().ToString("N"));

    public ConfigurazioneLocaleTest() => Directory.CreateDirectory(_cartella);

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("ProvaLocale__DaAmbiente", null);
        Directory.Delete(_cartella, recursive: true);
        GC.SuppressFinalize(this);
    }

    private ConfigurationManager Crea(string appsettings, string? locale, params string[] argomenti)
    {
        File.WriteAllText(Path.Combine(_cartella, "appsettings.json"), appsettings);
        if (locale is not null)
        {
            File.WriteAllText(Path.Combine(_cartella, ConfigurazioneLocale.NomeFile), locale);
        }

        var configurazione = new ConfigurationManager();
        configurazione.SetBasePath(_cartella);
        configurazione.AddJsonFile("appsettings.json", optional: false);
        configurazione.AddEnvironmentVariables();
        ConfigurazioneLocale.Aggiungi(configurazione, argomenti);
        return configurazione;
    }

    [Fact]
    public void IlFileLocaleDaLaStringaDiConnessioneEBatteAppsettings()
    {
        var configurazione = Crea(
            """{ "ConnectionStrings": { "Predefinita": "" }, "Altro": "base" }""",
            """{ "ConnectionStrings": { "Predefinita": "Server=locale;Database=DvCms" } }""");

        Assert.Equal("Server=locale;Database=DvCms", configurazione.GetConnectionString("Predefinita"));
        Assert.Equal("base", configurazione["Altro"]);
    }

    [Fact]
    public void SenzaFileLocaleNonCambiaNulla()
    {
        var configurazione = Crea("""{ "ConnectionStrings": { "Predefinita": "" }, "Altro": "base" }""", locale: null);

        Assert.True(string.IsNullOrEmpty(configurazione.GetConnectionString("Predefinita")));
        Assert.Equal("base", configurazione["Altro"]);
    }

    [Fact]
    public void LeVariabiliDAmbienteEdIlComandoRestanoPiuForti()
    {
        Environment.SetEnvironmentVariable("ProvaLocale__DaAmbiente", "ambiente");

        var configurazione = Crea(
            """{ "ProvaLocale": { "DaAmbiente": "base", "DaComando": "base" } }""",
            """{ "ProvaLocale": { "DaAmbiente": "locale", "DaComando": "locale" } }""",
            "--ProvaLocale:DaComando=comando");

        Assert.Equal("ambiente", configurazione["ProvaLocale:DaAmbiente"]);
        Assert.Equal("comando", configurazione["ProvaLocale:DaComando"]);
    }

    [Fact]
    public void IlFileLocaleNonViaggiaNelPacchettoPubblicato()
    {
        var progetto = File.ReadAllText(Path.Combine(CartellaRepository(), "src", "DvCms.Web", "DvCms.Web.csproj"));
        var gitignore = File.ReadAllText(Path.Combine(CartellaRepository(), ".gitignore"));

        Assert.Contains($"Update=\"{ConfigurazioneLocale.NomeFile}\"", progetto, StringComparison.Ordinal);
        Assert.Contains("CopyToPublishDirectory=\"Never\"", progetto, StringComparison.Ordinal);
        Assert.Contains(ConfigurazioneLocale.NomeFile, gitignore.Split('\n').Select(r => r.Trim()));
    }

    private static string CartellaRepository()
    {
        var cartella = new DirectoryInfo(AppContext.BaseDirectory);
        while (cartella is not null && !File.Exists(Path.Combine(cartella.FullName, "DvCms.sln")) && !File.Exists(Path.Combine(cartella.FullName, "DvCms.slnx")))
        {
            cartella = cartella.Parent;
        }

        return cartella?.FullName ?? throw new InvalidOperationException("Radice del repository non trovata.");
    }
}
