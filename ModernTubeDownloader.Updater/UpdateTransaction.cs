using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

[assembly: InternalsVisibleTo("ModernTubeDownloader.Tests")]

namespace ModernTubeDownloader.Updates;

public sealed record UpdateBackupEntry([property: JsonRequired] string RelativePath, [property: JsonRequired] bool Existed);
public sealed record UpdateJournal([property: JsonRequired] string Installation,
    [property: JsonRequired] List<UpdateBackupEntry> Files, [property: JsonRequired] bool Completed);

public enum UpdateRecoveryStatus { Unknown, Partial, Restored }

public sealed record UpdateFailure(string Operation, Exception Error, string? RelativePath = null);

public sealed class UpdateTransactionException(UpdateRecoveryStatus recovery, UpdateFailure failure,
    IReadOnlyList<UpdateFailure> recoveryFailures)
    : IOException("The application update transaction failed.", failure.Error)
{
    public UpdateRecoveryStatus Recovery { get; } = recovery;
    public UpdateFailure Failure { get; } = failure;
    public IReadOnlyList<UpdateFailure> RecoveryFailures { get; } = recoveryFailures;
}

/// <summary>Write-ahead backup per file. Only validated application package paths are eligible.</summary>
public static class UpdateTransaction
{
    public static void Install(string installation, string extracted, IReadOnlyList<string> files, Action<int>? beforeCopy = null) =>
        Install(installation, extracted, files, beforeCopy, null);

    // Test seam at the real publication boundary. The caller can hold a file
    // handle for only this move, without timing or altering the production path.
    internal static void Install(string installation, string extracted, IReadOnlyList<string> files,
        Action<int>? beforeCopy, Func<string, IDisposable?>? holdJournalForReplacement)
    {
        var operation = "validate-installation";
        try
        {
            installation = Path.TrimEndingDirectorySeparator(Path.GetFullPath(installation));
            UpdatePackageSecurity.EnsureNoReparsePoints(installation);
            UpdatePackageSecurity.EnsureNoReparsePoints(extracted);
            if (Path.GetPathRoot(installation) == installation)
                throw new InvalidDataException("An existing application installation is required.");

            operation = "lock-installation";
            var pathIdentity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(installation.ToUpperInvariant())));
            using var ownership = new Mutex(false, @"Global\ModernTubeDownloader.Update." + pathIdentity);
            bool acquired;
            try { acquired = ownership.WaitOne(0); }
            catch (AbandonedMutexException) { acquired = true; }
            if (!acquired) throw new IOException("Another updater is handling this installation.");
            try
            {
                operation = "prepare-installation";
                InstallOwned(installation, extracted, files, beforeCopy, holdJournalForReplacement);
            }
            finally { ownership.ReleaseMutex(); }
        }
        catch (Exception error) when (error is not UpdateTransactionException)
        {
            throw new UpdateTransactionException(UpdateRecoveryStatus.Unknown, Describe(operation, error), []);
        }
    }

    private static void InstallOwned(string installation, string extracted, IReadOnlyList<string> files,
        Action<int>? beforeCopy, Func<string, IDisposable?>? holdJournalForReplacement)
    {
        const string executableName = "ModernTubeDownloader.exe";
        var executable = Path.Combine(installation, executableName);
        UpdatePackageSecurity.EnsureNoReparsePoints(executable);
        var backup = Path.Combine(installation, ".mtd-update-backup");
        UpdatePackageSecurity.EnsureNoReparsePoints(backup);
        if (Directory.Exists(backup))
        {
            UpdateJournal previous;
            try { previous = ReadJournal(backup, installation); }
            catch (Exception error)
            {
                throw new UpdateTransactionException(UpdateRecoveryStatus.Unknown, new("read-journal", error), []);
            }
            if (!previous.Completed)
            {
                if (!File.Exists(executable) && !previous.Files.Any(file => file.Existed &&
                    file.RelativePath.Equals(executableName, StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidDataException("The journal cannot recover a missing application executable.");
                var recovery = RollBack(backup, previous);
                if (recovery.Status != UpdateRecoveryStatus.Restored)
                {
                    var failure = new UpdateFailure("recover-previous-transaction",
                        new IOException("The previous transaction could not be recovered and verified."));
                    throw new UpdateTransactionException(recovery.Status, failure, recovery.Failures);
                }
            }
            if (!File.Exists(executable)) throw new InvalidDataException("An existing application installation is required.");
            // Retire the completed journal atomically before deleting its backup files.
            // An interrupted cleanup must never leave a live folder with half its contents missing.
            var retired = backup + "-completed-" + Guid.NewGuid().ToString("N");
            Directory.Move(backup, retired);
            Directory.Delete(retired, true);
        }
        else if (!File.Exists(executable))
        {
            throw new InvalidDataException("An existing application installation is required.");
        }

        var journal = new UpdateJournal(installation, [], false);
        InitializeJournal(backup, journal);
        var operation = "prepare-file";
        string? relative = null;
        try
        {
            for (var index = 0; index < files.Count; index++)
            {
                operation = "prepare-file";
                relative = null;
                relative = UpdatePackageSecurity.ValidateRelativePath(files[index]);
                var destination = FilePath(installation, relative);
                var source = FilePath(extracted, relative);
                UpdatePackageSecurity.EnsureNoReparsePoints(source);
                UpdatePackageSecurity.EnsureNoReparsePoints(destination);
                var attributes = ReadAttributes(destination);
                var existed = attributes.HasValue;
                if ((attributes & FileAttributes.Directory) == FileAttributes.Directory)
                    throw new IOException("A directory occupies an application file path.");
                if ((attributes & FileAttributes.ReadOnly) == FileAttributes.ReadOnly)
                    throw new UnauthorizedAccessException("An application file is read-only; its attributes will not be changed.");
                if (existed)
                {
                    operation = "backup-file";
                    var saved = FilePath(Path.Combine(backup, "files"), relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(saved)!);
                    CopyDurably(destination, saved, false);
                }
                journal.Files.Add(new(relative, existed));
                SaveJournal(backup, journal, holdJournalForReplacement); // Durable before touching the destination.
                operation = "install-file";
                beforeCopy?.Invoke(index);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                CopyDurably(source, destination, true);
            }
            relative = null;
            SaveJournal(backup, journal with { Completed = true });
        }
        catch (Exception error)
        {
            var failure = Describe(operation, error, relative);
            var recovery = RollBack(backup, journal);
            throw new UpdateTransactionException(recovery.Status, failure, recovery.Failures);
        }
    }

    private static void InitializeJournal(string backup, UpdateJournal journal)
    {
        // Establish the first journal before publishing the active backup directory.
        // A failure here cannot leave a journal-less active transaction to block a retry.
        var staging = backup + "-init-" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(staging);
        try
        {
            SaveJournal(staging, journal);
            Directory.Move(staging, backup);
        }
        catch (Exception error)
        {
            var failure = Describe("initialize-journal", error);
            List<UpdateFailure> cleanup = [];
            try { Directory.Delete(staging, true); }
            catch (Exception cleanupError) { cleanup.Add(new("remove-unused-journal", cleanupError)); }
            throw new UpdateTransactionException(UpdateRecoveryStatus.Unknown, failure, cleanup);
        }
    }

    private static (UpdateRecoveryStatus Status, List<UpdateFailure> Failures) RollBack(string backup, UpdateJournal journal)
    {
        List<UpdateFailure> failures = [];
        var verifiedFiles = 0;
        foreach (var file in journal.Files.AsEnumerable().Reverse())
        {
            try
            {
                var destination = FilePath(journal.Installation, file.RelativePath);
                UpdatePackageSecurity.EnsureNoReparsePoints(destination);
                if (file.Existed)
                {
                    var saved = FilePath(Path.Combine(backup, "files"), file.RelativePath);
                    UpdatePackageSecurity.EnsureNoReparsePoints(saved);
                    // If a failed copy left the original bytes intact, avoid writing a locked file.
                    if (!SameContents(saved, destination))
                    {
                        CopyDurably(saved, destination, true);
                        if (!SameContents(saved, destination))
                            throw new IOException("The restored file could not be verified.");
                    }
                }
                else
                {
                    var attributes = ReadAttributes(destination);
                    if (attributes.HasValue) File.Delete(destination);
                    if (ReadAttributes(destination).HasValue)
                        throw new IOException("The newly installed file could not be removed.");
                }
                verifiedFiles++;
            }
            catch (Exception error)
            {
                failures.Add(new("restore-file", error, file.RelativePath));
            }
        }
        if (failures.Count != 0)
            return (verifiedFiles > 0 ? UpdateRecoveryStatus.Partial : UpdateRecoveryStatus.Unknown, failures);

        try { SaveJournal(backup, journal with { Completed = true }); }
        catch (Exception error)
        {
            // The bytes were verified, but durable completion was not. Keep the readable
            // incomplete journal so the next attempt must verify recovery again.
            failures.Add(Describe("complete-recovery", error));
            return (UpdateRecoveryStatus.Unknown, failures);
        }
        return (UpdateRecoveryStatus.Restored, failures);
    }

    private static void CopyDurably(string source, string destination, bool overwrite)
    {
        File.Copy(source, destination, overwrite);
        using var file = new FileStream(destination, FileMode.Open, FileAccess.Write, FileShare.None);
        file.Flush(true);
    }

    private static FileAttributes? ReadAttributes(string path)
    {
        try { return File.GetAttributes(path); }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }

    private static string FilePath(string directory, string relative) =>
        Path.Combine(directory, relative.Replace('/', Path.DirectorySeparatorChar));

    private static bool SameContents(string saved, string destination)
    {
        if (!ReadAttributes(destination).HasValue || new FileInfo(saved).Length != new FileInfo(destination).Length) return false;
        using var original = new FileStream(saved, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var current = new FileStream(destination, FileMode.Open, FileAccess.Read, FileShare.Read);
        return CryptographicOperations.FixedTimeEquals(SHA256.HashData(original), SHA256.HashData(current));
    }

    private static UpdateJournal ReadJournal(string backup, string installation)
    {
        var path = Path.Combine(backup, "journal.json");
        UpdatePackageSecurity.EnsureNoReparsePoints(path);
        var journal = JsonSerializer.Deserialize<UpdateJournal>(File.ReadAllText(path));
        if (journal?.Installation is null || journal.Files is null ||
            !Path.TrimEndingDirectorySeparator(Path.GetFullPath(journal.Installation)).Equals(installation, StringComparison.OrdinalIgnoreCase) ||
            journal.Files.Count > UpdatePackageSecurity.MaximumEntries)
            throw new InvalidDataException("Invalid update journal or installation.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in journal.Files)
        {
            if (file?.RelativePath is null || UpdatePackageSecurity.ValidateRelativePath(file.RelativePath) != file.RelativePath ||
                !names.Add(file.RelativePath))
                throw new InvalidDataException("Invalid or repeated file in the update journal.");
        }
        return journal with { Installation = installation };
    }

    private static void SaveJournal(string backup, UpdateJournal journal, Func<string, IDisposable?>? holdForReplacement = null)
    {
        var operation = "write-journal";
        try
        {
            var target = Path.Combine(backup, "journal.json");
            var temporary = target + ".tmp";
            UpdatePackageSecurity.EnsureNoReparsePoints(target);
            UpdatePackageSecurity.EnsureNoReparsePoints(temporary);
            using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, journal);
                stream.Flush(true);
            }
            operation = "replace-journal";
            using var held = holdForReplacement?.Invoke(target);
            File.Move(temporary, target, true);
        }
        catch (Exception error) { throw new JournalWriteException(operation, error); }
    }

    private static UpdateFailure Describe(string operation, Exception error, string? relative = null) =>
        error is JournalWriteException journalError
            ? new(journalError.Operation, journalError.InnerException!, relative)
            : new(operation, error, relative);

    private sealed class JournalWriteException(string operation, Exception inner) : IOException(operation, inner)
    {
        public string Operation { get; } = operation;
    }
}
