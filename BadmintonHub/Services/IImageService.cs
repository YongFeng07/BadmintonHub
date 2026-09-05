namespace BadmintonHub.Services;

/// <summary>
/// Profile-photo pipeline (P2): validates uploaded images, resizes them to a
/// fixed avatar size and stores them under /uploads/profiles. The same
/// service shape is reused later by the facility photo pipeline (resize/crop).
/// </summary>
public interface IImageService
{
    /// <summary>
    /// Validates and stores an uploaded profile photo for the given user id.
    /// Returns a user-facing error, or the site-relative path of the saved file.
    /// Replaces (and deletes) any previous photo of the same user.
    /// </summary>
    (string? Error, string? RelativePath) SaveProfilePhoto(IFormFile file, int userId);

    /// <summary>Deletes a previously stored photo file (safe no-op for null/foreign paths).</summary>
    void DeleteProfilePhoto(string? relativePath);
}
