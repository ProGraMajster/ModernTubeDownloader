using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Models;

namespace ModernTubeDownloader.Services;

public sealed class QueueProcessor : IDisposable
{
    private readonly DownloadQueueService queue;
    private readonly IDownloadJobExecutor executor;
    private readonly SettingsService settings;
    private readonly IAppLogger logger;
    private readonly IDownloadTiming timing;
    private readonly SemaphoreSlim signal = new(0, int.MaxValue);
    private readonly CancellationTokenSource shutdown = new();
    private readonly object activeSync = new();
    private readonly object pacingSync = new();
    private readonly Dictionary<Guid, ActiveDownload> activeDownloads = [];
    private readonly HashSet<Guid> cancellationsBeforeRegistration = [];
    private Task? loopTask;
    private DateTimeOffset? nextAdmissionAt;
    private bool hasAdmittedItem;
    private bool wasPaused;
    private bool pacingWasEnabled;
    private int previousMinimumDelay = -1;
    private int previousMaximumDelay = -1;

    public QueueProcessor(DownloadQueueService queue, IDownloadJobExecutor executor, SettingsService settings,
        IAppLogger logger, IDownloadTiming? timing = null)
    {
        this.queue = queue;
        this.executor = executor;
        this.settings = settings;
        this.logger = logger;
        this.timing = timing ?? new DownloadTiming();
        queue.Changed += QueueChanged;
        settings.Changed += SettingsChanged;
    }

    public Guid? ActiveItemId
    {
        get { lock (activeSync) return activeDownloads.Count == 0 ? null : activeDownloads.Keys.First(); }
    }

    public IReadOnlyCollection<Guid> ActiveItemIds
    {
        get { lock (activeSync) return activeDownloads.Keys.ToArray(); }
    }

    /// <summary>The scheduler's transient next admission time, only while pacing is the actual gate.</summary>
    public DateTimeOffset? NextAdmissionAt
    {
        get
        {
            if (queue.IsPaused || !settings.Current.EnableInterDownloadDelay ||
                !queue.Snapshot().Any(item => item.Status == DownloadStatus.Queued)) return null;
            lock (activeSync)
                if (activeDownloads.Count >= Math.Clamp(settings.Current.MaxSimultaneousDownloads, 1, 3)) return null;
            lock (pacingSync)
                return nextAdmissionAt is { } due && due > timing.UtcNow ? due : null;
        }
    }

    public TimeSpan? RemainingAdmissionDelay => NextAdmissionAt is { } due
        ? TimeSpan.FromTicks(Math.Max(0, (due - timing.UtcNow).Ticks)) : null;

    public void Start()
    {
        if (loopTask is not null)
            return;
        loopTask = Task.Run(ProcessLoopAsync);
        Wake();
    }

    public bool CancelActive(Guid id)
    {
        lock (activeSync)
        {
            if (activeDownloads.TryGetValue(id, out var active))
            {
                active.Cancellation.Cancel();
                return true;
            }

            // TryTakeNext publishes Waiting before the scheduler can register the
            // linked CTS. Remember a cancellation made in that tiny hand-off so
            // an enabled Cancel action can never silently lose the race.
            if (queue.Find(id)?.Status == DownloadStatus.Waiting)
                return cancellationsBeforeRegistration.Add(id);

            return false;
        }
    }

    public async Task StopAsync()
    {
        shutdown.Cancel();
        lock (activeSync)
            foreach (var active in activeDownloads.Values)
                active.Cancellation.Cancel();
        Wake();
        if (loopTask is not null)
        {
            try { await loopTask.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            catch (Exception ex) { logger.Error("The queue scheduler ended unexpectedly during shutdown.", ex); }
        }
    }

    private async Task ProcessLoopAsync()
    {
        try
        {
            while (!shutdown.IsCancellationRequested)
            {
                if (queue.IsPaused)
                {
                    wasPaused = true;
                    SetNextAdmissionAt(null);
                    await signal.WaitAsync(shutdown.Token).ConfigureAwait(false);
                    continue;
                }
                var pacingEnabled = settings.Current.EnableInterDownloadDelay;
                var minimumDelay = settings.Current.MinimumInterDownloadDelaySeconds;
                var maximumDelay = settings.Current.MaximumInterDownloadDelaySeconds;
                if (wasPaused || pacingWasEnabled != pacingEnabled ||
                    previousMinimumDelay != minimumDelay || previousMaximumDelay != maximumDelay)
                {
                    SetNextAdmissionAt(pacingEnabled && hasAdmittedItem
                        ? timing.UtcNow + timing.NextAdmissionDelay(minimumDelay, maximumDelay)
                        : null);
                    wasPaused = false;
                    pacingWasEnabled = pacingEnabled;
                    previousMinimumDelay = minimumDelay;
                    previousMaximumDelay = maximumDelay;
                }
                var wait = ScheduleAvailableDownloads();
                if (wait is { } delay && delay > TimeSpan.Zero)
                    await signal.WaitAsync(delay, shutdown.Token).ConfigureAwait(false);
                else
                    await signal.WaitAsync(shutdown.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (shutdown.IsCancellationRequested) { }
        finally
        {
            Task[] remaining;
            lock (activeSync)
            {
                foreach (var active in activeDownloads.Values)
                    active.Cancellation.Cancel();
                remaining = activeDownloads.Values.Select(active => active.Task).ToArray();
            }
            await Task.WhenAll(remaining).ConfigureAwait(false);
        }
    }

    private TimeSpan? ScheduleAvailableDownloads()
    {
        while (!shutdown.IsCancellationRequested && !queue.IsPaused)
        {
            var limit = Math.Clamp(settings.Current.MaxSimultaneousDownloads, 1, 3);
            lock (activeSync)
            {
                // Lowering the setting never kills existing jobs; it only closes
                // the admission gate until the active count drops below the limit.
                if (activeDownloads.Count >= limit)
                    return null;
            }

            DateTimeOffset? due;
            lock (pacingSync) due = nextAdmissionAt;
            if (settings.Current.EnableInterDownloadDelay && due is { } admissionDue)
            {
                var remaining = admissionDue - timing.UtcNow;
                if (remaining > TimeSpan.Zero)
                    return remaining;
            }

            var item = queue.TryTakeNext();
            if (item is null)
                return null;

            var cancellation = CancellationTokenSource.CreateLinkedTokenSource(shutdown.Token);
            var active = new ActiveDownload(cancellation);
            lock (activeSync)
            {
                activeDownloads.Add(item.Id, active);
                if (cancellationsBeforeRegistration.Remove(item.Id))
                    cancellation.Cancel();
                active.Task = Task.Run(() => ProcessItemAsync(item, active));
            }
            hasAdmittedItem = true;
            if (settings.Current.EnableInterDownloadDelay)
            {
                var delay = timing.NextAdmissionDelay(settings.Current.MinimumInterDownloadDelaySeconds,
                    settings.Current.MaximumInterDownloadDelaySeconds);
                SetNextAdmissionAt(timing.UtcNow + delay);
                logger.Info($"Admission paced QueueItemId={item.Id} DelayMs={delay.TotalMilliseconds:F0} Concurrency={limit}");
            }
            else SetNextAdmissionAt(null);
        }
        return null;
    }

    private async Task ProcessItemAsync(DownloadQueueItem item, ActiveDownload active)
    {
        try
        {
            var maxAttempts = settings.Current.RetryFailedDownloads
                ? 1 + settings.Current.AdditionalRetryAttempts : 1;
            for (var attempt = 1; attempt <= maxAttempts; attempt++)
            {
                active.Cancellation.Token.ThrowIfCancellationRequested();
                queue.Update(item.Id, current =>
                {
                    current.AttemptCount = attempt;
                    current.MaximumAttempts = maxAttempts;
                    current.RetryDelaySeconds = 0;
                    if (!settings.Current.ResumeInterruptedDownloads) current.TemporaryFiles.Clear();
                    current.FinalFile = null;
                    current.ProgressPercent = 0;
                }, true);
                var started = timing.UtcNow;
                logger.Info($"Download started QueueItemId={item.Id} MediaId={item.VideoId} Attempt={attempt} MaxAttempts={maxAttempts} QualityPreset={item.QualityPresetId} Container={item.PreferredVideoContainer} VideoFormatId={item.SelectedFormats?.VideoFormatId ?? "unresolved"} AudioFormatId={item.SelectedFormats?.AudioFormatId ?? "none"}");
                try
                {
                    await executor.ExecuteAsync(item, (update, persist) => queue.Update(item.Id, update, persist), active.Cancellation.Token).ConfigureAwait(false);
                    queue.Update(item.Id, current =>
                    {
                        current.FailureMessageKey = null;
                        current.FailureTechnicalSummary = null;
                        current.FailureCategory = null;
                        current.FailureHttpStatus = null;
                    }, true);
                    logger.Info($"Download completed QueueItemId={item.Id} Attempt={attempt} DurationMs={(timing.UtcNow - started).TotalMilliseconds:F0} Status={item.Status}");
                    return;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    var stage = item.Status;
                    var failure = DownloadFailureClassifier.Classify(ex, stage);
                    var retry = failure.Retryable && attempt < maxAttempts;
                    logger.Error($"Download failed QueueItemId={item.Id} MediaId={item.VideoId} Stage={stage} Attempt={attempt} MaxAttempts={maxAttempts} QualityPreset={item.QualityPresetId} Container={item.PreferredVideoContainer} VideoFormatId={item.SelectedFormats?.VideoFormatId ?? "unresolved"} AudioFormatId={item.SelectedFormats?.AudioFormatId ?? "none"} FailureCategory={failure.Category} HttpStatus={failure.HttpStatus?.ToString() ?? "none"} ExitCode={failure.ExitCode?.ToString() ?? "none"} Retryable={failure.Retryable} DurationMs={(timing.UtcNow - started).TotalMilliseconds:F0}", ex);
                    if (!retry)
                    {
                        queue.Update(item.Id, current =>
                        {
                            current.Status = DownloadStatus.Failed;
                            current.StatusMessage = "Failed";
                            current.StatusMessageKey = "Queue.Detail.Failed";
                            current.ErrorMessage = failure.Category.ToString();
                            current.FailureMessageKey = failure.UserMessageKey;
                            current.FailureTechnicalSummary = failure.TechnicalSummary;
                            current.FailureCategory = failure.Category.ToString();
                            current.FailureHttpStatus = failure.HttpStatus;
                            current.SpeedBytesPerSecond = null;
                            current.Eta = null;
                            current.CompletedAt = DateTimeOffset.Now;
                        }, true);
                        logger.Warning($"Download exhausted QueueItemId={item.Id} Attempt={attempt} MaxAttempts={maxAttempts} FailureCategory={failure.Category}");
                        return;
                    }
                    var delay = timing.RetryDelay(attempt, failure.Category);
                    queue.Update(item.Id, current =>
                    {
                        current.Status = DownloadStatus.Waiting;
                        current.StatusMessage = "Waiting to retry";
                        current.StatusMessageKey = "Queue.Detail.RetryWaiting";
                        current.RetryDelaySeconds = (int)Math.Ceiling(delay.TotalSeconds);
                        current.FailureMessageKey = failure.UserMessageKey;
                        current.FailureTechnicalSummary = failure.TechnicalSummary;
                        current.FailureCategory = failure.Category.ToString();
                        current.FailureHttpStatus = failure.HttpStatus;
                        current.SpeedBytesPerSecond = null;
                        current.Eta = null;
                    }, true);
                    logger.Warning($"Retry scheduled QueueItemId={item.Id} Attempt={attempt} MaxAttempts={maxAttempts} FailureCategory={failure.Category} DelayMs={delay.TotalMilliseconds:F0}");
                    await timing.DelayAsync(delay, active.Cancellation.Token).ConfigureAwait(false);
                    logger.Info($"Retry starting QueueItemId={item.Id} Attempt={attempt + 1} MaxAttempts={maxAttempts}");
                }
            }
        }
        catch (OperationCanceledException)
        {
            queue.Update(item.Id, queueItem =>
            {
                queueItem.Status = shutdown.IsCancellationRequested ? DownloadStatus.Interrupted : DownloadStatus.Cancelled;
                queueItem.StatusMessage = queueItem.Status.ToString();
                queueItem.StatusMessageKey = shutdown.IsCancellationRequested ? "Queue.Detail.Interrupted" : "Queue.Detail.Cancelled";
                queueItem.ErrorMessage = null;
                queueItem.FailureMessageKey = null;
                queueItem.FailureTechnicalSummary = null;
                queueItem.FailureCategory = null;
                queueItem.FailureHttpStatus = null;
                queueItem.RetryDelaySeconds = 0;
                queueItem.SpeedBytesPerSecond = null;
                queueItem.Eta = null;
                queueItem.CompletedAt = shutdown.IsCancellationRequested ? null : DateTimeOffset.Now;
            }, true);
            logger.Info($"Queue processing cancelled: {item.Id}.");
        }
        catch (Exception ex)
        {
            var failure = DownloadFailureClassifier.Classify(ex, item.Status);
            queue.Update(item.Id, current =>
            {
                current.Status = DownloadStatus.Failed;
                current.StatusMessageKey = "Queue.Detail.Failed";
                current.ErrorMessage = failure.Category.ToString();
                current.FailureMessageKey = failure.UserMessageKey;
                current.FailureTechnicalSummary = failure.TechnicalSummary;
                current.FailureCategory = failure.Category.ToString();
                current.FailureHttpStatus = failure.HttpStatus;
                current.CompletedAt = DateTimeOffset.Now;
            }, true);
            logger.Error($"Unexpected queue processing failure QueueItemId={item.Id} Stage={item.Status} FailureCategory={failure.Category}", ex);
        }
        finally
        {
            try { await queue.SaveAsync(CancellationToken.None).ConfigureAwait(false); }
            catch (Exception ex) { logger.Error($"Could not save the queue after processing item {item.Id}.", ex); }
            finally
            {
                // StopAsync must own the task until its final durable save has finished.
                lock (activeSync) activeDownloads.Remove(item.Id);
                active.Cancellation.Dispose();
                Wake();
            }
        }
    }

    private void QueueChanged(object? sender, QueueChangedEventArgs e)
    {
        if (e.Kind is QueueChangeKind.Collection or QueueChangeKind.Pause or QueueChangeKind.Order ||
            e.Kind == QueueChangeKind.Item && e.ItemId is { } id &&
            queue.Find(id)?.Status == DownloadStatus.Queued)
            Wake();
    }

    private void SettingsChanged(object? sender, EventArgs e) => Wake();

    private void Wake()
    {
        try { signal.Release(); } catch (SemaphoreFullException) { }
    }

    private void SetNextAdmissionAt(DateTimeOffset? due)
    {
        lock (pacingSync) nextAdmissionAt = due;
    }

    public void Dispose()
    {
        queue.Changed -= QueueChanged;
        settings.Changed -= SettingsChanged;
        shutdown.Dispose();
        signal.Dispose();
        lock (activeSync)
            foreach (var active in activeDownloads.Values)
                active.Cancellation.Dispose();
    }

    private sealed class ActiveDownload(CancellationTokenSource cancellation)
    {
        public CancellationTokenSource Cancellation { get; } = cancellation;
        public Task Task { get; set; } = Task.CompletedTask;
    }
}
