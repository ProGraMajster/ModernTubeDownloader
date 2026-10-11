using System.IO.Compression;
using System.Security.Cryptography;

namespace ModernTubeDownloader.Updates;

/// <summary>Shared package contract; no GUI, registry, cookie, queue or AppData services.</summary>
public static class UpdatePackageSecurity
{
    public const long MaximumArchiveBytes = 1_000_000_000;
    public const long MaximumExpandedBytes = 1_500_000_000;
    public const int MaximumEntries = 5000;
    private static readonly HashSet<string> AllowedDirectories = new(StringComparer.OrdinalIgnoreCase) { "native-host", "updater", "licenses" };
    private static readonly HashSet<string> ForbiddenFiles = new(StringComparer.OrdinalIgnoreCase)
    { "settings.json", "queue.json", "history.json", "live-sessions.json", "cookies.txt", "state.json", "appsettings.json" };
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    { ".exe", ".dll", ".json", ".dat", ".pri", ".png", ".ico", ".md", ".txt", ".config", ".bin", ".nuspec" };

    public static async Task VerifyAsync(string archive, string expectedHash, CancellationToken token = default)
    {
        if (expectedHash.Length != 64 || !expectedHash.All(Uri.IsHexDigit)) throw new InvalidDataException("Invalid SHA-256 checksum.");
        await using var stream = new FileStream(archive, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length is <= 0 or > MaximumArchiveBytes) throw new InvalidDataException("Archive size exceeds the update limit.");
        var hash = await SHA256.HashDataAsync(stream, token);
        if (!CryptographicOperations.FixedTimeEquals(hash, Convert.FromHexString(expectedHash))) throw new InvalidDataException("Update checksum mismatch.");
    }

    public static string ValidateRelativePath(string value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 240 || value.Contains('\\') || value.Contains(':') ||
            value.StartsWith('/') || value.Any(char.IsControl)) throw new InvalidDataException("Unsafe package path.");
        var parts = value.TrimEnd('/').Split('/');
        if (parts.Length > 8 || parts.Any(part => string.IsNullOrEmpty(part) || part is "." or ".." ||
            part.EndsWith('.') || part.EndsWith(' ') || part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || IsDevice(part)))
            throw new InvalidDataException("Unsafe package path component.");
        if (parts.Length > 1 && !AllowedDirectories.Contains(parts[0])) throw new InvalidDataException("Unexpected package directory.");
        if (ForbiddenFiles.Contains(parts[^1]) || parts[^1].StartsWith("cookies", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("User data is forbidden in an update.");
        if (!value.EndsWith('/') && parts[^1] != "LICENSE" && !AllowedExtensions.Contains(Path.GetExtension(parts[^1])))
            throw new InvalidDataException("Unexpected package file type.");
        return string.Join('/', parts);
    }

    private static bool IsDevice(string part)
    {
        var name = part.Split('.')[0].ToUpperInvariant();
        return name is "CON" or "PRN" or "AUX" or "NUL" || name.Length == 4 &&
            (name.StartsWith("COM", StringComparison.Ordinal) || name.StartsWith("LPT", StringComparison.Ordinal)) && name[3] is >= '1' and <= '9';
    }

    public static void EnsureNoReparsePoints(string path)
    {
        for (var current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Reparse points are not permitted in update paths.");
    }

    public static async Task<IReadOnlyList<string>> ExtractAsync(string archive, string destination, CancellationToken token = default)
    {
        EnsureNoReparsePoints(destination);
        if (Directory.Exists(destination) && Directory.EnumerateFileSystemEntries(destination).Any()) throw new IOException("Extraction requires an empty directory.");
        using var zip = ZipFile.OpenRead(archive);
        if (zip.Entries.Count is 0 or > MaximumEntries) throw new InvalidDataException("Invalid package entry count.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var files = new List<(ZipArchiveEntry Entry, string Relative)>();
        long expanded = 0;
        foreach (var entry in zip.Entries)
        {
            var relative = ValidateRelativePath(entry.FullName);
            if (!names.Add(relative) || (entry.ExternalAttributes >> 16 & 0xF000) == 0xA000)
                throw new InvalidDataException("Duplicate paths and symbolic links are forbidden.");
            if (entry.FullName.EndsWith('/')) continue;
            if (entry.Length is < 0 or > 256_000_000 || (expanded += entry.Length) > MaximumExpandedBytes)
                throw new InvalidDataException("Expanded update size exceeds the limit.");
            files.Add((entry, relative));
        }
        if (!files.Any(file => file.Relative.Equals("ModernTubeDownloader.exe", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("Application executable missing from update.");
        Directory.CreateDirectory(destination);
        foreach (var (entry, relative) in files)
        {
            token.ThrowIfCancellationRequested();
            var target = Path.Combine(destination, relative.Replace('/', Path.DirectorySeparatorChar));
            EnsureNoReparsePoints(target);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await using var input = entry.Open();
            await using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            await input.CopyToAsync(output, token);
            if (output.Length != entry.Length) throw new InvalidDataException("Truncated package entry.");
        }
        return files.Select(file => file.Relative).ToArray();
    }
}
