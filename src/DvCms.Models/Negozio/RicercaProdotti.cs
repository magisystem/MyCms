using System.Globalization;
using System.Text;

namespace DvCms.Models.Negozio;

/// <summary>Da quale parte del prodotto viene un termine: pesa in modo diverso nella pertinenza.</summary>
public enum CampoRicerca : byte
{
    Nome = 1,
    Codice = 2,
    Descrizione = 3,
}

/// <summary>
/// Una parola dell'indice di ricerca di un prodotto (M7.24). I termini sono già normalizzati (minuscole, senza accenti,
/// radice della parola): l'indice si interroga con gli stessi termini normalizzati, con la ricerca per prefisso.
/// I codici articolo non dipendono dalla lingua: usano <see cref="LinguaNeutra"/> (nessun vincolo verso le lingue).
/// </summary>
public class TermineRicercaProdotto
{
    /// <summary>Identificativo di lingua dei termini che valgono per ogni lingua (codici articolo).</summary>
    public static readonly Guid LinguaNeutra = Guid.Empty;

    public Guid IdTenant { get; set; }

    public Guid IdSito { get; set; }

    public Guid IdProdotto { get; set; }

    public Guid IdLingua { get; set; }

    public CampoRicerca Campo { get; set; }

    public string Termine { get; set; } = string.Empty;
}

/// <summary>
/// Analisi del testo per la ricerca nel catalogo: scompone in termini, toglie maiuscole, accenti e parole vuote (articoli,
/// preposizioni), riduce alla radice con una regola leggera per italiano e inglese. Le stesse regole valgono per l'indicizzazione e
/// per la ricerca, così "Magliette" trova "maglietta" e "perche" trova "perché". Nessuna dipendenza esterna: niente servizi di ricerca
/// da installare (il Full-Text di SQL Server richiede una funzione opzionale non presente in tutte le installazioni).
/// </summary>
public static class AnalizzatoreRicerca
{
    public const int LunghezzaMinima = 2;
    public const int LunghezzaMassima = 50;
    public const int MassimoTerminiRicerca = 5;
    public const int MassimoTerminiDescrizione = 300;

    private static readonly HashSet<string> _paroleVuoteItaliano = new(StringComparer.Ordinal)
    {
        "il", "lo", "la", "le", "li", "gli", "un", "uno", "una", "di", "da", "in", "con", "su", "per", "tra", "fra",
        "del", "dello", "della", "dei", "degli", "delle", "al", "allo", "alla", "ai", "agli", "alle", "nel", "nello",
        "nella", "nei", "negli", "nelle", "dal", "dalla", "dai", "dalle", "sul", "sulla", "sui", "sulle", "ed", "che", "non",
    };

    private static readonly HashSet<string> _paroleVuoteInglese = new(StringComparer.Ordinal)
    {
        "the", "an", "of", "and", "or", "to", "in", "on", "for", "with", "at", "by", "is", "it", "as",
    };

    /// <summary>Minuscole e senza accenti (decomposizione Unicode senza i segni combinati).</summary>
    public static string Normalizza(string? testo)
    {
        if (string.IsNullOrEmpty(testo))
        {
            return string.Empty;
        }

        var scomposto = testo.Normalize(NormalizationForm.FormD);
        var risultato = new StringBuilder(scomposto.Length);
        foreach (var carattere in scomposto)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(carattere) != UnicodeCategory.NonSpacingMark)
            {
                risultato.Append(char.ToLowerInvariant(carattere));
            }
        }

        return risultato.ToString();
    }

    /// <summary>
    /// I termini di un testo libero (nome o descrizione) nella lingua data, distinti, di lunghezza ammessa, al più
    /// <paramref name="massimo"/>. Una parola composta con trattino o trattino basso (<c>T-shirt</c>) dà le sue parti e, in più, la forma
    /// unita (<c>tshirt</c>), che è quella che una ricerca scritta con il trattino cerca.
    /// </summary>
    public static IReadOnlyList<string> Termini(string? testo, string? codiceLingua, int massimo = int.MaxValue) =>
        Scomponi(testo, codiceLingua, togliParoleVuote: true, ricerca: false, massimo);

    /// <summary>
    /// I termini di una ricerca: come <see cref="Termini"/>, ma una parola scritta con il trattino (<c>lam-b</c>, <c>TS-M-001</c>) vale
    /// solo nella forma unita (<c>lamb</c>, <c>tsm001</c>), perché è così che la si cerca fra i codici articolo. Se la ricerca è fatta di
    /// sole parole vuote le si tengono comunque (altrimenti "il" non troverebbe nulla).
    /// </summary>
    public static IReadOnlyList<string> TerminiRicerca(string? testo, string? codiceLingua)
    {
        var termini = Scomponi(testo, codiceLingua, togliParoleVuote: true, ricerca: true, MassimoTerminiRicerca);
        return termini.Count > 0 ? termini : Scomponi(testo, codiceLingua, togliParoleVuote: false, ricerca: true, MassimoTerminiRicerca);
    }

    /// <summary>
    /// I termini di un codice articolo: le sue parti (<c>TS-M-001</c> → <c>ts</c>, <c>m</c>, <c>001</c>) e il codice senza separatori
    /// (<c>tsm001</c>). Niente radice né parole vuote: un codice non è una parola.
    /// </summary>
    public static IReadOnlyList<string> TerminiCodice(string? codice)
    {
        var parti = Dividi(Normalizza(codice)).ToList();
        var minima = parti.Count > 1 ? 1 : LunghezzaMinima;
        var termini = parti.Where(p => p.Length >= minima).ToList();
        var unito = string.Concat(parti);
        if (unito.Length >= LunghezzaMinima)
        {
            termini.Add(unito);
        }

        return [.. termini.Select(p => p.Length > LunghezzaMassima ? p[..LunghezzaMassima] : p).Distinct(StringComparer.Ordinal)];
    }

    private static List<string> Scomponi(string? testo, string? codiceLingua, bool togliParoleVuote, bool ricerca, int massimo)
    {
        var lingua = LinguaBase(codiceLingua);
        var risultato = new List<string>();
        var visti = new HashSet<string>(StringComparer.Ordinal);

        bool Aggiungi(string termine)
        {
            if (termine.Length > LunghezzaMassima)
            {
                termine = termine[..LunghezzaMassima];
            }

            if (termine.Length >= LunghezzaMinima && visti.Add(termine))
            {
                risultato.Add(termine);
            }

            return risultato.Count >= massimo;
        }

        foreach (var grezzo in (testo ?? string.Empty).Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var normalizzato = Normalizza(grezzo);
            var parti = Dividi(normalizzato).ToList();
            var composta = parti.Count > 1 && (normalizzato.Contains('-', StringComparison.Ordinal) || normalizzato.Contains('_', StringComparison.Ordinal));
            if (composta && Aggiungi(string.Concat(parti)) && risultato.Count >= massimo)
            {
                break;
            }

            if (composta && ricerca)
            {
                continue;
            }

            foreach (var parola in parti)
            {
                if (parola.Length < LunghezzaMinima || (togliParoleVuote && EParolaVuota(parola, lingua)))
                {
                    continue;
                }

                if (Aggiungi(Radice(parola, lingua)))
                {
                    return risultato;
                }
            }

            if (risultato.Count >= massimo)
            {
                break;
            }
        }

        return risultato;
    }

    private static IEnumerable<string> Dividi(string normalizzato)
    {
        var corrente = new StringBuilder();
        foreach (var carattere in normalizzato)
        {
            if (char.IsLetterOrDigit(carattere))
            {
                corrente.Append(carattere);
            }
            else if (corrente.Length > 0)
            {
                yield return corrente.ToString();
                corrente.Clear();
            }
        }

        if (corrente.Length > 0)
        {
            yield return corrente.ToString();
        }
    }

    private static string LinguaBase(string? codice)
    {
        var testo = (codice ?? string.Empty).Trim().ToLowerInvariant();
        var separatore = testo.IndexOf('-', StringComparison.Ordinal);
        return separatore > 0 ? testo[..separatore] : testo;
    }

    private static bool EParolaVuota(string parola, string lingua) => lingua switch
    {
        "it" => _paroleVuoteItaliano.Contains(parola),
        "en" => _paroleVuoteInglese.Contains(parola),
        _ => false,
    };

    /// <summary>Radice leggera: tolta la vocale finale in italiano (magliette/maglietta → magliett), il plurale in inglese (shirts → shirt).</summary>
    private static string Radice(string parola, string lingua)
    {
        if (parola.All(char.IsDigit))
        {
            return parola;
        }

        switch (lingua)
        {
            case "it":
                return parola.Length > 4 && parola[^1] is 'a' or 'e' or 'i' or 'o' ? parola[..^1] : parola;
            case "en":
                if (parola.Length > 4 && parola.EndsWith("ies", StringComparison.Ordinal))
                {
                    return parola[..^3] + "y";
                }

                if (parola.Length > 4 && parola.EndsWith("es", StringComparison.Ordinal) && parola[^3] is 's' or 'x' or 'z' or 'h')
                {
                    return parola[..^2];
                }

                return parola.Length > 3 && parola.EndsWith('s') && !parola.EndsWith("ss", StringComparison.Ordinal) ? parola[..^1] : parola;
            default:
                return parola;
        }
    }
}
