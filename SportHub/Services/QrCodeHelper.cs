using QRCoder;

namespace SportHub.Services;

/// <summary>
/// Generates QR codes as PNG data URIs (no files on disk).
/// The QR content only ever contains the public reservation reference — never
/// personal or payment data (assignment security requirement).
/// </summary>
public static class QrCodeHelper
{
    public static string GenerateDataUri(string content)
    {
        var bytes = GenerateBytes(content);
        return $"data:image/png;base64,{Convert.ToBase64String(bytes)}";
    }

    /// <summary>G-M5: raw PNG bytes so the PDF e-receipt can embed the same QR code.</summary>
    public static byte[] GenerateBytes(string content)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(content, QRCodeGenerator.ECCLevel.Q);
        using var png = new PngByteQRCode(data);
        return png.GetGraphic(12);
    }
}
