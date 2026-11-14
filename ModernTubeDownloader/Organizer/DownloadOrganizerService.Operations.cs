using System.Security.Cryptography;
using System.Text.Json;
using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Models;

namespace ModernTubeDownloader.Organizer;

public sealed partial class DownloadOrganizerService
{
    private int movingFileCount, movingTotalFiles;
    private long movingCompletedBytes, movingTotalBytes;
    private sealed class JournalHeader
    {
        public int SchemaVersion { get; set; }
        public Guid Id { get; set; }
        public OrganizerJournalState State { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public int EntryCount { get; set; }
        public string? ErrorCode { get; set; }
        public bool UndoRequested { get; set; }
        public long ManifestLength { get; set; }
        public DateTime ManifestLastWriteUtc { get; set; }
    }
    private sealed record FileCheckpoint(string Destination, OrganizerFileState State, bool TransferStarted, bool UndoCompleted);
    private static bool IsManifest(string file) => Guid.TryParseExact(Path.GetFileNameWithoutExtension(file), "N", out _);
    private string StatePath(Guid id) => Path.Combine(JournalDirectory, id.ToString("N") + ".state.json");
    private string CheckpointDirectory(Guid id) => Path.Combine(JournalDirectory, id.ToString("N") + ".files");
    private async Task<JournalHeader> ReadHeaderAsync(string file, CancellationToken token)
    {
        OrganizerPathSafety.CheckAncestors(file);
        var manifest = new FileInfo(file);
        if (manifest.Length > 64 * 1024 * 1024 || !Guid.TryParseExact(Path.GetFileNameWithoutExtension(file), "N", out var id)) throw new IOException("Organizer.Error.Journal");
        JournalHeader? header;
        var statePath = StatePath(id); OrganizerPathSafety.CheckAncestors(statePath);
        if (File.Exists(statePath))
        {
            if (new FileInfo(statePath).Length > 65536) throw new IOException("Organizer.Error.Journal");
            header = await AtomicJsonFile.ReadAsync<JournalHeader>(statePath, token).ConfigureAwait(false);
            if (header is null || header.SchemaVersion != 1 || header.Id != id || header.ManifestLength != manifest.Length || header.ManifestLastWriteUtc != manifest.LastWriteTimeUtc)
                throw new IOException("Organizer.Error.Journal");
        }
        else
        {
            // An interrupted initial write can have a manifest but no compact state yet.
            await using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true);
            header = await JsonSerializer.DeserializeAsync<JournalHeader>(stream, JsonDefaults.Options, token).ConfigureAwait(false);
            if (header is null || header.SchemaVersion != 1 || header.Id != id) throw new IOException("Organizer.Error.Journal");
        }
        if (!Enum.IsDefined(header.State)) throw new IOException("Organizer.Error.Journal");
        return header;
    }
    /// <summary>Inspects every journal, including entries older than the bounded recent-operation UI.</summary>
    public async Task<bool> HasPendingRecoveryAsync(CancellationToken token = default)
    {
        if (!Directory.Exists(JournalDirectory)) return false;
        OrganizerPathSafety.CheckAncestors(JournalDirectory);
        foreach (var file in Directory.EnumerateFiles(JournalDirectory, "*.json").Where(file => !IsManifest(file)))
        {
            var name = Path.GetFileName(file);
            if (!name.EndsWith(".state.json", StringComparison.Ordinal) || !Guid.TryParseExact(name[..^11], "N", out var owner) ||
                !File.Exists(Path.Combine(JournalDirectory, owner.ToString("N") + ".json"))) return true;
        }
        foreach (var file in Directory.EnumerateFiles(JournalDirectory, "*.json").Where(IsManifest))
        {
            token.ThrowIfCancellationRequested(); OrganizerPathSafety.CheckAncestors(file);
            try
            {
                // Deserialize only the header. Unknown match arrays are streamed past and are
                // never retained, even when checking thousands of historical operations.
                var header = await ReadHeaderAsync(file, token).ConfigureAwait(false);
                if (header.State is not (OrganizerJournalState.Completed or OrganizerJournalState.Undone)) return true;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or FormatException) { return true; }
        }
        return false;
    }

    public async Task<IReadOnlyList<OrganizerJournalSummary>> JournalsAsync(CancellationToken token = default)
    {
        if (!Directory.Exists(JournalDirectory)) return [];
        OrganizerPathSafety.CheckAncestors(JournalDirectory);
        var result = new List<OrganizerJournalSummary>();
        foreach (var file in Directory.EnumerateFiles(JournalDirectory, "*.json").Where(IsManifest))
        {
            token.ThrowIfCancellationRequested();
            var journal = await ReadHeaderAsync(file, token).ConfigureAwait(false);
            result.Add(new(journal.Id, journal.CreatedAt, journal.State, journal.EntryCount, journal.ErrorCode));
            result.Sort((left, right) =>
            {
                static bool Pending(OrganizerJournalState state) => state is not (OrganizerJournalState.Completed or OrganizerJournalState.Undone);
                var pending = Pending(right.State).CompareTo(Pending(left.State));
                return pending != 0 ? pending : right.CreatedAt.CompareTo(left.CreatedAt);
            });
            if (result.Count > 100) result.RemoveAt(100);
        }
        return result;
    }

    public Task<OrganizerJournal> MoveSelectedAsync(OrganizerPlan plan, IReadOnlyCollection<Guid> selection, CancellationToken token = default) =>
        RunOwnedAsync(async ct =>
        {
            token = ct;
            if (history.LoadFailed || queue.Storage.State.IsBlocked) throw new IOException("Organizer.Error.Journal");
            if (await HasPendingRecoveryAsync(token).ConfigureAwait(false))
                throw new IOException("Organizer.Error.RecoveryRequired");
            var selected = plan.Matches.Where(match => selection.Contains(match.Id)).ToArray();
            if (selected.Length == 0 || selected.Any(match => match.Status != OrganizerMatchStatus.Found || match.Files.Count == 0))
                throw new IOException("Organizer.Error.Selection");
            if (selected.Select(match => match.MediaPath).Distinct(OrganizerPathSafety.Comparer).Count() != selected.Length)
                throw new IOException("Organizer.Reason.MultipleLists");
            var journal = new OrganizerJournal { ScanRoot = plan.ScanRoot,
                Matches = JsonSerializer.Deserialize<List<OrganizerMatch>>(JsonSerializer.Serialize(selected, JsonDefaults.Options), JsonDefaults.Options)! };
            ValidateJournal(journal);
            foreach (var match in journal.Matches)
            {
                EnsureInactive(match.MediaPath!);
                foreach (var file in match.Files)
                {
                    token.ThrowIfCancellationRequested(); ValidateFile(journal, match, file);
                    if (File.Exists(file.Destination) || Directory.Exists(file.Destination)) throw new IOException("Organizer.Reason.Collision");
                    await VerifyAsync(file.Source, file, token).ConfigureAwait(false);
                }
            }
            await SaveJournalAsync(journal).ConfigureAwait(false);
            return await TransferJournalAsync(journal, token).ConfigureAwait(false);
        }, token);

    public Task<OrganizerJournal> RecoverAsync(Guid id, CancellationToken token = default) => RunOwnedAsync(async ct =>
    {
        token = ct;
        var journal = await ReadJournalAsync(id, token).ConfigureAwait(false);
        if (journal.State is OrganizerJournalState.Completed or OrganizerJournalState.Undone) return journal;
        return journal.UndoRequested ? await UndoJournalAsync(journal, token).ConfigureAwait(false) : await TransferJournalAsync(journal, token).ConfigureAwait(false);
    }, token);

    public Task<OrganizerJournal> UndoAsync(Guid id, CancellationToken token = default) => RunOwnedAsync(async ct =>
    {
        token = ct;
        var journal = await ReadJournalAsync(id, token).ConfigureAwait(false);
        if (journal.State == OrganizerJournalState.Undone) return journal;
        if (journal.State != OrganizerJournalState.Completed) throw new IOException("Organizer.Error.RecoveryRequired");
        // Validate every member before the first reverse mutation. Changed media, JSON,
        // central originals or occupied old paths stop the whole undo without deleting.
        foreach (var match in journal.Matches)
        foreach (var file in match.Files)
        {
            ValidateFile(journal, match, file); EnsureInactive(file.Destination);
            await VerifyAsync(file.Destination, file, token).ConfigureAwait(false);
            if (file.CopyOnly) await VerifyAsync(file.Source, file, token).ConfigureAwait(false);
            else if (File.Exists(file.Source) || Directory.Exists(file.Source)) throw new IOException("Organizer.Reason.Collision");
        }
        journal.UndoRequested = true; journal.State = OrganizerJournalState.Undoing;
        await SaveJournalAsync(journal).ConfigureAwait(false);
        return await UndoJournalAsync(journal, token).ConfigureAwait(false);
    }, token);

    private async Task<OrganizerJournal> TransferJournalAsync(OrganizerJournal journal, CancellationToken token)
    {
        try
        {
            ValidateJournal(journal); journal.State = OrganizerJournalState.Moving; journal.ErrorCode = null;
            await SaveJournalAsync(journal).ConfigureAwait(false);
            var all = journal.Matches.SelectMany(match => match.Files).ToArray(); var count = 0; long bytes = 0;
            movingTotalFiles = all.Length; movingTotalBytes = all.Sum(value => value.Length); movingFileCount = 0; movingCompletedBytes = 0;
            foreach (var match in journal.Matches)
            {
                EnsureInactive(match.MediaPath!);
                foreach (var file in match.Files)
                {
                    await PauseBoundary(token).ConfigureAwait(false); ValidateFile(journal, match, file);
                    if (!file.TransferStarted)
                    {
                        if (File.Exists(file.Destination) || Directory.Exists(file.Destination)) throw new IOException("Organizer.Reason.Collision");
                        await VerifyAsync(file.Source, file, token).ConfigureAwait(false);
                        file.TransferStarted = true; await SaveFileCheckpointAsync(journal, file).ConfigureAwait(false);
                    }
                    if (!File.Exists(file.Destination))
                    {
                        if (file.State != OrganizerFileState.Pending) throw new IOException("Organizer.Error.Changed");
                        await TransferFileAsync(journal, file, file.Source, file.Destination, !file.CopyOnly, token).ConfigureAwait(false);
                        file.State = OrganizerFileState.Published;
                        await SaveFileCheckpointAsync(journal, file).ConfigureAwait(false);
                    }
                    await VerifyAsync(file.Destination, file, token).ConfigureAwait(false);
                    if (!file.CopyOnly && File.Exists(file.Source))
                    {
                        ValidateFile(journal, match, file); await VerifyAsync(file.Source, file, token).ConfigureAwait(false);
                        await VerifyAsync(file.Destination, file, token).ConfigureAwait(false);
                        File.Delete(file.Source); // A fully verified destination exists; never the only verified copy.
                    }
                    file.State = OrganizerFileState.SourceRemoved;
                    await SaveFileCheckpointAsync(journal, file).ConfigureAwait(false);
                    count++; bytes += file.Length; movingFileCount = count; movingCompletedBytes = bytes;
                    Report("Organizer.Phase.Moving", count, all.Length, bytes, movingTotalBytes, Path.GetFileName(file.Source));
                    observer?.Checkpoint("AfterFile", count);
                }
            }
            journal.State = OrganizerJournalState.UpdatingReferences; await SaveJournalAsync(journal).ConfigureAwait(false);
            observer?.Checkpoint("BeforeReferences", count);
            // Revalidate all destination members before changing any stored reference.
            foreach (var match in journal.Matches)
            foreach (var file in match.Files) { ValidateFile(journal, match, file); await VerifyAsync(file.Destination, file, token).ConfigureAwait(false); }
            var changes = journal.Matches.ToDictionary(match => match.MediaPath!, match => match.Files[0].Destination, OrganizerPathSafety.Comparer);
            await history.RelocatePathsAsync(changes, token).ConfigureAwait(false);
            observer?.Checkpoint("AfterHistory", count);
            await queue.RelocateCompletedPathsAsync(changes, token).ConfigureAwait(false);
            journal.State = OrganizerJournalState.Completed; await SaveJournalAsync(journal).ConfigureAwait(false);
            Report("Organizer.Phase.Completed", count, all.Length, bytes, bytes);
            logger.Info($"Organizer operation {journal.Id:N} completed with {count} files.");
            return journal;
        }
        catch (Exception error)
        {
            journal.State = OrganizerJournalState.RecoveryRequired;
            journal.ErrorCode = SafeError(error); await SaveJournalAsync(journal).ConfigureAwait(false);
            logger.Warning($"Organizer operation {journal.Id:N} requires recovery ({error.GetType().Name}).");
            throw;
        }
    }

    private async Task<OrganizerJournal> UndoJournalAsync(OrganizerJournal journal, CancellationToken token)
    {
        try
        {
            ValidateJournal(journal); var completed = 0;
            movingTotalFiles = journal.Matches.Sum(match => match.Files.Count); movingTotalBytes = journal.Matches.Sum(match => match.Bytes); movingFileCount = 0; movingCompletedBytes = 0;
            foreach (var match in journal.Matches.AsEnumerable().Reverse())
            foreach (var file in match.Files.AsEnumerable().Reverse())
            {
                await PauseBoundary(token).ConfigureAwait(false); ValidateFile(journal, match, file); EnsureInactive(file.Destination);
                if (file.UndoCompleted) continue;
                if (file.CopyOnly)
                {
                    await VerifyAsync(file.Source, file, token).ConfigureAwait(false);
                    if (File.Exists(file.Destination)) { await VerifyAsync(file.Destination, file, token).ConfigureAwait(false); File.Delete(file.Destination); }
                }
                else
                {
                    if (!File.Exists(file.Source)) await TransferFileAsync(journal, file, file.Destination, file.Source, true, token).ConfigureAwait(false);
                    await VerifyAsync(file.Source, file, token).ConfigureAwait(false);
                    if (File.Exists(file.Destination)) { await VerifyAsync(file.Destination, file, token).ConfigureAwait(false); File.Delete(file.Destination); }
                }
                file.UndoCompleted = true; await SaveFileCheckpointAsync(journal, file).ConfigureAwait(false);
                observer?.Checkpoint("AfterUndoFile", ++completed);
                movingFileCount = completed; movingCompletedBytes += file.Length;
                Report("Organizer.Phase.Moving", completed, movingTotalFiles, movingCompletedBytes, movingTotalBytes, Path.GetFileName(file.Source));
            }
            foreach (var match in journal.Matches)
            foreach (var file in match.Files) { ValidateFile(journal, match, file); await VerifyAsync(file.Source, file, token).ConfigureAwait(false); }
            var changes = journal.Matches.ToDictionary(match => match.Files[0].Destination, match => match.MediaPath!, OrganizerPathSafety.Comparer);
            await history.RelocatePathsAsync(changes, token).ConfigureAwait(false);
            await queue.RelocateCompletedPathsAsync(changes, token).ConfigureAwait(false);
            journal.State = OrganizerJournalState.Undone; journal.ErrorCode = null; await SaveJournalAsync(journal).ConfigureAwait(false);
            Report("Organizer.Phase.Undone", completed, completed, 0, 0); return journal;
        }
        catch (Exception error)
        { journal.State = OrganizerJournalState.RecoveryRequired; journal.ErrorCode = SafeError(error); await SaveJournalAsync(journal).ConfigureAwait(false); throw; }
    }

    private async Task TransferFileAsync(OrganizerJournal journal, OrganizerFile expected, string source, string destination, bool move, CancellationToken token)
    {
        OrganizerPathSafety.CheckAncestors(source); OrganizerPathSafety.CheckAncestors(destination);
        await VerifyAsync(source, expected, token).ConfigureAwait(false);
        if (File.Exists(destination) || Directory.Exists(destination)) throw new IOException("Organizer.Reason.Collision");
        var directory = Path.GetDirectoryName(destination)!;
        Directory.CreateDirectory(directory); OrganizerPathSafety.CheckAncestors(directory);
        var sameVolume = OrganizerPathSafety.Comparer.Equals(Path.GetPathRoot(source), Path.GetPathRoot(destination));
        if (move && sameVolume && observer?.ForceVerifiedCopy != true)
        {
            // No replacement; repeat containment/reparse validation directly at the mutation boundary.
            OrganizerPathSafety.CheckAncestors(source); OrganizerPathSafety.CheckAncestors(destination);
            File.Move(source, destination); return;
        }
        var temporary = Path.Combine(directory, "." + Path.GetFileName(destination) + "." + journal.Id.ToString("N") + ".organizing");
        if (File.Exists(temporary))
        {
            try { await VerifyAsync(temporary, expected, token).ConfigureAwait(false); }
            catch (IOException)
            {
                // Keep the interrupted attempt as evidence. Original source was verified above;
                // a fresh, exclusive staging name can restart safely without overwriting anything.
                temporary = Path.Combine(directory, "." + Path.GetFileName(destination) + "." + journal.Id.ToString("N") + "." + Guid.NewGuid().ToString("N") + ".organizing");
            }
        }
        if (!File.Exists(temporary))
        {
            observer?.Checkpoint("BeforeCopy", movingFileCount);
            await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            await using var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough);
            var buffer = new byte[1024 * 1024]; int read; long copied = 0;
            while ((read = await input.ReadAsync(buffer, token).ConfigureAwait(false)) != 0)
            {
                await PauseBoundary(token).ConfigureAwait(false); await output.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false); copied += read;
                Report("Organizer.Phase.Copying", movingFileCount, movingTotalFiles, movingCompletedBytes + copied, movingTotalBytes, Path.GetFileName(source));
                observer?.Checkpoint("AfterCopyChunk", movingFileCount);
            }
            await output.FlushAsync(token).ConfigureAwait(false); output.Flush(true);
        }
        await VerifyAsync(temporary, expected, token).ConfigureAwait(false);
        OrganizerPathSafety.CheckAncestors(source); OrganizerPathSafety.CheckAncestors(destination);
        File.Move(temporary, destination); // Atomic publication, never overwrite.
        File.SetLastWriteTimeUtc(destination, expected.LastWriteUtc); File.SetCreationTimeUtc(destination, expected.CreationUtc);
        observer?.Checkpoint("AfterCopyPublished", movingFileCount);
    }

    private static async Task VerifyAsync(string path, OrganizerFile expected, CancellationToken token)
    {
        OrganizerPathSafety.CheckAncestors(path);
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None, 65536, true);
        if (stream.Length != expected.Length || Convert.ToHexString(await SHA256.HashDataAsync(stream, token).ConfigureAwait(false)) != expected.Sha256)
            throw new IOException("Organizer.Error.Changed");
    }
    private void EnsureInactive(string path)
    {
        var version = Interlocked.Read(ref referenceVersion);
        if (indexedReferenceVersion != version)
        {
            protectedReferences = queue.Snapshot().Where(item => (item.Status != DownloadStatus.Completed || item.LiveSession is not null) && item.FinalFile is not null)
                .Select(item => item.FinalFile!).Concat(history.Snapshot().Where(entry => entry.WasLiveRecording || entry.WasPartial).Select(entry => entry.FinalPath))
                .ToHashSet(OrganizerPathSafety.Comparer);
            indexedReferenceVersion = version;
        }
        if (protectedReferences.Contains(path))
            throw new IOException("Organizer.Error.Active");
    }
    private void ValidateJournal(OrganizerJournal journal)
    {
        if (journal.SchemaVersion != 1 || !Enum.IsDefined(journal.State) || journal.Matches.Count is < 1 or > MaximumPlanEntries) throw new IOException("Organizer.Error.Journal");
        journal.ScanRoot = OrganizerPathSafety.DirectoryPath(journal.ScanRoot, true);
        if (IsProtected(journal.ScanRoot)) throw new IOException("Organizer.Error.OutsideRoot");
        var destinations = new HashSet<string>(OrganizerPathSafety.Comparer);
        foreach (var match in journal.Matches)
        {
            if (match.MediaPath is null || match.Files.Count is < 1 or > 200 || !OrganizerPathSafety.Comparer.Equals(match.MediaPath, match.Files[0].Source))
                throw new IOException("Organizer.Error.Journal");
            foreach (var file in match.Files)
            { ValidateFile(journal, match, file); if (!destinations.Add(file.Destination)) throw new IOException("Organizer.Reason.Collision"); }
        }
    }
    private void ValidateFile(OrganizerJournal journal, OrganizerMatch match, OrganizerFile file)
    {
        var destination = OrganizerPathSafety.DirectoryPath(match.DestinationDirectory);
        if (IsProtected(destination) || file.Length < 0 || file.Sha256.Length != 64 || file.Sha256.Any(c => !Uri.IsHexDigit(c))) throw new IOException("Organizer.Error.Journal");
        OrganizerPathSafety.CheckFile(file.Destination, destination);
        if (!OrganizerPathSafety.Comparer.Equals(Path.GetDirectoryName(file.Destination), destination)) throw new IOException("Organizer.Error.OutsideRoot");
        if (file.CopyOnly)
        {
            OrganizerPathSafety.CheckFile(file.Source, paths.MetadataDirectory);
            if (!OrganizerPathSafety.Comparer.Equals(Path.GetDirectoryName(file.Source), paths.MetadataDirectory) || Path.GetExtension(file.Source) != ".json")
                throw new IOException("Organizer.Error.OutsideRoot");
        }
        else { OrganizerPathSafety.CheckFile(file.Source, journal.ScanRoot); if (IsProtected(file.Source)) throw new IOException("Organizer.Error.OutsideRoot"); }
    }
    private async Task<OrganizerJournal> ReadJournalAsync(Guid id, CancellationToken token)
    {
        OrganizerPathSafety.CheckAncestors(JournalDirectory);
        var path = Path.Combine(JournalDirectory, id.ToString("N") + ".json");
        var header = await ReadHeaderAsync(path, token).ConfigureAwait(false);
        var journal = await AtomicJsonFile.ReadAsync<OrganizerJournal>(path, token).ConfigureAwait(false);
        if (journal is null || journal.Id != id) throw new IOException("Organizer.Error.Journal");
        journal.State = header.State; journal.ErrorCode = header.ErrorCode; journal.UndoRequested = header.UndoRequested;
        var checkpoints = CheckpointDirectory(id); OrganizerPathSafety.CheckAncestors(checkpoints);
        if (Directory.Exists(checkpoints))
        {
            var files = journal.Matches.SelectMany(match => match.Files).ToDictionary(file => file.Destination, OrganizerPathSafety.Comparer);
            foreach (var checkpointPath in Directory.EnumerateFiles(checkpoints, "*.json"))
            {
                token.ThrowIfCancellationRequested(); OrganizerPathSafety.CheckAncestors(checkpointPath);
                if (new FileInfo(checkpointPath).Length > 65536) throw new IOException("Organizer.Error.Journal");
                var checkpoint = await AtomicJsonFile.ReadAsync<FileCheckpoint>(checkpointPath, token).ConfigureAwait(false);
                if (checkpoint is null || !files.TryGetValue(checkpoint.Destination, out var file) || !Enum.IsDefined(checkpoint.State)) throw new IOException("Organizer.Error.Journal");
                file.State = checkpoint.State; file.TransferStarted = checkpoint.TransferStarted; file.UndoCompleted = checkpoint.UndoCompleted;
            }
        }
        ValidateJournal(journal); return journal;
    }
    private async Task SaveJournalAsync(OrganizerJournal journal)
    {
        OrganizerPathSafety.CheckAncestors(JournalDirectory);
        var manifest = Path.Combine(JournalDirectory, journal.Id.ToString("N") + ".json");
        if (!File.Exists(manifest))
        {
            var json = JsonSerializer.Serialize(journal, JsonDefaults.Options);
            if (System.Text.Encoding.UTF8.GetByteCount(json) > 64 * 1024 * 1024) throw new IOException("Organizer.Error.Limit");
            await AtomicJsonFile.WriteRawJsonAsync(manifest, json).ConfigureAwait(false);
        }
        var manifestInfo = new FileInfo(manifest);
        var header = new JournalHeader { SchemaVersion = 1, Id = journal.Id, State = journal.State, CreatedAt = journal.CreatedAt,
            EntryCount = journal.Matches.Count, ErrorCode = journal.ErrorCode, UndoRequested = journal.UndoRequested,
            ManifestLength = manifestInfo.Length, ManifestLastWriteUtc = manifestInfo.LastWriteTimeUtc };
        var statePath = StatePath(journal.Id); OrganizerPathSafety.CheckAncestors(statePath);
        await AtomicJsonFile.WriteRawJsonAsync(statePath, JsonSerializer.Serialize(header, JsonDefaults.Options)).ConfigureAwait(false);
    }
    private async Task SaveFileCheckpointAsync(OrganizerJournal journal, OrganizerFile file)
    {
        var directory = CheckpointDirectory(journal.Id); OrganizerPathSafety.CheckAncestors(directory);
        var key = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(file.Destination)));
        var path = Path.Combine(directory, key + ".json"); OrganizerPathSafety.CheckAncestors(path);
        await AtomicJsonFile.WriteRawJsonAsync(path, JsonSerializer.Serialize(new FileCheckpoint(file.Destination, file.State, file.TransferStarted, file.UndoCompleted), JsonDefaults.Options)).ConfigureAwait(false);
    }
    public static string SafeError(Exception error) => error is OperationCanceledException ? "Organizer.Error.Cancelled" :
        error.Message.StartsWith("Organizer.", StringComparison.Ordinal) && !error.Message.Contains(' ') ? error.Message : "Organizer.Error.Access";
}
