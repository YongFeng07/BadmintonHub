using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;

namespace BadmintonHub.Services;

/// <summary>
/// Image upload pipeline backed by SixLabors.ImageSharp (cross-platform, no
/// System.Drawing dependency). Uploaded images are decoded, validated by real
/// format, cropped and resized, then re-saved as JPEGs under wwwroot/uploads —
/// raw user files are never served as-is.
/// </summary>
public class ImageService : IImageService
{
    private const long MaxBytes = 5 * 1024 * 1024; // 5 MB
    private const int AvatarSize = 256;
    private const int FacilityWidth = 800;
    private const int FacilityHeight = 450;

    private static readonly HashSet<string> AllowedExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".webp" };

    private readonly IWebHostEnvironment _env;

    public ImageService(IWebHostEnvironment env) => _env = env;

    public (string? Error, string? RelativePath) SaveProfilePhoto(IFormFile file, int userId) =>
        SaveImage(file, "profiles", userId, AvatarSize, AvatarSize, "photo");

    public (string? Error, string? RelativePath) SaveFacilityPhoto(IFormFile file, int facilityId) =>
        SaveImage(file, "facilities", facilityId, FacilityWidth, FacilityHeight, "photo");

    public void DeleteProfilePhoto(string? relativePath) =>
        DeleteUnder(relativePath, "profiles");

    public void DeleteFacilityPhoto(string? relativePath) =>
        DeleteUnder(relativePath, "facilities");

    // ---------- shared implementation ----------

    private (string? Error, string? RelativePath) SaveImage(
        IFormFile file, string subFolder, int ownerId, int width, int height, string errorNoun)
    {
        if (file == null || file.Length == 0)
            return ($"Please choose a {errorNoun} file to upload.", null);
        if (file.Length > MaxBytes)
            return ($"The {errorNoun} must be 5 MB or smaller.", null);

        var extension = Path.GetExtension(file.FileName);
        if (!AllowedExtensions.Contains(extension))
            return ("Only JPG, PNG or WebP images are allowed.", null);

        try
        {
            using var input = file.OpenReadStream();
            using var image = Image.Load(input);

            // Trust the decoded format, not the file name — this rejects a renamed executable.
            var decoded = image.Metadata.DecodedImageFormat?.Name.ToLowerInvariant();
            if (decoded is not ("jpeg" or "png" or "webp"))
                return ("Only JPG, PNG or WebP images are allowed.", null);

            // Centre crop + resize, then save as a JPEG (universally supported, small).
            image.Mutate(x => x.Resize(new ResizeOptions
            {
                Size = new Size(width, height),
                Mode = ResizeMode.Crop
            }));

            var dir = Path.Combine(_env.WebRootPath ?? string.Empty, "uploads", subFolder);
            Directory.CreateDirectory(dir);

            var fileName = $"{ownerId}-{Guid.NewGuid():N}.jpg";
            image.SaveAsJpegAsync(Path.Combine(dir, fileName)).GetAwaiter().GetResult();
            return (null, $"/uploads/{subFolder}/{fileName}");
        }
        catch (Exception ex) when (ex is InvalidImageContentException or UnknownImageFormatException
                                   or IOException or UnauthorizedAccessException)
        {
            return ("That file could not be read as an image.", null);
        }
    }

    private void DeleteUnder(string? relativePath, string subFolder)
    {
        var prefix = $"/uploads/{subFolder}/";
        // Only delete files we manage under /uploads/<subFolder> (never a foreign path).
        if (string.IsNullOrWhiteSpace(relativePath) ||
            !relativePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return;

        try
        {
            var fullPath = Path.Combine(_env.WebRootPath ?? string.Empty,
                relativePath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(fullPath))
                File.Delete(fullPath);
        }
        catch (IOException)
        {
            // Best effort — an orphaned file is harmless; do not fail the request.
        }
    }
}
