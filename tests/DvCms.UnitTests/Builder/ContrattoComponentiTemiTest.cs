using System.Text.RegularExpressions;
using DvCms.Application.Builder;
using Xunit;

namespace DvCms.UnitTests.Builder;

/// <summary>
/// Controlla che catalogo dei token (<see cref="TokenComponenti"/>), <c>componenti.css</c> e le skin dei temi restino allineati
/// (contratto componenti-temi, docs/architecture/componenti-e-temi.md).
/// </summary>
public partial class ContrattoComponentiTemiTest
{
    [Fact]
    public void IlCatalogoHaNomiValidiEUnici()
    {
        Assert.All(TokenComponenti.Tutti, token =>
        {
            Assert.Matches("^--dv-[a-z]+(-[a-z]+)*$", token.Nome);
            Assert.False(string.IsNullOrWhiteSpace(token.Descrizione));
            Assert.False(string.IsNullOrWhiteSpace(token.Predefinito));
        });
        Assert.Equal(TokenComponenti.Tutti.Count, TokenComponenti.Tutti.Select(t => t.Nome).Distinct().Count());
    }

    [Fact]
    public void ComponentiCssLeggeSoloTokenDelCatalogoConIlPredefinitoComeRiserva()
    {
        var css = LeggiCss("componenti.css");
        var usati = new HashSet<string>(StringComparer.Ordinal);

        foreach (var uso in UsiDiToken(css))
        {
            var token = TokenComponenti.Tutti.SingleOrDefault(t => t.Nome == uso.Nome);
            Assert.True(token is not null, $"componenti.css usa {uso.Nome}, che non è nel catalogo TokenComponenti.");
            Assert.True(uso.Riserva is not null, $"{uso.Nome} è usato senza valore di riserva in var().");
            Assert.True(
                token!.Predefinito == uso.Riserva,
                $"La riserva di {uso.Nome} è '{uso.Riserva}' ma il predefinito del catalogo è '{token.Predefinito}'.");
            usati.Add(uso.Nome);
        }

        var inutilizzati = TokenComponenti.Tutti.Select(t => t.Nome).Where(n => !usati.Contains(n)).ToList();
        Assert.True(inutilizzati.Count == 0, "Token del catalogo mai letti da componenti.css: " + string.Join(", ", inutilizzati));
    }

    [Fact]
    public void ComponentiCssNonDichiaraITokenPerCheLiImpostanoITemi()
    {
        var css = LeggiCss("componenti.css");
        Assert.DoesNotMatch(DichiarazioneToken(), css);
    }

    [Fact]
    public void LeSkinDeiTemiImpostanoSoloTokenDelCatalogoSulCorpoDelTema()
    {
        var radice = TrovaRadiceRepository();
        var cartella = Path.Combine(radice, "src", "DvCms.Web", "wwwroot", "css", "temi");
        var skin = Directory.GetFiles(cartella, "*-componenti.css");
        Assert.NotEmpty(skin);

        foreach (var file in skin)
        {
            var chiave = Path.GetFileName(file)[..^"-componenti.css".Length];
            var css = ToglieCommenti(File.ReadAllText(file));

            var selettori = SelettoreBlocco().Matches(css).Select(m => m.Groups[1].Value.Trim()).ToList();
            Assert.Equal([$"body.tema-{chiave}"], selettori);

            var dichiarazioni = DichiarazioneToken().Matches(css).Select(m => m.Groups[1].Value).ToList();
            Assert.NotEmpty(dichiarazioni);
            foreach (var nome in dichiarazioni)
            {
                Assert.True(TokenComponenti.Esiste(nome), $"{Path.GetFileName(file)} imposta {nome}, che non è nel catalogo.");
            }

            // Nessuna altra proprietà: una skin imposta soltanto token.
            var proprieta = Proprieta().Matches(css).Select(m => m.Groups[1].Value).Where(p => !p.StartsWith("--dv-", StringComparison.Ordinal));
            Assert.Empty(proprieta);
        }
    }

    private static string LeggiCss(string nome) =>
        ToglieCommenti(File.ReadAllText(Path.Combine(TrovaRadiceRepository(), "src", "DvCms.Web", "wwwroot", "css", nome)));

    private static string ToglieCommenti(string css) => Commento().Replace(css, string.Empty);

    /// <summary>Trova ogni <c>var(--dv-x[, riserva])</c>, con la riserva letta fino alla parentesi di chiusura corrispondente.</summary>
    private static IEnumerable<(string Nome, string? Riserva)> UsiDiToken(string css)
    {
        foreach (Match inizio in InizioVar().Matches(css))
        {
            var nome = inizio.Groups[1].Value;
            var posizione = inizio.Index + inizio.Length;
            if (css[posizione] == ')')
            {
                yield return (nome, null);
                continue;
            }

            Assert.Equal(',', css[posizione]);
            posizione++;
            var profondita = 0;
            var avvio = posizione;
            while (posizione < css.Length)
            {
                var c = css[posizione];
                if (c == '(')
                {
                    profondita++;
                }
                else if (c == ')')
                {
                    if (profondita == 0)
                    {
                        break;
                    }

                    profondita--;
                }

                posizione++;
            }

            yield return (nome, css[avvio..posizione].Trim());
        }
    }

    private static string TrovaRadiceRepository()
    {
        var cartella = new DirectoryInfo(AppContext.BaseDirectory);
        while (cartella is not null && !File.Exists(Path.Combine(cartella.FullName, "AGENTS.md")))
        {
            cartella = cartella.Parent;
        }

        return cartella?.FullName ?? throw new InvalidOperationException("Radice del repository non trovata.");
    }

    [GeneratedRegex(@"/\*.*?\*/", RegexOptions.Singleline)]
    private static partial Regex Commento();

    [GeneratedRegex(@"var\((--dv-[a-z-]+)")]
    private static partial Regex InizioVar();

    [GeneratedRegex(@"(?:^|[{;\s])(--dv-[a-z-]+)\s*:")]
    private static partial Regex DichiarazioneToken();

    [GeneratedRegex(@"([^{}]+)\{")]
    private static partial Regex SelettoreBlocco();

    [GeneratedRegex(@"(?:^|[{;\s])([a-z-]+)\s*:")]
    private static partial Regex Proprieta();
}
