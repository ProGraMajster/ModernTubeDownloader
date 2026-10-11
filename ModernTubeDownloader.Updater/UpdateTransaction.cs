using System.Text.Json;

namespace ModernTubeDownloader.Updates;

public sealed record UpdateBackupEntry(string RelativePath, bool Existed);
public sealed record UpdateJournal(string Installation, List<UpdateBackupEntry> Files, bool Completed);

/// <summary>Write-ahead backup per file. Only validated application package paths are eligible.</summary>
public static class UpdateTransaction
{
    public static void Install(string installation, string extracted, IReadOnlyList<string> files, Action<int>? beforeCopy = null)
    {
        installation = Path.GetFullPath(installation);
        UpdatePackageSecurity.EnsureNoReparsePoints(installation);
        UpdatePackageSecurity.EnsureNoReparsePoints(extracted);
        if (Path.GetPathRoot(installation) == installation || !File.Exists(Path.Combine(installation, "ModernTubeDownloader.exe")))
            throw new InvalidDataException("An existing application installation is required.");
        var backup = Path.Combine(installation, ".mtd-update-backup");
        UpdatePackageSecurity.EnsureNoReparsePoints(backup);
        if (Directory.Exists(backup))
        {
            var previous = ReadJournal(backup);
            if (!Path.GetFullPath(previous.Installation).Equals(installation, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The backup belongs to another installation.");
            if (!previous.Completed) Recover(backup, previous);
            // This exact private backup folder is owned by the updater, never a user-data root.
            Directory.Delete(backup, true);
        }
        Directory.CreateDirectory(backup);
        var journal = new UpdateJournal(installation, [], false);
        SaveJournal(backup, journal);
        try
        {
            for (var index = 0; index < files.Count; index++)
            {
                var relative = UpdatePackageSecurity.ValidateRelativePath(files[index]);
                var destination = Path.Combine(installation, relative.Replace('/', Path.DirectorySeparatorChar));
                var source = Path.Combine(extracted, relative.Replace('/', Path.DirectorySeparatorChar));
                UpdatePackageSecurity.EnsureNoReparsePoints(source);
                var saved = Path.Combine(backup, "files", relative.Replace('/', Path.DirectorySeparatorChar));
                UpdatePackageSecurity.EnsureNoReparsePoints(destination);
                var existed = File.Exists(destination);
                if (Directory.Exists(destination)) throw new IOException("A directory occupies an application file path.");
                if (existed)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(saved)!);
                    File.Copy(destination, saved, false);
                }
                journal.Files.Add(new(relative, existed));
                SaveJournal(backup, journal); // Durable before touching the destination.
                beforeCopy?.Invoke(index);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(source, destination, true);
            }
            SaveJournal(backup, journal with { Completed = true });
        }
        catch
        {
            Recover(backup, journal);
            SaveJournal(backup, journal with { Completed = true });
            throw;
        }
    }

    private static void Recover(string backup, UpdateJournal journal)
    {
        foreach (var file in journal.Files.AsEnumerable().Reverse())
        {
            var relative = UpdatePackageSecurity.ValidateRelativePath(file.RelativePath);
            var destination = Path.Combine(journal.Installation, relative.Replace('/', Path.DirectorySeparatorChar));
            UpdatePackageSecurity.EnsureNoReparsePoints(destination);
            if (file.Existed) File.Copy(Path.Combine(backup, "files", relative.Replace('/', Path.DirectorySeparatorChar)), destination, true);
            else if (File.Exists(destination)) File.Delete(destination);
        }
    }
    private static UpdateJournal ReadJournal(string backup) => JsonSerializer.Deserialize<UpdateJournal>(File.ReadAllText(Path.Combine(backup, "journal.json")))
        ?? throw new InvalidDataException("Invalid update journal.");
    private static void SaveJournal(string backup, UpdateJournal journal)
    {
        var target = Path.Combine(backup, "journal.json");
        var temporary = target + ".tmp";
        using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        { JsonSerializer.Serialize(stream, journal); stream.Flush(true); }
        File.Move(temporary, target, true);
    }
}
