using ModernFormsNext;
using ModernFormsNext.Testing;
using ModernTubeDownloader.Localization;
using ModernTubeDownloader.Models;
using ModernTubeDownloader.Theming;
using ModernTubeDownloader.Views;

namespace ModernTubeDownloader.Tests;

[Collection("ModernFormsNext TestHost")]
public sealed class PlaylistDashboardTests
{
    [Theory]
    [InlineData(100, 1286, "pl")]
    [InlineData(100, 1030, "pl")]
    [InlineData(150, 1286, "pl")]
    [InlineData(200, 1286, "en")]
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
            Assert.True(list.Bottom < preview.Controls.OfType<Button>().Single(b => b.AccessibleAutomationId == "PlaylistAddSelectedButton").Top);
            preview.Dispose(); // TestHost closes its hierarchy again: cancellation/disposal must be idempotent.
        }
        finally { AppTypography.SetScale(100); }
    }
}
