using ModernFormsNext;
using ModernFormsNext.Accessibility;
using ModernFormsNext.Testing;
using ModernTubeDownloader.Theming;
using ModernTubeDownloader.Views;
using Path = System.IO.Path;

namespace ModernTubeDownloader.Tests;

public sealed partial class LiveDashboardPresentationTests
{
    [Theory]
    [InlineData(100, 1236, true)]
    [InlineData(150, 900, false)]
    [InlineData(200, 900, false)]
    public async Task LiveSearchAndSortStayReachableWhenToolbarMovesBetweenRows(int scale, int width, bool besideSources)
    {
        var directory = Path.Combine(Path.GetTempPath(), "mtd-live-toolbar-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var services = await CreateServicesAsync(directory);
            using (var host = ModernFormsTestHost.Create())
            {
                services.Appearance.Initialize(); services.Localization.SetLanguage("pl"); AppTypography.SetScale(scale);
                var view = new LiveView(services); AppTypography.SetDashboardPresentation(view, true); view.SetDashboard(true);
                host.Show(view, width, 1080); host.ProcessPendingWork();
                var sources = Find<FlowLayoutPanel>(view, "LiveSourceFilters");
                var search = Find<TextBox>(view, "LiveSearch");
                var sort = Find<ComboBox>(view, "LiveSort");
                var active = Find<Button>(view, "DashboardLiveActive");
                var recent = Find<Button>(view, "DashboardLiveRecent");
                Assert.Equal(besideSources, search.Top == sources.Top);
                foreach (var control in new Control[] { sources, search, sort, active, recent })
                {
                    Assert.True(control.Visible); Assert.True(control.Width > 0 && control.Height > 0);
                    foreach (var other in new Control[] { sources, search, sort, active, recent })
                        if (other != control) Assert.False(control.Bounds.IntersectsWith(other.Bounds), control.AccessibleAutomationId + " overlaps " + other.AccessibleAutomationId);
                }
                var scroll = Assert.IsAssignableFrom<ScrollableControl>(search.Parent);
                Assert.False(Assert.IsType<AccessibleScrollInfo>(scroll.AccessibilityObject.ScrollInfo).Horizontal.IsScrollable);
                // The existing search/filter contract remains active after both responsive layouts.
                search.Text = "no matching session"; host.ProcessPendingWork();
                Assert.Contains("0", Find<Label>(view, "LivePageSummary").Text);
            }
            await services.ShutdownAsync();
        }
        finally { AppTypography.SetScale(100); AppearancePresentation.Current = new(); if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
