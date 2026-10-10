using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Security.Cryptography;

namespace DvCms.Application.Accessi.DueFattori;

/// <summary>
/// TOTP secondo RFC 6238 (HMAC-SHA1, 6 cifre, passo di 30 secondi), compatibile con le comuni app di autenticazione.
/// Le funzioni sono pure: l'anti-replay (passo già usato) è responsabilità del chiamante, che lo registra sul database.
/// </summary>
public static class Totp
{
    public const int LunghezzaSegretoByte = 20;
    public const int Cifre = 6;
    public const int SecondiPasso = 30;

    /// <summary>Passi di tolleranza prima e dopo quello corrente (deriva degli orologi).</summary>
    public const int FinestraPassi = 1;

    public static byte[] GeneraSegreto() => RandomNumberGenerator.GetBytes(LunghezzaSegretoByte);

    public static long PassoDi(DateTimeOffset istante) => istante.ToUnixTimeSeconds() / SecondiPasso;

    [SuppressMessage("Security", "CA5350:Do Not Use Weak Cryptographic Algorithms", Justification = "HMAC-SHA1 è l'algoritmo del TOTP (RFC 6238) accettato da tutte le app di autenticazione; qui non protegge dati ma deriva un codice monouso.")]
    public static string CalcolaCodice(ReadOnlySpan<byte> segreto, long passo)
    {
        Span<byte> contatore = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(contatore, passo);
        Span<byte> hash = stackalloc byte[HMACSHA1.HashSizeInBytes];
        HMACSHA1.HashData(segreto, contatore, hash);

        var scostamento = hash[^1] & 0x0F;
        var binario = BinaryPrimitives.ReadInt32BigEndian(hash.Slice(scostamento, 4)) & 0x7FFFFFFF;
        return (binario % 1_000_000).ToString("D6", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Verifica il codice nella finestra di tolleranza e restituisce il passo corrispondente (il più recente che combacia),
    /// oppure nulla. Il confronto avviene a tempo costante su tutti i passi della finestra.
    /// </summary>
    public static long? Verifica(ReadOnlySpan<byte> segreto, string? codice, DateTimeOffset adesso)
    {
        var normalizzato = Normalizza(codice);
        if (normalizzato is null)
        {
            return null;
        }

        var corrente = PassoDi(adesso);
        long? trovato = null;
        for (var passo = corrente - FinestraPassi; passo <= corrente + FinestraPassi; passo++)
        {
            var atteso = CalcolaCodice(segreto, passo);
            if (CryptographicOperations.FixedTimeEquals(
                System.Text.Encoding.ASCII.GetBytes(atteso),
                System.Text.Encoding.ASCII.GetBytes(normalizzato)))
            {
                trovato = passo;
            }
        }

        return trovato;
    }

    /// <summary>Accetta "123456" e "123 456"; qualunque altra forma è rifiutata.</summary>
    public static string? Normalizza(string? codice)
    {
        if (string.IsNullOrWhiteSpace(codice))
        {
            return null;
        }

        var cifre = codice.Replace(" ", string.Empty, StringComparison.Ordinal);
        return cifre.Length == Cifre && cifre.All(char.IsAsciiDigit) ? cifre : null;
    }

    /// <summary>URI <c>otpauth://</c> da mostrare come codice QR o da incollare nell'app.</summary>
    public static string CreaUri(ReadOnlySpan<byte> segreto, string emittente, string account)
    {
        var etichetta = Uri.EscapeDataString(emittente) + ":" + Uri.EscapeDataString(account);
        return $"otpauth://totp/{etichetta}?secret={Base32.Codifica(segreto)}&issuer={Uri.EscapeDataString(emittente)}&algorithm=SHA1&digits={Cifre}&period={SecondiPasso}";
    }
}
