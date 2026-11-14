using ModernFormsNext;
using ModernFormsNext.Accessibility;
using ModernFormsNext.Testing;
using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Localization;
using ModernTubeDownloader.Models;
using ModernTubeDownloader.Services;
using ModernTubeDownloader.Theming;
using ModernTubeDownloader.Views;
using Path = System.IO.Path;

namespace ModernTubeDownloader.Tests;

public sealed partial class HistoryDashboardPresentationTests
{
    [Fact]
    public void ReusedHistoryRowRejectsStalePopupActionsAndRoutesCurrentRetryAndCopy()
    {
        var directory = Path.Combine(Path.GetTempPath(), "mtd-history-actions-" + Guid.NewGuid().ToString("N"));
        try
        {
            var paths = AppPaths.Create(directory); paths.EnsureCreated();
            using var thumbnails = new ThumbnailCacheService(paths, new NullAppLogger(), () => false);
            using var host = ModernFormsTestHost.Create();
            var text = new LocalizationService("en");
            var first = new DownloadHistoryEntry(Guid.NewGuid(), "First", "https://youtube.com/watch?v=first", "first", "Channel", "1080p", Path.Combine(directory, "first.mp4"), DateTimeOffset.UtcNow, "Completed");
            var second = first with { QueueItemId = Guid.NewGuid(), Title = "Second", SourceUrl = "https://twitch.tv/second", FinalPath = Path.Combine(directory, "second.mp4") };
            var row = new HistoryEntryCard(first, text, thumbnails) { Width = 1200 };
            var root = new Panel(); root.Controls.Add(row); var window = host.Show(root, 1200, 600);
            row.SetDashboard(true, true);
            var retries = new List<string>(); var copies = new List<string>();
            row.RetryRequested += (_, value) => retries.Add(value); row.CopySourceRequested += (_, value) => copies.Add(value);
            Find<Button>(row, "HistoryMore-" + first.QueueItemId.ToString("N")).PerformClick(); host.ProcessPendingWork();
            var popup = window.ActivePopup!; Assert.NotNull(popup);
            var menu = Assert.Single(popup.Window.Controls.OfType<ContextMenu>());
            Assert.False(menu.Items[0].Enabled); Assert.True(menu.Items[1].Enabled);
            var retryBounds = menu.Items.Single(item => item.Text == text["Common.Retry"]).Bounds;
            row.UpdateEntry(second);
            popup.Input.Click(new System.Drawing.Point(retryBounds.Left + 10, retryBounds.Top + retryBounds.Height / 2));
            Assert.Empty(retries);

            Find<Button>(row, "HistoryMore-" + second.QueueItemId.ToString("N")).PerformClick(); host.ProcessPendingWork();
            popup = window.ActivePopup!; menu = Assert.Single(popup.Window.Controls.OfType<ContextMenu>());
            retryBounds = menu.Items.Single(item => item.Text == text["Common.Retry"]).Bounds;
            popup.Input.Click(new System.Drawing.Point(retryBounds.Left + 10, retryBounds.Top + retryBounds.Height / 2));
            Assert.Equal([second.SourceUrl], retries);

            // The headless key mapper supports Shift+F10; physical Menu-key mapping
            // remains a native keyboard qualification case, not a weakened assertion.
            window.Input.Focus(row); window.Input.PressKey(Keys.Shift | Keys.F10); host.ProcessPendingWork();
            popup = window.ActivePopup!; Assert.NotNull(popup); menu = Assert.Single(popup.Window.Controls.OfType<ContextMenu>());
            var copyBounds = menu.Items.Single(item => item.Text == text["Dashboard.CopyUrl"]).Bounds;
            popup.Input.Click(new System.Drawing.Point(copyBounds.Left + 10, copyBounds.Top + copyBounds.Height / 2));
            Assert.Equal([second.SourceUrl], copies); Assert.Equal(second.QueueItemId, row.ItemId);
            row.SetDashboard(false, false);
            Assert.False(Find<Button>(row, "HistoryMore-" + second.QueueItemId.ToString("N")).Visible);
        }
        finally { AppearancePresentation.Current = new(); if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData(100)] [InlineData(200)]
    public async Task InspectorOffersCompleteReadOnlyPathsAndClearsThemWhenSelectionEnds(int scale)
    {
        var directory = Path.Combine(Path.GetTempPath(), "mtd-history-details-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var services = await CreateServicesAsync(directory);
            using (var host = ModernFormsTestHost.Create())
            {
                services.Appearance.Initialize(); AppTypography.SetScale(scale);
                var inspector = new DashboardInspector(services); AppTypography.SetDashboardPresentation(inspector, true); host.Show(inspector, 360, 900);
                var path = Path.Combine(directory, new string('x', 160), "Nagranie Łódź 日本語.mp4");
                inspector.ShowHistory(new DownloadHistoryEntry(Guid.NewGuid(), "Archive", "https://youtube.com/watch?v=test", "id", "Channel", "1080p", path, DateTimeOffset.UtcNow, "Completed"));
                host.ProcessPendingWork();
                var output = Find<TextBox>(inspector, "InspectorOutputPath");
                Assert.True(output.ReadOnly); Assert.Equal(path, output.Text); Assert.True(output.Visible);
                Assert.Equal(services.Localization["Status.Completed"], Find<Label>(inspector, "InspectorStatus").Text);
                Assert.False(Assert.IsType<AccessibleScrollInfo>(inspector.AccessibilityObject.ScrollInfo).Horizontal.IsScrollable);
                inspector.Clear(); host.ProcessPendingWork();
                Assert.False(output.Visible); Assert.Equal(System.Drawing.Rectangle.Empty, output.Bounds);
                Assert.False(Find<Label>(inspector, "InspectorStatus").Parent!.Visible);
            }
            await services.ShutdownAsync();
        }
        finally { AppTypography.SetScale(100); AppearancePresentation.Current = new(); if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
