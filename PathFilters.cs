namespace ImmichUploader;

/// <summary>
/// Shared path filtering helpers used by the incremental file scanner and the
/// real-time folder watcher so both agree on which files/directories to skip.
/// </summary>
public static class PathFilters
{
    /// <summary>Directory names that are never traversed or watched.</summary>
    public static readonly IReadOnlySet<string> ExcludedDirectoryNames =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "@eaDir", "@__thumb", ".Spotlight-V100", ".photostructure", "thumbnails",
            "Lightroom Catalog", "Recently Deleted", "$RECYCLE.BIN", "System Volume Information"
        };

    /// <summary>File names that are never uploaded (OS/app noise).</summary>
    public static readonly IReadOnlySet<string> ExcludedFileNames =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Thumbs.db", "desktop.ini", ".DS_Store"
        };

    public static bool IsExcludedDirectoryName(string name) => ExcludedDirectoryNames.Contains(name);

    /// <summary>
    /// True when any path segment is an excluded directory, the file has an
    /// excluded name, or the path points at a hidden/reparse-point entry.
    /// </summary>
    public static bool IsExcludedPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return true;

        var name = Path.GetFileName(path);
        if (ExcludedFileNames.Contains(name)) return true;
        if (name.StartsWith("~$", StringComparison.Ordinal)) return true;

        try
        {
            var full = Path.GetFullPath(path);
            var root = Path.GetPathRoot(full) ?? "";
            var remainder = full.Substring(root.Length);
            foreach (var segment in remainder.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            {
                if (segment.Length == 0) continue;
                if (IsExcludedDirectoryName(segment)) return true;
            }
        }
        catch
        {
            return true;
        }

        try
        {
            var attrs = File.GetAttributes(path);
            if ((attrs & FileAttributes.Hidden) == FileAttributes.Hidden) return true;
            if ((attrs & FileAttributes.System) == FileAttributes.System) return true;
            if ((attrs & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint) return true;
        }
        catch
        {
            // Missing/permissionless entries are treated as not-excluded here;
            // callers that need existence check it separately.
        }

        return false;
    }

    /// <summary>
    /// Boundary-safe containment check (avoids the "photos_backup" matching a
    /// watch on "photos" false positive).
    /// </summary>
    public static bool IsWithinFolder(string basePath, string candidatePath)
    {
        if (string.IsNullOrWhiteSpace(basePath) || string.IsNullOrWhiteSpace(candidatePath)) return false;
        try
        {
            var baseFull = Path.GetFullPath(basePath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var candFull = Path.GetFullPath(candidatePath);
            if (string.Equals(baseFull, candFull, StringComparison.OrdinalIgnoreCase)) return true;

            var prefix = baseFull + Path.DirectorySeparatorChar;
            return candFull.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Which configured folder root contains this path (or null).</summary>
    public static string? FindOwningFolder(IEnumerable<string> folders, string path)
    {
        foreach (var folder in folders)
        {
            if (IsWithinFolder(folder, path)) return folder;
        }
        return null;
    }

    /// <summary>
    /// Whether the real-time watcher should accept a candidate path: belongs to
    /// a configured folder, is a media file, and is not excluded.
    /// </summary>
    public static bool ShouldWatchPath(IEnumerable<string> folders, string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        if (!MediaFileClassifier.IsMediaFile(path)) return false;
        if (IsExcludedPath(path)) return false;
        return FindOwningFolder(folders, path) is not null;
    }
}
