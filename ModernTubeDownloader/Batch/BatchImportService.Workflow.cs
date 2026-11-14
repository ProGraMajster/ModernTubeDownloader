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
                if (selected.Length == 1 && automaticSession == selected[0].Id) return automatic;
                throw new InvalidOperationException("Batch.Error.Busy");
            }
            if (!analysis.IsCompleted) throw new InvalidOperationException("Batch.Error.Busy");
            automaticCancellation?.Dispose(); automaticCancellation = new(); automaticSession = selected[0].Id;
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
            var pending = StartAsync(session);
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

    private Task PersistPhaseAsync(BatchImportSession session, BatchWorkflowPhase phase)
    { lock (session.Sync) session.WorkflowPhase = phase; return store.SaveSessionAsync(session); }

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

    public Task<bool> HasUnfinishedSessionsAsync(CancellationToken token = default) => store.HasUnfinishedSessionsAsync(token);
}
