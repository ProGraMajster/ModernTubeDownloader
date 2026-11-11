using System.Text.Json;
using ModernFormsNext;
using ModernFormsNext.Testing;
using ModernTubeDownloader.Batch;
using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Services;
using ModernTubeDownloader.Shell;
using ModernTubeDownloader.Theming;
using ModernTubeDownloader.Views;
using Path = System.IO.Path;

namespace ModernTubeDownloader.Tests;

public sealed partial class BatchDashboardPresentationTests
{
    [Fact]
    public async Task ThumbnailPreferencePreservesOptOutAndPersistsExplicitChoiceWithoutChangingEntries()
    {
        var directory = Path.Combine(Path.GetTempPath(), "mtd-batch-thumbnail-choice-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var services = await CreateServicesAsync(directory);
            var session = await services.Batch.ImportTextAsync("https://example.test/a\nhttps://example.test/a\ninvalid");
            var before = JsonSerializer.Serialize(session.Snapshot());
            using (var host = ModernFormsTestHost.Create())
            {
                services.Appearance.Initialize();
                var view = new BatchImportView(services, session); view.SetDashboard(true); host.Show(view, 1320, 1024);
                Find<Button>(view, "BatchOptions").PerformClick(); host.ProcessPendingWork();
                var thumbnails = Find<CheckBox>(view, "BatchShowThumbnails");
                Assert.False(thumbnails.Checked); Assert.False(services.Appearance.Profile.BatchImport.Thumbnail);
                var metrics = Descendants(view).OfType<DashboardMetric>().ToArray();
                Assert.Equal(new[] { "3", "1", "1", "1" }, metrics.Select(metric => metric.Controls[2].Text));
                Assert.Contains(services.Localization["Batch.Status.Pending"], Find<Label>(view, "BatchParserDetails").Text);
                thumbnails.Checked = true;
                PumpUntil(host, () => thumbnails.Enabled && services.Appearance.Profile.BatchImport.Thumbnail);
                var loaded = new AppearanceProfileStore(Path.Combine(directory, "appearance.json")).LoadAsync(new NullAppLogger());
                PumpUntil(host, () => loaded.IsCompleted); Assert.True((await loaded).BatchImport.Thumbnail);
                thumbnails.Checked = false;
                PumpUntil(host, () => thumbnails.Enabled && !services.Appearance.Profile.BatchImport.Thumbnail);
                var restored = new AppearanceProfileStore(Path.Combine(directory, "appearance.json")).LoadAsync(new NullAppLogger());
                PumpUntil(host, () => restored.IsCompleted); Assert.False((await restored).BatchImport.Thumbnail);
                Assert.Empty(Descendants(view).OfType<PictureBox>());
                Assert.Equal(before, JsonSerializer.Serialize(session.Snapshot())); Assert.Empty(services.Queue.Snapshot());
            }
            await services.ShutdownAsync();
        }
        finally { AppearancePresentation.Current = new(); if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData(1320, 100)]
    [InlineData(1060, 150)]
    [InlineData(1700, 200)]
    public async Task OtherComposesWorkingToolsAndRealRecentMetadataAndRestoresClassic(int width, int scale)
    {
        var directory = Path.Combine(Path.GetTempPath(), "mtd-other-composition-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var services = await CreateServicesAsync(directory);
            var session = await services.Batch.ImportTextAsync("https://example.test/a\nhttps://example.test/b");
            using (var host = ModernFormsTestHost.Create())
            {
                services.Appearance.Initialize(); AppTypography.SetScale(scale);
                var opened = 0; var picked = 0; var routes = new List<AppPageId>();
                var view = new OtherView(services); view.RegisterTool("Batch", () => opened++); view.RegisterTool("Thumbnails", () => opened++);
                var batchCard = Find<Button>(view, "OtherOpenBatch").Parent!;
                var thumbnailCard = Find<Button>(view, "OtherOpenThumbnails").Parent!;
                var classicParent = batchCard.Parent;
                view.NavigateRequested += (_, page) => routes.Add(page); view.PickBatchFileRequested += (_, _) => picked++;
                AppTypography.SetDashboardPresentation(view, true); view.SetDashboard(true); host.Show(view, width, 1024);
                var refresh = view.RefreshToolsAsync(); PumpUntil(host, () => refresh.IsCompleted); await refresh; host.ProcessPendingWork();
                var recent = Find<Button>(view, "BatchRecent" + session.Id.ToString("N"));
                Assert.Contains(session.FileName, recent.AccessibleName); Assert.Contains("2 URL", recent.AccessibleName);
                Assert.Contains(session.CreatedAt.ToLocalTime().ToString("g", services.Localization.Culture), recent.AccessibleName);
                Assert.NotSame(batchCard.Parent, thumbnailCard.Parent);
                Assert.True(thumbnailCard.Top > 0);
                var tools = new[] { Find<Button>(view, "OtherOpenThumbnails"), Find<Button>(view, "OtherNavigateHistory"), Find<Button>(view, "OtherNavigateSettings"), Find<Button>(view, "OtherNavigateHelp") };
                foreach (var button in tools)
                {
                    button.PerformClick();
                    var tile = button.AccessibleAutomationId == "OtherOpenThumbnails" ? button.Parent! : button;
                    Assert.All(tile.Controls.Where(child => child.Visible), child =>
                    {
                        Assert.True(child.Width > 0 && child.Height > 0);
                        Assert.True(child.Left >= 0 && child.Top >= 0 && child.Right <= tile.Width && child.Bottom <= tile.Height,
                            $"{child.Text} escapes {button.AccessibleAutomationId} at {scale}%.");
                    });
                }
                Find<Button>(view, "OtherPickBatchFile").PerformClick();
                Assert.Equal(1, opened); Assert.Equal(1, picked);
                Assert.Equal(new[] { AppPageId.History, AppPageId.Settings, AppPageId.Help }, routes);
                Assert.Equal(4, Descendants(view).OfType<Button>().Count(button => button.AccessibleAutomationId?.StartsWith("OtherQuick", StringComparison.Ordinal) == true));
                view.SetDashboard(false); AppTypography.SetDashboardPresentation(view, false); host.ProcessPendingWork();
                Assert.Same(classicParent, thumbnailCard.Parent); Assert.Same(classicParent, batchCard.Parent);
                Assert.Equal(services.Localization["Other.Open"], Find<Button>(view, "OtherOpenThumbnails").Text);
                Assert.False(Find<Button>(view, "OtherPickBatchFile").Parent!.Visible);
                Assert.Equal(2, session.Snapshot().Count); Assert.Empty(services.Queue.Snapshot());
            }
            await services.ShutdownAsync();
        }
        finally { AppTypography.SetScale(100); AppearancePresentation.Current = new(); if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    private static IEnumerable<Control> Descendants(Control parent)
    {
        foreach (var child in parent.Controls) { yield return child; foreach (var nested in Descendants(child)) yield return nested; }
    }

    private static void PumpUntil(ModernFormsTestHost host, Func<bool> complete)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!complete() && DateTime.UtcNow < deadline) { host.ProcessPendingWork(); Thread.Sleep(5); }
        Assert.True(complete(), "The targeted presentation operation did not complete.");
    }
}
