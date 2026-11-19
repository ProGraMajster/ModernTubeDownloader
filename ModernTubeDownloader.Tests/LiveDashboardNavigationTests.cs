using ModernFormsNext;
using ModernFormsNext.Accessibility;
using ModernFormsNext.Testing;
using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Models;
using ModernTubeDownloader.Services;
using ModernTubeDownloader.Theming;
using ModernTubeDownloader.Views;
using Path = System.IO.Path;

namespace ModernTubeDownloader.Tests;

public sealed partial class LiveDashboardPresentationTests
{
    [Theory]
    [InlineData(100, 780)]
    [InlineData(150, 1100)]
    [InlineData(200, 1100)]
    public async Task LiveToolbarAndRowsFitAtLargeTextScales(int scale, int width)
    {
        var directory = Path.Combine(Path.GetTempPath(), "mtd-live-responsive-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var services = await CreateServicesAsync(directory);
            using (var host = ModernFormsTestHost.Create())
            {
                services.Appearance.Initialize(); AppTypography.SetScale(scale);
                services.Live.Add("https://example.test/live/upcoming", null, "720", LiveStartPolicy.FromNow, true);
                var view = new LiveView(services); AppTypography.SetDashboardPresentation(view, true); view.SetDashboard(true);
                host.Show(view, width, 1000); host.ProcessPendingWork();
                var active = Find<Button>(view, "DashboardLiveActive"); var recent = Find<Button>(view, "DashboardLiveRecent");
                Assert.True(active.Width >= DashboardVisual.ActionWidth(active));
                Assert.True(recent.Width >= DashboardVisual.ActionWidth(recent));
                var list = Find<FlowLayoutPanel>(view, "LiveActiveList");
                var row = Assert.IsType<LiveSessionCard>(Assert.Single(list.Controls));
                Assert.Equal(AccessibleControlType.ListItem, row.AccessibilityObject.ControlType);
                Assert.True(row.AccessibilityObject.PerformAction(AccessibleActions.Select));
                foreach (var child in row.Controls.Where(c => c.Visible))
                {
                    Assert.InRange(child.Right, 0, row.Width);
                    Assert.InRange(child.Bottom, 0, row.Height);
                }
                var input = Find<TextBox>(view, "LiveUrlInput"); var analyze = Find<Button>(view, "LiveAnalyzeButton");
                Assert.True(input.Right <= input.Parent!.Width); Assert.True(analyze.Right <= input.Parent.Width);
            }
            await services.ShutdownAsync();
        }
        finally { AppTypography.SetScale(100); AppearancePresentation.Current = new(); if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task FailedAnalysisRemainsVisibleAfterTheBusyStateEnds()
    {
        var directory = Path.Combine(Path.GetTempPath(), "mtd-live-analysis-error-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var services = await CreateServicesAsync(directory);
            using (var host = ModernFormsTestHost.Create())
            {
                var view = new LiveView(services); view.SetDashboard(true); host.Show(view, 1200, 900);
                await view.AcceptUrlAsync("not a source URL"); host.ProcessPendingWork();
                var message = Find<Label>(view, "LiveAnalysisSummary");
                Assert.True(message.Visible); Assert.NotEmpty(message.Text);
                Assert.True(message.Bottom <= message.Parent!.Height);
                Assert.True(Find<Button>(view, "LiveAnalyzeButton").Enabled);
                Assert.False(Find<Button>(view, "LiveStartButton").Visible);
            }
            await services.ShutdownAsync();
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task PagingFilteringAndSortingMaterializeOnlyTheVisibleCanonicalSessions()
    {
        var directory = Path.Combine(Path.GetTempPath(), "mtd-live-navigation-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var services = await CreateServicesAsync(directory);
            var sessions = Enumerable.Range(0, 85).Select(i => new LiveSession
            {
                Title = $"Archive {i:000}", SourceUrl = $"https://{(i % 2 == 0 ? "youtube.com" : "twitch.tv")}/live/{i}",
                CreatedAt = DateTimeOffset.UnixEpoch.AddMinutes(i), State = LiveSessionState.Completed
            }).ToArray();
            var persistence = new LiveSessionPersistenceService(AppPaths.Create(directory), new NullAppLogger());
            await persistence.SaveAsync(sessions); await services.Live.LoadAsync();
            using (var host = ModernFormsTestHost.Create())
            {
                services.Appearance.Initialize();
                var view = new LiveView(services); view.SetDashboard(true); host.Show(view, 1536, 1024);
                var recent = Find<FlowLayoutPanel>(view, "LivePartialList");
                Assert.Empty(recent.Controls);
                Find<Button>(view, "DashboardLiveRecent").PerformClick(); host.ProcessPendingWork();
                Assert.Equal(40, recent.Controls.Count);
                Assert.Equal("LiveSession-" + sessions[84].Id.ToString("N"), recent.Controls[0].AccessibleAutomationId);
                var firstCards = recent.Controls.ToArray();
                var disposedCards = 0;
                foreach (var card in firstCards) card.Disposed += (_, _) => disposedCards++;
                Find<Button>(view, "LiveNext").PerformClick(); host.ProcessPendingWork();
                Assert.Equal(40, recent.Controls.Count); Assert.Equal(40, disposedCards);
                Find<Button>(view, "LiveNext").PerformClick(); host.ProcessPendingWork();
                Assert.Equal(5, recent.Controls.Count); Assert.False(Find<Button>(view, "LiveNext").Enabled);
                Find<Button>(view, "LiveSourceYouTube").PerformClick(); host.ProcessPendingWork();
                Assert.Equal(40, recent.Controls.Count);
                Find<TextBox>(view, "LiveSearch").Text = "Archive 084"; host.ProcessPendingWork();
                Assert.Single(recent.Controls); Assert.False(Find<Button>(view, "LivePrevious").Enabled);
                Assert.Equal("LiveSession-" + sessions[84].Id.ToString("N"), recent.Controls[0].AccessibleAutomationId);
                Find<TextBox>(view, "LiveSearch").Text = "";
                Find<ComboBox>(view, "LiveSort").SelectedIndex = 1; host.ProcessPendingWork();
                Assert.Equal("LiveSession-" + sessions[0].Id.ToString("N"), recent.Controls[0].AccessibleAutomationId);
                Assert.Equal(85, services.Live.Snapshot().Count);
            }
            Assert.Equal(sessions.Select(s => s.Id).Order(), (await persistence.LoadAsync()).Select(s => s.Id).Order());
            await services.ShutdownAsync();
        }
        finally { AppearancePresentation.Current = new(); if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task InspectorResolvesCurrentSessionBeforeEditingOrAnalyzingReplayAndClearsActions()
    {
        var directory = Path.Combine(Path.GetTempPath(), "mtd-live-inspector-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var services = await CreateServicesAsync(directory);
            var session = services.Live.Add("https://example.test/live/upcoming", null, "720", LiveStartPolicy.FromNow, true);
            using (var host = ModernFormsTestHost.Create())
            {
                var inspector = new DashboardInspector(services); host.Show(inspector, 360, 900);
                LiveSession? edited = null, analyzed = null;
                inspector.LiveEditRequested += (_, item) => edited = item;
                inspector.LiveAnalyzeRequested += (_, item) => analyzed = item;
                inspector.ShowLive(session);
                Assert.True(services.Live.EditWaiting(session.Id, "1080", LiveStartPolicy.FromStart));
                Find<Button>(inspector, "InspectorLiveAction").PerformClick();
                Assert.Equal("1080", edited!.QualityPresetId); Assert.Equal(session.Id, edited.Id);
                services.Live.Update(session.Id, s => s.State = LiveSessionState.Completed, persist: false);
                inspector.ShowLive(services.Live.Find(session.Id)!);
                Assert.False(Find<Button>(inspector, "InspectorLiveAction").Visible);
                Find<Button>(inspector, "InspectorLiveAnalyze").PerformClick(); Assert.Equal(session.Id, analyzed!.Id);
                inspector.Clear();
                Assert.False(Find<Button>(inspector, "InspectorLiveAnalyze").Visible);
                Assert.Equal(System.Drawing.Rectangle.Empty, Find<Button>(inspector, "InspectorLiveAnalyze").Bounds);
            }
            await services.ShutdownAsync();
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
