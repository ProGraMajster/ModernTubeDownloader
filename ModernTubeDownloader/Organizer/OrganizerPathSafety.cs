namespace ModernTubeDownloader.Organizer;

internal static class OrganizerPathSafety
{
    internal static readonly StringComparer Comparer = StringComparer.OrdinalIgnoreCase;

    internal static string DirectoryPath(string path, bool mustExist = false, bool allowRoot = false)
    {
        var full = LocalPath(path);
        var root = Path.GetPathRoot(full)!;
        if (!allowRoot && Comparer.Equals(full, root)) throw new IOException("Organizer.Error.Root");
        var drive = new DriveInfo(root);
        if (drive.DriveType == DriveType.Network) throw new IOException("Organizer.Error.PathType");
        if (drive.DriveType is DriveType.NoRootDirectory or DriveType.Unknown)
            throw new IOException("Organizer.Error.DriveUnavailable");
        File.GetAttributes(root); // Preserve access and not-ready errors from the volume.

        var attributes = CheckAncestors(full, directoriesOnly: true);
        if (mustExist && attributes is null) throw new IOException("Organizer.Error.FolderMissing");
        return full;
    }

    internal static string LocalPath(string path)
    {
        // Require a drive and a separator before GetFullPath can resolve aliases.
        // This also excludes UNC and device namespaces using alternate separators.
        if (string.IsNullOrEmpty(path) || path.Length < 3 || !char.IsAsciiLetter(path[0]) || path[1] != ':' ||
            path[2] is not ('\\' or '/'))
            throw new IOException("Organizer.Error.PathType");

        foreach (var segment in path[3..].Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries))
            ValidateName(segment);

        // NTFS distinguishes canonically equivalent Unicode names. Preserve the
        // original spelling when resolving existing paths and journal entries.
        try { return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)); }
        catch (ArgumentException error) { throw new IOException("Organizer.Error.PathNormalization", error); }
        catch (PathTooLongException error) { throw new IOException("Organizer.Error.PathNormalization", error); }
    }

    internal static void ValidateName(string name)
    {
        if (name is "." or "..") throw new IOException("Organizer.Error.PathTraversal");
        if (name.Length is 0 or > 255 || name.EndsWith('.') || name.EndsWith(' ') ||
            name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new IOException("Organizer.Error.FolderName");
        var dot = name.IndexOf('.');
        var device = (dot < 0 ? name : name[..dot]).TrimEnd(' ').ToUpperInvariant();
        if (device is "CON" or "PRN" or "AUX" or "NUL" or "CONIN$" or "CONOUT$" ||
            device.Length == 4 && (device.StartsWith("COM", StringComparison.Ordinal) || device.StartsWith("LPT", StringComparison.Ordinal)) &&
            "123456789¹²³".Contains(device[3]))
            throw new IOException("Organizer.Error.FolderName");
    }

    internal static bool IsWithin(string path, string root)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        if (Comparer.Equals(full, root)) return false;
        var prefix = Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar;
        return full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    internal static void CheckFile(string file, string root)
    {
        var full = LocalPath(file);
        if (!Comparer.Equals(full, file) || !IsWithin(full, root)) throw new IOException("Organizer.Error.OutsideRoot");
        if (CheckAncestors(full) is { } attributes && (attributes & FileAttributes.Directory) != 0)
            throw new IOException("Organizer.Reason.Collision");
    }

    internal static FileAttributes? CheckAncestors(string path, bool directoriesOnly = false)
    {
        var full = Path.GetFullPath(path);
        var root = Path.GetPathRoot(full)!;
        if (Comparer.Equals(full, root)) return FileAttributes.Directory;

        // Inspect parent directories before touching any descendant of a link.
        var current = root;
        var attributes = FileAttributes.Directory;
        foreach (var segment in full[root.Length..].Split(Path.DirectorySeparatorChar))
        {
            current = Path.Combine(current, segment);
            try { attributes = File.GetAttributes(current); }
            catch (FileNotFoundException) { return null; }
            catch (DirectoryNotFoundException) { return null; }

            if ((attributes & (FileAttributes.ReparsePoint | FileAttributes.System)) != 0)
                throw new IOException("Organizer.Error.Reparse");
            if ((directoriesOnly || current != full) && (attributes & FileAttributes.Directory) == 0)
                throw new IOException("Organizer.Error.NotDirectory");
        }
        return attributes;
    }

    internal static string ListName(string name)
    {
        string normalized;
        try { normalized = name.Normalize(); }
        catch (ArgumentException error) { throw new IOException("Organizer.Error.PathNormalization", error); }

        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(normalized.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        if (cleaned.Length > 100)
            cleaned = cleaned[..(char.IsHighSurrogate(cleaned[99]) ? 99 : 100)];
        cleaned = cleaned.TrimEnd('.', ' ');
        if (cleaned.Length == 0) cleaned = "Import";
        try { ValidateName(cleaned); }
        catch (IOException) { cleaned = "_" + cleaned; ValidateName(cleaned); }

        return cleaned;
    }
}
