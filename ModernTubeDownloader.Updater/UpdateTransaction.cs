using System.Security.Cryptography;
using System.Text.Json;
using ModernTubeDownloader.Infrastructure;

namespace ModernTubeDownloader.Updates;

public sealed record UpdateBackupEntry(string RelativePath, bool Existed);
public sealed record UpdateJournal(string Installation, List<UpdateBackupEntry> Files, bool Completed);

/// <summary>Write-ahead backup per file. Only validated application package paths are eligible.</summary>
public static class UpdateTransaction
{
    public static void Install(string installation, string extracted, IReadOnlyList<string> files, Action<int>? beforeCopy = null,
        Action<DiagnosticStage, DiagnosticOutcome, int?, Exception?>? observe = null)
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
            if (!previous.Completed)
            {
                observe?.Invoke(DiagnosticStage.Recover, DiagnosticOutcome.Started, previous.Files.Count, null);
                Recover(backup, previous);
                observe?.Invoke(DiagnosticStage.Recover, DiagnosticOutcome.Completed, previous.Files.Count, null);
            }
            // This exact private backup folder is owned by the updater, never a user-data root.
            Directory.Delete(backup, true);
        }
        Directory.CreateDirectory(backup);
        var journal = new UpdateJournal(installation, [], false);
        var stage = DiagnosticStage.Journal;
        observe?.Invoke(stage, DiagnosticOutcome.Started, 0, null);
        SaveJournal(backup, journal);
        observe?.Invoke(DiagnosticStage.Replace, DiagnosticOutcome.Started, files.Count, null);
        try
        {
            for (var index = 0; index < files.Count; index++)
            {
                stage = DiagnosticStage.Replace;
                var relative = UpdatePackageSecurity.ValidateRelativePath(files[index]);
                var destination = Path.Combine(installation, relative.Replace('/', Path.DirectorySeparatorChar));
                var source = Path.Combine(extracted, relative.Replace('/', Path.DirectorySeparatorChar));
                UpdatePackageSecurity.EnsureNoReparsePoints(source);
                var saved = Path.Combine(backup, "files", relative.Replace('/', Path.DirectorySeparatorChar));
                UpdatePackageSecurity.EnsureNoReparsePoints(destination);
                var existed = File.Exists(destination);
                if (Directory.Exists(destination)) throw new IOException("A directory occupies an application file path.");
                if (existed && (File.GetAttributes(destination) & FileAttributes.ReadOnly) != 0)
                    throw new UnauthorizedAccessException("An application file is read-only; its attributes will not be changed.");
                if (existed)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(saved)!);
                    File.Copy(destination, saved, false);
                }
                journal.Files.Add(new(relative, existed));
                stage = DiagnosticStage.Journal;
                SaveJournal(backup, journal); // Durable before touching the destination.
                stage = DiagnosticStage.Replace;
                beforeCopy?.Invoke(index);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(source, destination, true);
                if ((index + 1) % 100 == 0)
                    observe?.Invoke(stage, DiagnosticOutcome.Progress, index + 1, null);
            }
            stage = DiagnosticStage.Commit;
            SaveJournal(backup, journal with { Completed = true });
            observe?.Invoke(stage, DiagnosticOutcome.Completed, files.Count, null);
        }
        catch (Exception error)
        {
            observe?.Invoke(stage, DiagnosticOutcome.Failed, journal.Files.Count, error);
            observe?.Invoke(DiagnosticStage.Rollback, DiagnosticOutcome.Started, journal.Files.Count, null);
            try
            {
                Recover(backup, journal);
                SaveJournal(backup, journal with { Completed = true });
                observe?.Invoke(DiagnosticStage.Rollback, DiagnosticOutcome.Completed, journal.Files.Count, null);
            }
            catch (Exception recovery)
            {
                observe?.Invoke(DiagnosticStage.Rollback, DiagnosticOutcome.Failed, journal.Files.Count, recovery);
                throw;
            }
            throw;
        }
    }

    private static void Recover(string backup, UpdateJournal journal)
    {
        var failures = new List<Exception>();
        foreach (var file in journal.Files.AsEnumerable().Reverse())
        {
            try
            {
                var relative = UpdatePackageSecurity.ValidateRelativePath(file.RelativePath);
                var destination = Path.Combine(journal.Installation, relative.Replace('/', Path.DirectorySeparatorChar));
                UpdatePackageSecurity.EnsureNoReparsePoints(destination);
                if (file.Existed)
                {
                    var saved = Path.Combine(backup, "files", relative.Replace('/', Path.DirectorySeparatorChar));
                    UpdatePackageSecurity.EnsureNoReparsePoints(saved);
                    // A denied copy may leave the destination intact. Do not write
                    // it again: its lock could otherwise prevent all earlier restores.
                    if (!SameContents(saved, destination)) File.Copy(saved, destination, true);
                }
                else if (File.Exists(destination)) File.Delete(destination);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            { failures.Add(error); }
        }
        if (failures.Count != 0) throw new AggregateException("Some application files could not be restored; the recovery journal remains incomplete.", failures);
    }

    private static bool SameContents(string saved, string destination)
    {
        if (!File.Exists(destination) || new FileInfo(saved).Length != new FileInfo(destination).Length) return false;
        using var original = new FileStream(saved, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var current = new FileStream(destination, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return CryptographicOperations.FixedTimeEquals(SHA256.HashData(original), SHA256.HashData(current));
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
