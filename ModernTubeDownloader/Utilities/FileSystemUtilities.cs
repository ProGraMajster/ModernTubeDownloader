using ModernTubeDownloader.Settings;

namespace ModernTubeDownloader.Utilities;

public static class FileSystemUtilities
{
    public static void EnsureWritableDirectory(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Directory.CreateDirectory(path);
        var probe = Path.Combine(path, $".mtd-write-{Guid.NewGuid():N}.tmp");
        using (File.Create(probe, 1, FileOptions.DeleteOnClose)) { }
    }

    public static string ResolveFinalPath(string directory, string baseName, string extension, FileConflictBehavior behavior, bool overwrite)
    {
        Directory.CreateDirectory(directory);
        baseName = string.IsNullOrWhiteSpace(baseName) ? "download" : baseName;
        extension = extension.Trim().TrimStart('.');
        var candidate = Path.Combine(directory, $"{baseName}.{extension}");
        if (!File.Exists(candidate) || overwrite || behavior == FileConflictBehavior.Overwrite)
            return candidate;
        if (behavior == FileConflictBehavior.Fail)
            throw new IOException($"The destination file already exists: {candidate}");

        for (var index = 2; index < 10_000; index++)
        {
            candidate = Path.Combine(directory, $"{baseName} ({index}).{extension}");
            if (!File.Exists(candidate))
                return candidate;
        }

        throw new IOException("Could not find an unused destination filename.");
    }

    public static string SafeIdentifier(string value)
    {
        var filtered = new string(value.Where(character => char.IsLetterOrDigit(character) || character is '-' or '_').ToArray());
        return string.IsNullOrWhiteSpace(filtered) ? Guid.NewGuid().ToString("N") : filtered[..Math.Min(filtered.Length, 80)];
    }
}
