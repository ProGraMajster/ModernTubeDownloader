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
public sealed class BatchDashboardPresentationTests
{
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
                    Assert.All(list.Controls, row => Assert.InRange(row.Height, 40, 60));
                }
                Assert.False(quality.Parent!.Visible);
                var collapsedContentHeight = classicBody!.DisplayRectangle.Height;
                Find<Button>(view, "BatchOptions").PerformClick(); host.ProcessPendingWork();
                Assert.True(quality.Parent.Visible); Assert.Same(quality.Parent, apply.Parent!.Parent);
                Assert.True(quality.Right <= quality.Parent.Width && quality.Bottom <= quality.Parent.Height);
                Assert.True(apply.Right <= apply.Parent.Width && apply.Bottom <= apply.Parent.Height);
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
