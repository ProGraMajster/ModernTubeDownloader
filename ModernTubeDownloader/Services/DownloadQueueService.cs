using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Models;

namespace ModernTubeDownloader.Services;

public sealed record QueueBatchAddResult(int Added, int DuplicatesSkipped);

public sealed class DownloadQueueService
{
    private readonly IQueuePersistence persistence;
    private readonly IAppLogger logger;
    private readonly SettingsService? settings;
    private readonly object sync = new();
    private readonly List<DownloadQueueItem> items = [];
    private bool isPaused;
    private readonly SemaphoreSlim checkpointGate = new(1, 1);
    private readonly SemaphoreSlim recoveryGate = new(1, 1);
    private long revision, savedRevision;
    public QueueStorageSafety Storage { get; }
    public bool HasUnsavedChanges { get { lock (sync) return revision != savedRevision; } }

    public DownloadQueueService(IQueuePersistence persistence, IAppLogger logger, SettingsService? settings = null,
        QueueStorageSafety? storage = null)
    {
        this.persistence = persistence; this.logger = logger; this.settings = settings;
        isPaused = settings?.Current.QueuePaused ?? false;
        Storage = storage ?? new();
        Storage.Changed += (_, _) =>
        {
            bool savePause = false;
            lock (sync)
            {
                if (Storage.State.IsBlocked && !isPaused) { isPaused = true; savePause = settings is not null; }
            }
            if (savePause)
            {
                settings!.Current.QueuePaused = true;
                _ = SavePauseSafelyAsync();
            }
            Changed?.Invoke(this, new QueueChangedEventArgs(QueueChangeKind.Pause));
        };
    }

    public event EventHandler<QueueChangedEventArgs>? Changed;

    public bool IsPaused
    {
        get { lock (sync) return isPaused || Storage.State.IsBlocked; }
    }

    public IReadOnlyList<DownloadQueueItem> Snapshot()
    {
        lock (sync)
            return items.ToArray();
    }

    public IReadOnlyList<DownloadNotificationState> NotificationSnapshot()
    {
        lock (sync) return items.Select(DownloadNotificationState.From).ToArray();
    }

    public IReadOnlyList<QueueProgressItem> ProgressSnapshot()
    {
        lock (sync) return items.Select(item => new QueueProgressItem(item.Id, item.Status, item.ProgressPercent,
            item.EstimatedSizeBytes, item.TotalBytes, item.DownloadedBytes, item.SpeedBytesPerSecond)).ToArray();
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
        lock (sync) { items.Add(item); revision++; }
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
                revision++;
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
            if (removed) revision++;
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
        lock (sync) { count = items.RemoveAll(item => item.Status is DownloadStatus.Queued or DownloadStatus.Waiting); if (count > 0) revision++; }
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
            revision++;
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
            revision++;
        }

        Changed?.Invoke(this, new QueueChangedEventArgs(QueueChangeKind.Item, id));
        _ = SaveSafelyAsync();
        return true;
    }


    public void SetPaused(bool paused)
    {
        if (!paused && Storage.State.IsBlocked) { _ = ResumeAsync(); return; }
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
            if (isPaused || Storage.State.IsBlocked)
                return null;
            item = items.FirstOrDefault(candidate => candidate.Status == DownloadStatus.Queued);
            if (item is not null)
            {
                item.Status = DownloadStatus.Waiting;
                item.StatusMessage = "Preparing download";
                item.StatusMessageKey = "Queue.Detail.Preparing";
                item.StartedAt = DateTimeOffset.Now;
                revision++;
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
            if (persist) revision++;
        }
        Changed?.Invoke(this, new QueueChangedEventArgs(QueueChangeKind.Item, id));
        if (persist)
            _ = SaveSafelyAsync();
    }

    public Task SaveAsync(CancellationToken cancellationToken = default) => SaveCheckpointAsync(true, cancellationToken);

    private async Task SaveCheckpointAsync(bool retryDegraded, CancellationToken cancellationToken = default)
    {
        await checkpointGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!retryDegraded && Storage.State.PersistenceDegraded) return;
            lock (sync) { if (!retryDegraded && savedRevision == revision) return; }
            try
            {
                DownloadQueueItem[] snapshot; long writingRevision;
                lock (sync) { snapshot = items.Select(item => item.PersistenceCopy()).ToArray(); writingRevision = revision; }
                await persistence.SaveAsync(snapshot, cancellationToken).ConfigureAwait(false);
                lock (sync) savedRevision = writingRevision;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception error) { ReportPersistenceFailure(error); throw; }
        }
        finally { checkpointGate.Release(); }
    }

    /// <summary>Explicit recovery only: probe, durable runtime checkpoint, then reopen admission.</summary>
    public async Task<bool> ResumeAsync(CancellationToken token = default)
    {
        await recoveryGate.WaitAsync(token).ConfigureAwait(false);
        var resumed = false;
        try
        {
            var failureVersion = Storage.State.FailureVersion;
            try
            {
                try { await Storage.CheckWritableAsync(token).ConfigureAwait(false); }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { return false; }
                catch (Exception probeError)
                {
                    Storage.Report(probeError);
                    logger.Warning($"Queue recovery write probe failed ({probeError.GetType().Name}); admission remains paused.");
                    return false; // An unavailable output folder is not proof that queue.json failed to save.
                }
                // Active phase transitions may mutate the queue during a save. Never
                // clear degraded state on a stale checkpoint; bound reconciliation.
                for (var attempt = 0; attempt < 3; attempt++)
                {
                    await SaveAsync(token).ConfigureAwait(false);
                    if (!HasUnsavedChanges) break;
                }
                if (HasUnsavedChanges) return false;
                if (settings is not null)
                {
                    settings.Current.QueuePaused = false;
                    await settings.SaveAsync(token).ConfigureAwait(false);
                }
                lock (sync)
                {
                    if (revision != savedRevision || Storage.State.FailureVersion != failureVersion) return false;
                    if (!Storage.Recover(failureVersion)) return false;
                    isPaused = false;
                }
                resumed = true;
                logger.Info("Queue storage recovery checkpoint succeeded; admission resumed.");
                Changed?.Invoke(this, new QueueChangedEventArgs(QueueChangeKind.Pause));
                return true;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { return false; }
            catch (Exception error) { ReportPersistenceFailure(error); return false; }
        }
        finally
        {
            // A new failure/transition during the checkpoint must not leave a
            // persisted unpause which could reopen admission after restart.
            if (!resumed && settings is not null && !settings.Current.QueuePaused)
            {
                settings.Current.QueuePaused = true;
                await SavePauseSafelyAsync().ConfigureAwait(false);
            }
            recoveryGate.Release();
        }
    }

    internal async Task CheckpointBeforeStartAsync(CancellationToken token)
    {
        Storage.ThrowIfBlocked();
        await SaveCheckpointAsync(false, token).ConfigureAwait(false);
        Storage.ThrowIfBlocked();
    }

    public void ReportStorageFailure(Exception error, string? directory = null)
    {
        if (Storage.Report(error, directory: directory))
        {
            logger.Warning($"Queue admission stopped: storage={Storage.State.Kind}; active processes and recovery data are retained.");
            Changed?.Invoke(this, new QueueChangedEventArgs(QueueChangeKind.Pause));
        }
    }

    private void ReportPersistenceFailure(Exception error)
    {
        if (Storage.Report(error, persistence: true))
        {
            logger.Error("Queue persistence degraded; new jobs are blocked until a successful explicit recovery checkpoint.", error);
            Changed?.Invoke(this, new QueueChangedEventArgs(QueueChangeKind.Pause));
        }
    }

    internal async Task SaveSafelyAsync()
    {
        // No per-progress/per-job retry storm on a known failing disk. Resume is
        // the bounded explicit retry boundary; dirty runtime state remains owned.
        if (Storage.State.PersistenceDegraded) return;
        try { await SaveCheckpointAsync(false).ConfigureAwait(false); }
        catch (Exception) { /* SaveAsync recorded and latched the actual failure. */ }
    }

    private async Task SavePauseSafelyAsync()
    {
        try { await settings!.SaveAsync().ConfigureAwait(false); }
        catch (Exception ex) { ReportPersistenceFailure(ex); }
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
