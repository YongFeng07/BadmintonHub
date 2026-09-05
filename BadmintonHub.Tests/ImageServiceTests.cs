using BadmintonHub.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using SixLabors.ImageSharp;

namespace BadmintonHub.Tests;

/// <summary>
/// P2 profile-photo pipeline: file validation (size/extension/real format),
/// 256×256 crop-resize, and safe deletion of managed files only.
/// </summary>
public class ImageServiceTests : IDisposable
{
    private sealed class FakeWebHostEnvironment : IWebHostEnvironment
    {
        public string WebRootPath { get; set; } = "";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ApplicationName { get; set; } = "";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = "";
        public string EnvironmentName { get; set; } = "";
    }

    // A known-valid 1×1 PNG so the tests need no binary fixture files.
    private static readonly byte[] TinyPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    private readonly string _tempDir;

    public ImageServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "bh-imgsvc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch (IOException) { }
    }

    private ImageService CreateService()
    {
        var env = new FakeWebHostEnvironment { WebRootPath = _tempDir };
        return new ImageService(env);
    }

    private static IFormFile FileFromBytes(byte[] bytes, string name) =>
        new FormFile(new MemoryStream(bytes), 0, bytes.Length, "photo", name);

    [Fact]
    public void SaveProfilePhoto_NullFile_ReturnsError()
    {
        var service = CreateService();

        var (error, path) = service.SaveProfilePhoto(null!, 1);

        Assert.NotNull(error);
        Assert.Null(path);
    }

    [Fact]
    public void SaveProfilePhoto_EmptyFile_ReturnsError()
    {
        var service = CreateService();

        var (error, _) = service.SaveProfilePhoto(FileFromBytes(Array.Empty<byte>(), "photo.png"), 1);

        Assert.Equal("Please choose a photo file to upload.", error);
    }

    [Fact]
    public void SaveProfilePhoto_OversizedFile_ReturnsError()
    {
        var service = CreateService();
        var big = new byte[5 * 1024 * 1024 + 1];

        var (error, _) = service.SaveProfilePhoto(FileFromBytes(big, "photo.png"), 1);

        Assert.Equal("The photo must be 5 MB or smaller.", error);
    }

    [Fact]
    public void SaveProfilePhoto_UnsupportedExtension_ReturnsError()
    {
        var service = CreateService();

        var (error, _) = service.SaveProfilePhoto(FileFromBytes(TinyPng, "photo.gif"), 1);

        Assert.Equal("Only JPG, PNG or WebP images are allowed.", error);
    }

    [Fact]
    public void SaveProfilePhoto_RenamedNonImage_ReturnsError()
    {
        var service = CreateService();
        // Executable bytes wearing a .jpg name must be rejected by real-format validation.
        var fakeJpeg = new byte[] { 0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00 };

        var (error, _) = service.SaveProfilePhoto(FileFromBytes(fakeJpeg, "photo.jpg"), 1);

        Assert.Equal("That file could not be read as an image.", error);
    }

    [Fact]
    public void SaveProfilePhoto_ValidPng_ResizesTo256AndSavesJpeg()
    {
        var service = CreateService();

        var (error, path) = service.SaveProfilePhoto(FileFromBytes(TinyPng, "photo.png"), 42);

        Assert.Null(error);
        Assert.NotNull(path);
        Assert.StartsWith("/uploads/profiles/", path);

        var fullPath = Path.Combine(_tempDir, path!.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(fullPath));

        using var saved = Image.Load(fullPath);
        Assert.Equal(256, saved.Width);
        Assert.Equal(256, saved.Height);
        Assert.Equal("JPEG", saved.Metadata.DecodedImageFormat?.Name);
    }

    [Fact]
    public void DeleteProfilePhoto_RemovesManagedFile()
    {
        var service = CreateService();
        var (_, path) = service.SaveProfilePhoto(FileFromBytes(TinyPng, "photo.png"), 7);
        var fullPath = Path.Combine(_tempDir, path!.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(fullPath));

        service.DeleteProfilePhoto(path);

        Assert.False(File.Exists(fullPath));
    }

    [Fact]
    public void DeleteProfilePhoto_NullOrForeignPaths_AreNoOps()
    {
        var service = CreateService();

        service.DeleteProfilePhoto(null);
        service.DeleteProfilePhoto("/etc/passwd");
        service.DeleteProfilePhoto("..\\..\\Windows\\System32\\config\\SAM");
        service.DeleteProfilePhoto("/uploads/other/thing.jpg");
    }

    // ---------- P3 facility photos (800×450 catalog covers) ----------

    [Fact]
    public void SaveFacilityPhoto_ValidPng_ResizesTo800x450AndSavesJpeg()
    {
        var service = CreateService();

        var (error, path) = service.SaveFacilityPhoto(FileFromBytes(TinyPng, "venue.png"), 3);

        Assert.Null(error);
        Assert.NotNull(path);
        Assert.StartsWith("/uploads/facilities/", path);

        var fullPath = Path.Combine(_tempDir, path!.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(fullPath));

        using var saved = Image.Load(fullPath);
        Assert.Equal(800, saved.Width);
        Assert.Equal(450, saved.Height);
        Assert.Equal("JPEG", saved.Metadata.DecodedImageFormat?.Name);
    }

    [Fact]
    public void SaveFacilityPhoto_UnsupportedExtension_ReturnsError()
    {
        var service = CreateService();

        var (error, path) = service.SaveFacilityPhoto(FileFromBytes(TinyPng, "venue.gif"), 3);

        Assert.Equal("Only JPG, PNG or WebP images are allowed.", error);
        Assert.Null(path);
    }

    [Fact]
    public void DeleteFacilityPhoto_RemovesManagedFileAndIgnoresForeignPaths()
    {
        var service = CreateService();
        var (_, path) = service.SaveFacilityPhoto(FileFromBytes(TinyPng, "venue.png"), 9);
        var fullPath = Path.Combine(_tempDir, path!.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(fullPath));

        service.DeleteFacilityPhoto(path);
        service.DeleteFacilityPhoto("/uploads/profiles/other.jpg"); // profile photo is not ours to delete

        Assert.False(File.Exists(fullPath));
    }
}
