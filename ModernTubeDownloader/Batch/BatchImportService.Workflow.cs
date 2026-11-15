using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Models;

namespace ModernTubeDownloader.Batch;

public sealed partial class BatchImportService
{
    public async Task SetDestinationAsync(BatchImportSession session, string root, string listName,
        bool createSubfolder, bool confirmSharedDestination = false, CancellationToken token = default)
    {
        await destinationGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
        root = DownloadOutputPolicy.NormalizeDirectory(root);
        var destination = createSubfolder ? DownloadOutputPolicy.ForList(root, listName) : root;
        var collisions = await store.FindImportsAsync(null, destination, token).ConfigureAwait(false);
        if (!confirmSharedDestination && collisions.Any(value => value.Id != session.Id))
            throw new InvalidDataException("Batch.Error.DestinationConflict");
        (string? Root, string? Name, bool Subfolder, string? Directory, bool Confirmed) previous;
        lock (sync)
        lock (session.Sync)
        {
            if (session.Entries.Any(entry => entry.QueueItemId is not null) || session.WorkflowPhase is BatchWorkflowPhase.Analyzing or BatchWorkflowPhase.Queueing ||
                !automatic.IsCompleted || !analysis.IsCompleted || handoffs.Count > 0)
                throw new InvalidDataException("Batch.Error.DestinationFrozen");
            previous = (session.OutputRoot, session.ListName, session.CreateListSubfolder, session.OutputDirectory, session.DestinationConfirmed);
            session.OutputRoot = root; session.ListName = listName; session.CreateListSubfolder = createSubfolder;
            session.OutputDirectory = destination; session.DestinationConfirmed = true;
        }
        try { await store.SaveSessionAsync(session, token).ConfigureAwait(false); }
        catch
        {
            lock (session.Sync)
                (session.OutputRoot, session.ListName, session.CreateListSubfolder, session.OutputDirectory, session.DestinationConfirmed) = previous;
            throw;
        }
        }
        finally { destinationGate.Release(); }
    }

    /// <summary>One shared preparation operation. Never resumes the global queue or owns a download worker.</summary>
    public Task RunAutomaticAsync(BatchImportSession session)
        => RunAutomaticAsync(new[] { session });

    public Task RunAutomaticAsync(IReadOnlyList<BatchImportSession> sessions)
    {
        if (sessions.Count == 0) return Task.CompletedTask;
        var selected = sessions.DistinctBy(value => value.Id).ToArray();
        lock (sync)
        {
            if (!automatic.IsCompleted)
            {
                if (automaticSelection.SequenceEqual(selected.Select(value => value.Id)) ||
                    selected.Length == 1 && automaticSession == selected[0].Id) return automatic;
                throw new InvalidOperationException("Batch.Error.Busy");
            }
            if (!analysis.IsCompleted) throw new InvalidOperationException("Batch.Error.Busy");
            automaticCancellation?.Dispose(); automaticCancellation = new(); automaticSession = selected[0].Id;
            automaticSelection = selected.Select(value => value.Id).ToArray();
            var token = automaticCancellation.Token;
            automatic = Task.Run(async () =>
            {
                foreach (var value in selected)
                {
                    if (token.IsCancellationRequested) break;
                    lock (sync) automaticSession = value.Id;
                    await RunAutomaticCoreAsync(value, token).ConfigureAwait(false);
                }
            });
            return automatic;
        }
    }

    private async Task RunAutomaticCoreAsync(BatchImportSession session, CancellationToken token)
    {
        try
        {
            if (!session.DestinationConfirmed) throw new InvalidDataException("Batch.Error.DestinationConflict");
            if (session.OutputDirectory is { } destination) DownloadOutputPolicy.NormalizeDirectory(destination);
            session.AutomaticRequested = true;
            await PersistPhaseAsync(session, BatchWorkflowPhase.Analyzing).ConfigureAwait(false);
            // Recover a link lost between a durable canonical admission and the Batch journal.
            var recovered = new List<BatchImportEntry>();
            lock (session.Sync)
                foreach (var entry in session.Entries.Where(entry => !entry.Removed && entry.QueueItemId is null))
                    if (queue.Find(entry.Id) is { } existing)
                    {
                        entry.QueueItemId = existing.Id; entry.Status = BatchEntryStatus.AddedToQueue;
                        entry.Revision++; recovered.Add(entry.Copy());
                    }
            if (recovered.Count > 0) await store.SaveEntriesAsync(session, recovered, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            await RetryFailedAsync(session, token: token).ConfigureAwait(false);
            var pending = StartAnalysisAsync(session, null, true);
            using (token.Register(() => { lock (sync) analysisCancellation?.Cancel(); }))
                await pending.ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            await PersistPhaseAsync(session, BatchWorkflowPhase.Queueing).ConfigureAwait(false);
            await AddReadyAsync(session, token: token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            await RefreshQueueAsync(session, token).ConfigureAwait(false);
            session.WorkflowPhase = BatchWorkflowPhase.Prepared;
            await PersistPhaseAsync(session, CurrentWorkflowPhase(session)).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Explicit restart/resume is required. Cancellation cannot be mistaken for completion.
            session.AutomaticRequested = false;
            await PersistPhaseAsync(session, BatchWorkflowPhase.Cancelled).ConfigureAwait(false);
        }
        catch
        {
            await PersistPhaseAsync(session, BatchWorkflowPhase.Attention).ConfigureAwait(false);
            throw;
        }
    }

    private async Task PersistPhaseAsync(BatchImportSession session, BatchWorkflowPhase phase)
    {
        lock (session.Sync) { session.WorkflowPhase = phase; session.PendingPhaseCommit = true; }
        await store.SaveSessionAsync(session).ConfigureAwait(false);
        lock (session.Sync) if (session.WorkflowPhase == phase) session.PendingPhaseCommit = false;
    }

    public BatchWorkflowPhase CurrentWorkflowPhase(BatchImportSession session)
    {
        lock (sync)
        {
            if (automaticSession == session.Id && !automatic.IsCompleted && session.WorkflowPhase is BatchWorkflowPhase.Analyzing or BatchWorkflowPhase.Queueing)
                return session.Paused && session.WorkflowPhase == BatchWorkflowPhase.Analyzing ? BatchWorkflowPhase.Paused : session.WorkflowPhase;
            if (ActiveSessionId == session.Id && !analysis.IsCompleted) return session.Paused ? BatchWorkflowPhase.Paused : BatchWorkflowPhase.Analyzing;
        }
        var entries = session.Snapshot();
        var ids = entries.Where(value => value.QueueItemId is not null).Select(value => value.QueueItemId!.Value).ToHashSet();
        var jobs = queue.ProgressSnapshot().Where(value => ids.Contains(value.Id)).ToArray();
        if (jobs.Any(value => value.Status is DownloadStatus.Merging or DownloadStatus.Finalizing)) return BatchWorkflowPhase.Finalizing;
        if (jobs.Any(value => value.Status is DownloadStatus.DownloadingVideo or DownloadStatus.DownloadingAudio or DownloadStatus.Waiting)) return BatchWorkflowPhase.Downloading;
        if (jobs.Any(value => value.Status == DownloadStatus.Queued)) return queue.IsPaused ? BatchWorkflowPhase.WaitingForQueue : BatchWorkflowPhase.Downloading;
        if (session.WorkflowPhase is BatchWorkflowPhase.Cancelled or BatchWorkflowPhase.Paused) return session.WorkflowPhase;
        if (entries.Any(value => value.Status is BatchEntryStatus.Failed or BatchEntryStatus.AuthRequired or BatchEntryStatus.Unsupported)) return BatchWorkflowPhase.Attention;
        return entries.Count > 0 && entries.All(value => value.Status is BatchEntryStatus.Completed or BatchEntryStatus.Duplicate or BatchEntryStatus.Skipped)
            ? BatchWorkflowPhase.Completed : BatchWorkflowPhase.Prepared;
    }

    /// <summary>A linked subset cannot estimate transfer for work still awaiting admission.</summary>
    internal static TimeSpan? TransferRemaining(BatchImportSession session, ModernTubeDownloader.Services.QueueProgressSummary linkedQueueSummary)
    {
        // Inspect the entire canonical session, not the currently visible/filter-matched page.
        // The projection does not load metadata or copy the large row collection.
        lock (session.Sync)
            return session.Entries.Any(entry => !entry.Removed && entry.QueueItemId is null &&
                entry.Status is BatchEntryStatus.Pending or BatchEntryStatus.Validating or BatchEntryStatus.Analyzing or BatchEntryStatus.Ready)
                ? null : linkedQueueSummary.TransferRemaining;
    }

    public async Task<bool> HasUnfinishedSessionsAsync(CancellationToken token = default)
    {
        if (queue.LoadFailed || IsOperating) return true;
        // Reconcile persisted links even when no Batch window has ever been opened.
        // Metadata alone may describe a download that completed after that window closed.
        try
        {
            await foreach (var summary in store.UnfinishedSessionsAsync(token).ConfigureAwait(false))
            {
                var session = await OpenAsync(summary.Id, token).ConfigureAwait(false);
                await RefreshQueueAsync(session, token).ConfigureAwait(false);
                if (session.WorkflowPhase != BatchWorkflowPhase.Completed) return true;
            }
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or System.Text.Json.JsonException)
        { logger.Error("Batch startup admission remains blocked by an unreadable session.", error); return true; }
        return queue.LoadFailed || IsOperating;
    }

    /// <summary>Explicit retry uses the same canonical job, destination and pause state.</summary>
    public async Task<int> RetryFailedAsync(BatchImportSession session, IReadOnlySet<Guid>? selected = null,
        CancellationToken token = default)
    {
        await destinationGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var retryIds = session.Snapshot().Where(entry => entry.QueueItemId is not null &&
                (selected is null || selected.Contains(entry.Id))).Select(entry => entry.QueueItemId!.Value).Distinct().ToArray();
            var retried = 0;
            try
            {
            foreach (var id in retryIds)
            {
                token.ThrowIfCancellationRequested();
                if (queue.Retry(id)) retried++;
            }
            }
            finally
            {
            if (retried > 0)
            {
                // Canonical admission already happened; cancellation cannot discard its checkpoint.
                await queue.CheckpointBeforeStartAsync(CancellationToken.None).ConfigureAwait(false);
                await RefreshQueueAsync(session, CancellationToken.None).ConfigureAwait(false);
            }
            }
            return retried;
        }
        finally { destinationGate.Release(); }
    }
}
