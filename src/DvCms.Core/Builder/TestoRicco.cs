using System.Text;
using DvCms.Core.Cms;
using DvCms.DTO.Builder;

namespace DvCms.Core.Builder;

/// <summary>
/// Testo con formattazione (grassetto, corsivo, collegamenti, elenchi) in un **markup a grammatica chiusa**, non in HTML (M5).
/// Il documento non contiene mai HTML del tenant: il server analizza il markup, lo riscrive in forma canonica e il renderer
/// ricostruisce gli elementi da un insieme fisso (<c>p</c>, <c>br</c>, <c>strong</c>, <c>em</c>, <c>a</c>, <c>ul</c>, <c>li</c>), con ogni
/// valore codificato. Non serve quindi alcun sanitizzatore HTML: ciò che non rientra nella grammatica è un errore, non viene "ripulito".
///
/// <para><b>Grammatica.</b> Blocchi separati da una riga vuota. Un blocco le cui righe iniziano tutte con <c>"- "</c> è un elenco, altrimenti è un
/// paragrafo (le righe interne sono a capo). Dentro i blocchi: <c>**grassetto**</c>, <c>*corsivo*</c> (marcatori da chiudere nello stesso
/// blocco o voce), <c>[etichetta](indirizzo)</c> con indirizzo ammesso (<c>/percorso</c> o <c>https://</c>, senza spazi né parentesi),
/// escape <c>\\</c>, <c>\*</c>, <c>\[</c>, <c>\]</c>, <c>\-</c>. Ogni altro carattere è testo; i caratteri di controllo sono vietati.
/// Le stesse regole sono implementate nell'editor per l'anteprima, verificate con gli stessi vettori di prova.</para>
/// </summary>
public static class TestoRicco
{
    public const int LunghezzaMassima = 5000;
    public const int BlocchiMassimi = 60;
    public const int VociMassime = 200;
    public const int CollegamentiMassimi = 50;

    /// <summary>Analizza il markup. Falso (senza dettagli: mai valori) se non rispetta la grammatica o i limiti.</summary>
    public static bool TentaAnalizza(string? markup, out IReadOnlyList<BloccoTestoRicco> blocchi)
    {
        blocchi = [];
        if (string.IsNullOrWhiteSpace(markup) || markup.Length > LunghezzaMassima)
        {
            return false;
        }

        var testo = markup.Replace("\r\n", "\n", StringComparison.Ordinal).Trim('\n', ' ');
        if (testo.Any(c => char.IsControl(c) && c != '\n'))
        {
            return false;
        }

        var risultato = new List<BloccoTestoRicco>();
        var voci = 0;
        var collegamenti = 0;
        foreach (var gruppo in DividiInBlocchi(testo))
        {
            if (gruppo.All(riga => riga.StartsWith("- ", StringComparison.Ordinal)))
            {
                var elencoVoci = new List<IReadOnlyList<InlineTestoRicco>>();
                foreach (var riga in gruppo)
                {
                    if (!TentaAnalizzaRiga(riga[2..], ref collegamenti, out var contenuto))
                    {
                        return false;
                    }

                    elencoVoci.Add(contenuto);
                }

                voci += elencoVoci.Count;
                risultato.Add(new BloccoTestoRicco.Elenco(elencoVoci));
            }
            else
            {
                var contenuto = new List<InlineTestoRicco>();
                var grassetto = false;
                var corsivo = false;
                for (var i = 0; i < gruppo.Count; i++)
                {
                    if (i > 0)
                    {
                        contenuto.Add(new InlineTestoRicco.ACapo());
                    }

                    if (!TentaAnalizzaInline(gruppo[i], contenuto, ref grassetto, ref corsivo, ref collegamenti))
                    {
                        return false;
                    }
                }

                if (grassetto || corsivo)
                {
                    return false;
                }

                risultato.Add(new BloccoTestoRicco.Paragrafo(Unisci(contenuto)));
            }

            if (risultato.Count > BlocchiMassimi || voci > VociMassime || collegamenti > CollegamentiMassimi)
            {
                return false;
            }
        }

        if (risultato.Count == 0)
        {
            return false;
        }

        blocchi = risultato;
        return true;
    }

    /// <summary>Forma canonica del markup: analisi e riscrittura (idempotente). Nullo se il markup non è valido.</summary>
    public static string? Canonico(string? markup) =>
        TentaAnalizza(markup, out var blocchi) ? Scrivi(blocchi) : null;

    public static string Scrivi(IReadOnlyList<BloccoTestoRicco> blocchi)
    {
        ArgumentNullException.ThrowIfNull(blocchi);

        var parti = new List<string>();
        foreach (var blocco in blocchi)
        {
            switch (blocco)
            {
                case BloccoTestoRicco.Elenco elenco:
                    parti.Add(string.Join('\n', elenco.Voci.Select(voce => "- " + ScriviInline(voce, primoDellaRiga: false))));
                    break;
                case BloccoTestoRicco.Paragrafo paragrafo:
                    parti.Add(ScriviInline(paragrafo.Contenuto, primoDellaRiga: true));
                    break;
            }
        }

        return string.Join("\n\n", parti);
    }

    private static IEnumerable<List<string>> DividiInBlocchi(string testo)
    {
        var corrente = new List<string>();
        foreach (var grezza in testo.Split('\n'))
        {
            var riga = grezza.Trim();
            if (riga.Length == 0)
            {
                if (corrente.Count > 0)
                {
                    yield return corrente;
                    corrente = [];
                }
            }
            else
            {
                corrente.Add(riga);
            }
        }

        if (corrente.Count > 0)
        {
            yield return corrente;
        }
    }

    private static bool TentaAnalizzaRiga(string riga, ref int collegamenti, out IReadOnlyList<InlineTestoRicco> contenuto)
    {
        var elenco = new List<InlineTestoRicco>();
        var grassetto = false;
        var corsivo = false;
        contenuto = elenco;
        if (!TentaAnalizzaInline(riga.Trim(), elenco, ref grassetto, ref corsivo, ref collegamenti) || grassetto || corsivo || elenco.Count == 0)
        {
            return false;
        }

        contenuto = Unisci(elenco);
        return true;
    }

    private static bool TentaAnalizzaInline(string riga, List<InlineTestoRicco> uscita, ref bool grassetto, ref bool corsivo, ref int collegamenti)
    {
        var corrente = new StringBuilder();
        var gras = grassetto;
        var cors = corsivo;

        void Chiudi()
        {
            if (corrente.Length > 0)
            {
                uscita.Add(new InlineTestoRicco.Testo(corrente.ToString(), gras, cors));
                corrente.Clear();
            }
        }

        var i = 0;
        while (i < riga.Length)
        {
            var c = riga[i];
            switch (c)
            {
                case '\\':
                    if (i + 1 >= riga.Length || "\\*[]-".IndexOf(riga[i + 1], StringComparison.Ordinal) < 0)
                    {
                        return false;
                    }

                    corrente.Append(riga[i + 1]);
                    i += 2;
                    break;
                case '*':
                    Chiudi();
                    if (i + 1 < riga.Length && riga[i + 1] == '*')
                    {
                        gras = !gras;
                        i += 2;
                    }
                    else
                    {
                        cors = !cors;
                        i++;
                    }

                    break;
                case '[':
                    if (!TentaAnalizzaCollegamento(riga, i, out var etichetta, out var indirizzo, out var fine))
                    {
                        return false;
                    }

                    Chiudi();
                    uscita.Add(new InlineTestoRicco.Collegamento(etichetta, indirizzo));
                    collegamenti++;
                    i = fine;
                    break;
                case ']':
                    return false;
                default:
                    corrente.Append(c);
                    i++;
                    break;
            }
        }

        Chiudi();
        grassetto = gras;
        corsivo = cors;
        return true;
    }

    /// <summary>L'etichetta è testo semplice (con escape); l'indirizzo non ha spazi né parentesi ed è ammesso dal sito.</summary>
    private static bool TentaAnalizzaCollegamento(string riga, int inizio, out string etichetta, out string indirizzo, out int fine)
    {
        etichetta = string.Empty;
        indirizzo = string.Empty;
        fine = inizio;
        var testo = new StringBuilder();
        var i = inizio + 1;
        while (i < riga.Length && riga[i] != ']')
        {
            if (riga[i] == '\\')
            {
                if (i + 1 >= riga.Length || "\\*[]-".IndexOf(riga[i + 1], StringComparison.Ordinal) < 0)
                {
                    return false;
                }

                testo.Append(riga[i + 1]);
                i += 2;
            }
            else if (riga[i] is '*' or '[')
            {
                return false;
            }
            else
            {
                testo.Append(riga[i]);
                i++;
            }
        }

        if (i + 1 >= riga.Length || riga[i] != ']' || riga[i + 1] != '(' || testo.Length == 0 || testo.ToString().Trim().Length == 0)
        {
            return false;
        }

        var chiusura = riga.IndexOf(')', i + 2);
        if (chiusura < 0)
        {
            return false;
        }

        var url = riga[(i + 2)..chiusura];
        if (url.Length == 0 || url.Length > RegistroModelliPagina.LunghezzaMassimaUrl || url.Contains('(', StringComparison.Ordinal) || !RegistroModelliPagina.UrlConsentito(url))
        {
            return false;
        }

        etichetta = testo.ToString();
        indirizzo = url;
        fine = chiusura + 1;
        return true;
    }

    private static List<InlineTestoRicco> Unisci(List<InlineTestoRicco> elementi)
    {
        var risultato = new List<InlineTestoRicco>();
        foreach (var elemento in elementi)
        {
            if (elemento is InlineTestoRicco.Testo testo && risultato.Count > 0 && risultato[^1] is InlineTestoRicco.Testo precedente
                && precedente.Grassetto == testo.Grassetto && precedente.Corsivo == testo.Corsivo)
            {
                risultato[^1] = precedente with { Valore = precedente.Valore + testo.Valore };
            }
            else
            {
                risultato.Add(elemento);
            }
        }

        return risultato;
    }

    private static string ScriviInline(IReadOnlyList<InlineTestoRicco> contenuto, bool primoDellaRiga)
    {
        var uscita = new StringBuilder();
        var inizioRiga = primoDellaRiga;
        foreach (var elemento in contenuto)
        {
            switch (elemento)
            {
                case InlineTestoRicco.ACapo:
                    uscita.Append('\n');
                    inizioRiga = primoDellaRiga;
                    break;
                case InlineTestoRicco.Collegamento collegamento:
                    uscita.Append('[').Append(Escape(collegamento.Etichetta, false)).Append("](").Append(collegamento.Indirizzo).Append(')');
                    inizioRiga = false;
                    break;
                case InlineTestoRicco.Testo testo:
                    uscita.Append(testo.Grassetto ? "**" : string.Empty).Append(testo.Corsivo ? "*" : string.Empty)
                        .Append(Escape(testo.Valore, inizioRiga && !testo.Grassetto && !testo.Corsivo))
                        .Append(testo.Corsivo ? "*" : string.Empty).Append(testo.Grassetto ? "**" : string.Empty);
                    inizioRiga = false;
                    break;
            }
        }

        return uscita.ToString();
    }

    /// <summary>Escape dei caratteri speciali; se il tratto apre una riga di paragrafo, "- " iniziale diventa "\\- " (altrimenti sarebbe un elenco).</summary>
    private static string Escape(string testo, bool apreLaRiga)
    {
        var uscita = new StringBuilder(testo.Length + 4);
        for (var i = 0; i < testo.Length; i++)
        {
            var c = testo[i];
            if (c is '\\' or '*' or '[' or ']')
            {
                uscita.Append('\\').Append(c);
            }
            else if (c == '-' && i == 0 && apreLaRiga && testo.Length > 1 && testo[1] == ' ')
            {
                uscita.Append("\\-");
            }
            else
            {
                uscita.Append(c);
            }
        }

        return uscita.ToString();
    }
}
