using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Models;

namespace ModernTubeDownloader.Services;

public sealed record QueueBatchAddResult(int Added, int DuplicatesSkipped);

public sealed class DownloadQueueService(
    QueuePersistenceService persistence,
    IAppLogger logger,
    SettingsService? settings = null)
{
    private readonly object sync = new();
    private readonly List<DownloadQueueItem> items = [];
    private bool isPaused = settings?.Current.QueuePaused ?? false;

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

    public bool ContainsPendingOrActive(
        string videoId,
        string qualityPresetId,
        PreferredVideoContainer preferredVideoContainer = PreferredVideoContainer.Auto,
        MediaTimeRange? requestedRange = null,
        SubtitleOptions? subtitles = null,
        SponsorBlockOptions? sponsorBlock = null)
    {
        lock (sync)
            return items.Any(item =>
                string.Equals(item.VideoId, videoId, StringComparison.Ordinal) &&
                string.Equals(item.QualityPresetId, qualityPresetId, StringComparison.OrdinalIgnoreCase) &&
                item.PreferredVideoContainer == preferredVideoContainer &&
                (item.RequestedRange ?? MediaTimeRange.Full) == (requestedRange ?? MediaTimeRange.Full) &&
                (subtitles is null && sponsorBlock is null || EnhancementKey(item.SubtitleOptions, item.SponsorBlockOptions) ==
                    EnhancementKey(subtitles, sponsorBlock)) &&
                item.Status is not (DownloadStatus.Completed or DownloadStatus.Failed or DownloadStatus.Cancelled or DownloadStatus.Partial));
    }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        var loaded = await persistence.LoadAsync(cancellationToken).ConfigureAwait(false);
        lock (sync)
        {
            items.Clear();
            foreach (var item in loaded)
            {
                if (item.LiveSession is not null)
                    throw new InvalidOperationException("Migrate legacy LIVE persistence before loading the VOD queue.");
                if (IsActive(item.Status) ||
                    (item.Status == DownloadStatus.Interrupted && settings?.Current.AutoResumeAfterRestart == true))
                {
                    item.Status = settings?.Current.AutoResumeAfterRestart == true
                        ? DownloadStatus.Queued : DownloadStatus.Interrupted;
                    item.StatusMessage = "Interrupted; resumable data was preserved";
                    item.StatusMessageKey = "Queue.Detail.Interrupted";
                    item.SpeedBytesPerSecond = null;
                    item.Eta = null;
                    item.AttemptCount = 0;
                    item.MaximumAttempts = 0;
                    item.RetryDelaySeconds = 0;
                    item.FailureMessageKey = null;
                    item.FailureTechnicalSummary = null;
                    item.FailureCategory = null;
                    item.FailureHttpStatus = null;
                    item.ErrorMessage = null;
                }
                items.Add(item);
            }
        }
        Changed?.Invoke(this, new QueueChangedEventArgs(QueueChangeKind.Collection));
    }

    public void Add(DownloadQueueItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        EnsureVod(item);
        DownloadOptionCompatibilityValidator.EnsureSupported(item.RequestedRange, item.SubtitleOptions,
            item.SponsorBlockOptions, item.PreferredVideoContainer);
        lock (sync)
            items.Add(item);
        logger.Info($"Queue item added: {item.Id}, media={item.VideoId}.");
        Changed?.Invoke(this, new QueueChangedEventArgs(QueueChangeKind.Collection, item.Id));
        _ = SaveSafelyAsync();
    }

    public QueueBatchAddResult AddRangeSkippingDuplicates(IReadOnlyList<DownloadQueueItem> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        foreach (var candidate in candidates)
        {
            EnsureVod(candidate);
            DownloadOptionCompatibilityValidator.EnsureSupported(candidate.RequestedRange, candidate.SubtitleOptions,
                candidate.SponsorBlockOptions, candidate.PreferredVideoContainer);
        }
        var added = 0;
        var duplicates = 0;
        Guid? lastAdded = null;
        lock (sync)
        {
            var activeKeys = items
                .Where(item => item.Status is not (DownloadStatus.Completed or DownloadStatus.Failed or DownloadStatus.Cancelled or DownloadStatus.Partial))
                .Select(item => (item.VideoId, Quality: (item.QualityPresetId ?? string.Empty).ToUpperInvariant(), item.PreferredVideoContainer,
                    Range: item.RequestedRange ?? MediaTimeRange.Full, Enhancements: EnhancementKey(item.SubtitleOptions, item.SponsorBlockOptions)))
                .ToHashSet();
            foreach (var item in candidates)
            {
                ArgumentNullException.ThrowIfNull(item);
                var key = (item.VideoId, Quality: (item.QualityPresetId ?? string.Empty).ToUpperInvariant(), item.PreferredVideoContainer,
                    Range: item.RequestedRange ?? MediaTimeRange.Full, Enhancements: EnhancementKey(item.SubtitleOptions, item.SponsorBlockOptions));
                if (!activeKeys.Add(key)) { duplicates++; continue; }
                items.Add(item);
                added++;
                lastAdded = item.Id;
            }
        }
        if (added > 0)
        {
            logger.Info($"Added {added} queue items in one batch; skipped {duplicates} duplicates.");
            Changed?.Invoke(this, new QueueChangedEventArgs(QueueChangeKind.Collection, lastAdded));
            _ = SaveSafelyAsync();
        }
        return new QueueBatchAddResult(added, duplicates);
    }

    public bool Remove(Guid id)
    {
        bool removed;
        lock (sync)
        {
            var item = items.FirstOrDefault(candidate => candidate.Id == id);
            removed = item is not null &&
                !IsActive(item.Status) && items.Remove(item);
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
            if (item is null || item.Status is not (DownloadStatus.Failed or DownloadStatus.Cancelled or DownloadStatus.Partial or DownloadStatus.Interrupted))
                return false;
            item.Status = DownloadStatus.Queued;
            item.StatusMessage = "Queued for retry";
            item.StatusMessageKey = "Queue.Detail.Retry";
            item.ErrorMessage = null;
            item.FailureMessageKey = null;
            item.FailureTechnicalSummary = null;
            item.FailureCategory = null;
            item.FailureHttpStatus = null;
            item.AttemptCount = 0;
            item.MaximumAttempts = 0;
            item.RetryDelaySeconds = 0;
            item.SelectedFormats = null;
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
        if (settings is not null)
        {
            settings.Current.QueuePaused = paused;
            _ = SavePauseSafelyAsync();
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
                item.StatusMessageKey = "Queue.Detail.Preparing";
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

    private async Task SavePauseSafelyAsync()
    {
        try { await settings!.SaveAsync().ConfigureAwait(false); }
        catch (Exception ex) { logger.Error("Could not save the queue pause state.", ex); }
    }

    private static bool IsActive(DownloadStatus status) => status is
        DownloadStatus.Waiting or DownloadStatus.DownloadingVideo or DownloadStatus.DownloadingAudio or
        DownloadStatus.Merging or DownloadStatus.Finalizing;

    private static void EnsureVod(DownloadQueueItem item)
    {
        if (item.LiveSession is not null || item.Status is DownloadStatus.WaitingForLive or
            DownloadStatus.RecordingLive or DownloadStatus.ReconnectingLive)
            throw new InvalidOperationException("LIVE sessions belong to LiveSessionService, not the VOD queue.");
    }

    private static string EnhancementKey(SubtitleOptions? subtitles, SponsorBlockOptions? sponsorBlock)
    {
        subtitles ??= new();
        sponsorBlock ??= new();
        var subtitleKey = subtitles.Enabled
            ? $"{subtitles.Source}:{string.Join(',', subtitles.Languages.Select(value => value.ToLowerInvariant()).Order(StringComparer.Ordinal))}:" +
              $"{subtitles.Format}:{subtitles.Embed}:{subtitles.KeepFiles}"
            : "off";
        var sponsorKey = sponsorBlock.Mode != SponsorBlockMode.Off
            ? $"{sponsorBlock.Mode}:{string.Join(',', sponsorBlock.Categories.Select(value => value.ToLowerInvariant()).Order(StringComparer.Ordinal))}"
            : "off";
        return $"{subtitleKey}|{sponsorKey}";
    }
}
