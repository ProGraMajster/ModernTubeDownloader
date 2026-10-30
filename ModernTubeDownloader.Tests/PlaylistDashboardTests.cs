using ModernFormsNext;
using ModernFormsNext.Testing;
using ModernTubeDownloader.Localization;
using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Models;
using ModernTubeDownloader.Services;
using ModernTubeDownloader.Theming;
using ModernTubeDownloader.Views;

namespace ModernTubeDownloader.Tests;

[Collection("ModernFormsNext TestHost")]
public sealed class PlaylistDashboardTests
{
    [Fact]
    public void GroupActionsUseCanonicalForwardAndReverseFocusTraversal()
    {
        using var host = ModernFormsTestHost.Create();
        var directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "mtd-group-focus-" + Guid.NewGuid().ToString("N"));
        try
        {
            var paths = AppPaths.Create(directory); paths.EnsureCreated();
            using var thumbnails = new ThumbnailCacheService(paths, new NullAppLogger(), () => false);
            var children = new[] { new DownloadQueueItem { PlaylistTitle = "A" }, new DownloadQueueItem { PlaylistTitle = "A" } };
            using var group = new PlaylistGroupCard(new(new("example.test", "a"), children), thumbnails, new("pl")) { Width = 960 };
            host.Show(group, 960, 220);
            var title = group.Controls.OfType<Button>().Single(b => b.AccessibleAutomationId?.StartsWith("PlaylistGroupToggle-") == true);
            var menu = group.Controls.OfType<Button>().Single(b => b.AccessibleAutomationId?.StartsWith("PlaylistGroupMenu-") == true);
            Assert.True(host.Input.Focus(title));
            host.Input.PressKey(Keys.Tab); Assert.True(menu.Focused);
            host.Input.PressKey(Keys.Tab | Keys.Shift); Assert.True(title.Focused);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData(100, 1286, "pl")]
    [InlineData(100, 1030, "pl")]
    [InlineData(150, 1286, "pl")]
    [InlineData(150, 1030, "pl")]
    [InlineData(200, 1286, "en")]
    [InlineData(200, 1030, "pl")]
    public void ModernPlaylistKeepsScaledRowsBoundedAndSelectionAcrossPagesAndShells(int scale, int width, string language)
    {
        using var host = ModernFormsTestHost.Create();
        try
        {
            AppTypography.SetScale(scale);
            using var preview = new PlaylistPreviewControl(new LocalizationService(language));
            host.Show(preview, width, AppTypography.TextHeight(600));
            preview.SetDashboard(true);
            preview.ShowPlaylist(new("qa", "Playlist QA 日本語", "Channel", "https://example.test/playlist/qa", null,
                Enumerable.Range(1, 120).Select(i => new PlaylistEntry($"p{i}", "Long title • Zażółć 日本語 🎬", $"https://example.test/watch/{i}", i,
                    i == 4 ? null : 90061, null, null, i == 3 ? "private" : null, null)).ToArray()), "1080");
            host.ProcessPendingWork();
            var list = preview.Controls.OfType<FlowLayoutPanel>().Single();
            var first = (PlaylistEntryRow)list.Controls[0];
            var label = first.Controls.OfType<Label>().Single(c => c.AccessibleAutomationId == "PlaylistEntryTitle1");
            Assert.True(label.Font.SizeInPoints >= 14 * scale / 100f);
            Assert.True(label.Bottom < first.Height);
            Assert.Equal(40, list.Controls.Count);
            Assert.False(((PlaylistEntryRow)list.Controls[2]).Selector.Enabled);
            preview.Controls.OfType<Button>().Single(b => b.AccessibleAutomationId == "PlaylistClearSelectionButton").PerformClick();
            first.Selector.Checked = true;
            preview.Controls.OfType<Button>().Single(b => b.AccessibleAutomationId == "PlaylistNextPageButton").PerformClick();
            ((PlaylistEntryRow)list.Controls[0]).Selector.Checked = true;
            preview.SetDashboard(false);
            preview.SetDashboard(true);
            Assert.Equal(2, preview.Selection!.SelectedCount);
            Assert.True(preview.Selection.IsSelected(0));
            Assert.True(preview.Selection.IsSelected(40));
            preview.Controls.OfType<Button>().Single(b => b.AccessibleAutomationId == "PlaylistPreviousPageButton").PerformClick();
            host.ProcessPendingWork();
            Assert.True(((PlaylistEntryRow)list.Controls[0]).Selector.Checked);
            Assert.Equal(40, list.Controls.Count);
            Assert.Equal(0, list.VerticalScrollProperties.Value);
            foreach (var button in preview.Controls.OfType<Button>().Where(b => b.Visible))
            {
                Assert.InRange(button.Left, 0, width);
                Assert.InRange(button.Right, 0, width);
                Assert.InRange(button.Bottom, 0, preview.Height);
            }
            Assert.True(preview.Controls.OfType<Button>().Single(b => b.AccessibleAutomationId == "PlaylistAddSelectedButton").Bottom < list.Top);
            preview.Dispose(); // TestHost closes its hierarchy again: cancellation/disposal must be idempotent.
        }
        finally { AppTypography.SetScale(100); }
    }
}
