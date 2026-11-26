using ModernTubeDownloader.Models;
using ModernTubeDownloader.Infrastructure;

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
        var id = Guid.NewGuid();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        int read = 0, failures = 0;
        void OnUnreadable(Exception error)
        {
            failures++;
            var cause = error.Data["BatchDiagnosticCode"] is int code ? code : error switch
            {
                System.Text.Json.JsonException => 6, UnauthorizedAccessException => 7, IOException => 8, _ => 0
            };
            logger.Event(new(DiagnosticModule.Batch, DiagnosticStage.Recover, DiagnosticOutcome.Failed, DiagnosticLevel.Warn,
                id, Exception: error, Counts: new(Code: cause)));
        }
        logger.Event(new(DiagnosticModule.Batch, DiagnosticStage.Recover, DiagnosticOutcome.Started, OperationId: id));
        try
        {
            await foreach (var summary in store.UnfinishedSessionsAsync(token,
                OnUnreadable).ConfigureAwait(false))
            {
                try { await OpenAsync(summary.Id, token).ConfigureAwait(false); read++; }
                catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or System.Text.Json.JsonException)
                { OnUnreadable(error); }
            }
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or System.Text.Json.JsonException)
        { OnUnreadable(error); }
        finally
        {
            logger.Event(new(DiagnosticModule.Batch, DiagnosticStage.Recover,
                token.IsCancellationRequested ? DiagnosticOutcome.Cancelled : failures > 0 ? DiagnosticOutcome.Failed : DiagnosticOutcome.Completed,
                failures > 0 ? DiagnosticLevel.Warn : DiagnosticLevel.Info, id,
                DurationMs: clock.ElapsedMilliseconds, Counts: new(Items: read, Errors: failures, Code: 1)));
        }
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
