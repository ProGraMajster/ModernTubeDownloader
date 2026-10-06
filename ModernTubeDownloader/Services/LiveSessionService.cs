using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Models;

namespace ModernTubeDownloader.Services;

public sealed class LiveSessionService(LiveSessionPersistenceService persistence,
    SettingsService settings, IAppLogger logger)
{
    private readonly object sync = new();
    private readonly List<LiveSession> sessions = [];
    private readonly SemaphoreSlim saveGate = new(1, 1);
    private ILiveSessionCommands? commands;
    public event EventHandler? Changed;

    // The service is the UI/API boundary; only the scheduler owns worker tasks/processes.
    internal void AttachScheduler(ILiveSessionCommands scheduler)
    {
        if (commands is not null) throw new InvalidOperationException("A LIVE scheduler is already attached.");
        commands = scheduler;
    }
    public bool StopAndSave(Guid id) => commands?.StopAndSave(id) == true;
    public bool Cancel(Guid id) => commands?.Cancel(id) ?? CancelPending(id);

    public IReadOnlyList<LiveSession> Snapshot() { lock (sync) return sessions.Select(item => item.Copy()).ToArray(); }
    public LiveSession? Find(Guid id) { lock (sync) return sessions.Find(item => item.Id == id)?.Copy(); }

    public async Task LoadAsync(CancellationToken token = default)
    {
        var loaded = await persistence.LoadAsync(token).ConfigureAwait(false);
        lock (sync)
        {
            sessions.Clear();
            foreach (var session in loaded)
            {
                if (session.IsActive || session.State == LiveSessionState.Interrupted)
                {
                    session.Interrupted = true;
                    session.StopRequested = false;
                    session.State = settings.Current.AutoResumeLiveAfterRestart
                        ? LiveSessionState.Pending : LiveSessionState.Interrupted;
                    if (session.State == LiveSessionState.Pending) session.ResumeCount++;
                    session.CurrentSpeed = null;
                    session.AttemptCount = 0;
                    session.RetryAt = null;
                }
                if (session.State == LiveSessionState.WaitingForLive) session.NextCheckAt = DateTimeOffset.UtcNow;
                sessions.Add(session);
            }
        }
        await SaveAsync(token).ConfigureAwait(false);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public LiveSession Add(string sourceUrl, VideoMetadata? metadata, string qualityPresetId,
        LiveStartPolicy policy, bool waiting = false)
    {
        if (!YtDlpMetadataService.TryValidateSourceUrl(sourceUrl, out var url, out _))
            throw new ArgumentException("Invalid LIVE source.", nameof(sourceUrl));
        if (metadata is not null && MediaAvailabilityPolicy.Classify(metadata) is not
            (MediaAvailabilityKind.ActiveLive or MediaAvailabilityKind.Upcoming))
            throw new ArgumentException("VOD belongs to the normal download queue.", nameof(metadata));
        var session = new LiveSession
        {
            SourceUrl = url, MediaId = metadata?.Id ?? string.Empty,
            Title = metadata?.Title ?? url, Channel = metadata?.DisplayChannel ?? string.Empty,
            ThumbnailUrl = metadata?.ThumbnailUrl, SourceLiveStatus = metadata?.LiveStatus,
            QualityPresetId = QualityPreset.Find(qualityPresetId).Id, StartPolicy = policy,
            State = waiting ? LiveSessionState.WaitingForLive : LiveSessionState.Pending,
            WaitForScheduledStart = waiting, NextCheckAt = waiting ? DateTimeOffset.UtcNow : null,
            MonitoringStartedAt = waiting ? DateTimeOffset.UtcNow : null,
            ScheduledStartAt = metadata?.ReleaseTimestamp is > 0
                ? DateTimeOffset.FromUnixTimeSeconds(metadata.ReleaseTimestamp.Value) : null,
            PreservePartial = settings.Current.PreservePartialLiveOnFailure,
            ResumeEnabled = settings.Current.ResumeLiveOnFailure,
            PreservePartialOnCancel = settings.Current.PreservePartialLiveOnCancel
        };
        lock (sync)
        {
            if (sessions.Any(item => item.SourceUrl == url && item.QualityPresetId == session.QualityPresetId &&
                (item.IsActive || item.State is LiveSessionState.Pending or LiveSessionState.WaitingForLive)))
                throw new InvalidOperationException("A LIVE session for this source already exists.");
            sessions.Add(session);
        }
        Notify(true);
        logger.Info($"LIVE added LiveSessionId={session.Id} LiveState={session.State} LiveStartPolicy={policy}");
        return session.Copy();
    }

    internal LiveSession? TakePending()
    {
        LiveSession? session;
        lock (sync)
        {
            session = sessions.Find(item => item.State == LiveSessionState.Pending);
            if (session is not null) session.State = LiveSessionState.Starting;
        }
        if (session is not null) Notify(true);
        return session;
    }

    internal void Update(Guid id, Action<LiveSession> update, bool persist = true)
    {
        LiveSession? logged = null;
        lock (sync)
        {
            var session = sessions.Find(item => item.Id == id);
            if (session is null) return;
            update(session);
            if (persist) logged = session.Copy();
        }
        if (logged is not null)
            logger.Info($"LIVE state LiveSessionId={logged.Id} LiveState={logged.State} " +
                $"LiveStartPolicy={logged.StartPolicy} ScheduledStart={logged.ScheduledStartAt:O} " +
                $"RecordingStartedAt={logged.RecordingStartedAt:O} RecordedDuration={logged.RecordedDuration} " +
                $"RecordedBytes={logged.BytesWritten} ReconnectAttempt={logged.ResumeCount} " +
                $"ResumePossible={logged.CanResume} PartIndex={logged.PartIndex} NextLiveCheckAt={logged.NextCheckAt:O}");
        Notify(persist);
    }

    public bool Resume(Guid id)
    {
        lock (sync)
        {
            var session = sessions.Find(item => item.Id == id);
            if (session?.CanResume != true) return false;
            session.State = LiveSessionState.Pending;
            session.StopRequested = false;
            session.CancelRequested = false;
            session.Interrupted = false;
            session.ResumeCount++;
            session.AttemptCount = 0;
            session.FailureCategory = null;
            session.FailureMessageKey = null;
            session.RecordingEndedAt = null;
        }
        Notify(true);
        return true;
    }

    public bool EditWaiting(Guid id, string qualityPresetId, LiveStartPolicy policy)
    {
        lock (sync)
        {
            var session = sessions.Find(item => item.Id == id);
            if (session?.State != LiveSessionState.WaitingForLive) return false;
            session.QualityPresetId = QualityPreset.Find(qualityPresetId).Id;
            session.StartPolicy = policy;
        }
        Notify(true);
        return true;
    }

    internal bool CancelPending(Guid id)
    {
        lock (sync)
        {
            var session = sessions.Find(item => item.Id == id);
            if (session?.State is not (LiveSessionState.Pending or LiveSessionState.WaitingForLive)) return false;
            session.State = LiveSessionState.Cancelled;
            session.NextCheckAt = null;
        }
        Notify(true);
        return true;
    }

    public async Task SaveAsync(CancellationToken token = default)
    {
        await saveGate.WaitAsync(token).ConfigureAwait(false);
        try { await persistence.SaveAsync(Snapshot(), token).ConfigureAwait(false); }
        finally { saveGate.Release(); }
    }

    private void Notify(bool persist)
    {
        Changed?.Invoke(this, EventArgs.Empty);
        if (persist) _ = SaveSafelyAsync();
    }
    private async Task SaveSafelyAsync()
    {
        try { await SaveAsync().ConfigureAwait(false); }
        catch (Exception ex) { logger.Error("Could not persist LIVE sessions.", ex); }
    }
}
