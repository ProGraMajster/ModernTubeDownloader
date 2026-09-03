using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Models;

namespace ModernTubeDownloader.Services;

public sealed class DownloadQueueService(QueuePersistenceService persistence, IAppLogger logger)
{
    private readonly object sync = new();
    private readonly List<DownloadQueueItem> items = [];
    private bool isPaused;

    public event EventHandler<QueueChangedEventArgs>? Changed;

    public bool IsPaused
    {
        get { lock (sync) return isPaused; }
    }

    public IReadOnlyList<DownloadQueueItem> Snapshot()
    {
        lock (sync)
            return items.ToArray();
    }

    public DownloadQueueItem? Find(Guid id)
    {
        lock (sync)
            return items.FirstOrDefault(item => item.Id == id);
    }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        var loaded = await persistence.LoadAsync(cancellationToken).ConfigureAwait(false);
        lock (sync)
        {
            items.Clear();
            foreach (var item in loaded)
            {
                if (IsActive(item.Status))
                {
                    item.Status = DownloadStatus.Queued;
                    item.StatusMessage = "Recovered after application restart";
                    item.ProgressPercent = 0;
                    item.SpeedBytesPerSecond = null;
                    item.Eta = null;
                }
                items.Add(item);
            }
        }
        Changed?.Invoke(this, new QueueChangedEventArgs(QueueChangeKind.Collection));
    }

    public void Add(DownloadQueueItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        lock (sync)
            items.Add(item);
        logger.Info($"Queue item added: {item.Id}, media={item.VideoId}.");
        Changed?.Invoke(this, new QueueChangedEventArgs(QueueChangeKind.Collection, item.Id));
        _ = SaveSafelyAsync();
    }

    public bool Remove(Guid id)
    {
        bool removed;
        lock (sync)
        {
            var item = items.FirstOrDefault(candidate => candidate.Id == id);
            removed = item is not null && !IsActive(item.Status) && items.Remove(item);
        }
        if (removed)
        {
            Changed?.Invoke(this, new QueueChangedEventArgs(QueueChangeKind.Collection, id));
            _ = SaveSafelyAsync();
        }
        return removed;
    }

    public int RemoveAllPending()
    {
        int count;
        lock (sync)
            count = items.RemoveAll(item => item.Status is DownloadStatus.Queued or DownloadStatus.Waiting);
        if (count > 0)
        {
            Changed?.Invoke(this, new QueueChangedEventArgs(QueueChangeKind.Collection));
            _ = SaveSafelyAsync();
        }
        return count;
    }

    public bool Move(Guid id, int targetIndex)
    {
        lock (sync)
        {
            var index = items.FindIndex(item => item.Id == id);
            if (index < 0 || IsActive(items[index].Status))
                return false;
            targetIndex = Math.Clamp(targetIndex, 0, items.Count - 1);
            if (targetIndex == index)
                return false;
            var item = items[index];
            items.RemoveAt(index);
            items.Insert(targetIndex, item);
        }

        Changed?.Invoke(this, new QueueChangedEventArgs(QueueChangeKind.Order, id));
        _ = SaveSafelyAsync();
        return true;
    }

    public bool MoveBy(Guid id, int offset)
    {
        lock (sync)
        {
            var index = items.FindIndex(item => item.Id == id);
            return index >= 0 && Move(id, index + offset);
        }
    }

    public bool Retry(Guid id)
    {
        lock (sync)
        {
            var item = items.FirstOrDefault(candidate => candidate.Id == id);
            if (item is null || item.Status is not (DownloadStatus.Failed or DownloadStatus.Cancelled))
                return false;
            item.Status = DownloadStatus.Queued;
            item.StatusMessage = "Queued for retry";
            item.ErrorMessage = null;
            item.ProgressPercent = 0;
            item.DownloadedBytes = null;
            item.TotalBytes = null;
            item.SpeedBytesPerSecond = null;
            item.Eta = null;
            item.StartedAt = null;
            item.CompletedAt = null;
            item.HasPostProcessingWarnings = false;
        }

        Changed?.Invoke(this, new QueueChangedEventArgs(QueueChangeKind.Item, id));
        _ = SaveSafelyAsync();
        return true;
    }

    public void SetPaused(bool paused)
    {
        lock (sync)
        {
            if (isPaused == paused)
                return;
            isPaused = paused;
        }
        logger.Info(paused ? "Queue paused." : "Queue resumed.");
        Changed?.Invoke(this, new QueueChangedEventArgs(QueueChangeKind.Pause));
    }

    internal DownloadQueueItem? TryTakeNext()
    {
        DownloadQueueItem? item;
        lock (sync)
        {
            if (isPaused)
                return null;
            item = items.FirstOrDefault(candidate => candidate.Status == DownloadStatus.Queued);
            if (item is not null)
            {
                item.Status = DownloadStatus.Waiting;
                item.StatusMessage = "Preparing download";
                item.StartedAt = DateTimeOffset.Now;
            }
        }
        if (item is not null)
            Changed?.Invoke(this, new QueueChangedEventArgs(QueueChangeKind.Item, item.Id));
        return item;
    }

    internal void Update(Guid id, Action<DownloadQueueItem> update, bool persist = false)
    {
        lock (sync)
        {
            var item = items.FirstOrDefault(candidate => candidate.Id == id)
                ?? throw new InvalidOperationException("The queue item no longer exists.");
            update(item);
        }
        Changed?.Invoke(this, new QueueChangedEventArgs(QueueChangeKind.Item, id));
        if (persist)
            _ = SaveSafelyAsync();
    }

    public Task SaveAsync(CancellationToken cancellationToken = default) => persistence.SaveAsync(Snapshot(), cancellationToken);

    private async Task SaveSafelyAsync()
    {
        try { await SaveAsync().ConfigureAwait(false); }
        catch (Exception ex) { logger.Error("Could not save the download queue.", ex); }
    }

    private static bool IsActive(DownloadStatus status) => status is
        DownloadStatus.Waiting or DownloadStatus.DownloadingVideo or DownloadStatus.DownloadingAudio or
        DownloadStatus.Merging or DownloadStatus.Finalizing;
}
