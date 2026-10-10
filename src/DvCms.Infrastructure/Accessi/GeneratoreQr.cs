using QRCoder;

namespace DvCms.Infrastructure.Accessi;

/// <summary>Codice QR come SVG in linea (nessuna immagine esterna, nessuno script): serve a configurare l'app di autenticazione.</summary>
public static class GeneratoreQr
{
    public static string Svg(string contenuto)
    {
        ArgumentException.ThrowIfNullOrEmpty(contenuto);

        using var generatore = new QRCodeGenerator();
        using var dati = generatore.CreateQrCode(contenuto, QRCodeGenerator.ECCLevel.M);
        return new SvgQRCode(dati).GetGraphic(4, "#000000", "#ffffff", drawQuietZones: true, SvgQRCode.SizingMode.ViewBoxAttribute);
    }
}
