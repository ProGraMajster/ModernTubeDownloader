using ModernFormsNext;
using ModernFormsNext.Testing;
using ModernTubeDownloader.Batch;
using ModernTubeDownloader.Models;
using ModernTubeDownloader.Theming;
using ModernTubeDownloader.Views;
using Path = System.IO.Path;

namespace ModernTubeDownloader.Tests;

public sealed partial class BatchDashboardPresentationTests
{
    [Theory]
    [InlineData(122, "BatchWorkflowMode")]
    [InlineData(122, "BatchSessionChoice")]
    [InlineData(122, "BatchFilter")]
    [InlineData(2750, "BatchWorkflowMode")]
    [InlineData(10000, "BatchWorkflowMode")]
    public async Task IdleBatchKeepsFocusedWorkflowPopupDraftRowsAndGeometryForFifteenClockTicks(int count, string popupId)
    {
        var root = Path.Combine(Path.GetTempPath(), "mtd-batch-idle-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var services = await CreateServicesAsync(root);
            var text = string.Join('\n', Enumerable.Range(0, count - 4).Select(index => "https://example.test/watch/" + index)
                .Concat(Enumerable.Range(0, 4).Select(index => "https://example.test/watch/" + index)));
            var session = await services.Batch.ImportTextAsync(text);
            var second = await services.Batch.ImportTextAsync("https://example.test/watch/other-list");
            Assert.Equal(count - 4, session.Snapshot().Count(row => row.Status == BatchEntryStatus.Pending));
            Assert.Equal(4, session.Snapshot().Count(row => row.Status == BatchEntryStatus.Duplicate));
            using (var host = ModernFormsTestHost.Create())
            {
                services.Appearance.Initialize();
                var view = new BatchImportView(services, session); view.SetDashboard(true); host.Show(view, 1650, 927);
                view.Present(second); view.Present(session);
                host.ProcessPendingWork(); host.Clock.Advance(TimeSpan.FromSeconds(1));
                var rows = Find<FlowLayoutPanel>(view, "BatchRows"); var original = rows.Controls.ToArray();
                var rootInput = Find<TextBox>(view, "BatchDestinationRoot"); rootInput.Text += "-unsaved";
                var draft = rootInput.Text; var bounds = original.Select(row => row.Bounds).ToArray();
                var combo = Find<ComboBox>(view, popupId);
                combo.Select(); combo.DroppedDown = true;
                Assert.True(combo.Selected); Assert.True(combo.DroppedDown);
                var viewport = rows.DisplayRectangle;
                var enabledChanges = 0; combo.EnabledChanged += (_, _) => enabledChanges++;
                var renders = view.PresentationRenderCount; var creates = view.PresentationRowCreatedCount; var disposals = view.PresentationRowDisposedCount;
                for (var index = 0; index < 15; index++) host.Clock.Advance(TimeSpan.FromSeconds(1));
                Assert.Equal(renders, view.PresentationRenderCount);
                Assert.Equal(creates, view.PresentationRowCreatedCount); Assert.Equal(disposals, view.PresentationRowDisposedCount);
                Assert.Equal(0, enabledChanges); Assert.True(combo.Selected); Assert.True(combo.DroppedDown);
                Assert.Equal(original, rows.Controls.ToArray()); Assert.Equal(bounds, rows.Controls.Select(row => row.Bounds).ToArray());
                Assert.Equal(viewport, rows.DisplayRectangle); Assert.Equal(draft, rootInput.Text);
                Assert.Equal(20, rows.Controls.Count); Assert.Empty(services.Queue.Snapshot());
                combo.DroppedDown = false;
            }
            await services.ShutdownAsync();
        }
        finally { AppearancePresentation.Current = new(); if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task RealAnalysisUpdatesRetainedRowsAndLinkedProgressCannotRebuildThePage()
    {
        var root = Path.Combine(Path.GetTempPath(), "mtd-batch-retained-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var services = await CreateServicesAsync(root);
            var session = await services.Batch.ImportTextAsync("https://example.test/watch/retained?title=Retained");
            var id = session.Snapshot()[0].Id;
            using (var host = ModernFormsTestHost.Create())
            {
                services.Appearance.Initialize();
                var view = new BatchImportView(services, session); view.SetDashboard(true); host.Show(view, 1650, 927);
                host.ProcessPendingWork(); host.Clock.Advance(TimeSpan.FromSeconds(1));
                var rows = Find<FlowLayoutPanel>(view, "BatchRows"); var original = Assert.Single(rows.Controls);
                var selected = Find<CheckBox>(view, "BatchSelect1"); selected.Checked = true;
                var filter = Find<ComboBox>(view, "BatchFilter"); filter.Select(); filter.DroppedDown = true;
                // The actual shared service executes FakeTool metadata and persists the journal.
                var analysis = services.Batch.StartAsync(session); PumpBatchOperation(host, analysis); await analysis;
                host.ProcessPendingWork(); host.Clock.Advance(TimeSpan.FromSeconds(1));
                Assert.Equal(BatchEntryStatus.Ready, session.Snapshot()[0].Status);
                Assert.Same(original, Assert.Single(rows.Controls)); Assert.Same(selected, Find<CheckBox>(view, "BatchSelect1"));
                Assert.True(selected.Checked); Assert.True(filter.DroppedDown); Assert.True(filter.Selected);
                Assert.Equal(services.Localization["Batch.Status.Ready"], Find<Label>(view, "BatchEntryStatus1").Text);
                Assert.True(Find<Button>(view, "BatchEntryAdd1").Enabled);
                filter.DroppedDown = false;
                var handoff = services.Batch.AddReadyAsync(session); PumpBatchOperation(host, handoff); Assert.Equal(1, await handoff);
                services.Queue.Update(id, item => item.Status = DownloadStatus.DownloadingVideo);
                var refresh = services.Batch.RefreshQueueAsync(session); PumpBatchOperation(host, refresh); await refresh;
                host.ProcessPendingWork(); host.Clock.Advance(TimeSpan.FromSeconds(1));
                var renders = view.PresentationRenderCount; var progressUpdates = view.PresentationProgressCount;
                for (var index = 0; index < 30; index++)
                    services.Queue.Update(id, item => { item.ProgressPercent = index; item.DownloadedBytes = index * 10; item.SpeedBytesPerSecond = 100; });
                host.ProcessPendingWork(); host.Clock.Advance(TimeSpan.FromSeconds(1));
                Assert.Equal(renders, view.PresentationRenderCount);
                Assert.Equal(progressUpdates + 1, view.PresentationProgressCount);
                Assert.Same(original, Assert.Single(rows.Controls)); Assert.True(selected.Checked);
                view.Visible = false;
                services.Queue.Update(id, item => item.ProgressPercent = 50);
                host.Clock.Advance(TimeSpan.FromSeconds(10));
                Assert.Equal(progressUpdates + 1, view.PresentationProgressCount);
                view.Visible = true; host.ProcessPendingWork(); host.Clock.Advance(TimeSpan.FromSeconds(1));
                Assert.Same(original, Assert.Single(rows.Controls));
                var rowDisposed = false; original.Disposed += (_, _) => rowDisposed = true;
                view.Dispose(); services.Queue.Update(id, item => item.ProgressPercent = 60);
                host.Clock.Advance(TimeSpan.FromSeconds(10));
                Assert.True(rowDisposed);
            }
            await services.ShutdownAsync();
        }
        finally { AppearancePresentation.Current = new(); if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private static void PumpBatchOperation(ModernFormsTestHost host, Task operation)
    {
        // Only the real child-process/filesystem completion uses a bounded readiness
        // pump. All presentation cadence and the fifteen-second idle case use TestClock.
        var deadline = System.Diagnostics.Stopwatch.StartNew();
        while (!operation.IsCompleted && deadline.Elapsed < TimeSpan.FromSeconds(5))
        { host.ProcessPendingWork(); Thread.Sleep(5); }
        Assert.True(operation.IsCompleted, "The real Batch operation did not complete within the existing five-second host budget.");
    }

    [Theory]
    [InlineData(100)]
    [InlineData(200)]
    public async Task FirstAttachedBatchRowUsesTheActualModernTypographyWithoutAThemeOrScaleRefresh(int scale)
    {
        var root = Path.Combine(Path.GetTempPath(), "mtd-batch-first-font-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var services = await CreateServicesAsync(root);
            var session = await services.Batch.ImportTextAsync("https://example.test/watch/first-font");
            using (var host = ModernFormsTestHost.Create())
            {
                services.Appearance.Initialize(); AppTypography.SetScale(scale);
                var view = new BatchImportView(services, session); view.SetDashboard(true); host.Show(view, 1650, 927);
                var title = Find<Label>(view, "BatchEntryTitle1");
                var status = Find<Label>(view, "BatchEntryStatus1");
                var quality = Find<Label>(view, "BatchEntryQuality1");
                // Independently attach an ordinary label to the same real presentation
                // root and apply the public SmallText recipe. No refresh is applied to rows.
                using var reference = new Label { Text = "Ready • 1080p" };
                view.Controls.Add(reference); AppTypography.Bind(reference, AppTextToken.SmallText);
                Assert.True(reference.Font.SizeInPoints > AppTypography.Font(AppTextToken.SmallText).SizeInPoints);
                Assert.Equal(reference.Font.SizeInPoints, title.Font.SizeInPoints);
                Assert.Equal(reference.Font.SizeInPoints, status.Font.SizeInPoints);
                Assert.Equal(reference.Font.SizeInPoints, quality.Font.SizeInPoints);
                view.Controls.Remove(reference);
            }
            await services.ShutdownAsync();
        }
        finally { AppTypography.SetScale(100); AppearancePresentation.Current = new(); if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task DetachedBatchRevokesQueuedRefreshAndReattachesWithCommittedStateOnce()
    {
        var root = Path.Combine(Path.GetTempPath(), "mtd-batch-detached-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var services = await CreateServicesAsync(root);
            var session = await services.Batch.ImportTextAsync("https://example.test/watch/detached");
            using (var host = ModernFormsTestHost.Create())
            {
                services.Appearance.Initialize();
                var container = new Panel(); var view = new BatchImportView(services, session); view.SetDashboard(true);
                container.Controls.Add(view); host.Show(container, 1650, 927); host.Clock.Advance(TimeSpan.FromSeconds(1));
                var row = Assert.Single(Find<FlowLayoutPanel>(view, "BatchRows").Controls);
                // Queue the production observer update, then detach before its presentation tick.
                var ids = session.Snapshot().Select(entry => entry.Id).ToHashSet();
                var edit = services.Batch.EditAsync(session, ids, quality: "720"); PumpBatchOperation(host, edit); await edit;
                var renders = view.PresentationRenderCount;
                container.Controls.Remove(view);
                host.Clock.Advance(TimeSpan.FromSeconds(15));
                Assert.Equal(renders, view.PresentationRenderCount);
                var second = services.Batch.EditAsync(session, ids, quality: "1080"); PumpBatchOperation(host, second); await second;
                host.Clock.Advance(TimeSpan.FromSeconds(15)); Assert.Equal(renders, view.PresentationRenderCount);
                container.Controls.Add(view); host.Clock.Advance(TimeSpan.FromSeconds(1));
                Assert.Equal(renders + 1, view.PresentationRenderCount);
                Assert.Same(row, Assert.Single(Find<FlowLayoutPanel>(view, "BatchRows").Controls));
                Assert.Contains(services.Localization["Quality.1080"], Find<Label>(view, "BatchEntryQuality1").Text);
                host.Clock.Advance(TimeSpan.FromSeconds(15)); Assert.Equal(renders + 1, view.PresentationRenderCount);
            }
            await services.ShutdownAsync();
        }
        finally { AppearancePresentation.Current = new(); if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
