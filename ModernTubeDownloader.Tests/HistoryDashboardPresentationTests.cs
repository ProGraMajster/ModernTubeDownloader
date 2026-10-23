using System.Text.Json;
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

[Collection("ModernFormsNext TestHost")]
public sealed class HistoryDashboardPresentationTests
{
    [Theory]
    [InlineData(100, 1200)]
    [InlineData(150, 1600)]
    [InlineData(200, 1200)]
    public void TitleUsesOnlyNeededLinesAndClassicRecoveryRestoresTheCard(int scale, int width)
    {
        var directory = Path.Combine(Path.GetTempPath(), "mtd-history-row-" + Guid.NewGuid().ToString("N"));
        try
        {
            var paths = AppPaths.Create(directory); paths.EnsureCreated();
            using var thumbnails = new ThumbnailCacheService(paths, new NullAppLogger(), () => false);
            using var host = ModernFormsTestHost.Create();
            AppTypography.SetScale(scale);
            var localization = new LocalizationService("en");
            var entry = new DownloadHistoryEntry(Guid.NewGuid(), "Short title", "https://example.test/video", "video", "Channel", "1080p", "video.mp4", DateTimeOffset.UtcNow, "Completed");
            var row = new HistoryEntryCard(entry, localization, thumbnails) { Width = width };
            var title = row.Controls.OfType<Label>().Single(label => label.Text == entry.Title);
            var facts = row.Controls.OfType<Label>().Single(label => label.Text.Contains(entry.Channel, StringComparison.Ordinal));
            var classicTitleFont = title.Font; var classicFactsFont = facts.Font; var classicFacts = facts.Text;
            var classicMargin = row.Margin;
            var root = new Panel(); root.Controls.Add(row); host.Show(root, width, 600);

            row.SetDashboard(false, false);
            Assert.Equal(classicTitleFont, title.Font); Assert.Equal(classicFactsFont, facts.Font);
            row.SetDashboard(true, false);
            var shortTitleHeight = title.Height;
            Assert.InRange(facts.Top - title.Bottom, 1, 4);
            Assert.True(facts.Bottom < row.Height);
            var placeholder = Find<AppVectorIcon>(row, "HistoryThumbnailPlaceholder");
            Assert.True(placeholder.Visible);
            Assert.Equal(24, placeholder.Width); Assert.Equal(24, placeholder.Height);
            Assert.Equal(placeholder.Parent!.Width / 2, placeholder.Left + placeholder.Width / 2);
            Assert.Equal(placeholder.Parent.Height / 2, placeholder.Top + placeholder.Height / 2);
            Assert.Equal(AccessibleControlType.ListItem, row.AccessibilityObject.ControlType);
            var selected = 0; row.SelectionRequested += (_, selectedEntry) => { Assert.Equal(entry.QueueItemId, selectedEntry.QueueItemId); selected++; };
            Assert.True(row.AccessibilityObject.PerformAction(AccessibleActions.Select)); Assert.Equal(1, selected);

            // This title needs multiple lines at both table and compact widths.
            title.Text = string.Join(" ", Enumerable.Repeat("A longer Unicode title Łódź 日本語", 12));
            row.SetDashboard(true, true);
            Assert.True(title.Height > shortTitleHeight);
            Assert.InRange(facts.Top - title.Bottom, 1, 4);
            Assert.True(facts.Bottom < row.Height);
            Assert.True(row.AccessibilityObject.State.HasFlag(AccessibleStates.Selected));
            Assert.All(row.Controls.Where(control => control.Visible), control =>
            {
                Assert.True(control.Left >= 0 && control.Top >= 0);
                Assert.True(control.Right <= row.Width && control.Bottom <= row.Height);
            });

            row.SetDashboard(false, false);
            Assert.Equal(classicTitleFont, title.Font); Assert.Equal(classicFactsFont, facts.Font);
            Assert.Equal(classicFacts, facts.Text); Assert.Equal(classicMargin, row.Margin);
            Assert.False(title.Multiline); Assert.False(title.AutoEllipsis); Assert.False(facts.Multiline);
            Assert.False(placeholder.Visible);
            Assert.All(row.Controls.Where(control => control.AccessibleAutomationId?.StartsWith("HistoryCell", StringComparison.Ordinal) == true), control => Assert.False(control.Visible));
            Assert.All(row.Controls.OfType<AppVectorIcon>(), icon => Assert.False(icon.Visible));
            Assert.All(row.Controls.OfType<Button>().SelectMany(button => button.Controls.OfType<AppVectorIcon>()), icon => Assert.False(icon.Visible));
            Assert.Contains(row.Controls.OfType<Button>(), button => button.Text == localization["Common.OpenFolder"] && button.Visible);
        }
        finally
        {
            AppTypography.SetScale(100); AppearancePresentation.Current = new();
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task InspectorAndColumnsAlignWithTheTableAndHiddenChromeDoesNotExtendClassic()
    {
        var directory = Path.Combine(Path.GetTempPath(), "mtd-history-columns-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var services = await CreateServicesAsync(directory);
            for (var i = 0; i < 16; i++)
                await services.History.AddAsync(new DownloadHistoryEntry(Guid.NewGuid(), "Recorded item " + i, "https://example.test/" + i, "id-" + i, "Channel", "1080p", "video-" + i + ".mp4", DateTimeOffset.UtcNow.AddMinutes(-i), "Completed"));
            var before = JsonSerializer.Serialize(services.History.Snapshot());
            using (var host = ModernFormsTestHost.Create())
            {
                services.Appearance.Initialize();
                var view = new HistoryView(services);
                var search = Find<TextBox>(view, "HistorySearch"); var classicPadding = search.Padding;
                view.SetDashboard(true); host.Show(view, 1536, 1024);
                var header = Find<Panel>(view, "HistoryTableHeader");
                var inspector = Find<DashboardInspector>(view, "DashboardInspector");
                var list = Find<FlowLayoutPanel>(view, "HistoryList");
                var row = list.Controls.OfType<HistoryEntryCard>().First();
                Assert.Equal(header.Top, inspector.Top);
                Assert.Equal(header.Bottom, list.Top);
                var sourceHeading = header.Controls.OfType<Label>().Single(label => Equals(label.Tag, "Dashboard.Source"));
                var source = Find<Label>(row, "HistoryCellSource");
                Assert.Equal(header.Left + sourceHeading.Left, list.Left + row.Left + source.Left);
                var dateHeading = Find<Button>(view, "HistoryDateSort");
                var date = Find<Label>(row, "HistoryCellDate");
                Assert.Equal(header.Left + dateHeading.Left, list.Left + row.Left + date.Left);
                var searchIcon = Assert.Single(search.Parent!.Controls.OfType<AppVectorIcon>());
                Assert.True(searchIcon.Visible); Assert.True(search.Padding.Left > searchIcon.Right);

                AppearancePresentation.Current.ModernDashboardShell.Inspector = DashboardInspectorMode.Hidden;
                view.Width--; view.Width++; host.ProcessPendingWork();
                Assert.False(inspector.Visible); Assert.Equal(0, inspector.Width); Assert.Equal(0, inspector.Height);

                view.SetDashboard(false);
                Assert.Equal(classicPadding, search.Padding); Assert.False(searchIcon.Visible);
                Assert.False(header.Visible); Assert.Equal(0, header.Width); Assert.Equal(0, header.Height);
                Assert.Equal(16, list.Controls.OfType<HistoryEntryCard>().Count());
                Assert.Equal(before, JsonSerializer.Serialize(services.History.Snapshot()));
            }
            await services.ShutdownAsync();
        }
        finally
        {
            AppTypography.SetScale(100); AppearancePresentation.Current = new();
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    private static T Find<T>(Control root, string id) where T : Control
        => Descendants(root).OfType<T>().Single(control => control.AccessibleAutomationId == id);

    private static IEnumerable<Control> Descendants(Control root)
        => root.Controls.SelectMany(child => new[] { child }.Concat(Descendants(child)));

    private static async Task<AppServices> CreateServicesAsync(string directory)
    {
        var paths = AppPaths.Create(directory); paths.EnsureCreated();
        var settings = new SettingsService(paths, new NullAppLogger()); await settings.LoadAsync();
        settings.Current.QueuePaused = true; settings.Current.EnableWebRemote = false; settings.Current.Startup.ShowTrayIcon = false;
        settings.Current.Updates.AutomaticChecks = false;
        settings.Current.FinalOutputDirectory = Path.Combine(directory, "output"); settings.Current.TemporaryDirectory = Path.Combine(directory, "temp");
        settings.Current.UseCustomYtDlp = settings.Current.UseCustomFfmpeg = settings.Current.UseCustomDeno = true;
        settings.Current.CustomYtDlpPath = settings.Current.CustomFfmpegPath = settings.Current.CustomFfprobePath = CoreTests.FakeToolPath();
        var toolsDirectory = Path.Combine(directory, "fake"); Directory.CreateDirectory(toolsDirectory);
        foreach (var file in Directory.EnumerateFiles(AppContext.BaseDirectory, "ModernTubeDownloader.FakeTool.*")) File.Copy(file, Path.Combine(toolsDirectory, Path.GetFileName(file)));
        settings.Current.CustomDenoPath = Path.Combine(toolsDirectory, "deno.exe"); File.Copy(CoreTests.FakeToolPath(), settings.Current.CustomDenoPath);
        await settings.SaveAsync(); var services = AppServices.Create(paths); await services.Tools.Initialization; return services;
    }
}
