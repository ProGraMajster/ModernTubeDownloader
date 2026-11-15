using ModernFormsNext;
using ModernTubeDownloader.Batch;
using ModernTubeDownloader.Models;
using ModernTubeDownloader.Services;

namespace ModernTubeDownloader.Views;

internal sealed partial class BatchImportView
{
    private const int RefreshValues = 1, RefreshData = 2;
    private int pendingRefresh, refreshArmed;
    private readonly object presentationSync = new();
    private int observedOperating;
    private int presentationGeneration;
    private volatile bool presentationVisible;
    private Guid snapshotSessionId;
    private long snapshotRevision = -1;
    private IReadOnlyList<BatchImportEntry> presentationEntries = [];
    private IReadOnlySet<Guid> presentationJobs = new HashSet<Guid>();
    private long presentationKnownBytes;
    private int presentationUnknown;
    private bool presentationUnresolved;
    private (Guid Session, long Revision, BatchFilter Filter, string Search, int Page)? pageKey;
    private BatchPage? cachedPage;
    private (bool HasSession, bool ResumeQueue, bool Error)? presentationLayout;
    internal int PresentationRenderCount { get; private set; }
    internal int PresentationProgressCount { get; private set; }
    internal int PresentationRowCreatedCount { get; private set; }
    internal int PresentationRowDisposedCount { get; private set; }

    private void InitializeRefresh()
    {
        presentationVisible = Visible && Parent is not null;
        observedOperating = services.Batch.IsOperating ? 1 : 0;
        services.Batch.SessionChanged += BatchSessionChanged;
        services.Queue.Changed += BatchQueueChanged;
        timer.Tick += (_, _) => DrainPresentationRefresh();
        VisibleChanged += (_, _) => RefreshPresentationAttachment();
        ParentChanged += (_, _) => RefreshPresentationAttachment();
    }

    private void RefreshPresentationAttachment()
    {
        // Detachment deliberately does not raise VisibleChanged in the framework.
        // Revoke queued callbacks from the old owner before arming the new one.
        lock (presentationSync)
        {
            presentationGeneration++; refreshArmed = 0;
            presentationVisible = Visible && Parent is not null;
        }
        timer.Stop();
        if (presentationVisible) RequestPresentationRefresh(RefreshData);
    }

    private void BatchSessionChanged(object? sender, BatchSessionChanged change)
    {
        // Other lists do not invalidate the current page. Only a real global
        // operation transition affects this view's shared action availability.
        var operating = services.Batch.IsOperating ? 1 : 0;
        var changed = Interlocked.Exchange(ref observedOperating, operating) != operating;
        if (!disposed && (session?.Id == change.SessionId || changed)) RequestPresentationRefresh(RefreshData);
    }

    private void BatchQueueChanged(object? sender, QueueChangedEventArgs change)
    {
        if (disposed || session is null || change.Kind == QueueChangeKind.Order) return;
        if (change.ItemId is { } id && !Volatile.Read(ref presentationJobs).Contains(id)) return;
        RequestPresentationRefresh(change.Kind == QueueChangeKind.Item ? RefreshValues : RefreshData);
    }

    private void RequestPresentationRefresh(int flags)
    {
        if (disposed) return;
        Interlocked.Or(ref pendingRefresh, flags);
        int generation;
        lock (presentationSync)
        {
            if (!presentationVisible || refreshArmed != 0) return;
            refreshArmed = 1; generation = presentationGeneration;
        }
        Application.RunOnUIThread(() =>
        {
            if (generation != Volatile.Read(ref presentationGeneration)) return;
            if (disposed || !presentationVisible || Parent is null) { Interlocked.Exchange(ref refreshArmed, 0); return; }
            // A one-shot, coalesced presentation update. No service operation, busy
            // transition, full-tree polling or permanent idle timer is involved.
            timer.Start();
        });
    }

    private void DrainPresentationRefresh()
    {
        timer.Stop(); lock (presentationSync) refreshArmed = 0;
        if (disposed || !presentationVisible || Parent is null) return;
        TraceBatchEvent("tick.dirty");
        var flags = Interlocked.Exchange(ref pendingRefresh, 0);
        if ((flags & RefreshData) != 0) Render();
        else if ((flags & RefreshValues) != 0) RefreshProgressValues();
        if (Volatile.Read(ref pendingRefresh) != 0) RequestPresentationRefresh(0);
    }

    private IReadOnlyList<BatchImportEntry> PresentationEntries()
    {
        if (session is not { } current) return [];
        var revision = Volatile.Read(ref current.PresentationRevision);
        if (snapshotSessionId == current.Id && snapshotRevision == revision) return presentationEntries;
        presentationEntries = current.Snapshot();
        snapshotSessionId = current.Id; snapshotRevision = revision;
        Volatile.Write(ref presentationJobs, presentationEntries.Where(entry => entry.QueueItemId is not null)
            .Select(entry => entry.QueueItemId!.Value).ToHashSet());
        presentationKnownBytes = 0; presentationUnknown = 0;
        foreach (var entry in presentationEntries.Where(entry => !string.IsNullOrWhiteSpace(entry.Identity) &&
            entry.Status is not (BatchEntryStatus.Duplicate or BatchEntryStatus.Skipped)))
            if (entry.EstimatedSizeBytes is > 0 && presentationKnownBytes <= long.MaxValue - entry.EstimatedSizeBytes.Value)
                presentationKnownBytes += entry.EstimatedSizeBytes.Value;
            else presentationUnknown++;
        presentationUnresolved = presentationEntries.Any(entry => entry.QueueItemId is null && entry.Status is
            BatchEntryStatus.Pending or BatchEntryStatus.Validating or BatchEntryStatus.Analyzing or BatchEntryStatus.Ready);
        return presentationEntries;
    }

    private BatchPage PresentationPage(BatchImportSession current)
    {
        var key = (current.Id, Volatile.Read(ref current.PresentationRevision),
            (BatchFilter)Math.Max(0, filter.SelectedIndex), search.Text, page);
        if (pageKey == key && cachedPage is not null) return cachedPage;
        cachedPage = services.Batch.Page(current, key.Item3, key.Item4, page);
        pageKey = (current.Id, key.Item2, key.Item3, key.Item4, cachedPage.Page);
        return cachedPage;
    }

    private void LayoutPresentationChange()
    {
        var current = (session is not null, buttons["ResumeQueue"].Visible, lastErrorKey is not null);
        if (presentationLayout == current) return;
        presentationLayout = current; LayoutPage();
    }

    private void RefreshProgressValues()
    {
        if (disposed) return;
        PresentationProgressCount++;
        if (operation is { } current)
        {
            status.Text = workflowStage.Text = services.Localization.Get("Batch.Operation." + current.Phase,
                current.Completed, current.Total?.ToString(services.Localization.Culture) ?? "—");
            progress.Value = current.Total is > 0 ? (int)(100L * current.Completed / current.Total.Value) : 0;
        }
        RefreshTransferProgress();
    }

    private void RefreshTransferProgress()
    {
        if (session is null) return;
        _ = PresentationEntries();
        var jobs = Volatile.Read(ref presentationJobs);
        var linked = services.Queue.ProgressSnapshot(jobs);
        var summary = QueueProgressSummary.Build(linked, services.Queue.IsPaused);
        downloadProgress.Value = (int)summary.Percent;
        overall.Text = services.Localization.Get("Batch.Overall", summary.Settled, summary.Total, summary.Completed, summary.Failed,
            (presentationKnownBytes / 1073741824d).ToString("N2", services.Localization.Culture), presentationUnknown,
            !presentationUnresolved && summary.TransferRemaining is { } eta ? eta.ToString(@"d\.hh\:mm\:ss") : "—");
    }

    private void DisposeRefresh()
    {
        services.Batch.SessionChanged -= BatchSessionChanged;
        services.Queue.Changed -= BatchQueueChanged;
        lock (presentationSync) { presentationGeneration++; refreshArmed = 0; presentationVisible = false; }
        timer.Stop(); Interlocked.Exchange(ref pendingRefresh, 0);
    }
}
