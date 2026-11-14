using System.Text;

namespace ModernTubeDownloader.Infrastructure;

/// <summary>Lexical and existing-ancestor checks for explicitly selected per-list destinations.</summary>
public static class DownloadOutputPolicy
{
    public static string ForList(string root, string listName)
    {
        var parent = NormalizeDirectory(root);
        var name = Path.GetFileName(listName).Normalize(NormalizationForm.FormC);
        var invalid = Path.GetInvalidFileNameChars();
        name = new string(name.Select(c => invalid.Contains(c) || c is '/' or '\\' || char.IsControl(c) ? '_' : c).ToArray()).Trim().TrimEnd('.', ' ');
        if (string.IsNullOrWhiteSpace(name) || name is "." or "..") name = "Import";
        if (Reserved(name)) name = "_" + name;
        if (name.Length > 80) name = name[..80].TrimEnd('.', ' ');
        var result = NormalizeDirectory(Path.Combine(parent, name));
        if (!result.StartsWith(parent.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Batch.Error.Destination");
        return result;
    }

    public static string NormalizeDirectory(string directory)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(directory) || !Path.IsPathFullyQualified(directory) ||
                directory.StartsWith("\\\\?\\", StringComparison.Ordinal) || directory.StartsWith("\\\\.\\", StringComparison.Ordinal))
                throw new InvalidDataException("Batch.Error.Destination");
            var input = directory.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
            var root = Path.GetPathRoot(input)!;
            var parts = input[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
                if (part is "." or ".." || part.EndsWith(' ') || part.EndsWith('.') || Reserved(part) ||
                    part.Any(c => Path.GetInvalidFileNameChars().Contains(c) || char.IsControl(c)))
                    throw new InvalidDataException("Batch.Error.Destination");
            var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(input).Normalize(NormalizationForm.FormC));
            if (full.Length > 240) throw new InvalidDataException("Batch.Error.Destination");
            // Preserve the canonical storage latch classification if a prepared
            // job's drive/share disappears between import and execution.
            if (!Directory.Exists(root)) throw new DirectoryNotFoundException("Batch.Error.Destination");
            for (var ancestor = full; !string.IsNullOrEmpty(ancestor); ancestor = Path.GetDirectoryName(ancestor))
            {
                if (File.Exists(ancestor)) throw new InvalidDataException("Batch.Error.Destination");
                if (Directory.Exists(ancestor) && (File.GetAttributes(ancestor) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("Batch.Error.Destination");
            }
            return full;
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException)
        { throw new InvalidDataException("Batch.Error.Destination", error); }
    }

    private static bool Reserved(string name)
    {
        var stem = name.Split('.')[0].ToUpperInvariant();
        return stem is "CON" or "PRN" or "AUX" or "NUL" or "CONIN$" or "CONOUT$" ||
            stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal) || stem.StartsWith("LPT", StringComparison.Ordinal)) &&
            (stem[3] is >= '0' and <= '9' or '¹' or '²' or '³');
    }
}
