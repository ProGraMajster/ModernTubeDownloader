using ModernTubeDownloader.Models;

namespace ModernTubeDownloader.Batch;

public sealed partial class BatchImportService
{
    private readonly object reconciliationSync = new();
    private readonly Dictionary<Guid, HashSet<Guid>> sessionsByJob = [];
    private readonly Dictionary<Guid, DownloadStatus> observedStatuses = [];
    private readonly HashSet<Guid> trackedSessions = [];
    private readonly HashSet<Guid> dirtySessions = [];
    private Task reconciliation = Task.CompletedTask;
    private bool observationStopped;

    /// <summary>Call after canonical queue load and before starting its workers. Raw video metadata is never loaded.</summary>
    public async Task InitializeAsync(CancellationToken token = default)
    {
        if (queue.LoadFailed) return;
        try
        {
            await foreach (var summary in store.UnfinishedSessionsAsync(token,
                error => logger.Error("Batch session recovery requires attention; original data was retained.", error)).ConfigureAwait(false))
            {
                try { await OpenAsync(summary.Id, token).ConfigureAwait(false); }
                catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or System.Text.Json.JsonException)
                { logger.Error("Batch session recovery failed for " + summary.Id, error); }
            }
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or System.Text.Json.JsonException)
        { logger.Error("Batch session recovery requires attention; original data was retained.", error); }
    }

    private void TrackSession(BatchImportSession session)
    {
        var entries = session.Snapshot();
        lock (reconciliationSync)
        {
            trackedSessions.Add(session.Id);
            foreach (var entry in entries)
            {
                Track(entry.Id);
                if (entry.QueueItemId is { } id && id != entry.Id) Track(id);
            }
        }
        void Track(Guid id)
        {
            if (!sessionsByJob.TryGetValue(id, out var owners)) sessionsByJob[id] = owners = [];
            owners.Add(session.Id);
        }
    }

    private bool ObservedCompleted(Guid id)
    {
        lock (reconciliationSync) return observedStatuses.TryGetValue(id, out var status) && status == DownloadStatus.Completed;
    }

    private void QueueChanged(object? sender, QueueChangedEventArgs change)
    {
        // Progress ticks with the same status are O(1), without a queue scan or disk write.
        // Capture terminal status in the event: ClearCompleted may remove the job before
        // the asynchronous reconciliation runs, but completion must remain idempotent.
        lock (reconciliationSync)
        {
            if (observationStopped || change.Kind == QueueChangeKind.Order) return;
            if (change.Kind == QueueChangeKind.Item && change.ItemId is { } id)
            {
                if (!sessionsByJob.TryGetValue(id, out var owners)) return;
                if (change.Status is { } status)
                {
                    if (observedStatuses.TryGetValue(id, out var previous) && previous == status) return;
                    observedStatuses[id] = status;
                }
                dirtySessions.UnionWith(owners);
            }
            else
                dirtySessions.UnionWith(trackedSessions);
            if (dirtySessions.Count > 0 && reconciliation.IsCompleted)
                reconciliation = Task.Run(ReconcileObservedSessionsAsync);
        }
    }

    private async Task ReconcileObservedSessionsAsync()
    {
        while (true)
        {
            Guid[] ids;
            lock (reconciliationSync)
            {
                if (dirtySessions.Count == 0)
                {
                    // Publish idle under the same lock used by QueueChanged, avoiding
                    // a lost event between the final empty check and Task completion.
                    reconciliation = Task.CompletedTask;
                    return;
                }
                ids = dirtySessions.ToArray(); dirtySessions.Clear();
            }
            foreach (var id in ids)
            {
                BatchImportSession? session;
                lock (sync) loaded.TryGetValue(id, out session);
                if (session is null) continue;
                try { await RefreshQueueCoreAsync(session, CancellationToken.None).ConfigureAwait(false); }
                catch (Exception error) { logger.Error("Batch queue reconciliation failed for session " + id, error); }
            }
        }
    }

    private void StopObservingQueue()
    {
        lock (reconciliationSync) observationStopped = true;
        queue.Changed -= QueueChanged;
    }
}
