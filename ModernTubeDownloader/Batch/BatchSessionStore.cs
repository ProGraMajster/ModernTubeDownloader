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
    public Task SaveSessionAsync(BatchImportSession session, CancellationToken token = default)
        => AtomicJsonFile.WriteAsync(Path.Combine(Folder(session.Id), "session.json"), session, token);
    public async Task SaveEntryAsync(BatchImportSession session, BatchImportEntry entry, CancellationToken token = default)
        => await SaveEntriesAsync(session, [entry], token).ConfigureAwait(false);

    public async Task SaveEntriesAsync(BatchImportSession session, IEnumerable<BatchImportEntry> entries, CancellationToken token = default)
    {
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            await using var writer = new StreamWriter(new FileStream(Path.Combine(Folder(session.Id), "updates.jsonl"), FileMode.Append, FileAccess.Write, FileShare.Read, 8192, true), new UTF8Encoding(false));
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
        var session = await AtomicJsonFile.ReadAsync<BatchImportSession>(Path.Combine(Folder(id), "session.json"), token)
            ?? throw new InvalidDataException("Batch.Error.Session");
        if (session.SchemaVersion != 1 || session.Id != id) throw new InvalidDataException("Batch.Error.Session");
        await ReadRows(Path.Combine(Folder(id), "entries.jsonl"), false);
        await ReadRows(Path.Combine(Folder(id), "updates.jsonl"), true);
        session.Paused = true;
        foreach (var entry in session.Entries)
            if (entry.Status is BatchEntryStatus.Analyzing or BatchEntryStatus.Validating) entry.Status = BatchEntryStatus.Pending;
        return session;

        async Task ReadRows(string path, bool journal)
        {
            if (!File.Exists(path)) { if (!journal) throw new InvalidDataException("Batch.Error.Session"); return; }
            using var reader = new StreamReader(path, Encoding.UTF8);
            while (await reader.ReadLineAsync(token) is { } line)
            {
                BatchImportEntry? row;
                try { row = JsonSerializer.Deserialize<BatchImportEntry>(line, Lines); }
                catch (JsonException) when (journal)
                { if (await reader.ReadLineAsync(token) is not null) throw; break; } // A torn final append has no committed effect.
                if (row is null || row.Id == Guid.Empty || row.Url.Length > 4096 || !Enum.IsDefined(row.Status)) throw new InvalidDataException("Batch.Error.Session");
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
