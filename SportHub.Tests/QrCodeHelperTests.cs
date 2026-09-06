using SportHub.Services;

namespace SportHub.Tests;

/// <summary>
/// QR confirmation codes are PNG data URIs generated from the public
/// reservation reference only (assignment security requirement).
/// </summary>
public class QrCodeHelperTests
{
    private static readonly byte[] PngSignature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    [Fact]
    public void GenerateDataUri_ReturnsValidPng()
    {
        var dataUri = QrCodeHelper.GenerateDataUri("SH-2026-000001");

        Assert.StartsWith("data:image/png;base64,", dataUri);
        var bytes = Convert.FromBase64String(dataUri["data:image/png;base64,".Length..]);
        Assert.True(bytes.Length > 100);
        Assert.Equal(PngSignature, bytes.Take(8));
    }

    [Fact]
    public void GenerateDataUri_DifferentContent_ProducesDifferentImage()
    {
        var first = QrCodeHelper.GenerateDataUri("SH-2026-000001");
        var second = QrCodeHelper.GenerateDataUri("SH-2026-000002");

        Assert.NotEqual(first, second);
    }
}
