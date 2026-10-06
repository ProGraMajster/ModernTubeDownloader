using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Models;

namespace ModernTubeDownloader.Services;

public interface ILiveRecordingExecutor
{
    Task ExecuteAsync(LiveSession session, Action<Action<LiveSession>, bool> update, CancellationToken token);
}

public interface ILiveSessionCommands
{
    bool StopAndSave(Guid id);
    bool Cancel(Guid id);
}

public sealed class LiveRecordingScheduler : IDisposable, ILiveSessionCommands
{
    private readonly LiveSessionService live;
    private readonly ILiveRecordingExecutor executor;
    private readonly ILiveAvailabilityProbe probe;
    private readonly SettingsService settings;
    private readonly IAppLogger logger;
    private readonly IDownloadTiming timing;
    private readonly object sync = new();
    private readonly Dictionary<Guid, Worker> recordings = [];
    private readonly Dictionary<Guid, Worker> monitors = [];
    private readonly HashSet<Guid> earlyCancel = [];
    private readonly CancellationTokenSource shutdown = new();
    private readonly SemaphoreSlim signal = new(0, 1);
    private Task? loop;

    public LiveRecordingScheduler(LiveSessionService live, ILiveRecordingExecutor executor,
        ILiveAvailabilityProbe probe, SettingsService settings, IAppLogger logger, IDownloadTiming? timing = null)
    {
        this.live = live; this.executor = executor; this.probe = probe;
        this.settings = settings; this.logger = logger; this.timing = timing ?? new DownloadTiming();
        live.AttachScheduler(this);
        live.Changed += Changed; settings.Changed += Changed;
    }
    public IReadOnlyCollection<Guid> ActiveIds { get { lock (sync) return recordings.Keys.ToArray(); } }
    public void Start() { loop ??= Task.Run(LoopAsync); Wake(); }

    public bool StopAndSave(Guid id) => CancelWorker(id, save: true);
    public bool Cancel(Guid id)
    {
        if (live.CancelPending(id))
        {
            lock (sync) if (monitors.TryGetValue(id, out var monitor)) monitor.Cancellation.Cancel();
            return true;
        }
        return CancelWorker(id, save: false);
    }
    private bool CancelWorker(Guid id, bool save)
    {
        lock (sync)
        {
            if (live.Find(id)?.IsActive != true) return false;
            live.Update(id, current => { current.StopRequested = save; current.CancelRequested = !save; });
            if (recordings.TryGetValue(id, out var worker)) worker.Cancellation.Cancel();
            else earlyCancel.Add(id);
            return true;
        }
    }

    public async Task StopAsync()
    {
        shutdown.Cancel();
        lock (sync)
        {
            foreach (var worker in recordings.Values.Concat(monitors.Values)) worker.Cancellation.Cancel();
        }
        Wake();
        if (loop is not null) await loop.ConfigureAwait(false);
        await live.SaveAsync().ConfigureAwait(false);
    }

    private async Task LoopAsync()
    {
        try
        {
            while (!shutdown.IsCancellationRequested)
            {
                lock (sync)
                {
                    while (recordings.Count < Math.Clamp(settings.Current.MaxSimultaneousLiveRecordings, 1, 3))
                    {
                        var session = live.TakePending();
                        if (session is null) break;
                        var worker = new Worker(CancellationTokenSource.CreateLinkedTokenSource(shutdown.Token));
                        recordings.Add(session.Id, worker);
                        if (earlyCancel.Remove(session.Id)) worker.Cancellation.Cancel();
                        worker.Task = Task.Run(() => RecordAsync(session, worker));
                    }
                    // Monitoring is independent of capture slots, bounded to one
                    // short metadata extraction at a time rather than an hours-long process.
                    if (monitors.Count == 0)
                    {
                        var due = live.Snapshot().Where(item => item.State == LiveSessionState.WaitingForLive &&
                                (item.NextCheckAt is null || item.NextCheckAt <= timing.UtcNow))
                            .OrderBy(item => item.NextCheckAt).FirstOrDefault();
                        if (due is not null)
                        {
                            var worker = new Worker(CancellationTokenSource.CreateLinkedTokenSource(shutdown.Token));
                            monitors.Add(due.Id, worker);
                            worker.Task = Task.Run(() => PollAsync(due, worker));
                        }
                    }
                }
                var next = live.Snapshot().Where(item => item.State == LiveSessionState.WaitingForLive &&
                    item.NextCheckAt > timing.UtcNow).Select(item => item.NextCheckAt).Min();
                var delay = next is { } at ? at - timing.UtcNow : TimeSpan.FromSeconds(1);
                await signal.WaitAsync(delay > TimeSpan.Zero ? delay : TimeSpan.FromMilliseconds(50), shutdown.Token)
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (shutdown.IsCancellationRequested) { }
        finally
        {
            Task[] pending;
            lock (sync) pending = recordings.Values.Concat(monitors.Values).Select(worker => worker.Task).ToArray();
            await Task.WhenAll(pending).ConfigureAwait(false);
        }
    }

    private async Task PollAsync(LiveSession session, Worker worker)
    {
        try
        {
            logger.Info($"LIVE polling LiveSessionId={session.Id} LiveState=WaitingForLive NextLiveCheckAt={session.NextCheckAt:O}");
            var result = await probe.ProbeAsync(session.SourceUrl, worker.Cancellation.Token).ConfigureAwait(false);
            live.Update(session.Id, current =>
            {
                if (current.State != LiveSessionState.WaitingForLive) return;
                current.ScheduledStartAt = result.ScheduledStartAt ?? current.ScheduledStartAt;
                if (result.Kind == MediaAvailabilityKind.ActiveLive)
                { current.State = LiveSessionState.Pending; current.NextCheckAt = null; }
                else if (result.Kind == MediaAvailabilityKind.Upcoming)
                { current.NextCheckAt = timing.UtcNow + NextCheckDelay(); }
                else
                { current.State = LiveSessionState.Failed; current.StatusMessageKey = "Live.WaitEnded"; current.NextCheckAt = null; }
            });
        }
        catch (OperationCanceledException) when (worker.Cancellation.IsCancellationRequested) { }
        catch (Exception ex)
        {
            var failure = DownloadFailureClassifier.Classify(ex);
            logger.Error($"LIVE polling failed LiveSessionId={session.Id} FailureCategory={failure.Category}", ex);
            live.Update(session.Id, current =>
            {
                if (current.State != LiveSessionState.WaitingForLive) return;
                current.FailureCategory = failure.Category.ToString(); current.FailureMessageKey = failure.UserMessageKey;
                if (failure.Retryable) current.NextCheckAt = timing.UtcNow + NextCheckDelay();
                else { current.State = LiveSessionState.Failed; current.NextCheckAt = null; }
            });
        }
        finally { lock (sync) monitors.Remove(session.Id); worker.Cancellation.Dispose(); Wake(); }
    }
    private TimeSpan NextCheckDelay() => timing.NextAdmissionDelay(settings.Current.MinimumLiveCheckSeconds,
        settings.Current.MaximumLiveCheckSeconds);

    private async Task RecordAsync(LiveSession session, Worker worker)
    {
        try
        {
            var max = session.ResumeEnabled ? 1 + settings.Current.AdditionalRetryAttempts : 1;
            for (var attempt = 1; attempt <= max; attempt++)
            {
                worker.Cancellation.Token.ThrowIfCancellationRequested();
                live.Update(session.Id, current =>
                {
                    current.AttemptCount = attempt; current.MaximumAttempts = max; current.RetryAt = null;
                    if (attempt > 1) current.ResumeCount++;
                });
                try
                {
                    await executor.ExecuteAsync(session, (change, persist) => live.Update(session.Id, change, persist),
                        worker.Cancellation.Token).ConfigureAwait(false);
                    return;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    var failure = DownloadFailureClassifier.Classify(ex, isLiveCapture: true);
                    var retry = failure.Retryable && attempt < max;
                    logger.Error($"LIVE capture failed LiveSessionId={session.Id} LiveState={session.State} " +
                        $"ReconnectAttempt={attempt} FailureCategory={failure.Category} ResumePossible={retry}", ex);
                    live.Update(session.Id, current =>
                    {
                        current.FailureCategory = failure.Category.ToString(); current.FailureMessageKey = failure.UserMessageKey;
                        current.CurrentSpeed = null;
                        current.State = retry ? LiveSessionState.Reconnecting
                            : HasData(current) ? LiveSessionState.Partial : LiveSessionState.Failed;
                        current.RetryAt = retry ? timing.UtcNow + timing.RetryDelay(attempt, failure.Category) : null;
                    });
                    if (!retry) return;
                    await timing.DelayAsync(timing.RetryDelay(attempt, failure.Category), worker.Cancellation.Token)
                        .ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
            live.Update(session.Id, current =>
            {
                current.Interrupted = shutdown.IsCancellationRequested;
                current.State = shutdown.IsCancellationRequested ? LiveSessionState.Interrupted
                    : current.StopRequested ? current.Parts.Count > 0 ? LiveSessionState.Completed
                        : HasData(current) ? LiveSessionState.Partial : LiveSessionState.Failed
                    : current.PreservePartialOnCancel && HasData(current) ? LiveSessionState.Partial : LiveSessionState.Cancelled;
                current.StatusMessageKey = current.StopRequested ? "Live.StoppedAndSaved" : null;
                current.CurrentSpeed = null; current.RetryAt = null;
            });
            if (!shutdown.IsCancellationRequested && session.CancelRequested && !session.PreservePartialOnCancel)
                LiveRecordingExecutor.RemoveRawWorkspace(session, logger);
        }
        catch (Exception ex)
        {
            logger.Error($"Unexpected LIVE worker failure LiveSessionId={session.Id}", ex);
            live.Update(session.Id, current => { current.State = HasData(current) ? LiveSessionState.Partial : LiveSessionState.Failed; });
        }
        finally
        {
            try { await live.SaveAsync().ConfigureAwait(false); }
            catch (Exception ex) { logger.Error($"Could not save completed LIVE worker LiveSessionId={session.Id}", ex); }
            finally
            {
                lock (sync) recordings.Remove(session.Id);
                worker.Cancellation.Dispose();
                Wake();
            }
        }
    }
    private static bool HasData(LiveSession session) => session.Parts.Count > 0 ||
        LiveRecordingExecutor.HasRawData(session.WorkspacePath);
    private void Changed(object? sender, EventArgs e) => Wake();
    private void Wake() { try { signal.Release(); } catch (SemaphoreFullException) { } }
    public void Dispose()
    {
        live.Changed -= Changed; settings.Changed -= Changed;
        shutdown.Dispose(); signal.Dispose();
    }
    private sealed class Worker(CancellationTokenSource cancellation)
    { public CancellationTokenSource Cancellation { get; } = cancellation; public Task Task { get; set; } = Task.CompletedTask; }
}
