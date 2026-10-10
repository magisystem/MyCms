using System.Globalization;
using System.Text;

namespace DvCms.Domain.Cms;

/// <summary>
/// Regole dello slug di una pagina: un solo segmento di percorso, in minuscolo, con lettere ASCII, cifre e trattini
/// (nessun punto, quindi mai un nome di file). Gli slug riservati non sono utilizzabili.
/// </summary>
public static class NormalizzatoreSlug
{
    public const int LunghezzaMassima = 100;

    /// <summary>Percorsi dell'applicazione che uno slug non può occupare: area admin, asset statici, pagina di errore.</summary>
    public static IReadOnlySet<string> Riservati { get; } =
        new HashSet<string>(StringComparer.Ordinal) { "admin", "lib", "errore", "media" };

    public static bool ERiservato(string slug) => Riservati.Contains(slug);

    /// <summary>Normalizza (spazi tolti, minuscolo) e controlla il formato; gli slug riservati sono rifiutati.</summary>
    public static bool TentaNormalizza(string? slug, out string normalizzato)
    {
        normalizzato = string.Empty;
        if (string.IsNullOrWhiteSpace(slug))
        {
            return false;
        }

        var candidato = slug.Trim().ToLowerInvariant();
        if (!FormatoValido(candidato) || ERiservato(candidato))
        {
            return false;
        }

        normalizzato = candidato;
        return true;
    }

    /// <summary>Proposta di slug da un titolo: senza accenti, parole separate da trattini. Può risultare vuota o riservata.</summary>
    public static string DaTitolo(string? titolo)
    {
        if (string.IsNullOrWhiteSpace(titolo))
        {
            return string.Empty;
        }

        var costruttore = new StringBuilder();
        var ultimoTrattino = true;
        foreach (var carattere in titolo.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(carattere) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            var minuscolo = char.ToLowerInvariant(carattere);
            if (minuscolo is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                costruttore.Append(minuscolo);
                ultimoTrattino = false;
            }
            else if (!ultimoTrattino)
            {
                costruttore.Append('-');
                ultimoTrattino = true;
            }
        }

        var slug = costruttore.ToString().Trim('-');
        return slug.Length > LunghezzaMassima ? slug[..LunghezzaMassima].TrimEnd('-') : slug;
    }

    private static bool FormatoValido(string slug)
    {
        if (slug.Length is 0 or > LunghezzaMassima || slug[0] == '-' || slug[^1] == '-')
        {
            return false;
        }

        var trattinoPrecedente = false;
        foreach (var carattere in slug)
        {
            var trattino = carattere == '-';
            var consentito = carattere is >= 'a' and <= 'z' or >= '0' and <= '9' || trattino;
            if (!consentito || (trattino && trattinoPrecedente))
            {
                return false;
            }

            trattinoPrecedente = trattino;
        }

        return true;
    }
}
