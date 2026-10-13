using System.Globalization;
using System.Text;

namespace DvCms.Extender;

/// <summary>
/// Normalizza gli host dei domini registrati e delle richieste: minuscolo, senza porta né punto finale,
/// nomi internazionali in forma ASCII (punycode). Accetta solo nomi DNS (o <c>localhost</c>), mai indirizzi IP.
/// </summary>
public static class NormalizzatoreHost
{
    public const int LunghezzaMassima = 253;

    public static bool TentaNormalizza(string? host, out string normalizzato)
    {
        normalizzato = string.Empty;
        if (string.IsNullOrWhiteSpace(host))
        {
            return false;
        }

        var candidato = host.Trim();
        if (candidato.Length > LunghezzaMassima + 7)
        {
            return false;
        }

        // Una porta finale (host:8080) si ignora; più di un due punti o parentesi quadre indicano un IPv6, rifiutato.
        var posizioneDuePunti = candidato.IndexOf(':', StringComparison.Ordinal);
        if (posizioneDuePunti >= 0)
        {
            if (candidato.IndexOf(':', posizioneDuePunti + 1) >= 0 || !SoloCifre(candidato.AsSpan(posizioneDuePunti + 1)))
            {
                return false;
            }

            candidato = candidato[..posizioneDuePunti];
        }

        candidato = candidato.TrimEnd('.');
        if (candidato.Length == 0)
        {
            return false;
        }

        string ascii;
        try
        {
            ascii = new IdnMapping().GetAscii(candidato).ToLowerInvariant();
        }
        catch (ArgumentException)
        {
            return false;
        }

        if (ascii.Length == 0 || ascii.Length > LunghezzaMassima || !EtichetteValide(ascii) || EIndirizzoIp(ascii))
        {
            return false;
        }

        normalizzato = ascii;
        return true;
    }

    private static bool SoloCifre(ReadOnlySpan<char> testo)
    {
        if (testo.IsEmpty)
        {
            return false;
        }

        foreach (var carattere in testo)
        {
            if (carattere is < '0' or > '9')
            {
                return false;
            }
        }

        return true;
    }

    private static bool EtichetteValide(string host)
    {
        foreach (var etichetta in host.Split('.'))
        {
            if (etichetta.Length is 0 or > 63 || etichetta[0] == '-' || etichetta[^1] == '-')
            {
                return false;
            }

            foreach (var carattere in etichetta)
            {
                var consentito = carattere is >= 'a' and <= 'z' or >= '0' and <= '9' or '-';
                if (!consentito)
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool EIndirizzoIp(string host)
    {
        // Quattro etichette numeriche (IPv4): non è un nome di dominio registrabile.
        var etichette = host.Split('.');
        return etichette.Length == 4 && etichette.All(etichetta => etichetta.All(char.IsAsciiDigit));
    }

    /// <summary>Solo per i messaggi: rappresentazione sicura di un host non valido (senza caratteri di controllo).</summary>
    public static string PerMessaggio(string? host)
    {
        var costruttore = new StringBuilder();
        foreach (var carattere in host ?? string.Empty)
        {
            costruttore.Append(char.IsControl(carattere) ? '?' : carattere);
        }

        return costruttore.Length > 80 ? costruttore.ToString(0, 80) : costruttore.ToString();
    }
}
