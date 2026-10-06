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

    public static string ResolveFinalPath(string directory, string baseName, string extension, FileConflictBehavior behavior,
        bool overwrite, IReadOnlyList<string>? companionSuffixes = null)
    {
        Directory.CreateDirectory(directory);
        baseName = string.IsNullOrWhiteSpace(baseName) ? "download" : baseName;
        extension = extension.Trim().TrimStart('.');
        companionSuffixes ??= [];
        var candidate = Path.Combine(directory, $"{baseName}.{extension}");
        if (overwrite || behavior == FileConflictBehavior.Overwrite || IsAvailable(baseName))
            return candidate;
        if (behavior == FileConflictBehavior.Fail)
            throw new IOException($"The destination file or a companion file already exists: {candidate}");

        for (var index = 2; index < 10_000; index++)
        {
            var numberedBase = $"{baseName} ({index})";
            candidate = Path.Combine(directory, $"{numberedBase}.{extension}");
            if (IsAvailable(numberedBase))
                return candidate;
        }

        throw new IOException("Could not find an unused destination filename.");

        bool IsAvailable(string candidateBase) =>
            !File.Exists(Path.Combine(directory, $"{candidateBase}.{extension}")) &&
            companionSuffixes.All(suffix => !File.Exists(Path.Combine(directory, $"{candidateBase}.{suffix}")));
    }

    public static string SafeIdentifier(string value)
    {
        var filtered = new string(value.Where(character => char.IsLetterOrDigit(character) || character is '-' or '_').ToArray());
        return string.IsNullOrWhiteSpace(filtered) ? Guid.NewGuid().ToString("N") : filtered[..Math.Min(filtered.Length, 80)];
    }
}
