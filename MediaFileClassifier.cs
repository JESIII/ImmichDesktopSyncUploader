namespace ImmichUploader;

/// <summary>
/// Classifies paths as uploadable media. Mirrors the extension families that
/// immich-go handles so the watcher and the incremental scanner agree on what
/// counts as "a new photo/video".
/// </summary>
public static class MediaFileClassifier
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".tif", ".tiff", ".webp",
        ".heic", ".heif", ".avif", ".jxl", ".insp",
        ".dng", ".cr2", ".cr3", ".arw", ".raf", ".nef", ".nrw",
        ".orf", ".rw2", ".pef", ".srw", ".3fr", ".x3f", ".mrw", ".srf"
    };

    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mov", ".avi", ".mkv", ".webm", ".m4v", ".3gp",
        ".mpg", ".mpeg", ".wmv", ".flv", ".ts", ".mts", ".m2ts", ".mod"
    };

    public static bool IsImage(string? path)
    {
        return path is not null && ImageExtensions.Contains(Path.GetExtension(path));
    }

    public static bool IsVideo(string? path)
    {
        return path is not null && VideoExtensions.Contains(Path.GetExtension(path));
    }

    /// <summary>True when the path has a supported image or video extension.</summary>
    public static bool IsMediaFile(string? path)
    {
        return IsImage(path) || IsVideo(path);
    }

    /// <summary>True for files immich-go can ingest alongside media (sidecars/xmp/edits).</summary>
    public static bool IsSidecar(string? path)
    {
        var ext = path is null ? "" : Path.GetExtension(path);
        return ext.Equals(".xmp", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".json", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".aae", StringComparison.OrdinalIgnoreCase);
    }
}
