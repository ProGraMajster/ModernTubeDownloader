using System.Text;

namespace ModernTubeDownloader.Organizer;

internal static class OrganizerPathSafety
{
    internal static readonly StringComparer Comparer = StringComparer.OrdinalIgnoreCase;
    internal static string DirectoryPath(string path, bool mustExist = false)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path) || path.StartsWith("\\\\", StringComparison.Ordinal) ||
            path.StartsWith("\\?", StringComparison.Ordinal) || path.IndexOf(':', 2) >= 0) throw new IOException("Organizer.Error.Path");
        foreach (var raw in path[Path.GetPathRoot(path)!.Length..].TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                     .Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.None)) ValidateName(raw);
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path.Normalize(NormalizationForm.FormC)));
        var root = Path.GetPathRoot(full)!;
        if (Comparer.Equals(full, Path.TrimEndingDirectorySeparator(root))) throw new IOException("Organizer.Error.Root");
        foreach (var segment in full[root.Length..].Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) ValidateName(segment);
        CheckAncestors(full);
        if (File.Exists(full) || mustExist && !Directory.Exists(full)) throw new IOException("Organizer.Error.Path");
        return full;
    }

    internal static void ValidateName(string name)
    {
        if (name.Length == 0 || name is "." or ".." || name.EndsWith('.') || name.EndsWith(' ') || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new IOException("Organizer.Error.Path");
        var device = name.Split('.')[0].ToUpperInvariant();
        if (device is "CON" or "PRN" or "AUX" or "NUL" or "CONIN$" or "CONOUT$" ||
            device.Length == 4 && (device.StartsWith("COM") || device.StartsWith("LPT")) && "123456789¹²³".Contains(device[3])) throw new IOException("Organizer.Error.Path");
    }

    internal static bool IsWithin(string path, string root) =>
        Path.GetFullPath(path).StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    internal static void CheckFile(string file, string root)
    {
        if (!IsWithin(file, root)) throw new IOException("Organizer.Error.OutsideRoot");
        ValidateName(Path.GetFileName(file)); CheckAncestors(file);
        if (Directory.Exists(file)) throw new IOException("Organizer.Error.Path");
    }

    internal static void CheckAncestors(string path)
    {
        for (var current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
        {
            var root = Path.GetPathRoot(current);
            if (Comparer.Equals(current, root)) break;
            if (File.Exists(current) || Directory.Exists(current))
            {
                var attributes = File.GetAttributes(current);
                if ((attributes & (FileAttributes.ReparsePoint | FileAttributes.System)) != 0) throw new IOException("Organizer.Error.Reparse");
            }
        }
    }

    internal static string ListFolder(string root, string name)
    {
        root = DirectoryPath(root);
        var cleaned = new string(name.Normalize(NormalizationForm.FormC).Select(c => Path.GetInvalidFileNameChars().Contains(c) || c is '/' or '\\' ? '_' : c).ToArray()).Trim().TrimEnd('.');
        if (cleaned.Length > 100) cleaned = cleaned[..100].TrimEnd('.', ' ');
        if (string.IsNullOrWhiteSpace(cleaned) || cleaned is "." or "..") cleaned = "Import";
        try { ValidateName(cleaned); } catch (IOException) { cleaned = "_" + cleaned; ValidateName(cleaned); }
        return DirectoryPath(Path.Combine(root, cleaned));
    }
}
