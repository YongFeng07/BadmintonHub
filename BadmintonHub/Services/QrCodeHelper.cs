using QRCoder;

namespace BadmintonHub.Services;

/// <summary>
/// Generates QR codes as PNG data URIs (no files on disk).
/// The QR content only ever contains the public reservation reference — never
/// personal or payment data (assignment security requirement).
/// </summary>
public static class QrCodeHelper
{
    public static string GenerateDataUri(string content)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(content, QRCodeGenerator.ECCLevel.Q);
        using var png = new PngByteQRCode(data);
        var bytes = png.GetGraphic(12);
        return $"data:image/png;base64,{Convert.ToBase64String(bytes)}";
    }
}
