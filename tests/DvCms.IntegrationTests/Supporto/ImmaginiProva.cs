namespace DvCms.IntegrationTests.Supporto;

/// <summary>Byte di immagini minime ma con intestazioni corrette (basta per la lettura di tipo e dimensioni, non per mostrarle).</summary>
public static class ImmaginiProva
{
    public static byte[] Png(int larghezza, int altezza)
    {
        using var flusso = new MemoryStream();
        flusso.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        flusso.Write([0, 0, 0, 13]);
        flusso.Write("IHDR"u8);
        flusso.Write(Grande(larghezza));
        flusso.Write(Grande(altezza));
        flusso.Write([8, 2, 0, 0, 0]);
        flusso.Write([0, 0, 0, 0]);
        flusso.Write([0, 0, 0, 0]);
        flusso.Write("IEND"u8);
        flusso.Write([0xAE, 0x42, 0x60, 0x82]);
        return flusso.ToArray();
    }

    public static byte[] Jpeg(int larghezza, int altezza)
    {
        using var flusso = new MemoryStream();
        flusso.Write([0xFF, 0xD8]);
        flusso.Write([0xFF, 0xE0, 0x00, 0x10, (byte)'J', (byte)'F', (byte)'I', (byte)'F', 0x00, 0x01, 0x01, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00]);
        flusso.Write([0xFF, 0xC0, 0x00, 0x0B, 0x08, (byte)(altezza >> 8), (byte)altezza, (byte)(larghezza >> 8), (byte)larghezza, 0x01, 0x01, 0x11, 0x00]);
        flusso.Write([0xFF, 0xD9]);
        return flusso.ToArray();
    }

    public static byte[] Gif(int larghezza, int altezza) =>
        [.. "GIF89a"u8, (byte)larghezza, (byte)(larghezza >> 8), (byte)altezza, (byte)(altezza >> 8), 0x00, 0x00, 0x00, 0x3B];

    public static byte[] WebpEsteso(int larghezza, int altezza)
    {
        var corpo = new List<byte>();
        corpo.AddRange("WEBP"u8.ToArray());
        corpo.AddRange("VP8X"u8.ToArray());
        corpo.AddRange([10, 0, 0, 0]);
        corpo.AddRange([0, 0, 0, 0]);
        corpo.AddRange(Piccolo24(larghezza - 1));
        corpo.AddRange(Piccolo24(altezza - 1));
        return Riff(corpo);
    }

    public static byte[] WebpLossy(int larghezza, int altezza)
    {
        var corpo = new List<byte>();
        corpo.AddRange("WEBP"u8.ToArray());
        corpo.AddRange("VP8 "u8.ToArray());
        corpo.AddRange([10, 0, 0, 0]);
        corpo.AddRange([0x00, 0x00, 0x00, 0x9D, 0x01, 0x2A]);
        corpo.AddRange([(byte)larghezza, (byte)(larghezza >> 8), (byte)altezza, (byte)(altezza >> 8)]);
        return Riff(corpo);
    }

    public static byte[] WebpLossless(int larghezza, int altezza)
    {
        var bit = (uint)((larghezza - 1) | ((altezza - 1) << 14));
        var corpo = new List<byte>();
        corpo.AddRange("WEBP"u8.ToArray());
        corpo.AddRange("VP8L"u8.ToArray());
        corpo.AddRange([5, 0, 0, 0]);
        corpo.Add(0x2F);
        corpo.AddRange([(byte)bit, (byte)(bit >> 8), (byte)(bit >> 16), (byte)(bit >> 24)]);
        return Riff(corpo);
    }

    private static byte[] Riff(List<byte> corpo)
    {
        var lunghezza = (uint)corpo.Count;
        return [.. "RIFF"u8, (byte)lunghezza, (byte)(lunghezza >> 8), (byte)(lunghezza >> 16), (byte)(lunghezza >> 24), .. corpo];
    }

    private static byte[] Grande(int valore) => [(byte)(valore >> 24), (byte)(valore >> 16), (byte)(valore >> 8), (byte)valore];

    private static byte[] Piccolo24(int valore) => [(byte)valore, (byte)(valore >> 8), (byte)(valore >> 16)];
}
