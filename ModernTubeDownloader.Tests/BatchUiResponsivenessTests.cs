using ModernFormsNext;
using ModernFormsNext.Testing;
using ModernTubeDownloader.Batch;
using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Models;
using ModernTubeDownloader.Services;
using ModernTubeDownloader.Settings;
using ModernTubeDownloader.Theming;
using ModernTubeDownloader.Views;
using Path = System.IO.Path;

namespace ModernTubeDownloader.Tests;

[Collection("ModernFormsNext TestHost")]
public sealed class BatchUiResponsivenessTests
{
    [Theory]
    [InlineData("pl", AppThemeMode.Dark)]
    [InlineData("en", AppThemeMode.Light)]
    public async Task ThirteenHundredJobsUseBoundedCardsAndGlobalProgressAcrossPages(string language, AppThemeMode theme)
    {
        var root = Path.Combine(Path.GetTempPath(), "mtd-batch-ui-" + Guid.NewGuid().ToString("N"));
        try
        {
            var paths = AppPaths.Create(root); paths.EnsureCreated();
            var settings = new SettingsService(paths, new NullAppLogger()); await settings.LoadAsync();
            settings.Current.Language = language; settings.Current.ThemeMode = theme; settings.Current.QueuePaused = true;
            settings.Current.UseCustomYtDlp = settings.Current.UseCustomFfmpeg = settings.Current.UseCustomDeno = true;
            settings.Current.CustomYtDlpPath = settings.Current.CustomFfmpegPath = settings.Current.CustomFfprobePath = CoreTests.FakeToolPath();
            var deno = Path.Combine(root, "deno"); Directory.CreateDirectory(deno);
            foreach (var source in Directory.EnumerateFiles(AppContext.BaseDirectory, "ModernTubeDownloader.FakeTool*"))
                File.Copy(source, Path.Combine(deno, Path.GetFileName(source)));
            settings.Current.CustomDenoPath = Path.Combine(deno, "deno.exe"); File.Copy(CoreTests.FakeToolPath(), settings.Current.CustomDenoPath);
            await settings.SaveAsync();
            using var services = AppServices.Create(paths); await services.Tools.Initialization;
            var items = Enumerable.Range(0, 1300).Select(i => new DownloadQueueItem { VideoId = "row-" + i, Title = "Żółć 世界 " + i,
                EstimatedSizeBytes = 1000, Status = i < 7 ? DownloadStatus.Completed : DownloadStatus.Queued }).ToArray();
            services.Queue.AddRangeSkippingDuplicates(items);
            using (var host = ModernFormsTestHost.Create())
            {
                services.Appearance.Initialize();
                var view = new DownloadsView(services); host.Show(view, 1100, 820); host.ProcessPendingWork();
                var list = Find<FlowLayoutPanel>(view, "QueueList");
                Assert.Equal(50, list.Controls.OfType<QueueItemCard>().Count());
                Assert.Contains("7/1300", Find<Label>(view, "QueueOverallSummary").Text);
                view.RevealQueueItem(items[^1].Id); host.ProcessPendingWork();
                Assert.Equal(50, list.Controls.OfType<QueueItemCard>().Count());
                Assert.Contains(list.Controls.OfType<QueueItemCard>(), card => card.AccessibleAutomationId == $"QueueItem-{items[^1].Id:N}");
                Assert.False(Find<Button>(view, "QueuePageNext").Enabled);
                Find<Button>(view, "QueuePagePrevious").PerformClick(); host.ProcessPendingWork();
                Assert.Equal(50, list.Controls.OfType<QueueItemCard>().Count());
                Assert.True(Find<Button>(view, "QueuePageNext").Enabled);
                foreach (var scale in new[] { 100, 125, 150, 200, 100 })
                {
                    AppTypography.SetScale(scale); view.Width = 1000; host.ProcessPendingWork();
                    Assert.True(Find<Label>(view, "QueueOverallSummary").Height >= AppTypography.TextHeight(44));
                    Assert.Equal(50, list.Controls.OfType<QueueItemCard>().Count());
                }
                using var batch = new BatchImportForm(services);
                Assert.Equal(1, Find<NumericUpDown>(batch.Controls[0], "BatchWorkers").Value);
                Assert.NotNull(Find<ProgressBar>(batch.Controls[0], "BatchDownloadProgress"));
                Assert.DoesNotContain("[Batch.", Find<Label>(batch.Controls[0], "BatchOverallSummary").Text);
            }
            await services.ShutdownAsync();
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    private static T Find<T>(Control control, string id) where T : Control
    {
        if (control is T value && value.AccessibleAutomationId == id) return value;
        foreach (var child in control.Controls)
        {
            try { return Find<T>(child, id); }
            catch (InvalidOperationException) { }
        }
        throw new InvalidOperationException("Control missing: " + id);
    }
}
