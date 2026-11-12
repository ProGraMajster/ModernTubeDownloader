using System.Text.Json;
using ModernFormsNext;
using ModernFormsNext.Testing;
using ModernTubeDownloader.Batch;
using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Models;
using ModernTubeDownloader.Services;
using ModernTubeDownloader.Theming;
using ModernTubeDownloader.Views;
using Path = System.IO.Path;

namespace ModernTubeDownloader.Tests;

[Collection("ModernFormsNext TestHost")]
public sealed partial class BatchDashboardPresentationTests
{
    [Fact]
    public async Task ClipboardRendersCanonicalRowsAndOtherPickerDispatchesOnce()
    {
        var directory = Path.Combine(Path.GetTempPath(), "mtd-batch-clipboard-view-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var services = await CreateServicesAsync(directory);
            using (var host = ModernFormsTestHost.Create())
            {
                services.Appearance.Initialize();
                var view = new BatchImportView(services); view.SetDashboard(true); host.Show(view, 1320, 1024);
                Assert.Equal(services.Localization["Batch.NoFile"], Find<Label>(view, "BatchSourceFileName").Text);
                Assert.Equal(services.Localization["Batch.Empty"], Find<Label>(view, "BatchStatus").Text);
                Assert.Equal(services.Localization.Get("Dashboard.ImportSummaryFacts", 0, 0, 0, 0),
                    Find<Label>(view, "BatchImportSummaryFacts").Text);
                Assert.DoesNotContain(services.Localization["Batch.Empty"], Find<Label>(view, "BatchParserDetails").Text);
                Assert.Equal(services.Localization.Get("Batch.Overall", 0, 0, 0, 0,
                    0d.ToString("N2", services.Localization.Culture), 0, "—"), Find<Label>(view, "BatchOverallSummary").Text);
                view.SetDashboard(false);
                Assert.Equal(services.Localization["Batch.Empty"], Find<Label>(view, "BatchOverallSummary").Text);
                view.SetDashboard(true);
                Task import = Task.CompletedTask;
                host.Dispatcher.Post(() => import = view.ImportClipboardTextAsync("https://example.test/a\nhttps://example.test/a\ninvalid"));
                host.ProcessPendingWork();
                var deadline = DateTime.UtcNow.AddSeconds(5);
                while (!import.IsCompleted && DateTime.UtcNow < deadline) { host.ProcessPendingWork(); Thread.Sleep(5); }
                Assert.True(import.IsCompleted); await import; host.ProcessPendingWork();
                Assert.Equal(3, Find<FlowLayoutPanel>(view, "BatchRows").Controls.Count);
                Assert.Equal("", Find<TextBox>(view, "BatchFilePath").Text);
                Assert.True(Find<Button>(view, "BatchAnalyze").Enabled);
                Assert.Empty(services.Queue.Snapshot());
                view.SetDashboard(false); host.ProcessPendingWork();
                view.SetDashboard(true); host.ProcessPendingWork();
                Assert.Equal(3, Find<FlowLayoutPanel>(view, "BatchRows").Controls.Count);
                Assert.True(Find<Button>(view, "BatchAnalyze").Enabled);
                view.SetDashboard(false); Assert.False(Find<Button>(view, "BatchPaste").Visible);
                using var other = new OtherView(services); var opened = 0;
                other.RegisterTool("Batch", () => { }); other.SetDashboard(true); other.Size = new(1320, 1024);
                other.PickBatchFileRequested += (_, _) => opened++;
                var pick = Find<Button>(other, "OtherPickBatchFile"); pick.PerformClick();
                Assert.Equal(1, opened); Assert.True(pick.Width > 0 && pick.Right <= pick.Parent!.Width);
                other.SetDashboard(false); Assert.False(pick.Parent!.Visible);
            }
            var recent = await services.Batch.RecentAsync(); Assert.Single(recent); Assert.Equal(3, recent[0].ImportedCount);
            await services.ShutdownAsync();
        }
        finally { AppearancePresentation.Current = new(); if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData(1320, 100)]
    [InlineData(1060, 150)]
    [InlineData(1700, 200)]
    public async Task TableAndOptionsReflowWithoutChangingSessionOrClassicControls(int width, int scale)
    {
        var directory = Path.Combine(Path.GetTempPath(), "mtd-batch-presentation-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var services = await CreateServicesAsync(directory);
            var session = new BatchImportSession { FileName = "isolated-qa.txt", ImportedCount = 3 };
            foreach (var status in new[] { BatchEntryStatus.Ready, BatchEntryStatus.Duplicate, BatchEntryStatus.Failed })
            {
                var entry = new BatchImportEntry { Ordinal = session.Entries.Count + 1, Status = status,
                    Title = "Łódź 日本語 " + new string('界', 100), Source = "example.test", Url = "https://example.test/" + status,
                    ErrorKey = status == BatchEntryStatus.Failed ? "Batch.Error.Analysis" : null };
                session.Entries.Add(entry); session.ById.Add(entry.Id, entry);
            }
            var before = JsonSerializer.Serialize(session.Snapshot());
            using (var host = ModernFormsTestHost.Create())
            {
                services.Appearance.Initialize(); AppTypography.SetScale(scale);
                var view = new BatchImportView(services, session); host.Show(view, width, 1024); host.ProcessPendingWork();
                var path = Find<TextBox>(view, "BatchFilePath"); var quality = Find<ComboBox>(view, "BatchQuality");
                var previous = Find<Button>(view, "BatchPrevious"); var apply = Find<Button>(view, "BatchApply");
                var classicBody = path.Parent; var classicToolbar = previous.Parent;
                AppTypography.SetDashboardPresentation(view, true); view.SetDashboard(true); host.ProcessPendingWork();
                Assert.True(Find<Button>(view, "BatchPaste").Visible);
                Assert.True(Find<Button>(view, "BatchPaste").Enabled);
                var pageHeading = Find<Label>(view, "BatchTitle");
                Assert.Equal(services.Localization["Dashboard.Batch"], pageHeading.Text);
                Assert.True(pageHeading.Visible && pageHeading.Height >= DashboardVisual.LabelHeight(pageHeading, pageHeading.Width, 1));
                foreach (var action in new[] { "Add", "Analyze", "Pause", "Cancel", "Export" })
                {
                    var button = Find<Button>(view, "Batch" + action); var owner = button.Parent!;
                    Assert.True(button.Visible && button.Width > 0 && button.Height > 0);
                    Assert.True(button.Left >= 0 && button.Top >= 0 && button.Right <= owner.Width && button.Bottom <= owner.Height,
                        $"Batch {action} is clipped by its toolbar at {scale}%.");
                }
                Assert.True(Find<Label>(view, "BatchColumnTitle").CurrentStyle.GetFontSize() >= 12 * scale / 100);
                var list = Find<FlowLayoutPanel>(view, "BatchRows"); Assert.Equal(3, list.Controls.Count);
                foreach (var row in list.Controls)
                    Assert.All(row.Controls.Where(c => c.Visible), c =>
                    {
                        Assert.True(c.Left >= 0 && c.Top >= 0 && c.Width > 0 && c.Height > 0);
                        Assert.True(c.Right <= row.Width && c.Bottom <= row.Height, $"{c.AccessibleAutomationId} escapes its row at {scale}%.");
                    });
                var states = Enumerable.Range(1, 3).Select(i => Find<Label>(view, "BatchEntryStatus" + i)).ToArray();
                // AppUi uses theme resources on TextBrush; the fallback style colour is intentionally unset.
                var statusColors = states.Select(s => Assert.IsType<ModernFormsNext.Drawing.SolidColorBrush>(s.TextBrush).Color).ToArray();
                Assert.Equal(3, statusColors.Distinct().Count());
                Assert.Equal(new[] { AppUi.GetColor(AppThemeTokens.Success), AppUi.GetColor(AppThemeTokens.Warning), AppUi.GetColor(AppThemeTokens.Error) }, statusColors);
                Assert.Contains(services.Localization["Batch.Error.Analysis"], states[2].AccessibleName);
                if (scale == 100)
                {
                    var header = Find<Panel>(view, "BatchTableHeader"); Assert.True(header.Top < 470);
                    foreach (var column in new[] { "Url", "Title", "Status", "Quality" })
                        Assert.Equal(Find<Label>(view, "BatchColumn" + column).Left, Find<Label>(view, "BatchEntry" + column + "1").Left);
                    Assert.All(list.Controls, row =>
                    {
                        Assert.Equal(32, row.Height);
                        var selectionControl = Assert.Single(row.Controls.OfType<CheckBox>());
                        Assert.True(selectionControl.Visible && selectionControl.Width > 0 && selectionControl.Height > 0);
                        var rowActions = row.Controls.OfType<Button>().ToArray();
                        Assert.Equal(4, rowActions.Length);
                        foreach (var action in rowActions)
                        {
                            Assert.True(action.Visible);
                            Assert.False(string.IsNullOrWhiteSpace(action.AccessibleName));
                            Assert.False(string.IsNullOrWhiteSpace(action.AccessibleAutomationId));
                            Assert.Equal(26, action.Width); Assert.Equal(26, action.Height);
                            Assert.True(action.Left >= selectionControl.Right && action.Bottom <= row.Height && action.Right <= row.Width);
                        }
                    });
                }
                Assert.False(quality.Parent!.Visible);
                var collapsedContentHeight = classicBody!.DisplayRectangle.Height;
                Find<Button>(view, "BatchOptions").PerformClick(); host.ProcessPendingWork();
                Assert.True(quality.Parent.Visible); Assert.Same(quality.Parent, apply.Parent!.Parent);
                Assert.True(quality.Right <= quality.Parent.Width && quality.Bottom <= quality.Parent.Height);
                Assert.True(apply.Right <= apply.Parent.Width && apply.Bottom <= apply.Parent.Height);
                var mode = Find<ComboBox>(view, "BatchRangeMode");
                var range = mode.Parent!; var fullHeight = range.Height;
                mode.SelectedIndex = 1; host.ProcessPendingWork();
                Assert.All(range.Controls.Where(c => c.Visible), c =>
                {
                    Assert.True(c.Right <= range.Width && c.Bottom <= range.Height);
                    Assert.True(c.Height > 0 && c.Width > 0);
                });
                Assert.True(apply.Parent.Top >= range.Bottom);
                mode.SelectedIndex = 0; host.ProcessPendingWork();
                Assert.Equal(fullHeight, range.Height);
                Assert.Equal(System.Drawing.Rectangle.Empty, Find<TextBox>(view, "BatchRangeStart").Bounds);
                Assert.Equal(System.Drawing.Rectangle.Empty, Find<TextBox>(view, "BatchRangeEnd").Bounds);
                Find<Button>(view, "BatchOptions").PerformClick(); host.ProcessPendingWork();
                Assert.False(quality.Parent.Visible);
                Assert.Equal(collapsedContentHeight, classicBody.DisplayRectangle.Height);
                view.SetDashboard(false); AppTypography.SetDashboardPresentation(view, false); host.ProcessPendingWork();
                Assert.Same(classicBody, path.Parent); Assert.Same(classicBody, quality.Parent);
                Assert.Same(classicToolbar, previous.Parent); Assert.Same(classicToolbar, apply.Parent);
                Assert.False(Find<Panel>(view, "BatchTableHeader").Visible);
                Assert.Equal(services.Localization["Batch.Apply"], apply.Text);
                Assert.Equal(before, JsonSerializer.Serialize(session.Snapshot()));
            }
            await services.ShutdownAsync();
        }
        finally { AppTypography.SetScale(100); AppearancePresentation.Current = new(); if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    private static async Task<AppServices> CreateServicesAsync(string directory)
    {
        var paths = AppPaths.Create(directory); paths.EnsureCreated();
        var settings = new SettingsService(paths, new NullAppLogger()); await settings.LoadAsync();
        settings.Current.QueuePaused = true; settings.Current.EnableWebRemote = false; settings.Current.Startup.ShowTrayIcon = false;
        settings.Current.Updates.AutomaticChecks = false; settings.Current.FinalOutputDirectory = Path.Combine(directory, "output"); settings.Current.TemporaryDirectory = Path.Combine(directory, "temp");
        settings.Current.UseCustomYtDlp = settings.Current.UseCustomFfmpeg = settings.Current.UseCustomDeno = true;
        settings.Current.CustomYtDlpPath = settings.Current.CustomFfmpegPath = settings.Current.CustomFfprobePath = CoreTests.FakeToolPath();
        var tools = Path.Combine(directory, "fake"); Directory.CreateDirectory(tools);
        foreach (var file in Directory.EnumerateFiles(AppContext.BaseDirectory, "ModernTubeDownloader.FakeTool.*")) File.Copy(file, Path.Combine(tools, Path.GetFileName(file)));
        settings.Current.CustomDenoPath = Path.Combine(tools, "deno.exe"); File.Copy(CoreTests.FakeToolPath(), settings.Current.CustomDenoPath);
        await settings.SaveAsync(); var services = AppServices.Create(paths); await services.Tools.Initialization; return services;
    }
    private static T Find<T>(Control parent, string id) where T : Control
    {
        foreach (var child in parent.Controls)
        {
            if (child.AccessibleAutomationId == id && child is T result) return result;
            try { return Find<T>(child, id); } catch (KeyNotFoundException) { }
        }
        throw new KeyNotFoundException(id);
    }
}
