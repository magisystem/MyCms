using System.Text;

namespace DvCms.Extender;

/// <summary>Base32 (RFC 4648, alfabeto maiuscolo, senza riempimento) per i segreti TOTP e i codici di recupero.</summary>
public static class Base32
{
    private const string Alfabeto = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static string Codifica(ReadOnlySpan<byte> dati)
    {
        var risultato = new StringBuilder((dati.Length * 8 + 4) / 5);
        var buffer = 0;
        var bit = 0;
        foreach (var b in dati)
        {
            buffer = (buffer << 8) | b;
            bit += 8;
            while (bit >= 5)
            {
                risultato.Append(Alfabeto[(buffer >> (bit - 5)) & 31]);
                bit -= 5;
            }

            buffer &= (1 << bit) - 1;
        }

        if (bit > 0)
        {
            risultato.Append(Alfabeto[(buffer << (5 - bit)) & 31]);
        }

        return risultato.ToString();
    }

    /// <summary>Decodifica ignorando spazi, trattini e maiuscole/minuscole; false se compaiono altri caratteri.</summary>
    public static bool TentaDecodifica(string? testo, out byte[] dati)
    {
        dati = [];
        if (testo is null)
        {
            return false;
        }

        var uscita = new List<byte>(testo.Length * 5 / 8);
        var buffer = 0;
        var bit = 0;
        foreach (var carattere in testo)
        {
            if (carattere is ' ' or '-')
            {
                continue;
            }

            var valore = Alfabeto.IndexOf(char.ToUpperInvariant(carattere), StringComparison.Ordinal);
            if (valore < 0)
            {
                return false;
            }

            buffer = (buffer << 5) | valore;
            bit += 5;
            if (bit >= 8)
            {
                uscita.Add((byte)((buffer >> (bit - 8)) & 0xFF));
                bit -= 8;
                buffer &= (1 << bit) - 1;
            }
        }

        dati = [.. uscita];
        return true;
    }
}
