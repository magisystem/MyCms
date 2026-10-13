using System.Net;
using System.Text;
using System.Text.Json;
using DvCms.Core.Builder;
using DvCms.DTO.Builder;
using Xunit;

namespace DvCms.UnitTests.Builder;

/// <summary>Markup del testo con formattazione (M5): grammatica chiusa, forma canonica idempotente, vettori condivisi con l'editor.</summary>
public sealed class TestoRiccoTest
{
    private sealed record Vettore(string Input, string? Canonico, string? Html, bool Errore);

    public static TheoryData<string> Vettori()
    {
        var dati = new TheoryData<string>();
        for (var i = 0; i < Carica().Count; i++)
        {
            dati.Add(i.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        return dati;
    }

    [Theory]
    [MemberData(nameof(Vettori))]
    public void IVettoriCondivisiConLEditorDannoLoStessoRisultato(string indice)
    {
        var vettore = Carica()[int.Parse(indice, System.Globalization.CultureInfo.InvariantCulture)];

        var analizzato = TestoRicco.TentaAnalizza(vettore.Input, out var blocchi);

        if (vettore.Errore)
        {
            Assert.False(analizzato);
            return;
        }

        Assert.True(analizzato);
        Assert.Equal(vettore.Canonico, TestoRicco.Scrivi(blocchi));
        Assert.Equal(vettore.Html, Html(blocchi));
        // La forma canonica è stabile: riscriverla non la cambia e dà lo stesso HTML.
        Assert.Equal(vettore.Canonico, TestoRicco.Canonico(vettore.Canonico));
        Assert.True(TestoRicco.TentaAnalizza(vettore.Canonico, out var seconda));
        Assert.Equal(vettore.Html, Html(seconda));
    }

    [Fact]
    public void IlRoundTripCasualeEIdempotenteENonProduceMaHtmlNonPrevisto()
    {
        var caso = new Random(12345);
        const string alfabeto = "ab *\\[]()-\n/x:https";
        var accettati = 0;
        for (var n = 0; n < 4000; n++)
        {
            var lunghezza = caso.Next(1, 24);
            var sb = new StringBuilder();
            for (var i = 0; i < lunghezza; i++)
            {
                sb.Append(alfabeto[caso.Next(alfabeto.Length)]);
            }

            if (!TestoRicco.TentaAnalizza(sb.ToString(), out var blocchi))
            {
                continue;
            }

            accettati++;
            var canonico = TestoRicco.Scrivi(blocchi);
            Assert.True(TestoRicco.TentaAnalizza(canonico, out var ripetuto), $"il canonico di '{sb}' non si rilegge");
            Assert.Equal(canonico, TestoRicco.Scrivi(ripetuto));
            Assert.Equal(Html(blocchi), Html(ripetuto));

            // Nell'HTML compaiono solo gli elementi fissi: ogni '<' è l'inizio di uno di essi.
            var html = Html(blocchi);
            foreach (var parte in html.Split('<').Skip(1))
            {
                Assert.Matches("^/?(p|br|strong|em|a href=\"[^\"<>]*\"|ul|li)>", parte);
            }
        }

        Assert.True(accettati > 300, "il generatore deve produrre abbastanza casi validi");
    }

    [Fact]
    public void ILimitiSonoRispettati()
    {
        Assert.False(TestoRicco.TentaAnalizza(new string('a', TestoRicco.LunghezzaMassima + 1), out _));
        Assert.False(TestoRicco.TentaAnalizza(string.Join("\n\n", Enumerable.Repeat("a", TestoRicco.BlocchiMassimi + 1)), out _));
        Assert.True(TestoRicco.TentaAnalizza(string.Join("\n\n", Enumerable.Repeat("a", TestoRicco.BlocchiMassimi)), out _));
        Assert.False(TestoRicco.TentaAnalizza(string.Join(' ', Enumerable.Repeat("[a](/x)", TestoRicco.CollegamentiMassimi + 1)), out _));
        Assert.False(TestoRicco.TentaAnalizza(string.Join('\n', Enumerable.Repeat("- v", TestoRicco.VociMassime + 1)), out _));
    }

    /// <summary>HTML di riferimento dei blocchi: lo stesso che produce l'anteprima dell'editor (e, con più cura, il renderer di Razor).</summary>
    internal static string Html(IReadOnlyList<BloccoTestoRicco> blocchi)
    {
        var sb = new StringBuilder();
        foreach (var blocco in blocchi)
        {
            switch (blocco)
            {
                case BloccoTestoRicco.Paragrafo p:
                    sb.Append("<p>").Append(HtmlInline(p.Contenuto)).Append("</p>");
                    break;
                case BloccoTestoRicco.Elenco e:
                    sb.Append("<ul>");
                    foreach (var voce in e.Voci)
                    {
                        sb.Append("<li>").Append(HtmlInline(voce)).Append("</li>");
                    }

                    sb.Append("</ul>");
                    break;
            }
        }

        return sb.ToString();
    }

    private static string HtmlInline(IReadOnlyList<InlineTestoRicco> contenuto)
    {
        var sb = new StringBuilder();
        foreach (var elemento in contenuto)
        {
            switch (elemento)
            {
                case InlineTestoRicco.ACapo:
                    sb.Append("<br>");
                    break;
                case InlineTestoRicco.Collegamento c:
                    sb.Append("<a href=\"").Append(WebUtility.HtmlEncode(c.Indirizzo)).Append("\">").Append(WebUtility.HtmlEncode(c.Etichetta)).Append("</a>");
                    break;
                case InlineTestoRicco.Testo t:
                    sb.Append(t.Grassetto ? "<strong>" : string.Empty).Append(t.Corsivo ? "<em>" : string.Empty)
                        .Append(WebUtility.HtmlEncode(t.Valore))
                        .Append(t.Corsivo ? "</em>" : string.Empty).Append(t.Grassetto ? "</strong>" : string.Empty);
                    break;
            }
        }

        return sb.ToString();
    }

    private static List<Vettore> Carica()
    {
        var cartella = new DirectoryInfo(AppContext.BaseDirectory);
        while (cartella is not null && !File.Exists(Path.Combine(cartella.FullName, "DvCms.sln")))
        {
            cartella = cartella.Parent;
        }

        var percorso = Path.Combine(cartella!.FullName, "frontend", "page-builder", "test", "vettori-testo-ricco.json");
        using var documento = JsonDocument.Parse(File.ReadAllText(percorso));
        return [.. documento.RootElement.EnumerateArray().Select(e => new Vettore(
            e.GetProperty("input").GetString()!,
            e.TryGetProperty("canonico", out var c) ? c.GetString() : null,
            e.TryGetProperty("html", out var h) ? h.GetString() : null,
            e.TryGetProperty("errore", out var er) && er.GetBoolean()))];
    }
}
