using System.Diagnostics;
using ModernFormsNext;
using ModernFormsNext.Testing;
using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Models;
using ModernTubeDownloader.Services;
using ModernTubeDownloader.Views;
using Path = System.IO.Path;

namespace ModernTubeDownloader.Tests;

[Collection("ModernFormsNext TestHost")]
public sealed class HistoryPagingIntegrationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TenThousandRecordsRemainAccessibleWithFortyReusedCardsDuringNavigationSearchAndAppend(bool modern)
    {
        var root = Path.Combine(Path.GetTempPath(), "MTD.History.Paging.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var paths = AppPaths.Create(root); paths.EnsureCreated();
            var settings = new SettingsService(paths, new NullAppLogger()); await settings.LoadAsync();
            settings.Current.Language = "en"; settings.Current.QueuePaused = true; settings.Current.DownloadThumbnail = false;
            settings.Current.UseCustomYtDlp = settings.Current.UseCustomFfmpeg = settings.Current.UseCustomDeno = true;
            settings.Current.CustomYtDlpPath = settings.Current.CustomFfmpegPath = settings.Current.CustomFfprobePath = CoreTests.FakeToolPath();
            var tools = Path.Combine(root, "tools"); Directory.CreateDirectory(tools);
            foreach (var source in Directory.EnumerateFiles(AppContext.BaseDirectory, "ModernTubeDownloader.FakeTool*"))
                File.Copy(source, Path.Combine(tools, Path.GetFileName(source)));
            settings.Current.CustomDenoPath = Path.Combine(tools, "deno.exe"); File.Copy(CoreTests.FakeToolPath(), settings.Current.CustomDenoPath);
            await settings.SaveAsync();
            var entries = Enumerable.Range(0, 10000).Select(i => new DownloadHistoryEntry(Guid.NewGuid(), "Entry " + i,
                "https://fixture.invalid/" + i, "id" + i, "", "1080p", Path.Combine(root, "missing"), DateTimeOffset.UtcNow.AddHours(-i), "Completed")).ToArray();
            await AtomicJsonFile.WriteAsync(paths.HistoryFile, entries);
            using var services = AppServices.Create(paths); await services.Tools.Initialization;
            using (var host = ModernFormsTestHost.Create())
            {
                services.Appearance.Initialize();
                using var view = new HistoryView(services, modern) { Visible = false };
                var list = Find<FlowLayoutPanel>(view, "HistoryList");
                Assert.Empty(list.Controls.OfType<HistoryEntryCard>());
                host.Show(view, 1200, 850); view.Visible = true;
                var summary = Find<Label>(view, "HistoryPageSummary");
                PumpUntil(() => (modern ? summary.AccessibleName : summary.Text)!.Contains("10000 / 10000"));
                var originals = list.Controls.OfType<HistoryEntryCard>().ToArray();
                Assert.Equal(40, originals.Length);
                Assert.True(list.HorizontalScrollProperties.Maximum < list.HorizontalScrollProperties.LargeChange, "History rows overflow the viewport horizontally.");
                Find<Button>(view, modern ? "HistoryNext" : "HistoryNextPage").PerformClick();
                Assert.Equal(originals, list.Controls.OfType<HistoryEntryCard>().ToArray());
                Assert.Contains(entries[40].QueueItemId.ToString("N"), originals[0].AccessibleAutomationId);
                for (var i = 0; i < 20; i++) { view.Visible = false; view.Visible = true; host.ProcessPendingWork(); }
                Assert.Equal(originals, list.Controls.OfType<HistoryEntryCard>().ToArray());
                if (modern)
                {
                    Find<Button>(view, "HistoryLast").PerformClick();
                    Assert.Equal(originals, list.Controls.OfType<HistoryEntryCard>().ToArray());
                    Assert.Equal(entries[9960].QueueItemId, originals[0].ItemId);
                    Assert.Equal(entries[9960].Title, Find<Label>(view, "InspectorTitle").Text);
                    Assert.False(Find<Button>(view, "HistoryLast").Enabled);
                    Find<NumericUpDown>(view, "HistoryJumpPage").Value = 20;
                    Assert.Equal(entries[760].QueueItemId, originals[0].ItemId);
                    Assert.Equal(entries[760].Title, Find<Label>(view, "InspectorTitle").Text);
                    Find<Button>(view, "HistoryFirst").PerformClick();
                    Assert.Equal(entries[0].QueueItemId, originals[0].ItemId);
                    Assert.False(Find<Button>(view, "HistoryFirst").Enabled);
                    Find<Button>(view, "HistoryFilterErrors").PerformClick();
                    PumpUntil(() => summary.AccessibleName!.Contains("Results: 0 / 10000"));
                    Assert.False(Find<NumericUpDown>(view, "HistoryJumpPage").Enabled);
                    Assert.Equal(services.Localization["Dashboard.SelectItem"], Find<Label>(view, "InspectorTitle").Text);
                    Find<Button>(view, "HistoryFilterAll").PerformClick();
                    PumpUntil(() => summary.AccessibleName!.Contains("Results: 10000 / 10000"));
                    Find<Button>(view, "HistoryDateSort").PerformClick();
                    PumpUntil(() => originals[0].ItemId == entries[^1].QueueItemId);
                    Find<Button>(view, "HistoryDateSort").PerformClick();
                    PumpUntil(() => originals[0].ItemId == entries[0].QueueItemId);
                    Find<ComboBox>(view, "HistoryDateRange").SelectedIndex = 1;
                    PumpUntil(() => summary.AccessibleName!.Contains("Results: 168 / 10000"));
                    Find<ComboBox>(view, "HistoryDateRange").SelectedIndex = 0;
                    PumpUntil(() => summary.AccessibleName!.Contains("Results: 10000 / 10000"));
                }
                Find<TextBox>(view, "HistorySearch").Text = "日本語 Needle";
                var appended = entries[0] with { QueueItemId = Guid.NewGuid(), Title = "日本語 Needle", CompletedAt = DateTimeOffset.UtcNow };
                var saving = services.History.AddAsync(appended);
                PumpUntil(() => saving.IsCompleted && (modern ? summary.AccessibleName : summary.Text)!.Contains("Results: 1 / 10001"));
                Assert.True(saving.IsCompletedSuccessfully);
                Assert.Single(list.Controls.OfType<HistoryEntryCard>(), card => card.Visible);
                Assert.Contains(appended.QueueItemId.ToString("N"), originals[0].AccessibleAutomationId);
                Assert.Equal(originals, list.Controls.OfType<HistoryEntryCard>().ToArray());
                var restored = new HistoryService(paths, new NullAppLogger());
                var loading = restored.LoadAsync(); PumpUntil(() => loading.IsCompleted);
                Assert.True(loading.IsCompletedSuccessfully);
                Assert.Equal(10001, restored.Snapshot().Count);
                Assert.Contains(restored.Snapshot(), entry => entry.QueueItemId == appended.QueueItemId);

                void PumpUntil(Func<bool> completed)
                {
                    var deadline = Stopwatch.StartNew();
                    while (!completed() && deadline.Elapsed < TimeSpan.FromSeconds(5))
                    { host.ProcessPendingWork(); host.Clock.Advance(TimeSpan.FromMilliseconds(5)); Thread.Sleep(5); }
                    Assert.True(completed(), "History did not converge within five seconds.");
                }
            }
            await services.ShutdownAsync();
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    private static T Find<T>(Control root, string id) where T : Control
        => Descendants(root).OfType<T>().Single(control => control.AccessibleAutomationId == id);
    private static IEnumerable<Control> Descendants(Control root)
    {
        yield return root;
        foreach (var child in root.Controls)
            foreach (var descendant in Descendants(child)) yield return descendant;
    }
}
