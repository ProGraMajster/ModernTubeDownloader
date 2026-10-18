using System.Text.Json;
using ModernFormsNext;
using ModernFormsNext.Testing;
using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Localization;
using ModernTubeDownloader.Models;
using ModernTubeDownloader.Services;
using ModernTubeDownloader.Shell;
using ModernTubeDownloader.Theming;
using ModernTubeDownloader.Views;
using Path = System.IO.Path;

namespace ModernTubeDownloader.Tests;

[Collection("ModernFormsNext TestHost")]
public sealed class DashboardShellTests
{
    [Theory]
    [InlineData("{}")] [InlineData("{\"schemaVersion\":1}")]
    [InlineData("{\"schemaVersion\":1,\"typography\":{\"appTextScale\":150}}")]
    public void ExistingProfilesMigrateToClassic(string json)
    {
        var p = AppearanceProfileStore.Validate(json); Assert.Equal(AppInterfaceMode.Classic, p.InterfaceMode); Assert.Equal(2, p.SchemaVersion);
        Assert.NotNull(p.ModernDashboardShell);
    }
    [Theory]
    [InlineData(AppearanceTheme.Dark)] [InlineData(AppearanceTheme.Light)]
    public void DashboardPresetUsesReadableActionTextAndRecoveryDoesNotPersistClassic(AppearanceTheme theme)
    {
        using var test = ModernFormsTestHost.Create();
        var settings = new SettingsService(AppPaths.Create(Path.GetTempPath()), new NullAppLogger());
        using var appearance = new AppAppearanceService(settings, new NullAppLogger());
        var p = AppearanceProfile.Preset("ModernDashboard"); p.Global.Theme = theme;
        Assert.True(appearance.ApplyProfileAsync(p).IsCompletedSuccessfully); appearance.Initialize();
        foreach (var token in new[] { AppThemeTokens.Accent, AppThemeTokens.AccentHover, AppThemeTokens.AccentPressed })
        {
            var fg = AppUi.GetColor(AppThemeTokens.AccentText); var bg = AppUi.GetColor(token);
            Assert.True(AppAppearanceService.ColorContrast(System.Drawing.Color.FromArgb(fg.Red, fg.Green, fg.Blue), System.Drawing.Color.FromArgb(bg.Red, bg.Green, bg.Blue)) >= 4.5);
        }
        appearance.ForceClassicForSession(); Assert.Equal(AppInterfaceMode.Classic, appearance.InterfaceMode);
        Assert.Equal(AppInterfaceMode.ModernDashboard, appearance.Profile.InterfaceMode);
        AppearancePresentation.Current = new();
    }
    [Theory]
    [InlineData("{\"interfaceMode\":123}")]
    [InlineData("{\"modernDashboardShell\":null}")]
    [InlineData("{\"modernDashboardShell\":{\"sidebar\":123}}")]
    public void UnknownShellConfigurationIsRejected(string json) => Assert.ThrowsAny<Exception>(() => AppearanceProfileStore.Validate(json));

    [Fact]
    public async Task InterfaceAndDashboardPreferencesSurviveRestartAndResetIndependently()
    {
        var directory = Path.Combine(Path.GetTempPath(), "mtd-shell-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try
        {
            var store = new AppearanceProfileStore(Path.Combine(directory, "appearance.json"));
            var p = new AppearanceProfile { InterfaceMode = AppInterfaceMode.ModernDashboard };
            p.ModernDashboardShell.Sidebar = DashboardSidebarMode.Compact; p.Typography.AppTextScale = 175;
            await store.SaveAsync(p); var restored = await store.LoadAsync(new NullAppLogger());
            Assert.Equal(AppInterfaceMode.ModernDashboard, restored.InterfaceMode); Assert.Equal(DashboardSidebarMode.Compact, restored.ModernDashboardShell.Sidebar);
            restored.ResetSection("ModernDashboardShell"); Assert.Equal(DashboardSidebarMode.Auto, restored.ModernDashboardShell.Sidebar);
            Assert.Equal(175, restored.Typography.AppTextScale); Assert.Equal(AppInterfaceMode.ModernDashboard, restored.InterfaceMode);
            Assert.Equal(AppInterfaceMode.Classic, new AppearanceProfile().InterfaceMode);
        }
        finally { Directory.Delete(directory, true); }
    }
    [Fact]
    public void TwentySwitchesKeepTheSamePagesDisposeChromeAndDispatchOnce()
    {
        using var test = ModernFormsTestHost.Create();
        using var pages = new Panel(); var page = new Panel { AccessibleAutomationId = "SharedDownloads" }; pages.Controls.Add(page);
        using var host = new AppShellHost(pages, new("en"), new NullAppLogger()); test.Show(host, 1536, 960);
        var routed = new List<AppPageId>(); host.Navigate += (_, target) => routed.Add(target);
        host.Select(AppPageId.History);
        for (var i = 0; i < 20; i++)
        {
            host.Apply(AppInterfaceMode.ModernDashboard); test.ProcessPendingWork();
            Assert.Equal(AppPageId.History, host.CurrentPage); Assert.Same(page, pages.Controls.Single());
            var oldRoot = host.Controls.Single(); var disposed = false; oldRoot.Disposed += (_, _) => disposed = true;
            Find<Button>(host, "NavLive").PerformClick(); Assert.Equal(i + 1, routed.Count);
            host.Apply(AppInterfaceMode.Classic); test.ProcessPendingWork();
            Assert.True(disposed); Assert.Same(page, pages.Controls.Single()); Assert.Equal(AppInterfaceMode.Classic, host.Mode);
            Assert.Single(host.Controls);
        }
        Assert.All(routed, page => Assert.Equal(AppPageId.Live, page));
    }
    [Fact]
    public void ConstructorFailureFallsBackWithoutLosingTheCurrentPage()
    {
        using var test = ModernFormsTestHost.Create(); using var pages = new Panel();
        using var host = new AppShellHost(pages, new("pl"), new NullAppLogger(), (mode, navigate) =>
            mode == AppInterfaceMode.ModernDashboard ? throw new InvalidOperationException("construction fixture") : new ClassicAppShell(new("pl"), navigate));
        var fallbacks = 0; host.Fallback += (_, _) => fallbacks++; host.Select(AppPageId.Live); host.Apply(AppInterfaceMode.ModernDashboard);
        Assert.Equal(1, fallbacks); Assert.Equal(AppInterfaceMode.Classic, host.Mode); Assert.Equal(AppPageId.Live, host.CurrentPage); Assert.NotNull(pages.Parent);
    }
    [Theory]
    [InlineData(1280, 720, 100)] [InlineData(1366, 768, 100)] [InlineData(1600, 900, 100)]
    [InlineData(1920, 1080, 150)] [InlineData(2560, 1440, 200)] [InlineData(2276, 1280, 100)]
    public void ChromeReflowsAtLogicalSizesWithoutChangingTextScaleOrDpi(int width, int height, int scale)
    {
        using var test = ModernFormsTestHost.Create(); using var pages = new Panel();
        try
        {
            AppTypography.SetScale(scale); using var host = new AppShellHost(pages, new("pl"), new NullAppLogger());
            host.Apply(AppInterfaceMode.ModernDashboard); test.Show(host, width, height); test.ProcessPendingWork();
            var navigation = Find<Button>(host, "NavDownloads"); var settings = Find<Button>(host, "NavSettings");
            Assert.True(navigation.Width >= 40); Assert.True(navigation.Bounds.Y >= 0); Assert.True(settings.Bottom <= height);
            Assert.True(pages.Width >= 520); Assert.Equal(scale, AppTypography.TextScale);
        }
        finally { AppTypography.SetScale(100); }
    }
    [Theory]
    [InlineData("pl", 100)] [InlineData("en", 150)] [InlineData("pl", 200)]
    public void QueueRowsHaveBoundedHeightLongTextAndNamedActions(string language, int scale)
    {
        using var test = ModernFormsTestHost.Create(); var directory = Path.Combine(Path.GetTempPath(), "mtd-row-" + Guid.NewGuid().ToString("N"));
        try
        {
            AppTypography.SetScale(scale); var paths = AppPaths.Create(directory); paths.EnsureCreated();
            using var thumbnails = new ThumbnailCacheService(paths, new NullAppLogger(), () => false);
            var row = new QueueItemCard(thumbnails, new(language)) { Width = 960 }; row.SetDashboard(true);
            row.UpdateItem(new DownloadQueueItem { Title = new string('界', 150) + " Łódź 日本語", Status = DownloadStatus.Queued, SourceUrl = "https://example.test/video" }, false);
            test.Show(row, 960, row.Height); test.ProcessPendingWork();
            Assert.InRange(row.Height, 84, 260); Assert.All(row.Controls.Where(c => c.Visible), c => { Assert.True(c.Left >= 0); Assert.True(c.Width >= 0); Assert.True(c.Right <= row.Width); Assert.True(c.Bottom <= row.Height, $"{c.GetType().Name} extends below its row."); });
            Assert.True((row.AccessibilityObject.SupportedActions & ModernFormsNext.Accessibility.AccessibleActions.Select) != 0);
            var primary = row.Controls.OfType<Button>().First(); Assert.False(string.IsNullOrWhiteSpace(primary.AccessibleName));
        }
        finally { AppTypography.SetScale(100); if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
    [Fact]
    public void SpeedHistoryKeepsTheNewestSampleAtNowBeforeAndAfterWrapping()
    {
        var samples = new SpeedSamples(120);
        samples.Add(12); Assert.Equal(1, samples.HorizontalFraction(0));
        samples.Add(24); Assert.Equal(119d / 120, samples.HorizontalFraction(0), 10); Assert.Equal(1, samples.HorizontalFraction(1));
        for (var i = 0; i < 200; i++) samples.Add(i);
        Assert.Equal(1d / 120, samples.HorizontalFraction(0), 10); Assert.Equal(1, samples.HorizontalFraction(119));
        Assert.Throws<ArgumentOutOfRangeException>(() => samples.HorizontalFraction(120));
    }
    [Fact]
    public void FocusedQueueRowSelectsWithSpaceAndEnterThroughTheNormalInputPipeline()
    {
        using var host = ModernFormsTestHost.Create();
        var directory = Path.Combine(Path.GetTempPath(), "mtd-row-keyboard-" + Guid.NewGuid().ToString("N"));
        try
        {
            var paths = AppPaths.Create(directory); paths.EnsureCreated();
            using var thumbnails = new ThumbnailCacheService(paths, new NullAppLogger(), () => false);
            var row = new QueueItemCard(thumbnails, new("pl")) { Width = 960 }; row.SetDashboard(true);
            var item = new DownloadQueueItem { Title = "Keyboard selection", SourceUrl = "https://example.test/video", Status = DownloadStatus.Queued };
            row.UpdateItem(item, false); var selections = new List<Guid>();
            row.ActionRequested += (_, args) => { if (args.Action == QueueItemAction.Select) selections.Add(args.ItemId); };
            host.Show(row, 960, 200); Assert.True(host.Input.Focus(row));
            host.Input.PressKey(Keys.Space); host.Input.PressKey(Keys.Enter);
            Assert.Equal(new[] { item.Id, item.Id }, selections);
            var button = row.Controls.OfType<Button>().First(c => c.Visible && c.Enabled);
            Assert.True(host.Input.Focus(button)); host.Input.PressKey(Keys.Space);
            Assert.Equal(2, selections.Count); // A child action must not also select its parent.
            row.SetDashboard(false); Assert.False(row.TabStop);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
    [Fact]
    public void SpeedHistoryAndProcessActivityAreBoundedAndSanitized()
    {
        var samples = new SpeedSamples(120); for (var i = 0; i < 10000; i++) samples.Add(i);
        Assert.Equal(120, samples.Count); Assert.Equal(9880, samples.Snapshot()[0]); Assert.Equal(9999, samples.Snapshot()[^1]);
        using (ProcessActivity.Track(-10, "C:/private/secret-token.exe")) { Assert.Contains(ProcessActivity.Snapshot(), p => p.Name == "Tool"); Assert.DoesNotContain(JsonSerializer.Serialize(ProcessActivity.Snapshot()), "secret"); }
        Assert.DoesNotContain(ProcessActivity.Snapshot(), p => p.Name.Contains("secret"));
    }
    [Theory]
    [InlineData("live", AppPageId.Live)] [InlineData("history", AppPageId.History)] [InlineData("downloads", AppPageId.Downloads)]
    public void NotificationsUseTheSharedNavigationContract(string target, AppPageId expected)
        => Assert.Equal(expected, AppNavigation.ForNotification(new("downloads", Guid.NewGuid(), target)));
    [Theory]
    [InlineData("https://www.youtube.com/live/abc", AppPageId.Live)]
    [InlineData("https://www.youtube.com/watch?v=abc", AppPageId.Downloads)]
    [InlineData("https://example.test/watch/abc", AppPageId.Downloads)]
    public void ExternalLinksUseTheSharedNavigationContract(string url, AppPageId expected)
        => Assert.Equal(expected, AppNavigation.ForExternalRequest(new(url, true)));

    [Fact]
    public async Task ActualInterfaceChangesKeepTheQueueProcessorSchedulerHistoryBatchAndRemoteInstances()
    {
        var directory = Path.Combine(Path.GetTempPath(), "mtd-runtime-shell-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var services = await CreateServicesAsync(directory);
            var pendingWrites = new List<Task>();
            using (var test = ModernFormsTestHost.Create())
            {
                services.Appearance.Profile.Global.Animations = false;
                using var form = new MainForm(services);
                var processor = services.Processor; var queue = services.Queue; var scheduler = services.LiveScheduler;
                var history = services.History; var batch = services.Batch; var remote = services.WebRemote;
                form.Navigate(AppPageId.History);
                for (var i = 0; i < 20; i++)
                {
                    var p = services.Appearance.Profile.Copy(); p.InterfaceMode = AppInterfaceMode.ModernDashboard;
                    pendingWrites.Add(services.Appearance.ApplyProfileAsync(p)); test.ProcessPendingWork();
                    Assert.True(form.InterfaceMode == AppInterfaceMode.ModernDashboard, ReadLogs(services.Paths.LogDirectory)); Assert.Equal(AppPageId.History, form.CurrentPage);
                    p.InterfaceMode = AppInterfaceMode.Classic; pendingWrites.Add(services.Appearance.ApplyProfileAsync(p)); test.ProcessPendingWork();
                    Assert.Equal(AppInterfaceMode.Classic, form.InterfaceMode);
                    Assert.Same(processor, services.Processor); Assert.Same(queue, services.Queue); Assert.Same(scheduler, services.LiveScheduler);
                    Assert.Same(history, services.History); Assert.Same(batch, services.Batch); Assert.Same(remote, services.WebRemote);
                }
            }
            await Task.WhenAll(pendingWrites);
            await services.ShutdownAsync();
        }
        finally { AppearancePresentation.Current = new(); AppTypography.SetScale(100); if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData(0, 100, 1280, 720)] [InlineData(100, 100, 1536, 1024)]
    [InlineData(100, 150, 1280, 720)] [InlineData(100, 200, 1920, 1080)]
    public async Task DashboardQueuePagesLargeCollectionsAndRevealsItemsOutsideTheFirstPage(int count, int scale, int width, int height)
    {
        var directory = Path.Combine(Path.GetTempPath(), "mtd-layout-shell-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var services = await CreateServicesAsync(directory);
            var items = Enumerable.Range(0, count).Select(i => new DownloadQueueItem { VideoId = "qa-" + i, Title = "QA " + i + " Łódź 日本語 " + new string('x', 100), SourceUrl = "https://example.test/" + i, Status = DownloadStatus.Completed }).ToArray();
            services.Queue.AddRangeSkippingDuplicates(items);
            using (var test = ModernFormsTestHost.Create())
            {
                services.Appearance.Initialize(); AppTypography.SetScale(scale);
                var view = new DownloadsView(services); view.SetDashboard(true); AppTypography.SetDashboardPresentation(view, true);
                test.Show(view, width, height); test.ProcessPendingWork();
                var list = Find<FlowLayoutPanel>(view, "QueueList"); Assert.Equal(Math.Min(40, count), list.Controls.OfType<QueueItemCard>().Count());
                if (count > 0) { view.RevealQueueItem(items[^1].Id); test.ProcessPendingWork(); Assert.Equal(items[^1].Id, view.SelectedItemId); Assert.Contains(list.Controls.OfType<QueueItemCard>(), c => c.AccessibleAutomationId == $"QueueItem-{items[^1].Id:N}"); }
                var queuePanel = Find<Panel>(view, "DashboardQueuePanel"); Assert.True(queuePanel.Width >= 520);
                Assert.All(view.Controls.Where(c => c.Visible), c => { Assert.True(c.Width >= 0 && c.Height >= 0); Assert.True(c.Left >= 0); });
            }
            await services.ShutdownAsync();
        }
        finally { AppTypography.SetScale(100); AppearancePresentation.Current = new(); if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData(100)] [InlineData(150)] [InlineData(200)]
    public async Task QueuePresentationRoundtripRetainsHostSelectionAndCanonicalRecords(int scale)
    {
        var directory = Path.Combine(Path.GetTempPath(), "mtd-queue-roundtrip-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var services = await CreateServicesAsync(directory);
            var items = Enumerable.Range(0, 100).Select(i => new DownloadQueueItem
            {
                VideoId = "qa-" + i, Title = "QA " + i + " Łódź 日本語", SourceUrl = "https://example.test/" + i,
                Status = DownloadStatus.Completed
            }).ToArray();
            services.Queue.AddRangeSkippingDuplicates(items);
            var before = JsonSerializer.Serialize(services.Queue.Snapshot());
            using (var host = ModernFormsTestHost.Create())
            {
                services.Appearance.Initialize(); AppTypography.SetScale(scale);
                var view = new DownloadsView(services); host.Show(view, 1920, 1080);
                var list = Find<FlowLayoutPanel>(view, "QueueList"); var parent = list.Parent;
                view.SetDashboard(true); AppTypography.SetDashboardPresentation(view, true);
                view.RevealQueueItem(items[^1].Id); host.ProcessPendingWork();
                for (var cycle = 0; cycle < 2; cycle++)
                {
                    DashboardVisual.RestoreTree(view); AppTypography.SetDashboardPresentation(view, false);
                    view.SetDashboard(false); host.ProcessPendingWork();
                    Assert.Same(parent, list.Parent);
                    Assert.Equal(100, list.Controls.OfType<QueueItemCard>().Count());
                    Assert.Equal(items[^1].Id, view.SelectedItemId);
                    view.SetDashboard(true); AppTypography.SetDashboardPresentation(view, true); host.ProcessPendingWork();
                    Assert.Same(list, Find<FlowLayoutPanel>(view, "QueueList")); Assert.Same(parent, list.Parent);
                    Assert.Equal(1, parent!.Controls.Count(control => ReferenceEquals(control, list)));
                    Assert.Equal(20, list.Controls.OfType<QueueItemCard>().Count());
                    Assert.Equal(items[^1].Id, view.SelectedItemId);
                    var selected = Find<QueueItemCard>(list, $"QueueItem-{items[^1].Id:N}");
                    Assert.True(selected.AccessibilityObject.State.HasFlag(ModernFormsNext.Accessibility.AccessibleStates.Selected));
                    Assert.True(list.Width > 0 && list.Height > 0);
                    Assert.Equal(before, JsonSerializer.Serialize(services.Queue.Snapshot()));
                }
            }
            await services.ShutdownAsync();
        }
        finally { AppearancePresentation.Current = new(); AppTypography.SetScale(100); if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task HistoryFiltersDateRangeAndSortUseTheCanonicalHistoryWithoutMutatingIt()
    {
        var directory = Path.Combine(Path.GetTempPath(), "mtd-history-presentation-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var services = await CreateServicesAsync(directory);
            var recent = new DownloadHistoryEntry(Guid.NewGuid(), "Recent audio", "https://example.test/a", "a", "Channel", "best", "audio.mp3", DateTimeOffset.UtcNow.AddDays(-1), "Completed");
            var old = recent with { QueueItemId = Guid.NewGuid(), Title = "Old playlist video", FinalPath = "video.mp4", CompletedAt = DateTimeOffset.UtcNow.AddDays(-40), PlaylistId = "playlist" };
            await services.History.AddAsync(recent); await services.History.AddAsync(old);
            var before = JsonSerializer.Serialize(services.History.Snapshot());
            using (var host = ModernFormsTestHost.Create())
            {
                var view = new HistoryView(services, true); AppTypography.SetDashboardPresentation(view, true);
                host.Show(view, 1536, 1024);
                var list = Find<FlowLayoutPanel>(view, "HistoryList");
                Assert.Equal(recent.QueueItemId, list.Controls.OfType<HistoryEntryCard>().First().ItemId);
                Find<Button>(view, "HistoryDateSort").PerformClick();
                Assert.Equal(old.QueueItemId, list.Controls.OfType<HistoryEntryCard>().First().ItemId);
                Find<Button>(view, "HistoryFilterAudio").PerformClick(); Assert.Single(list.Controls.OfType<HistoryEntryCard>());
                Assert.Equal(recent.QueueItemId, list.Controls.OfType<HistoryEntryCard>().Single().ItemId);
                Find<Button>(view, "HistoryFilterPlaylists").PerformClick(); Assert.Equal(old.QueueItemId, list.Controls.OfType<HistoryEntryCard>().Single().ItemId);
                Find<Button>(view, "HistoryFilterAll").PerformClick();
                Find<ComboBox>(view, "HistoryDateRange").SelectedIndex = 1;
                Assert.Equal(recent.QueueItemId, list.Controls.OfType<HistoryEntryCard>().Single().ItemId);
                Assert.Equal(before, JsonSerializer.Serialize(services.History.Snapshot()));
                view.SetDashboard(false); Assert.Equal(2, list.Controls.OfType<HistoryEntryCard>().Count());
            }
            await services.ShutdownAsync();
        }
        finally { AppearancePresentation.Current = new(); if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData(100)] [InlineData(150)] [InlineData(200)]
    public async Task RepeatedSettingsReflowKeepsLanguageLabelInsideItsCard(int scale)
    {
        var directory = Path.Combine(Path.GetTempPath(), "mtd-settings-presentation-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var services = await CreateServicesAsync(directory);
            using (var host = ModernFormsTestHost.Create())
            {
                AppTypography.SetScale(scale);
                var view = new SettingsView(services); view.SetDashboard(true); AppTypography.SetDashboardPresentation(view, true);
                host.Show(view, 1920, 1080);
                IEnumerable<Control> Descendants(Control parent) => parent.Controls.SelectMany(c => new[] { c }.Concat(Descendants(c)));
                var label = Descendants(view).OfType<Label>().Single(c => c.Text == services.Localization["Settings.Language"]);
                var initial = label.Top;
                for (var i = 0; i < 5; i++)
                {
                    view.Width = 1536; host.ProcessPendingWork(); view.Width = 1920; host.ProcessPendingWork();
                    Assert.Equal(initial, label.Top); Assert.True(label.Bottom <= label.Parent!.Height);
                }
            }
            await services.ShutdownAsync();
        }
        finally { AppTypography.SetScale(100); AppearancePresentation.Current = new(); if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    private static string ReadLogs(string directory)
    {
        return string.Join("\n", Directory.GetFiles(directory).Select(path =>
        { using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete); using var reader = new StreamReader(stream); return reader.ReadToEnd(); }));
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
        foreach (var child in parent.Controls) { if (child.AccessibleAutomationId == id && child is T result) return result; try { return Find<T>(child, id); } catch (KeyNotFoundException) { } }
        throw new KeyNotFoundException(id);
    }
}
