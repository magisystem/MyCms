namespace DvCms.Application.Media;

/// <summary>Tipo e dimensioni di un'immagine riconosciuta dalla sua firma.</summary>
public sealed record ImmagineRilevata(string TipoContenuto, string Estensione, int Larghezza, int Altezza);

/// <summary>
/// Riconosce PNG, JPEG, GIF e WebP dalla firma e ne legge le dimensioni dalle intestazioni, senza decodificare l'immagine
/// (nessuna libreria grafica, nessun rischio di bombe di decompressione in questa fase). Il tipo dichiarato dal client e
/// l'estensione non contano: decide solo il contenuto. SVG e ogni altro formato (che può contenere script) non sono ammessi.
/// </summary>
public static class RilevatoreImmagini
{
    private static readonly byte[] _firmaPng = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>Estensioni ammesse, con il tipo che ciascuna deve avere.</summary>
    public static IReadOnlyDictionary<string, string> EstensioniAmmesse { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".png"] = "image/png",
        [".gif"] = "image/gif",
        [".webp"] = "image/webp",
    };

    /// <summary>Immagine riconosciuta con dimensioni plausibili (almeno 1x1), o nullo se il contenuto non è una di quelle ammesse o è malformato.</summary>
    public static ImmagineRilevata? Rileva(ReadOnlySpan<byte> dati)
    {
        var rilevata = RilevaPng(dati) ?? RilevaJpeg(dati) ?? RilevaGif(dati) ?? RilevaWebp(dati);
        return rilevata is { Larghezza: > 0, Altezza: > 0 } ? rilevata : null;
    }

    private static ImmagineRilevata? RilevaPng(ReadOnlySpan<byte> dati)
    {
        // Firma + chunk IHDR (lunghezza 13, "IHDR", larghezza, altezza).
        if (dati.Length < 24 || !dati[..8].SequenceEqual(_firmaPng) || !dati[12..16].SequenceEqual("IHDR"u8) || Intero32Grande(dati, 8) != 13)
        {
            return null;
        }

        var larghezza = Intero32Grande(dati, 16);
        var altezza = Intero32Grande(dati, 20);
        return larghezza is > 0 and <= int.MaxValue && altezza is > 0 and <= int.MaxValue
            ? new ImmagineRilevata("image/png", ".png", (int)larghezza, (int)altezza)
            : null;
    }

    private static ImmagineRilevata? RilevaGif(ReadOnlySpan<byte> dati)
    {
        if (dati.Length < 14 || !(dati[..6].SequenceEqual("GIF87a"u8) || dati[..6].SequenceEqual("GIF89a"u8)))
        {
            return null;
        }

        // Dopo il descrittore dello schermo (7 byte) e l'eventuale tavolozza globale deve iniziare un blocco GIF vero
        // (estensione, immagine o fine): un testo che comincia per "GIF89a" non è una GIF.
        var tavolozza = (dati[10] & 0x80) != 0 ? 3 * (1 << ((dati[10] & 0x07) + 1)) : 0;
        var blocco = 13 + tavolozza;
        if (blocco >= dati.Length || dati[blocco] is not (0x21 or 0x2C or 0x3B))
        {
            return null;
        }

        return new ImmagineRilevata("image/gif", ".gif", dati[6] | (dati[7] << 8), dati[8] | (dati[9] << 8));
    }

    private static ImmagineRilevata? RilevaJpeg(ReadOnlySpan<byte> dati)
    {
        if (dati.Length < 4 || dati[0] != 0xFF || dati[1] != 0xD8)
        {
            return null;
        }

        var posizione = 2;
        while (posizione + 4 <= dati.Length)
        {
            if (dati[posizione] != 0xFF)
            {
                return null;
            }

            // Byte di riempimento 0xFF prima del marcatore.
            while (posizione < dati.Length && dati[posizione] == 0xFF)
            {
                posizione++;
            }

            if (posizione >= dati.Length)
            {
                return null;
            }

            var marcatore = dati[posizione++];
            if (marcatore is 0x01 or (>= 0xD0 and <= 0xD8))
            {
                continue;
            }

            if (marcatore == 0xD9 || posizione + 2 > dati.Length)
            {
                return null;
            }

            var lunghezza = (dati[posizione] << 8) | dati[posizione + 1];
            if (lunghezza < 2)
            {
                return null;
            }

            // Marcatori SOFn (inizio del frame) tranne DHT, JPG e DAC.
            if (marcatore is >= 0xC0 and <= 0xCF && marcatore is not (0xC4 or 0xC8 or 0xCC))
            {
                if (lunghezza < 8 || posizione + 7 > dati.Length)
                {
                    return null;
                }

                var altezza = (dati[posizione + 3] << 8) | dati[posizione + 4];
                var larghezza = (dati[posizione + 5] << 8) | dati[posizione + 6];
                return new ImmagineRilevata("image/jpeg", ".jpg", larghezza, altezza);
            }

            posizione += lunghezza;
        }

        return null;
    }

    private static ImmagineRilevata? RilevaWebp(ReadOnlySpan<byte> dati)
    {
        if (dati.Length < 25 || !dati[..4].SequenceEqual("RIFF"u8) || !dati[8..12].SequenceEqual("WEBP"u8))
        {
            return null;
        }

        var formato = dati[12..16];
        if (formato.SequenceEqual("VP8 "u8) && dati.Length >= 30)
        {
            // Lossy: dopo il tag del frame (3 byte) c'è il codice di avvio 9D 01 2A, poi larghezza e altezza a 14 bit.
            return dati[23] == 0x9D && dati[24] == 0x01 && dati[25] == 0x2A
                ? new ImmagineRilevata("image/webp", ".webp", (dati[26] | (dati[27] << 8)) & 0x3FFF, (dati[28] | (dati[29] << 8)) & 0x3FFF)
                : null;
        }

        if (formato.SequenceEqual("VP8L"u8))
        {
            // Lossless: firma 0x2F, poi larghezza-1 e altezza-1 a 14 bit ciascuna.
            if (dati[20] != 0x2F)
            {
                return null;
            }

            var bit = (uint)(dati[21] | (dati[22] << 8) | (dati[23] << 16) | (dati[24] << 24));
            return new ImmagineRilevata("image/webp", ".webp", (int)(bit & 0x3FFF) + 1, (int)((bit >> 14) & 0x3FFF) + 1);
        }

        if (formato.SequenceEqual("VP8X"u8) && dati.Length >= 30)
        {
            // Esteso: tela larga e alta (valore-1) a 24 bit.
            return new ImmagineRilevata(
                "image/webp",
                ".webp",
                (dati[24] | (dati[25] << 8) | (dati[26] << 16)) + 1,
                (dati[27] | (dati[28] << 8) | (dati[29] << 16)) + 1);
        }

        return null;
    }

    private static long Intero32Grande(ReadOnlySpan<byte> dati, int posizione) =>
        ((long)dati[posizione] << 24) | ((long)dati[posizione + 1] << 16) | ((long)dati[posizione + 2] << 8) | dati[posizione + 3];
}
