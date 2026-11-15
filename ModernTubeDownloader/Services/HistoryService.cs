using System.Text.Json;
using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Models;

namespace ModernTubeDownloader.Services;

public sealed class HistoryService(AppPaths paths, IAppLogger logger)
{
    private static readonly JsonSerializerOptions ReadOptions = new(JsonDefaults.Options) { AllowDuplicateProperties = false };
    private readonly object sync = new();
    private readonly SemaphoreSlim saveGate = new(1, 1);
    private readonly List<DownloadHistoryEntry> entries = [];
    private bool hasObservedFile;

    public event EventHandler? Changed;
    public bool LoadFailed { get; private set; }

    public IReadOnlyList<DownloadHistoryEntry> Snapshot()
    {
        using var timing = PerformanceDiagnostics.Measure("history.snapshot.sort");
        lock (sync)
            return entries.OrderByDescending(entry => entry.CompletedAt).ToArray();
    }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        await saveGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // A failed read must stay blocked for this runtime even if a later
            // caller retries after the damaged file was removed or replaced.
            if (LoadFailed) return;
            await using var stream = new FileStream(paths.HistoryFile, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
            hasObservedFile = true;
            var loaded = await JsonSerializer.DeserializeAsync<List<DownloadHistoryEntry?>>(
                stream, ReadOptions, cancellationToken).ConfigureAwait(false)
                ?? throw new JsonException("The saved history is null, not a history collection.");
            var identities = new HashSet<(Guid QueueItemId, int? LivePartIndex)>(loaded.Count);
            var validated = new DownloadHistoryEntry[loaded.Count];
            for (var index = 0; index < loaded.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var entry = loaded[index] ?? throw new JsonException("The saved history contains a null row.");
                if (entry.QueueItemId == Guid.Empty || !identities.Add((entry.QueueItemId, entry.LivePartIndex)))
                    throw new JsonException("The saved history contains an empty or duplicate canonical identity.");
                validated[index] = Normalize(entry);
            }
            lock (sync)
            {
                entries.Clear();
                entries.AddRange(validated);
            }
        }
        catch (FileNotFoundException) when (!hasObservedFile) { /* A new profile has no history yet. */ }
        catch (DirectoryNotFoundException) when (!hasObservedFile) { /* A new profile directory may not exist yet. */ }
        catch (Exception ex)
        {
            LoadFailed = true;
            logger.Error("Could not read download history.", ex);
        }
        finally { saveGate.Release(); }
    }

    private static DownloadHistoryEntry Normalize(DownloadHistoryEntry entry)
    {
        return entry with
        {
            Title = entry.Title ?? string.Empty,
            SourceUrl = entry.SourceUrl ?? string.Empty,
            VideoId = entry.VideoId ?? string.Empty,
            Channel = entry.Channel ?? string.Empty,
            Quality = entry.Quality ?? string.Empty,
            FinalPath = entry.FinalPath ?? string.Empty,
            Status = entry.Status ?? string.Empty,
            DurationSeconds = entry.DurationSeconds is > 0 and var duration && double.IsFinite(duration) ? duration : null,
            FileSizeBytes = entry.FileSizeBytes is >= 0 ? entry.FileSizeBytes : null
        };
    }

    public async Task AddAsync(DownloadHistoryEntry entry, CancellationToken cancellationToken = default)
    {
        await saveGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureWritable();
            List<DownloadHistoryEntry> next;
            lock (sync) next = entries.Where(existing => existing.QueueItemId != entry.QueueItemId ||
                existing.LivePartIndex != entry.LivePartIndex).ToList();
            next.Add(Normalize(entry));
            await AtomicJsonFile.WriteAsync(paths.HistoryFile, next, cancellationToken).ConfigureAwait(false);
            hasObservedFile = true;
            lock (sync) { entries.Clear(); entries.AddRange(next); }
        }
        finally { saveGate.Release(); }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Durably replaces only file references. Replaying an organizer journal is idempotent.</summary>
    public async Task RelocatePathsAsync(IReadOnlyDictionary<string, string> replacements, CancellationToken cancellationToken = default)
    {
        await saveGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureWritable();
            var lookup = new Dictionary<string, string>(replacements, StringComparer.OrdinalIgnoreCase);
            List<DownloadHistoryEntry> next;
            lock (sync) next = entries.Select(entry => lookup.TryGetValue(entry.FinalPath, out var path)
                ? entry with { FinalPath = path } : entry).ToList();
            await AtomicJsonFile.WriteAsync(paths.HistoryFile, next, cancellationToken).ConfigureAwait(false);
            hasObservedFile = true;
            lock (sync) { entries.Clear(); entries.AddRange(next); }
        }
        finally { saveGate.Release(); }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        await saveGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureWritable();
            await AtomicJsonFile.WriteAsync(paths.HistoryFile, Snapshot(), cancellationToken).ConfigureAwait(false);
            hasObservedFile = true;
        }
        finally
        {
            saveGate.Release();
        }
    }

    private void EnsureWritable()
    {
        if (LoadFailed) throw new IOException("History could not be loaded; preserve the original file and restart after recovery.");
    }
}
