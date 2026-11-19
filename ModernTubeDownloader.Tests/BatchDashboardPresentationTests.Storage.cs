using ModernFormsNext;
using ModernFormsNext.Testing;
using ModernTubeDownloader.Batch;
using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Theming;
using ModernTubeDownloader.Views;
using Path = System.IO.Path;

namespace ModernTubeDownloader.Tests;

public sealed partial class BatchDashboardPresentationTests
{
    [Fact]
    public async Task FailedPostAdmissionJournalRefreshesRetainedRowsAndLinkedProgressWithoutLosingCanonicalId()
    {
        var directory = Path.Combine(Path.GetTempPath(), "mtd-batch-failed-admission-view-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var services = await CreateServicesAsync(directory);
            var session = await services.Batch.ImportTextAsync("https://example.test/watch/admission-journal");
            await services.Batch.StartAsync(session);
            var original = Assert.Single(session.Snapshot());
            Assert.Equal(BatchEntryStatus.Ready, original.Status);
            using (var host = ModernFormsTestHost.Create())
            {
                services.Appearance.Initialize();
                var view = new BatchImportView(services, session); view.SetDashboard(true); host.Show(view, 1650, 927);
                host.ProcessPendingWork(); host.Clock.Advance(TimeSpan.FromSeconds(1));
                var row = Assert.Single(Find<FlowLayoutPanel>(view, "BatchRows").Controls);
                Assert.True(Find<Button>(view, "BatchEntryAdd1").Enabled);
                Assert.True(Find<Button>(view, "BatchDestinationSave").Enabled);
                var revision = session.PresentationRevision;
                var notifications = new System.Collections.Concurrent.ConcurrentQueue<BatchSessionChanged>();
                services.Batch.SessionChanged += (_, change) => notifications.Enqueue(change);
                var journal = Path.Combine(directory, "Batch", session.Id.ToString("N"), "updates.jsonl");
                using (var locked = new FileStream(journal, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read))
                {
                    var handoff = services.Batch.AddReadyAsync(session);
                    PumpBatchOperation(host, handoff);
                    await Assert.ThrowsAsync<IOException>(() => handoff);
                }
                host.ProcessPendingWork(); host.Clock.Advance(TimeSpan.FromSeconds(1));
                Assert.True(session.PresentationRevision > revision);
                Assert.All(notifications, change => Assert.Equal(BatchPresentationChange.State, change.Change));
                var admitted = Assert.Single(session.Snapshot());
                Assert.Equal(original.Id, admitted.Id); Assert.Equal(original.Id, admitted.QueueItemId);
                Assert.Equal(original.Id, Assert.Single(services.Queue.Snapshot()).Id);
                Assert.Same(row, Assert.Single(Find<FlowLayoutPanel>(view, "BatchRows").Controls));
                Assert.Equal(services.Localization["Batch.Status.AddedToQueue"], Find<Label>(view, "BatchEntryStatus1").Text);
                Assert.False(Find<Button>(view, "BatchEntryAdd1").Enabled);
                Assert.False(Find<Button>(view, "BatchDestinationSave").Enabled);
                Assert.Equal(services.Localization.Get("Batch.Workflow.Facts", 1, 0, 0, 0, 0, 1), Find<Label>(view, "BatchWorkflowFacts").Text);
                // The failed handoff could not persist its final workflow phase.
                // Reconcile it now that storage is writable, before asserting that
                // a subsequent bytes-only event has no remaining State work.
                var reconcile = services.Batch.RefreshQueueAsync(session);
                PumpBatchOperation(host, reconcile); await reconcile;
                var read = AtomicJsonFile.ReadAsync<BatchImportSession>(Path.Combine(directory, "Batch", session.Id.ToString("N"), "session.json"));
                PumpBatchOperation(host, read);
                Assert.Equal(BatchWorkflowPhase.WaitingForQueue, Assert.IsType<BatchImportSession>(await read).WorkflowPhase);
                host.ProcessPendingWork(); host.Clock.Advance(TimeSpan.FromSeconds(1));
                var renders = view.PresentationRenderCount;
                var progressUpdates = view.PresentationProgressCount;
                services.Queue.Update(original.Id, item => item.DownloadedBytes = 1234);
                host.ProcessPendingWork(); host.Clock.Advance(TimeSpan.FromSeconds(1));
                Assert.Equal(renders, view.PresentationRenderCount);
                Assert.Equal(progressUpdates + 1, view.PresentationProgressCount);
                var retry = services.Batch.AddReadyAsync(session); PumpBatchOperation(host, retry);
                Assert.Equal(0, await retry); Assert.Equal(original.Id, Assert.Single(services.Queue.Snapshot()).Id);
                host.Dispatcher.ThrowUnhandledExceptions();
            }
            await services.ShutdownAsync();
        }
        finally { AppearancePresentation.Current = new(); if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
