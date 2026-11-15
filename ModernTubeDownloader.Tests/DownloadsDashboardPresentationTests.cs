using ModernFormsNext;
using ModernFormsNext.Accessibility;
using ModernFormsNext.Testing;
using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Services;
using ModernTubeDownloader.Settings;
using ModernTubeDownloader.Shell;
using ModernTubeDownloader.Theming;
using ModernTubeDownloader.Views;
using Path = System.IO.Path;

namespace ModernTubeDownloader.Tests;

[Collection("ModernFormsNext TestHost")]
public sealed class DownloadsDashboardPresentationTests
{
    [Theory]
    [InlineData(AppThemeMode.Dark, AppThemeMode.Light, 100)]
    [InlineData(AppThemeMode.Light, AppThemeMode.Dark, 100)]
    [InlineData(AppThemeMode.Dark, AppThemeMode.Light, 200)]
    [InlineData(AppThemeMode.Light, AppThemeMode.Dark, 200)]
    public async Task RetainedReadonlyInspectorFieldsRenderLikeFreshFieldsAfterThemeChange(AppThemeMode initial, AppThemeMode target, int scale)
    {
        var directory = Path.Combine(Path.GetTempPath(), "mtd-readonly-inspector-theme-" + Guid.NewGuid().ToString("N"));
        AppServices? services = null;
        Task? write = null;
        try
        {
            services = await CreateServicesAsync(directory);
            services.Appearance.Profile.InterfaceMode = AppInterfaceMode.ModernDashboard;
            services.Appearance.Profile.Global.Theme = (AppearanceTheme)(int)initial;
            services.Appearance.Profile.Global.Animations = false;
            services.Appearance.Profile.Typography.AppTextScale = scale;
            services.Settings.Current.ThemeMode = initial;
            var item = new ModernTubeDownloader.Models.DownloadQueueItem
            {
                Title = "Theme test — Łódź", SourceUrl = "https://example.test/watch/readonly-fields",
                SelectedFormats = new ModernTubeDownloader.Models.FormatSelection("18", null, "mp4", null, false, false),
                RawMetadataJson = """{"id":"readonly","duration":120,"formats":[{"format_id":"18","ext":"mp4","height":720,"vcodec":"h264","acodec":"mp4a.40.2","url":"https://example.test/file"}]}"""
            };
            using (var host = ModernFormsTestHost.Create())
            {
                services.Appearance.Initialize();
                var root = new Panel(); AppUi.Surface(root);
                AppTypography.SetDashboardPresentation(root, true);
                var window = host.Show(root, 1400, 960);
                // Exercise the production inspector/editor constructors after the
                // window exists, matching late-created dashboard theme bindings.
                var inspector = CreateInspector();
                var audio = Find<TextBox>(inspector, "InspectorAudio");
                var folder = Find<TextBox>(inspector, "InspectorFolder");
                var audioText = audio.Text; var folderText = folder.Text;
                Assert.Equal("mp4a.40.2", audioText);
                Assert.False(string.IsNullOrWhiteSpace(folderText));
                Assert.True(audio.ReadOnly && folder.ReadOnly && audio.Enabled && folder.Enabled);
                var textChanges = 0;
                audio.TextChanged += (_, _) => textChanges++;
                folder.TextChanged += (_, _) => textChanges++;
                using var before = window.CaptureRenderedSnapshot();
                write = services.Appearance.SetModeAsync(target);
                host.ProcessPendingWork();
                using var retained = window.CaptureRenderedSnapshot();
                Assert.Equal(audioText, audio.Text); Assert.Equal(folderText, folder.Text);
                Assert.Equal(0, textChanges);
                Assert.True(audio.ReadOnly && folder.ReadOnly && audio.Enabled && folder.Enabled);
                Assert.False(before.CopyPixels().SequenceEqual(retained.CopyPixels()), "The target palette must change the rendered inspector.");
                root.Controls.Remove(inspector); inspector.Dispose();
                var freshInspector = CreateInspector();
                Assert.Equal(audioText, Find<TextBox>(freshInspector, "InspectorAudio").Text);
                Assert.Equal(folderText, Find<TextBox>(freshInspector, "InspectorFolder").Text);
                using var fresh = window.CaptureRenderedSnapshot();
                Assert.True(retained.CopyPixels().SequenceEqual(fresh.CopyPixels()),
                    "The retained inspector, including read-only glyphs, must render exactly like a fresh inspector in the target theme.");
                host.Dispatcher.ThrowUnhandledExceptions();

                DashboardInspector CreateInspector()
                {
                    var value = new DashboardInspector(services);
                    root.Controls.Add(value); AppTypography.SetDashboardPresentation(value, true);
                    value.SetBounds(10, 10, 1380, 940); value.ShowQueue(item);
                    host.ProcessPendingWork(); return value;
                }
            }
            if (write is not null) await write;
        }
        finally
        {
            if (write is not null) await write;
            if (services is not null) { await services.ShutdownAsync(); services.Dispose(); }
            AppTypography.SetScale(100); AppearancePresentation.Current = new();
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Theory]
    [InlineData(100, 1100)]
    [InlineData(200, 1536)]
    public async Task ThemeTransactionRetainsOpenDrawerDraftAndWidgetsWithoutResize(int scale, int width)
    {
        var directory = Path.Combine(Path.GetTempPath(), "mtd-downloads-theme-drawer-" + Guid.NewGuid().ToString("N"));
        AppServices? services = null;
        try
        {
            services = await CreateServicesAsync(directory);
            services.Appearance.Profile.InterfaceMode = AppInterfaceMode.ModernDashboard;
            services.Appearance.Profile.Global.Animations = false;
            services.Appearance.Profile.Global.Theme = AppearanceTheme.Dark;
            services.Settings.Current.ThemeMode = AppThemeMode.Dark;
            services.Appearance.Profile.Typography.AppTextScale = scale;
            services.Appearance.Profile.ModernDashboardShell.Inspector = DashboardInspectorMode.Auto;
            services.Appearance.Profile.ModernDashboardShell.Widgets = DashboardWidgetsMode.All;
            var item = new ModernTubeDownloader.Models.DownloadQueueItem
            {
                Title = "Retained drawer draft", SourceUrl = "https://example.test/watch/theme-drawer",
                RawMetadataJson = """{"id":"drawer","duration":120,"formats":[{"format_id":"18","ext":"mp4","height":720,"vcodec":"h264","acodec":"aac","url":"https://example.test/file"}]}"""
            };
            services.Queue.Add(item);
            using (var host = ModernFormsTestHost.Create())
            using (var form = new MainForm(services, backgroundActivation: true))
            {
                Ui(() => host.Show(form, width, 1024));
                var view = Assert.Single(form.Controls.SelectMany(control => new[] { control }.Concat(Descendants(control))).OfType<DownloadsView>());
                Ui(() =>
                {
                    form.Navigate(AppPageId.Downloads);
                    view.RevealQueueItem(item.Id);
                    var toggle = Find<Button>(view, "DashboardToggleInspector");
                    Assert.True(toggle.Visible && toggle.Width > 0,
                        $"Drawer fixture must expose its toggle: page={view.ClientSize}, viewport={view.DisplayRectangle}, scale={scale}.");
                    toggle.PerformClick();
                });
                var inspector = Find<DashboardInspector>(view, "DashboardInspector");
                var widgets = Find<Control>(view, "DashboardWidgets");
                var quality = Find<ComboBox>(inspector, "InspectorQuality");
                var container = Find<ComboBox>(inspector, "InspectorContainer");
                Ui(() =>
                {
                    quality.SelectedIndex = 4; container.SelectedIndex = 2;
                    Find<Button>(inspector, "InspectorOptions").PerformClick();
                    Find<ComboBox>(inspector, "InspectorRangeMode").SelectedIndex = 1;
                    Find<TextBox>(inspector, "InspectorRangeStart").Text = "00:00:02";
                    Find<TextBox>(inspector, "InspectorRangeEnd").Text = "00:00:10";
                });
                var pageSize = view.ClientSize;
                var processor = services.Processor;
                var darkInput = quality.Style.GetBackgroundColor();
                var darkCanvas = AppUi.GetColor(DownloadsVisual.Background);
                AssertRetainedPresentation();

                foreach (var target in new[] { AppThemeMode.Light, AppThemeMode.Dark })
                {
                    Task? change = null;
                    Ui(() => change = services.Appearance.SetModeAsync(target));
                    // Match the existing shell lifecycle tests: pump the owning UI
                    // while persistence completes; the poll is not a layout delay.
                    var deadline = DateTime.UtcNow.AddSeconds(5);
                    while (!change!.IsCompleted && DateTime.UtcNow < deadline)
                    { host.ProcessPendingWork(); Thread.Sleep(5); }
                    Assert.True(change!.IsCompleted, "The isolated theme transaction did not complete.");
                    await change; host.ProcessPendingWork();
                    // No resize, inspector toggle, selection refresh or synthetic layout:
                    // the real presentation transaction must restore the retained page.
                    AssertRetainedPresentation();
                    if (target == AppThemeMode.Light)
                    {
                        Assert.NotEqual(darkInput, quality.Style.GetBackgroundColor());
                        Assert.NotEqual(darkCanvas, AppUi.GetColor(DownloadsVisual.Background));
                    }
                    else
                    {
                        Assert.Equal(darkInput, quality.Style.GetBackgroundColor());
                        Assert.Equal(darkCanvas, AppUi.GetColor(DownloadsVisual.Background));
                    }
                }

                Ui(() => form.Navigate(AppPageId.Other));
                Ui(() => form.Navigate(AppPageId.Downloads));
                AssertRetainedPresentation();
                host.Dispatcher.ThrowUnhandledExceptions();

                void AssertRetainedPresentation()
                {
                    Assert.Equal(pageSize, view.ClientSize);
                    Assert.Same(processor, services.Processor);
                    Assert.Equal(item.Id, view.SelectedItemId);
                    Assert.Same(inspector, Find<DashboardInspector>(view, "DashboardInspector"));
                    Assert.Same(quality, Find<ComboBox>(inspector, "InspectorQuality"));
                    Assert.True(inspector.Visible && inspector.Width > 0 && inspector.Height > 0,
                        $"The open drawer lost its bounds: {inspector.Bounds}.");
                    Assert.True(inspector.Top >= Find<Control>(view, "DashboardQueuePanel").Bottom,
                        "This case must exercise the narrow drawer rather than the inline inspector.");
                    Assert.True(widgets.Visible && widgets.Width > 0 && widgets.Height > 0);
                    Assert.True(widgets.Top >= inspector.Bottom);
                    Assert.Contains(widgets.Controls, control => control.Visible);
                    Assert.All(widgets.Controls.Where(control => control.Visible), control =>
                        Assert.True(control.Width > 0 && control.Height > 0, $"Widget lost its bounds: {control.Bounds}."));
                    foreach (var field in new Control[] { quality, container })
                    {
                        Assert.True(field.Visible && field.Width > 0 && field.Height > 0);
                        var fieldPosition = field.PointToScreen(System.Drawing.Point.Empty);
                        var inspectorPosition = inspector.PointToScreen(System.Drawing.Point.Empty);
                        Assert.InRange(fieldPosition.X - inspectorPosition.X, 0, inspector.ClientSize.Width - field.Width);
                    }
                    Assert.False(Assert.IsType<AccessibleScrollInfo>(inspector.AccessibilityObject.ScrollInfo).Horizontal.IsScrollable);
                    Assert.Equal(4, quality.SelectedIndex); Assert.Equal(2, container.SelectedIndex);
                    Assert.Equal(1, Find<ComboBox>(inspector, "InspectorRangeMode").SelectedIndex);
                    Assert.Equal("00:00:02", Find<TextBox>(inspector, "InspectorRangeStart").Text);
                    Assert.Equal("00:00:10", Find<TextBox>(inspector, "InspectorRangeEnd").Text);
                    Assert.Equal("best", item.QualityPresetId); // Appearance must not commit a draft.
                    Assert.Same(item, services.Queue.Find(item.Id));
                }
                void Ui(Action action)
                { host.Dispatcher.Post(action); host.ProcessPendingWork(); host.Dispatcher.ThrowUnhandledExceptions(); }
            }
        }
        finally
        {
            if (services is not null) { await services.ShutdownAsync(); services.Dispose(); }
            AppTypography.SetScale(100); AppearancePresentation.Current = new();
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    private static IEnumerable<Control> Descendants(Control parent)
    { foreach (var child in parent.Controls) { yield return child; foreach (var nested in Descendants(child)) yield return nested; } }

    [Theory]
    [InlineData(100)] [InlineData(150)] [InlineData(200)]
    public async Task EmptyInspectorReflowsWhenItsHiddenPageIsFirstShown(int scale)
    {
        var directory = Path.Combine(Path.GetTempPath(), "mtd-empty-inspector-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var services = await CreateServicesAsync(directory);
            using (var host = ModernFormsTestHost.Create())
            {
                services.Appearance.Initialize(); AppTypography.SetScale(scale);
                var root = new Panel(); var page = new Panel { Visible = false, Width = 1200, Height = 700 }; root.Controls.Add(page);
                var inspector = new DashboardInspector(services); page.Controls.Add(inspector);
                AppTypography.SetDashboardPresentation(root, true); host.Show(root, 1280, 720);
                inspector.SetBounds(800, 60, 560, 580); inspector.SetBounds(900, 60, 300, 580);
                page.Visible = true; host.ProcessPendingWork();
                Assert.False(Assert.IsType<AccessibleScrollInfo>(inspector.AccessibilityObject.ScrollInfo).Horizontal.IsScrollable);
                Assert.All(inspector.Controls.Where(c => !c.Visible), c => Assert.Equal(System.Drawing.Rectangle.Empty, c.Bounds));
            }
            await services.ShutdownAsync();
        }
        finally { AppTypography.SetScale(100); AppearancePresentation.Current = new(); if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData(100)]
    [InlineData(150)]
    [InlineData(200)]
    public async Task StartingDirectlyInModernKeepsTheAnalysisRangeVisible(int scale)
    {
        var directory = Path.Combine(Path.GetTempPath(), "mtd-modern-start-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var services = await CreateServicesAsync(directory);
            using (var host = ModernFormsTestHost.Create())
            {
                services.Appearance.Initialize(); AppTypography.SetScale(scale);
                var view = new DownloadsView(services, useDashboard: true);
                AppTypography.SetDashboardPresentation(view, true); host.Show(view, 1280, 900); view.SetDashboard(true);
#pragma warning disable xUnit1031 // Keep the TestHost on its owning thread.
                view.AcceptExternalUrlAsync("https://example.test/watch/sample123", true).GetAwaiter().GetResult();
#pragma warning restore xUnit1031
                foreach (var custom in new[] { false, true, false })
                {
                    var mode = Find<ComboBox>(view, "VideoRangeMode"); mode.SelectedIndex = custom ? 1 : 0; host.ProcessPendingWork();
                    var range = Assert.IsType<MediaRangeEditor>(mode.Parent);
                    Assert.True(range.ResponsiveLayout); Assert.True(range.Visible); Assert.True(range.Height >= mode.Height);
                    Assert.All(range.Controls.Where(c => c.Visible), c => { Assert.True(c.Height > 0); Assert.True(c.Bottom <= range.Height); Assert.True(c.Right <= range.ClientSize.Width); });
                    Assert.All(range.Controls.OfType<Label>().Where(c => c.Visible), c => Assert.True(c.Width >= DashboardVisual.TextWidth(c, c.Text), c.Text));
                    var add = Find<Button>(view, "AddToQueueButton");
                    Assert.True(add.Width >= DashboardVisual.ActionWidth(add), "Analysis action text must fit its current rendered style and padding.");
                    Assert.True(Find<Button>(view, "AddToQueueButton").Top >= range.Bottom);
                }
            }
            await services.ShutdownAsync();
        }
        finally { AppTypography.SetScale(100); AppearancePresentation.Current = new(); if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData(100, 1280)]
    [InlineData(150, 1469)]
    [InlineData(200, 1800)]
    public async Task AnalysisCardTracksVisibleRangeContentAndRestoresClassic(int scale, int width)
    {
        var directory = Path.Combine(Path.GetTempPath(), "mtd-analysis-geometry-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var services = await CreateServicesAsync(directory);
            using (var host = ModernFormsTestHost.Create())
            {
                services.Appearance.Initialize(); AppTypography.SetScale(scale);
                var view = new DownloadsView(services); host.Show(view, width, 900); host.ProcessPendingWork();
#pragma warning disable xUnit1031 // Native test host lifetime stays on its UI thread.
                view.AcceptExternalUrlAsync("https://example.test/watch/sample123", true).GetAwaiter().GetResult();
#pragma warning restore xUnit1031
                host.ProcessPendingWork();
                var preview = Find<Panel>(view, "PreviewCard");
                var classic = preview.Controls.ToDictionary(c => c, c => c.Bounds);
                var classicHeight = preview.Height;
                AppTypography.SetDashboardPresentation(view, true); view.SetDashboard(true); host.ProcessPendingWork();
                var fullHeight = preview.Height;
                foreach (var custom in new[] { true, false, true, false })
                {
                    Find<ComboBox>(view, "VideoRangeMode").SelectedIndex = custom ? 1 : 0; host.ProcessPendingWork();
                    var range = (MediaRangeEditor)Find<ComboBox>(view, "VideoRangeMode").Parent!;
                    Assert.True(range.Height >= Find<ComboBox>(view, "VideoRangeMode").Height);
                    Assert.InRange(range.Height - range.Controls.Where(c => c.Visible).Max(c => c.Bottom), 0, 4);
                    Assert.All(preview.Controls.Where(c => !c.Visible), c => Assert.Equal(System.Drawing.Rectangle.Empty, c.Bounds));
                    Assert.All(preview.Controls.Where(c => c.Visible), c => { Assert.InRange(c.Left, 8, preview.Width); Assert.True(c.Right <= preview.Width - 8); Assert.True(c.Bottom <= preview.Height - 8); });
                    Assert.Equal(custom, Find<Label>(view, "VideoRangeKeyframeHint").Visible);
                    var last = preview.Controls.Where(c => c.Visible).Max(c => c.Bottom);
                    Assert.InRange(preview.Height - last, 8, 18);
                    Assert.False(Assert.IsType<AccessibleScrollInfo>(view.AccessibilityObject.ScrollInfo).Horizontal.IsScrollable);
                    if (!custom) Assert.Equal(fullHeight, preview.Height);
                }
                DashboardVisual.RestoreTree(view); AppTypography.SetDashboardPresentation(view, false); view.SetDashboard(false); host.ProcessPendingWork();
                Assert.Equal(classicHeight, preview.Height);
                Assert.All(classic, pair => Assert.Equal(pair.Value, pair.Key.Bounds));
            }
            await services.ShutdownAsync();
        }
        finally { AppTypography.SetScale(100); AppearancePresentation.Current = new(); if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData(100, 300)]
    [InlineData(150, 380)]
    [InlineData(200, 440)]
    public async Task InspectorReflowsAfterItsVerticalRailAppearsAndRangeUsesVisibleContent(int scale, int width)
    {
        var directory = Path.Combine(Path.GetTempPath(), "mtd-inspector-geometry-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var services = await CreateServicesAsync(directory);
            using (var host = ModernFormsTestHost.Create())
            {
                services.Appearance.Initialize(); AppTypography.SetScale(scale);
                var inspector = new DashboardInspector(services);
                AppTypography.SetDashboardPresentation(inspector, true);
                inspector.ShowQueue(new ModernTubeDownloader.Models.DownloadQueueItem
                {
                    Title = "Długi tytuł Łódź 日本語", Channel = "Test channel", SourceUrl = "https://example.test/video",
                    Status = ModernTubeDownloader.Models.DownloadStatus.Queued,
                    RawMetadataJson = "{\"id\":\"test\",\"title\":\"Długi tytuł Łódź 日本語\",\"duration\":120}"
                });
                host.Show(inspector, width, 450); host.ProcessPendingWork();
                foreach (var custom in new[] { false, true, false })
                {
                    Find<Button>(inspector, "InspectorOptions").PerformClick();
                    Find<ComboBox>(inspector, "InspectorRangeMode").SelectedIndex = custom ? 1 : 0;
                    host.ProcessPendingWork();
                    var scroll = Assert.IsType<AccessibleScrollInfo>(inspector.AccessibilityObject.ScrollInfo);
                    Assert.False(scroll.Horizontal.IsScrollable, "Inspector Options acquired horizontal scrolling after the vertical rail appeared.");
                    foreach (var id in new[] { "InspectorQuality", "InspectorContainer", "InspectorRangeMode", "InspectorRangeStart", "InspectorRangeEnd", "InspectorApplyOptions" })
                    {
                        var control = Find<Control>(inspector, id);
                        if (control.Visible) Assert.InRange(control.Right, 1, control.Parent!.ClientSize.Width);
                    }
                    var range = (MediaRangeEditor)Find<ComboBox>(inspector, "InspectorRangeMode").Parent!;
                    var last = range.Controls.Where(c => c.Visible).Max(c => c.Bottom);
                    Assert.InRange(range.Height - last, 0, 4);
                    Assert.All(range.Controls.Where(c => !c.Visible), c => Assert.Equal(System.Drawing.Rectangle.Empty, c.Bounds));
                    Find<Button>(inspector, "InspectorDetails").PerformClick(); host.ProcessPendingWork();
                    Assert.False(Assert.IsType<AccessibleScrollInfo>(inspector.AccessibilityObject.ScrollInfo).Horizontal.IsScrollable);
                    host.Resize(width + 200, 700); host.ProcessPendingWork(); host.Resize(width, 450); host.ProcessPendingWork();
                }
                inspector.Clear(); host.ProcessPendingWork();
                Assert.All(inspector.Controls.Where(c => !c.Visible), c => Assert.Equal(System.Drawing.Rectangle.Empty, c.Bounds));
                Assert.False(Assert.IsType<AccessibleScrollInfo>(inspector.AccessibilityObject.ScrollInfo).Horizontal.IsScrollable);
            }
            await services.ShutdownAsync();
        }
        finally { AppTypography.SetScale(100); AppearancePresentation.Current = new(); if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData(100, 288)] [InlineData(150, 380)] [InlineData(200, 440)]
    public async Task InlineOptionsShareDraftValidateAndPersistWithoutEditingStartedItems(int scale, int width)
    {
        var directory = Path.Combine(Path.GetTempPath(), "mtd-inline-options-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var services = await CreateServicesAsync(directory);
            var item = new ModernTubeDownloader.Models.DownloadQueueItem
            {
                Title = "Queued inline test", SourceUrl = "https://www.youtube.com/watch?v=isolatedQA",
                RawMetadataJson = """{"id":"inline","duration":120,"formats":[{"format_id":"18","ext":"mp4","height":720,"vcodec":"h264","acodec":"aac","url":"https://example.test/file"}]}"""
            };
            services.Queue.Add(item);
            using (var host = ModernFormsTestHost.Create())
            {
                services.Appearance.Initialize(); AppTypography.SetScale(scale);
                var inspector = new DashboardInspector(services); AppTypography.SetDashboardPresentation(inspector, true);
                inspector.ShowQueue(item); host.Show(inspector, width, 620); host.ProcessPendingWork();
                var quality = Find<ComboBox>(inspector, "InspectorQuality");
                var container = Find<ComboBox>(inspector, "InspectorContainer");
                Assert.True(quality.Visible && quality.Enabled && container.Visible && container.Enabled);
                Find<Control>(inspector, "InspectorSource").Select(); host.Input.PressKey(Keys.Tab);
                Assert.True(Find<Button>(inspector, "InspectorCopyUrl").Selected);
                container.Select(); host.Input.PressKey(Keys.Tab); Assert.True(quality.Selected);
                host.Input.PressKey(Keys.Shift | Keys.Tab); Assert.True(container.Selected);
                quality.Enabled = false; host.Input.PressKey(Keys.Tab);
                Assert.True(Find<Button>(inspector, "InspectorSubtitles").Selected);
                quality.Enabled = true;
                quality.SelectedIndex = 4; container.SelectedIndex = 2;
                Find<Button>(inspector, "InspectorOptions").PerformClick();
                var mode = Find<ComboBox>(inspector, "InspectorRangeMode"); mode.SelectedIndex = 1;
                var start = Find<TextBox>(inspector, "InspectorRangeStart"); var end = Find<TextBox>(inspector, "InspectorRangeEnd");
                start.Text = "00:00:02"; end.Text = "00:00:10";
                Find<Button>(inspector, "InspectorDetails").PerformClick();
                inspector.ShowQueue(item); // Progress/selection refresh must not erase the draft.
                Assert.Same(quality, Find<ComboBox>(inspector, "InspectorQuality"));
                Assert.Equal(4, quality.SelectedIndex); Assert.Equal(2, container.SelectedIndex);
                Assert.Equal(1, mode.SelectedIndex); Assert.Equal("00:00:02", start.Text);
                Assert.Equal("best", item.QualityPresetId); // No writes before Apply.
                Find<Button>(inspector, "InspectorApplyOptions").PerformClick();
                Assert.Equal("720", item.QualityPresetId);
                Assert.Equal(ModernTubeDownloader.Models.PreferredVideoContainer.Mkv, item.PreferredVideoContainer);
                Assert.Equal(TimeSpan.FromSeconds(2), item.RequestedRange.Start);
                Assert.Equal(TimeSpan.FromSeconds(10), item.RequestedRange.End);
                Find<Button>(inspector, "InspectorOptions").PerformClick();
                end.Text = "00:00:01"; Find<Button>(inspector, "InspectorApplyOptions").PerformClick();
                Assert.Equal(TimeSpan.FromSeconds(10), item.RequestedRange.End); // Rejected range is not persisted.
                end.Text = "00:00:10";
                services.Queue.Update(item.Id, value => value.Status = ModernTubeDownloader.Models.DownloadStatus.DownloadingVideo);
                quality.SelectedIndex = 1;
                Find<Button>(inspector, "InspectorApplyOptions").PerformClick(); // Status changed before UI notification.
                Assert.Equal("720", item.QualityPresetId);
                foreach (var state in new[] { ModernTubeDownloader.Models.DownloadStatus.DownloadingVideo, ModernTubeDownloader.Models.DownloadStatus.Completed })
                {
                    services.Queue.Update(item.Id, value => value.Status = state); inspector.ShowQueue(item);
                    Assert.False(quality.Enabled); Assert.False(container.Enabled);
                    Assert.False(Find<Button>(inspector, "InspectorSubtitles").Enabled);
                    Assert.True(Find<TextBox>(inspector, "InspectorFolder").ReadOnly);
                    Assert.True(Find<TextBox>(inspector, "InspectorAudio").ReadOnly);
                    host.ProcessPendingWork();
                    Assert.False(Assert.IsType<AccessibleScrollInfo>(inspector.AccessibilityObject.ScrollInfo).Horizontal.IsScrollable);
                }
            }
            await services.Queue.SaveAsync();
            using var document = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(AppPaths.Create(directory).QueueFile));
            Assert.Equal("720", document.RootElement[0].GetProperty("qualityPresetId").GetString());
            await services.ShutdownAsync();
        }
        finally { AppTypography.SetScale(100); AppearancePresentation.Current = new(); if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    private static T Find<T>(Control root, string id) where T : Control
    {
        if (root.AccessibleAutomationId == id) return (T)root;
        foreach (var child in root.Controls) { var result = FindOrNull<T>(child, id); if (result is not null) return result; }
        throw new InvalidOperationException(id);
    }
    private static T? FindOrNull<T>(Control root, string id) where T : Control
    {
        if (root.AccessibleAutomationId == id) return (T)root;
        foreach (var child in root.Controls) { var result = FindOrNull<T>(child, id); if (result is not null) return result; }
        return null;
    }

    [Theory]
    [InlineData(100)]
    [InlineData(150)]
    [InlineData(200)]
    public async Task HiddenClassicPanelsDoNotKeepWideScrollCanvasAfterModernResize(int scale)
    {
        var directory = Path.Combine(Path.GetTempPath(), "mtd-downloads-canvas-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var services = await CreateServicesAsync(directory);
            using (var host = ModernFormsTestHost.Create())
            {
                services.Appearance.Initialize(); AppTypography.SetScale(scale);
                var view = new DownloadsView(services); host.Show(view, 2880, 1200); host.ProcessPendingWork();
                var sharedControls = view.Controls.ToArray();
                AppTypography.SetDashboardPresentation(view, true); view.SetDashboard(true);
                host.Resize(1469, 900); host.ProcessPendingWork();
                Assert.Equal(1469, view.Width);
                var scroll = Assert.IsType<AccessibleScrollInfo>(view.AccessibilityObject.ScrollInfo);
                var geometry = string.Join("; ", view.Controls.Select(control => $"{control.GetType().Name}/{control.AccessibleAutomationId}: visible={control.Visible} bounds={control.Bounds}"));
                Assert.False(scroll.Horizontal.IsScrollable, geometry);
                Assert.All(sharedControls.Where(control => !control.Visible), control => Assert.Equal(System.Drawing.Rectangle.Empty, control.Bounds));
            }
            await services.ShutdownAsync();
        }
        finally
        {
            AppTypography.SetScale(100); AppearancePresentation.Current = new();
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
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
}
