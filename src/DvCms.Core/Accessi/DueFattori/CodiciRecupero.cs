using System.Security.Cryptography;
using System.Text;
using DvCms.Extender;

namespace DvCms.Core.Accessi.DueFattori;

/// <summary>Codici di recupero monouso: 16 caratteri Base32 (80 bit) in quattro gruppi, salvati solo come hash SHA-256.</summary>
public static class CodiciRecupero
{
    public const int Quantita = 10;
    private const int ByteEntropia = 10;

    public static IReadOnlyList<string> Genera()
    {
        var codici = new List<string>(Quantita);
        for (var i = 0; i < Quantita; i++)
        {
            var testo = Base32.Codifica(RandomNumberGenerator.GetBytes(ByteEntropia));
            codici.Add(string.Join('-', Enumerable.Range(0, 4).Select(g => testo.Substring(g * 4, 4))));
        }

        return codici;
    }

    /// <summary>Forma canonica (senza separatori, maiuscola) oppure nulla se non ha la forma di un codice di recupero.</summary>
    public static string? Normalizza(string? codice)
    {
        if (string.IsNullOrWhiteSpace(codice))
        {
            return null;
        }

        var pulito = new string(codice.Where(c => c is not (' ' or '-')).ToArray()).ToUpperInvariant();
        return pulito.Length == 16 && Base32.TentaDecodifica(pulito, out _) ? pulito : null;
    }

    public static byte[] Hash(string codiceNormalizzato) => SHA256.HashData(Encoding.ASCII.GetBytes(codiceNormalizzato));
}
