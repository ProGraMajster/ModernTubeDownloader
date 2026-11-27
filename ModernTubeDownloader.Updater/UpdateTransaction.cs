using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ModernTubeDownloader.Infrastructure;

namespace ModernTubeDownloader.Updates;

public sealed record UpdateBackupEntry([property: JsonRequired] string RelativePath, [property: JsonRequired] bool Existed,
    string? OriginalSha256 = null, string? AddedSha256 = null);
public sealed record UpdateJournal([property: JsonRequired] string Installation,
    [property: JsonRequired] List<UpdateBackupEntry> Files, [property: JsonRequired] bool Completed, bool UsesWriteMarkers = false);

public enum UpdateRecoveryStatus { Unknown, Partial, Restored }

internal enum UpdateOperation
{
    ValidateInstallation = 1, LockInstallation, PrepareInstallation, ReadJournal, RecoverPreviousTransaction,
    RetireJournal, InitializeJournal, PrepareFile, BackupFile, WriteJournal, ReplaceJournal, InstallFile, RestoreFile,
    RemoveUnusedJournal, PublishResult, Restart, UnverifiedLegacyJournal
}

internal sealed record UpdateFailure(UpdateOperation Operation, Exception Error);

internal sealed class UpdateTransactionException(UpdateRecoveryStatus recovery, UpdateFailure failure,
    IReadOnlyList<UpdateFailure> recoveryFailures)
    : IOException("The application update transaction failed.", failure.Error)
{
    public UpdateRecoveryStatus Recovery { get; } = recovery;
    public UpdateFailure Failure { get; } = failure;
    public IReadOnlyList<UpdateFailure> RecoveryFailures { get; } = recoveryFailures;
}

/// <summary>Publish the verified backup plan before changing any installed file.</summary>
public static class UpdateTransaction
{
    private static readonly JsonSerializerOptions JournalReadOptions = new() { AllowDuplicateProperties = false };
    public static void Install(string installation, string extracted, IReadOnlyList<string> files, Action<int>? beforeCopy = null) =>
        Install(installation, extracted, files, beforeCopy, null);

    internal static void Install(string installation, string extracted, IReadOnlyList<string> files,
        Action<int>? beforeCopy, Action<DiagnosticStage, DiagnosticOutcome, DiagnosticCounters?, Exception?>? observe,
        Action<string>? beforeCommit = null)
    {
        var operation = UpdateOperation.ValidateInstallation;
        try
        {
            installation = Path.TrimEndingDirectorySeparator(Path.GetFullPath(installation));
            UpdatePackageSecurity.EnsureNoReparsePoints(installation);
            UpdatePackageSecurity.EnsureNoReparsePoints(extracted);
            if (Path.GetPathRoot(installation) == installation)
                throw new InvalidDataException("An existing application installation is required.");

            operation = UpdateOperation.LockInstallation;
            using var owner = new Mutex(false, UpdatePackageSecurity.InstallationMutexName(installation));
            bool acquired;
            try { acquired = owner.WaitOne(0); }
            catch (AbandonedMutexException) { acquired = true; }
            if (!acquired) throw new IOException("Another updater is handling this installation.");
            try
            {
                operation = UpdateOperation.PrepareInstallation;
                InstallOwned(installation, extracted, files, beforeCopy, observe, beforeCommit);
            }
            finally { owner.ReleaseMutex(); }
        }
        catch (Exception error) when (error is not UpdateTransactionException)
        {
            throw new UpdateTransactionException(UpdateRecoveryStatus.Unknown, Describe(operation, error), []);
        }
    }

    private static void InstallOwned(string installation, string extracted, IReadOnlyList<string> files, Action<int>? beforeCopy,
        Action<DiagnosticStage, DiagnosticOutcome, DiagnosticCounters?, Exception?>? observe, Action<string>? beforeCommit)
    {
        var executable = Path.Combine(installation, "ModernTubeDownloader.exe");
        var backup = Path.Combine(installation, UpdatePackageSecurity.BackupDirectoryName);
        UpdatePackageSecurity.EnsureNoReparsePoints(executable);
        UpdatePackageSecurity.EnsureNoReparsePoints(backup);
        if (ReadAttributes(backup) is not null)
        {
            UpdateJournal prior;
            try { prior = ReadJournal(backup, installation); }
            catch (Exception error)
            {
                throw new UpdateTransactionException(UpdateRecoveryStatus.Unknown, new(UpdateOperation.ReadJournal, error), []);
            }

            if (!prior.Completed)
            {
                observe?.Invoke(DiagnosticStage.Recover, DiagnosticOutcome.Started, new(Files: prior.Files.Count), null);
                var recovery = RollBack(backup, prior, observe);
                if (recovery.Status != UpdateRecoveryStatus.Restored)
                {
                    observe?.Invoke(DiagnosticStage.Recover, DiagnosticOutcome.Failed, new(Files: prior.Files.Count), null);
                    throw new UpdateTransactionException(recovery.Status,
                        new(UpdateOperation.RecoverPreviousTransaction, new IOException("The previous installation could not be verified.")),
                        recovery.Failures);
                }
                observe?.Invoke(DiagnosticStage.Recover, DiagnosticOutcome.Completed, new(Files: prior.Files.Count), null);
            }

            if (!File.Exists(executable)) throw new InvalidDataException("An existing application installation is required.");
            RetireBackup(backup);
        }
        if (!File.Exists(executable)) throw new InvalidDataException("An existing application installation is required.");

        var journal = PrepareBackup(installation, extracted, files, backup, observe);
        var operation = UpdateOperation.InstallFile;
        try
        {
            observe?.Invoke(DiagnosticStage.Replace, DiagnosticOutcome.Started, new(Files: files.Count), null);
            for (var index = 0; index < journal.Files.Count; index++)
            {
                var entry = journal.Files[index];
                var destination = FilePath(installation, entry.RelativePath);
                var source = FilePath(extracted, entry.RelativePath);
                UpdatePackageSecurity.EnsureNoReparsePoints(source);
                UpdatePackageSecurity.EnsureNoReparsePoints(destination);
                beforeCopy?.Invoke(index);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                using var input = OpenRead(source);
                if (entry.Existed)
                {
                    // Verify and replace using the same exclusive handle.
                    using var current = new FileStream(destination, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                    if (!Matches(current, entry.OriginalSha256!))
                        throw new IOException("An application file changed after backup preparation.");
                    MarkWrite(backup, index);
                    current.SetLength(0);
                    current.Position = 0;
                    input.CopyTo(current);
                    current.Flush(true);
                }
                else
                {
                    // A new path is either absent or complete after interruption.
                    var staged = Path.Combine(backup, "add-" + index.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    WriteDurably(input, staged, overwrite: false);
                    if (!Matches(staged, entry.AddedSha256!)) throw new IOException("The staged file no longer matches the package.");
                    MarkWrite(backup, index);
                    File.Move(staged, destination, overwrite: false);
                }
                if ((index + 1) % 100 == 0)
                    observe?.Invoke(DiagnosticStage.Replace, DiagnosticOutcome.Progress, new(Files: index + 1), null);
            }
            observe?.Invoke(DiagnosticStage.Replace, DiagnosticOutcome.Completed, new(Files: files.Count), null);
            operation = UpdateOperation.ReplaceJournal;
            observe?.Invoke(DiagnosticStage.Commit, DiagnosticOutcome.Started, new(Files: files.Count), null);
            beforeCommit?.Invoke(JournalPath(backup));
            SaveJournal(backup, journal with { Completed = true }, observe);
            observe?.Invoke(DiagnosticStage.Commit, DiagnosticOutcome.Completed, new(Files: files.Count), null);
        }
        catch (Exception error)
        {
            var failure = Describe(operation, error);
            observe?.Invoke(DiagnosticStage.Rollback, DiagnosticOutcome.Started, new(Files: journal.Files.Count, Code: (int)failure.Operation), null);
            var recovered = RollBack(backup, journal, observe);
            observe?.Invoke(DiagnosticStage.Rollback,
                recovered.Status == UpdateRecoveryStatus.Restored ? DiagnosticOutcome.Completed : DiagnosticOutcome.Failed,
                new(Files: journal.Files.Count, Errors: recovered.Failures.Count, Context: (int)recovered.Status), null);
            throw new UpdateTransactionException(recovered.Status, failure, recovered.Failures);
        }
    }

    private static UpdateJournal PrepareBackup(string installation, string extracted, IReadOnlyList<string> files,
        string backup, Action<DiagnosticStage, DiagnosticOutcome, DiagnosticCounters?, Exception?>? observe)
    {
        var operation = UpdateOperation.InitializeJournal;
        var journal = new UpdateJournal(installation, [], false, UsesWriteMarkers: true);
        var staging = backup + "-init-" + Guid.NewGuid().ToString("N");
        var owned = false;
        try
        {
            Directory.CreateDirectory(staging);
            owned = true;
            Directory.CreateDirectory(Path.Combine(staging, "started"));
            observe?.Invoke(DiagnosticStage.Journal, DiagnosticOutcome.Started, new(Files: files.Count), null);
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in files)
            {
                operation = UpdateOperation.PrepareFile;
                var relative = UpdatePackageSecurity.ValidateRelativePath(file);
                if (relative != file || !names.Add(relative)) throw new InvalidDataException("Invalid or repeated application file.");
                var destination = FilePath(installation, relative);
                var source = FilePath(extracted, relative);
                UpdatePackageSecurity.EnsureNoReparsePoints(source);
                UpdatePackageSecurity.EnsureNoReparsePoints(destination);
                var attributes = ReadAttributes(destination);
                if ((attributes & FileAttributes.Directory) == FileAttributes.Directory)
                    throw new IOException("A directory occupies an application file path.");
                if ((attributes & FileAttributes.ReadOnly) == FileAttributes.ReadOnly)
                    throw new UnauthorizedAccessException("An application file is read-only.");
                string? hash = null;
                if (attributes.HasValue)
                {
                    operation = UpdateOperation.BackupFile;
                    var saved = FilePath(Path.Combine(staging, "files"), relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(saved)!);
                    using var original = OpenRead(destination);
                    hash = Convert.ToHexString(SHA256.HashData(original));
                    original.Position = 0;
                    WriteDurably(original, saved, overwrite: false);
                    if (!Matches(saved, hash)) throw new IOException("The backup did not match the original file.");
                }
                string? addedHash = null;
                if (!attributes.HasValue)
                {
                    using var input = OpenRead(source);
                    addedHash = Convert.ToHexString(SHA256.HashData(input));
                }
                journal.Files.Add(new(relative, attributes.HasValue, hash, addedHash));
            }
            operation = UpdateOperation.InitializeJournal;
            SaveJournal(staging, journal, observe);
            Directory.Move(staging, backup);
            observe?.Invoke(DiagnosticStage.Journal, DiagnosticOutcome.Completed, new(Files: files.Count), null);
            return journal;
        }
        catch (Exception error)
        {
            List<UpdateFailure> cleanup = [];
            if (owned)
            {
                try { if (Directory.Exists(staging)) Directory.Delete(staging, true); }
                catch (Exception cleanupError) { cleanup.Add(new(UpdateOperation.RemoveUnusedJournal, cleanupError)); }
            }
            throw new UpdateTransactionException(UpdateRecoveryStatus.Unknown, Describe(operation, error), cleanup);
        }
    }

    private static void RetireBackup(string backup)
    {
        // Publish retirement before cleaning up, so partial cleanup cannot invalidate an active transaction.
        var retired = backup + "-completed-" + Guid.NewGuid().ToString("N");
        try { Directory.Move(backup, retired); }
        catch (Exception error) { throw new UpdateTransactionException(UpdateRecoveryStatus.Unknown, new(UpdateOperation.RetireJournal, error), []); }
        try { Directory.Delete(retired, true); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }

    private static (UpdateRecoveryStatus Status, List<UpdateFailure> Failures) RollBack(string backup, UpdateJournal journal,
        Action<DiagnosticStage, DiagnosticOutcome, DiagnosticCounters?, Exception?>? observe)
    {
        List<UpdateFailure> failures = [];
        // A legacy incomplete journal cannot authenticate its originals. Preserve it for manual recovery.
        if (!journal.UsesWriteMarkers)
            return (UpdateRecoveryStatus.Unknown, [new(UpdateOperation.UnverifiedLegacyJournal,
                new InvalidDataException("The legacy journal does not contain original file checksums."))]);

        var verifiedFiles = 0;
        for (var index = journal.Files.Count - 1; index >= 0; index--)
        {
            var file = journal.Files[index];
            try
            {
                var marker = MarkerPath(backup, index);
                UpdatePackageSecurity.EnsureNoReparsePoints(marker);
                var markerAttributes = ReadAttributes(marker);
                var destination = FilePath(journal.Installation, file.RelativePath);
                UpdatePackageSecurity.EnsureNoReparsePoints(destination);
                if (markerAttributes is null)
                {
                    var unchanged = file.Existed ? Matches(destination, file.OriginalSha256!) : ReadAttributes(destination) is null;
                    if (!unchanged) throw new InvalidDataException("An unmarked application file changed outside the recorded update.");
                    verifiedFiles++;
                    continue;
                }
                if ((markerAttributes & FileAttributes.Directory) != 0)
                    throw new InvalidDataException("An update write marker is not a regular file.");
                if (file.Existed)
                {
                    var hash = file.OriginalSha256!;
                    if (!Matches(destination, hash))
                    {
                        var saved = FilePath(Path.Combine(backup, "files"), file.RelativePath);
                        UpdatePackageSecurity.EnsureNoReparsePoints(saved);
                        using var original = OpenRead(saved);
                        if (!Matches(original, hash)) throw new InvalidDataException("The backup no longer matches the original checksum.");
                        original.Position = 0;
                        WriteDurably(original, destination, overwrite: true);
                        if (!Matches(destination, hash)) throw new IOException("The restored file did not match the original checksum.");
                    }
                }
                else
                {
                    if (ReadAttributes(destination).HasValue)
                    {
                        if (!Matches(destination, file.AddedSha256!))
                            throw new InvalidDataException("The added file no longer matches the update package.");
                        File.Delete(destination);
                        if (ReadAttributes(destination).HasValue)
                            throw new IOException("The newly installed file could not be removed.");
                    }
                }
                verifiedFiles++;
            }
            catch (Exception error)
            {
                failures.Add(new(UpdateOperation.RestoreFile, error));
            }
        }
        if (failures.Count != 0)
            return (verifiedFiles > 0 ? UpdateRecoveryStatus.Partial : UpdateRecoveryStatus.Unknown, failures);

        try { SaveJournal(backup, journal with { Completed = true }, observe); }
        catch (Exception error)
        {
            failures.Add(Describe(UpdateOperation.ReplaceJournal, error));
            return (UpdateRecoveryStatus.Unknown, failures);
        }
        return (UpdateRecoveryStatus.Restored, failures);
    }

    private static FileStream OpenRead(string path) => new(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.SequentialScan);

    private static string MarkerPath(string backup, int index) =>
        Path.Combine(backup, "started", index.ToString(System.Globalization.CultureInfo.InvariantCulture));

    private static void MarkWrite(string backup, int index)
    {
        var path = MarkerPath(backup, index);
        UpdatePackageSecurity.EnsureNoReparsePoints(path);
        using var marker = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        marker.WriteByte(1);
        marker.Flush(true);
    }

    private static void WriteDurably(Stream source, string destination, bool overwrite)
    {
        using var file = new FileStream(destination, overwrite ? FileMode.Create : FileMode.CreateNew, FileAccess.Write, FileShare.None);
        source.CopyTo(file);
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

    private static bool Matches(string path, string hash)
    {
        if (ReadAttributes(path) is null) return false;
        using var file = OpenRead(path);
        return Matches(file, hash);
    }

    private static bool Matches(Stream file, string hash) =>
        CryptographicOperations.FixedTimeEquals(SHA256.HashData(file), Convert.FromHexString(hash));

    private static bool ValidHash(string? hash) => hash is { Length: 64 } && hash.All(Uri.IsHexDigit);

    private static string JournalPath(string backup) => Path.Combine(backup, "journal.json");

    private static UpdateJournal ReadJournal(string backup, string installation)
    {
        var path = JournalPath(backup);
        UpdatePackageSecurity.EnsureNoReparsePoints(path);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        if (stream.Length > 2_500_000) throw new InvalidDataException("The update journal exceeds its size limit.");
        var journal = JsonSerializer.Deserialize<UpdateJournal>(stream, JournalReadOptions);
        if (journal?.Installation is null || journal.Files is null ||
            !Path.TrimEndingDirectorySeparator(Path.GetFullPath(journal.Installation)).Equals(installation, StringComparison.OrdinalIgnoreCase) ||
            journal.Files.Count > UpdatePackageSecurity.MaximumEntries)
            throw new InvalidDataException("Invalid update journal or installation.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (journal.UsesWriteMarkers)
        {
            var markers = Path.Combine(backup, "started");
            UpdatePackageSecurity.EnsureNoReparsePoints(markers);
            if (!Directory.Exists(markers)) throw new InvalidDataException("The update write markers are missing.");
        }
        foreach (var file in journal.Files)
        {
            if (file?.RelativePath is null || UpdatePackageSecurity.ValidateRelativePath(file.RelativePath) != file.RelativePath ||
                !names.Add(file.RelativePath) || journal.UsesWriteMarkers &&
                (file.Existed ? !ValidHash(file.OriginalSha256) || file.AddedSha256 is not null :
                    !ValidHash(file.AddedSha256) || file.OriginalSha256 is not null))
                throw new InvalidDataException("Invalid or repeated file in the update journal.");
        }
        return journal with { Installation = installation };
    }

    private static void SaveJournal(string backup, UpdateJournal journal,
        Action<DiagnosticStage, DiagnosticOutcome, DiagnosticCounters?, Exception?>? observe)
    {
        UpdateFilePublication.Write(JournalPath(backup), journal, (attempt, code) =>
            observe?.Invoke(DiagnosticStage.Journal, DiagnosticOutcome.Progress,
                new(Code: (int)UpdateOperation.ReplaceJournal, Context: code, Items: attempt), null));
    }

    private static UpdateFailure Describe(UpdateOperation operation, Exception error) =>
        error is UpdatePublicationException publication ? new(publication.Operation, publication.InnerException!) : new(operation, error);
}

internal sealed class UpdatePublicationException(UpdateOperation operation, Exception inner) : IOException("Update file publication failed.", inner)
{
    public UpdateOperation Operation { get; } = operation;
}

internal static class UpdateFilePublication
{
    private static readonly int[] RetryDelayMs = [40, 120, 300];

    internal static void Replace(string temporary, string target, Action<int, int>? retry)
    {
        for (var attempt = 0; ; attempt++)
        {
            try { File.Move(temporary, target, overwrite: true); return; }
            catch (IOException error) when (attempt < RetryDelayMs.Length &&
                error.HResult is unchecked((int)0x80070020) or unchecked((int)0x80070021))
            {
                retry?.Invoke(attempt + 1, error.HResult);
                Thread.Sleep(RetryDelayMs[attempt]);
            }
        }
    }

    internal static void Write<T>(string target, T value, Action<int, int>? retry = null)
    {
        var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        var operation = UpdateOperation.WriteJournal;
        try
        {
            UpdatePackageSecurity.EnsureNoReparsePoints(target);
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, value);
                stream.Flush(true);
            }
            operation = UpdateOperation.ReplaceJournal;
            Replace(temporary, target, retry);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            try { File.Delete(temporary); }
            catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException) { }
            throw new UpdatePublicationException(operation, error);
        }
    }
}
