using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Models;

namespace ModernTubeDownloader.Services;

public sealed class HistoryService(AppPaths paths, IAppLogger logger)
{
    private readonly object sync = new();
    private readonly SemaphoreSlim saveGate = new(1, 1);
    private readonly List<DownloadHistoryEntry> entries = [];

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
        try
        {
            var loaded = await AtomicJsonFile.ReadAsync<List<DownloadHistoryEntry?>>(paths.HistoryFile, cancellationToken).ConfigureAwait(false) ?? [];
            lock (sync)
            {
                entries.Clear();
                entries.AddRange(loaded.OfType<DownloadHistoryEntry>().Select(Normalize));
            }
            LoadFailed = false;
        }
        catch (Exception ex)
        {
            LoadFailed = true;
            logger.Error("Could not read download history.", ex);
        }
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
        if (LoadFailed) throw new IOException("History could not be loaded; preserve the original file before recovery.");
        await saveGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            List<DownloadHistoryEntry> next;
            lock (sync) next = entries.Where(existing => existing.QueueItemId != entry.QueueItemId ||
                existing.LivePartIndex != entry.LivePartIndex).ToList();
            next.Add(Normalize(entry));
            await AtomicJsonFile.WriteAsync(paths.HistoryFile, next, cancellationToken).ConfigureAwait(false);
            lock (sync) { entries.Clear(); entries.AddRange(next); }
        }
        finally { saveGate.Release(); }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Durably replaces only file references. Replaying an organizer journal is idempotent.</summary>
    public async Task RelocatePathsAsync(IReadOnlyDictionary<string, string> replacements, CancellationToken cancellationToken = default)
    {
        if (LoadFailed) throw new IOException("Organizer.Error.Journal");
        await saveGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var lookup = new Dictionary<string, string>(replacements, StringComparer.OrdinalIgnoreCase);
            List<DownloadHistoryEntry> next;
            lock (sync) next = entries.Select(entry => lookup.TryGetValue(entry.FinalPath, out var path)
                ? entry with { FinalPath = path } : entry).ToList();
            await AtomicJsonFile.WriteAsync(paths.HistoryFile, next, cancellationToken).ConfigureAwait(false);
            lock (sync) { entries.Clear(); entries.AddRange(next); }
        }
        finally { saveGate.Release(); }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        if (LoadFailed) throw new IOException("History could not be loaded; preserve the original file before recovery.");
        await saveGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await AtomicJsonFile.WriteAsync(paths.HistoryFile, Snapshot(), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            saveGate.Release();
        }
    }
}
