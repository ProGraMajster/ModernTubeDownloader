using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Models;

namespace ModernTubeDownloader.Services;

public sealed class HistoryService(AppPaths paths, IAppLogger logger)
{
    private readonly object sync = new();
    private readonly SemaphoreSlim saveGate = new(1, 1);
    private readonly List<DownloadHistoryEntry> entries = [];

    public event EventHandler? Changed;

    public IReadOnlyList<DownloadHistoryEntry> Snapshot()
    {
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
        }
        catch (Exception ex)
        {
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
            Status = entry.Status ?? string.Empty
        };
    }

    public async Task AddAsync(DownloadHistoryEntry entry, CancellationToken cancellationToken = default)
    {
        lock (sync)
        {
            entries.RemoveAll(existing => existing.QueueItemId == entry.QueueItemId &&
                existing.LivePartIndex == entry.LivePartIndex);
            entries.Add(entry);
        }
        await SaveAsync(cancellationToken).ConfigureAwait(false);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
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
