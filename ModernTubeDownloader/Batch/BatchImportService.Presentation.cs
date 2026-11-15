namespace ModernTubeDownloader.Batch;

[Flags]
public enum BatchPresentationChange { State = 1, Rows = 2 }

public sealed record BatchSessionChanged(Guid SessionId, long Revision, BatchPresentationChange Change);

public sealed partial class BatchImportService
{
    private long notificationSequence;
    /// <summary>A committed row/phase mutation or completed operation, never a polling heartbeat.</summary>
    public event EventHandler<BatchSessionChanged>? SessionChanged;

    private void NotifySession(BatchImportSession session, BatchPresentationChange change = BatchPresentationChange.Rows | BatchPresentationChange.State)
    {
        if ((change & BatchPresentationChange.Rows) != 0) Interlocked.Increment(ref session.PresentationRevision);
        var revision = Interlocked.Increment(ref notificationSequence);
        if (SessionChanged is not { } changed) return;
        var notification = new BatchSessionChanged(session.Id, revision, change);
        foreach (EventHandler<BatchSessionChanged> handler in changed.GetInvocationList())
            try { handler(this, notification); }
            catch (Exception error) { logger.Error("Batch presentation observer failed.", error); }
    }

    // The completion notification runs after the owning task is complete, so IsOperating
    // and IsAnalyzing already reflect the final state. This does not own another worker.
    private async Task NotifyWhenFinishedAsync(Task task, params BatchImportSession[] sessions)
    {
        await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
        foreach (var session in sessions) NotifySession(session, BatchPresentationChange.State);
        try { await task.ConfigureAwait(false); }
        catch { foreach (var session in sessions) InvalidateFailedProjection(session); throw; }
        finally { foreach (var session in sessions) NotifySession(session, BatchPresentationChange.State); }
    }

    private static void InvalidateFailedProjection(BatchImportSession session)
    {
        // A failed write is not a committed Rows notification. Memory can still
        // contain canonical admissions which must never be rolled back, however;
        // completion State observers must reread those real IDs/statuses instead
        // of keeping stale Ready rows and an empty linked-progress projection.
        Interlocked.Increment(ref session.PresentationRevision);
    }
}
