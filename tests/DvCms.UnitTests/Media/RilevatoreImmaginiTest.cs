using DvCms.Application.Media;
using DvCms.IntegrationTests.Supporto;
using Xunit;

namespace DvCms.UnitTests.Media;

public class RilevatoreImmaginiTest
{
    public static TheoryData<string, byte[], string, int, int> Immagini => new()
    {
        { "png", ImmaginiProva.Png(640, 480), "image/png", 640, 480 },
        { "jpeg", ImmaginiProva.Jpeg(1920, 1080), "image/jpeg", 1920, 1080 },
        { "gif", ImmaginiProva.Gif(300, 200), "image/gif", 300, 200 },
        { "webp esteso", ImmaginiProva.WebpEsteso(4000, 3000), "image/webp", 4000, 3000 },
        { "webp lossy", ImmaginiProva.WebpLossy(800, 600), "image/webp", 800, 600 },
        { "webp lossless", ImmaginiProva.WebpLossless(1024, 768), "image/webp", 1024, 768 },
    };

    [Theory]
    [MemberData(nameof(Immagini))]
    public void RiconosceTipoEDimensioniDallaFirma(string nome, byte[] dati, string tipo, int larghezza, int altezza)
    {
        var rilevata = RilevatoreImmagini.Rileva(dati);

        Assert.NotNull(rilevata);
        Assert.Equal(tipo, rilevata.TipoContenuto);
        Assert.Equal(larghezza, rilevata.Larghezza);
        Assert.Equal(altezza, rilevata.Altezza);
        Assert.False(string.IsNullOrEmpty(nome));
    }

    [Fact]
    public void IlJpegConUnaSezioneEsifPrimaDelFrameSiLegge()
    {
        // APP1 (Exif) lunga 300 byte prima del frame, con byte di riempimento 0xFF.
        var exif = new byte[300];
        byte[] dati = [0xFF, 0xD8, 0xFF, 0xE1, (byte)(exif.Length + 2 >> 8), (byte)(exif.Length + 2), .. exif, 0xFF, 0xFF, 0xFF, 0xC2, 0x00, 0x0B, 0x08, 0x02, 0x00, 0x03, 0x00, 0x01, 0x01, 0x11, 0x00];

        var rilevata = RilevatoreImmagini.Rileva(dati);

        Assert.NotNull(rilevata);
        Assert.Equal((768, 512), (rilevata.Larghezza, rilevata.Altezza));
    }

    [Fact]
    public void UnFileDiTestoUnSvgOUnHtmlNonSonoImmagini()
    {
        Assert.Null(RilevatoreImmagini.Rileva("<svg xmlns=\"http://www.w3.org/2000/svg\"></svg>"u8));
        Assert.Null(RilevatoreImmagini.Rileva("<html><script>alert(1)</script></html>"u8));
        Assert.Null(RilevatoreImmagini.Rileva("GIF89a come testo"u8));
        Assert.Null(RilevatoreImmagini.Rileva([]));
        Assert.Null(RilevatoreImmagini.Rileva([0xFF]));
    }

    [Fact]
    public void UnaFirmaGiustaConDimensioniZeroOIntestazioneTroncataERifiutata()
    {
        Assert.Null(RilevatoreImmagini.Rileva(ImmaginiProva.Png(0, 10)));
        Assert.Null(RilevatoreImmagini.Rileva(ImmaginiProva.Gif(10, 0)));
        Assert.Null(RilevatoreImmagini.Rileva(ImmaginiProva.Png(10, 10).AsSpan(0, 20)));
        Assert.Null(RilevatoreImmagini.Rileva(ImmaginiProva.Jpeg(10, 10).AsSpan(0, 15)));
        Assert.Null(RilevatoreImmagini.Rileva(ImmaginiProva.WebpLossy(10, 10).AsSpan(0, 22)));
    }

    [Fact]
    public void UnJpegSenzaFrameOConLunghezzeFalseNonVaInLoopNeLeggeFuoriDaiLimiti()
    {
        Assert.Null(RilevatoreImmagini.Rileva([0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x01, 0x00, 0x00]));
        Assert.Null(RilevatoreImmagini.Rileva([0xFF, 0xD8, 0xFF, 0xE0, 0xFF, 0xFF, 0x00, 0x00]));
        Assert.Null(RilevatoreImmagini.Rileva([0xFF, 0xD8, 0x00, 0x00, 0x00, 0x00]));
        Assert.Null(RilevatoreImmagini.Rileva([0xFF, 0xD8, 0xFF, 0xD9, 0x00, 0x00]));
    }

    [Fact]
    public void LeFirmeNonSiConfondonoFraLoro()
    {
        // Un PNG con estensione e tipo qualsiasi resta un PNG; un JPEG non è un PNG.
        Assert.Equal("image/png", RilevatoreImmagini.Rileva(ImmaginiProva.Png(1, 1))!.TipoContenuto);
        Assert.Equal("image/jpeg", RilevatoreImmagini.Rileva(ImmaginiProva.Jpeg(1, 1))!.TipoContenuto);
    }

    [Fact]
    public void SoloLeEstensioniDelleImmaginiAmmesseSonoInElenco()
    {
        Assert.Equal(["image/gif", "image/jpeg", "image/jpeg", "image/png", "image/webp"], RilevatoreImmagini.EstensioniAmmesse.Values.Order());
        Assert.DoesNotContain(".svg", RilevatoreImmagini.EstensioniAmmesse.Keys);
        Assert.DoesNotContain(".html", RilevatoreImmagini.EstensioniAmmesse.Keys);
        Assert.DoesNotContain(".pdf", RilevatoreImmagini.EstensioniAmmesse.Keys);
    }
}
