using System.Text;
using System.Text.Json;
using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Models;

namespace ModernTubeDownloader.Batch;

/// <summary>Immutable import, append-only lightweight row journal, and separate metadata. Never stores source-file paths.</summary>
public sealed class BatchSessionStore(string root)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private static readonly JsonSerializerOptions Lines = new(JsonDefaults.Options) { WriteIndented = false };
    private string Folder(Guid id) => Path.Combine(root, id.ToString("N"));
    public async Task CreateAsync(BatchImportSession session, IAsyncEnumerable<BatchImportEntry> entries, CancellationToken token)
    {
        var folder = Folder(session.Id); Directory.CreateDirectory(folder);
        var temporary = Path.Combine(folder, "entries.tmp");
        try
        {
        await using (var writer = new StreamWriter(new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true), new UTF8Encoding(false)))
        {
            await foreach (var entry in entries.WithCancellation(token))
            {
                await writer.WriteLineAsync(JsonSerializer.Serialize(entry, Lines).AsMemory(), token);
                session.Entries.Add(entry); session.ById.Add(entry.Id, entry); session.ImportedCount++;
            }
            await writer.FlushAsync(token);
        }
        File.Move(temporary, Path.Combine(folder, "entries.jsonl"));
        await SaveSessionAsync(session, token); // Commit marker: incomplete imports are not discoverable.
        }
        catch (Exception error) when (error is InvalidDataException or JsonException or BatchJsonFormatException || error is OperationCanceledException && token.IsCancellationRequested)
        {
            // No session is published to the caller and no queue handoff is possible
            // inside Create. Retain partial rows, but distinguish an explicit stop
            // from an unknown crash folder that still requires fail-closed recovery.
            await AtomicJsonFile.WriteAsync(Path.Combine(folder, "import-terminal.json"),
                new ImportTerminal(1, session.Id, error is OperationCanceledException ? "Cancelled" : "Rejected", false, DateTimeOffset.UtcNow),
                CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }
    private sealed record ImportTerminal(int SchemaVersion, Guid SessionId, string Outcome,
        [property: System.Text.Json.Serialization.JsonRequired] bool QueueAdmission, DateTimeOffset RecordedAt);

    private static async Task<bool> IsTerminalImportAsync(string directory, Guid id, CancellationToken token)
    {
        FileStream stream;
        try { stream = new FileStream(Path.Combine(directory, "import-terminal.json"), FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true); }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
        await using (stream)
        {
            if (stream.Length is <= 0 or > 4096) return false;
            var marker = await JsonSerializer.DeserializeAsync<ImportTerminal>(stream, JsonDefaults.Options, token).ConfigureAwait(false);
            return marker is { SchemaVersion: 1, QueueAdmission: false, Outcome: "Cancelled" or "Rejected" } &&
                marker.SessionId == id && marker.RecordedAt != default;
        }
    }
    public async Task SaveSessionAsync(BatchImportSession session, CancellationToken token = default)
    {
        await session.MetadataWriteGate.WaitAsync(token).ConfigureAwait(false);
        try { await AtomicJsonFile.WriteAsync(Path.Combine(Folder(session.Id), "session.json"), session.MetadataSnapshot(), token).ConfigureAwait(false); }
        finally { session.MetadataWriteGate.Release(); }
    }
    internal static InvalidDataException InvalidSession(int code)
    {
        var error = new InvalidDataException("Batch.Error.Session");
        error.Data["BatchDiagnosticCode"] = code;
        return error;
    }

    internal async IAsyncEnumerable<BatchImportSession> UnfinishedSessionsAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken token, Action<Exception>? onUnreadable = null)
    {
        if (!Directory.Exists(root)) yield break;
        foreach (var directory in Directory.EnumerateDirectories(root))
        {
            token.ThrowIfCancellationRequested();
            if (!Guid.TryParseExact(Path.GetFileName(directory), "N", out var id)) continue;
            BatchImportSession? value;
            try
            {
                value = await AtomicJsonFile.ReadAsync<BatchImportSession>(Path.Combine(directory, "session.json"), token).ConfigureAwait(false);
                if (value is null && await IsTerminalImportAsync(directory, id, token).ConfigureAwait(false)) continue;
                if (value is null) throw InvalidSession(1);
                if (value.SchemaVersion != 1) throw InvalidSession(2);
                if (value.Id != id) throw InvalidSession(3);
                if (!Enum.IsDefined(value.WorkflowPhase)) throw InvalidSession(4);
            }
            catch (Exception error) when (onUnreadable is not null && (error is IOException or InvalidDataException or UnauthorizedAccessException or JsonException))
            { onUnreadable(error); continue; }
            if (value.WorkflowPhase != BatchWorkflowPhase.Completed && value.ImportedCount > 0) yield return value;
        }
    }
    public async Task<bool> HasUnfinishedSessionsAsync(CancellationToken token = default)
    {
        if (!Directory.Exists(root)) return false;
        foreach (var directory in Directory.EnumerateDirectories(root))
        {
            token.ThrowIfCancellationRequested();
            if (!Guid.TryParseExact(Path.GetFileName(directory), "N", out var id)) continue;
            var value = await AtomicJsonFile.ReadAsync<BatchImportSession>(Path.Combine(directory, "session.json"), token);
            if (value is null && await IsTerminalImportAsync(directory, id, token).ConfigureAwait(false)) continue;
            if (value is null || value.SchemaVersion != 1 || value.Id != id ||
                value.WorkflowPhase != BatchWorkflowPhase.Completed && value.ImportedCount > 0) return true;
        }
        return false;
    }
    public async Task<IReadOnlyList<BatchImportSession>> FindImportsAsync(string? fingerprint, string? destination, CancellationToken token)
    {
        if (!Directory.Exists(root)) return [];
        var result = new List<BatchImportSession>();
        foreach (var directory in Directory.EnumerateDirectories(root))
        {
            token.ThrowIfCancellationRequested();
            if (!Guid.TryParseExact(Path.GetFileName(directory), "N", out var id)) continue;
            var value = await AtomicJsonFile.ReadAsync<BatchImportSession>(Path.Combine(directory, "session.json"), token);
            if (value?.SchemaVersion != 1 || value.Id != id) continue;
            if (fingerprint is not null && value.SourceFingerprint == fingerprint || destination is not null &&
                string.Equals(value.OutputDirectory, destination, StringComparison.OrdinalIgnoreCase)) result.Add(value);
        }
        return result;
    }
    public async Task SaveEntryAsync(BatchImportSession session, BatchImportEntry entry, CancellationToken token = default)
        => await SaveEntriesAsync(session, [entry], token).ConfigureAwait(false);

    public async Task SaveEntriesAsync(BatchImportSession session, IEnumerable<BatchImportEntry> entries, CancellationToken token = default)
    {
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var path = Path.Combine(Folder(session.Id), "updates.jsonl");
            // A crash may leave a complete JSON object without its trailing newline.
            var needsSeparator = false;
            var hasTail = false;
            if (File.Exists(path))
            {
                using var tail = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                if (tail.Length > 0) { hasTail = true; tail.Seek(-1, SeekOrigin.End); needsSeparator = tail.ReadByte() != '\n'; }
            }
            if (hasTail)
            {
                var torn = false;
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    stream.Position = LastLineStart(stream);
                    using var reader = new StreamReader(stream, Encoding.UTF8);
                    try { _ = JsonSerializer.Deserialize<BatchImportEntry>(await reader.ReadLineAsync(token) ?? "", Lines); }
                    catch (JsonException) { torn = true; }
                }
                if (torn) { await RecoverJournalAsync(path, token).ConfigureAwait(false); needsSeparator = false; }
            }
            await using var writer = new StreamWriter(new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read, 8192, true), new UTF8Encoding(false));
            if (needsSeparator) await writer.WriteLineAsync().ConfigureAwait(false);
            foreach (var entry in entries)
                await writer.WriteLineAsync(JsonSerializer.Serialize(entry, Lines).AsMemory(), token).ConfigureAwait(false);
            await writer.FlushAsync(token).ConfigureAwait(false);
        }
        finally { gate.Release(); }
    }
    public async Task<IReadOnlyList<BatchImportSession>> RecentAsync(CancellationToken token = default)
    {
        if (!Directory.Exists(root)) return [];
        var result = new List<BatchImportSession>();
        foreach (var directory in Directory.EnumerateDirectories(root))
        {
            if (!Guid.TryParseExact(Path.GetFileName(directory), "N", out var id)) continue;
            var value = await AtomicJsonFile.ReadAsync<BatchImportSession>(Path.Combine(directory, "session.json"), token);
            if (value?.SchemaVersion == 1 && value.Id == id) result.Add(value);
        }
        return result.OrderByDescending(value => value.CreatedAt).Take(30).ToArray();
    }
    public async Task<BatchImportSession> LoadAsync(Guid id, CancellationToken token = default)
    {
        await gate.WaitAsync(token).ConfigureAwait(false);
        try { return await LoadCoreAsync(id, token).ConfigureAwait(false); }
        finally { gate.Release(); }
    }
    private async Task<BatchImportSession> LoadCoreAsync(Guid id, CancellationToken token)
    {
        var session = await AtomicJsonFile.ReadAsync<BatchImportSession>(Path.Combine(Folder(id), "session.json"), token)
            ?? throw InvalidSession(1);
        if (session.SchemaVersion != 1) throw InvalidSession(2);
        if (session.Id != id) throw InvalidSession(3);
        if (!Enum.IsDefined(session.WorkflowPhase)) throw InvalidSession(4);
        await ReadRows(Path.Combine(Folder(id), "entries.jsonl"), false);
        await ReadRows(Path.Combine(Folder(id), "updates.jsonl"), true);
        session.Paused = true;
        if (session.WorkflowPhase is BatchWorkflowPhase.Analyzing or BatchWorkflowPhase.Queueing)
            session.WorkflowPhase = BatchWorkflowPhase.Paused;
        foreach (var entry in session.Entries)
            if (entry.Status is BatchEntryStatus.Analyzing or BatchEntryStatus.Validating) entry.Status = BatchEntryStatus.Pending;
        return session;

        async Task ReadRows(string path, bool journal)
        {
            if (!File.Exists(path)) { if (!journal) throw InvalidSession(5); return; }
            var torn = false;
            using (var reader = new StreamReader(path, Encoding.UTF8))
            {
            while (await reader.ReadLineAsync(token) is { } line)
            {
                BatchImportEntry? row;
                try { row = JsonSerializer.Deserialize<BatchImportEntry>(line, Lines); }
                catch (JsonException) when (journal)
                { if (await reader.ReadLineAsync(token) is not null) throw; torn = true; break; }
                if (row is null || row.Id == Guid.Empty || row.Url is null || row.Url.Length > 4096 ||
                    row.Identity is null || row.Source is null || row.Title is null || row.Quality is null || row.Range is null ||
                    !Enum.IsDefined(row.Status) || !Enum.IsDefined(row.Container) || !Enum.IsDefined(row.Range.Mode))
                    throw new InvalidDataException("Batch.Error.Session");
                if (session.ById.TryGetValue(row.Id, out var previous))
                {
                    if (!journal) throw new InvalidDataException("Batch.Error.Session");
                    if (row.Ordinal != previous.Ordinal) throw new InvalidDataException("Batch.Error.Session");
                    if (row.Revision < previous.Revision) continue;
                    session.Entries[previous.Ordinal - 1] = row; session.ById[row.Id] = row;
                }
                else
                {
                    if (journal || row.Ordinal != session.Entries.Count + 1 || session.Entries.Count >= BatchImportParser.MaximumEntries) throw new InvalidDataException("Batch.Error.Session");
                    session.Entries.Add(row); session.ById.Add(row.Id, row);
                }
            }
            }
            if (torn) await RecoverJournalAsync(path, token).ConfigureAwait(false);
        }
    }
    private static long LastLineStart(FileStream stream)
    {
        var end = stream.Length;
        if (end == 0) return 0;
        stream.Position = end - 1;
        if (stream.ReadByte() == '\n') end--;
        var buffer = new byte[8192];
        while (end > 0)
        {
            var start = Math.Max(0, end - buffer.Length); var length = (int)(end - start);
            stream.Position = start; stream.ReadExactly(buffer.AsSpan(0, length));
            for (var index = length - 1; index >= 0; index--)
                if (buffer[index] == '\n') return start + index + 1;
            end = start;
        }
        return 0;
    }
    private static async Task RecoverJournalAsync(string path, CancellationToken token)
    {
        // Retain every original byte, including the torn tail, before replacing the
        // active journal. Continuing to append to the tail would corrupt the next restart.
        var backup = path + ".torn-" + Guid.NewGuid().ToString("N");
        File.Copy(path, backup, false);
        var temporary = path + ".recovered-" + Guid.NewGuid().ToString("N");
        await using (var source = new FileStream(backup, FileMode.Open, FileAccess.Read, FileShare.Read, 8192, true))
        await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 8192, true))
        {
            var remaining = LastLineStart(source); source.Position = 0;
            var buffer = new byte[8192];
            while (remaining > 0)
            {
                var count = (int)Math.Min(remaining, buffer.Length);
                await source.ReadExactlyAsync(buffer.AsMemory(0, count), token).ConfigureAwait(false);
                await stream.WriteAsync(buffer.AsMemory(0, count), token).ConfigureAwait(false);
                remaining -= count;
            }
            await stream.FlushAsync(token).ConfigureAwait(false);
            stream.Flush(flushToDisk: true);
        }
        token.ThrowIfCancellationRequested();
        File.Move(temporary, path, true);
    }
    public Task SaveMetadataAsync(Guid session, Guid entry, VideoMetadata metadata, CancellationToken token)
        => AtomicJsonFile.WriteRawJsonAsync(Path.Combine(Folder(session), entry.ToString("N") + ".json"),
            string.IsNullOrWhiteSpace(metadata.RawJson) ? JsonSerializer.Serialize(metadata, JsonDefaults.Options) : metadata.RawJson, token);
    public async Task<VideoMetadata?> MetadataAsync(Guid session, Guid entry, CancellationToken token = default)
    {
        var path = Path.Combine(Folder(session), entry.ToString("N") + ".json");
        if (!File.Exists(path)) return null;
        var json = await File.ReadAllTextAsync(path, token);
        var value = JsonSerializer.Deserialize<VideoMetadata>(json, JsonDefaults.Options);
        if (value is not null) value.RawJson = json;
        return value;
    }
}
