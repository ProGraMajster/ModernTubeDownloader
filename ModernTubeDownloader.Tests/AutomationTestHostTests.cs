using ModernFormsNext;
using ModernFormsNext.Testing;
using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Localization;
using ModernTubeDownloader.Models;
using ModernTubeDownloader.Services;
using ModernTubeDownloader.Settings;
using ModernTubeDownloader.Theming;
using ModernTubeDownloader.Views;
using SkiaSharp;

namespace ModernTubeDownloader.Tests;

[CollectionDefinition("ModernFormsNext TestHost", DisableParallelization = true)]
public sealed class ModernFormsTestHostCollection;

[Collection("ModernFormsNext TestHost")]
public sealed class AutomationTestHostTests
{
    [Theory]
    [InlineData(AppThemeMode.Dark)]
    [InlineData(AppThemeMode.Light)]
    public async Task PreviewComboBoxesUseSemanticReadableColorsInEveryState(AppThemeMode mode)
    {
        var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ModernTubeDownloader.ComboStyle.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var paths = AppPaths.Create(root);
            var settings = new SettingsService(paths, new NullAppLogger());
            await settings.LoadAsync();
            settings.Current.ThemeMode = mode;
            using var host = ModernFormsTestHost.Create();
            using var appearance = new AppAppearanceService(settings, new NullAppLogger());
            appearance.Initialize();
            Assert.Equal(AppUi.GetColor(AppThemeTokens.Surface), Theme.ControlLowColor);
            Assert.Equal(AppUi.GetColor(AppThemeTokens.SurfaceHover), Theme.ControlHighlightLowColor);
            using var combo = new ComboBox();
            AppUi.Input(combo);
            combo.Items.Add("1080p");
            combo.SelectedIndex = 0;
            Assert.Equal("1080p", combo.SelectedItem);
            AssertComboState(combo.Style, AppThemeTokens.Surface, AppThemeTokens.TextPrimary);
            AssertComboState(combo.StyleHover, AppThemeTokens.SurfaceHover, AppThemeTokens.TextPrimary);
            AssertComboState(combo.StyleFocused, AppThemeTokens.SurfaceHover, AppThemeTokens.TextPrimary);
            AssertComboState(combo.StylePressed, AppThemeTokens.SurfaceSecondary, AppThemeTokens.TextPrimary);
            AssertComboState(combo.StyleDisabled, AppThemeTokens.SurfaceSecondary, AppThemeTokens.TextDisabled);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    private static void AssertComboState(ControlStyle style, string background, string foreground)
    {
        Assert.Equal(AppUi.GetColor(background), style.GetBackgroundColor());
        Assert.Equal(AppUi.GetColor(foreground), style.GetForegroundColor());
        Assert.Null(style.BackgroundBrush);
        Assert.Null(style.ForegroundBrush);
    }

    [Theory]
    [InlineData(AppThemeMode.Light)]
    [InlineData(AppThemeMode.Dark)]
    public async Task InputTextAndPlaceholderMeetReadableContrast(AppThemeMode mode)
    {
        var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ModernTubeDownloader.InputContrast.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var settings = new SettingsService(AppPaths.Create(root), new NullAppLogger());
            await settings.LoadAsync();
            settings.Current.ThemeMode = mode;
            using var host = ModernFormsTestHost.Create();
            using var appearance = new AppAppearanceService(settings, new NullAppLogger());
            appearance.Initialize();

            using var input = new TextBox { Text = "C:\\Videos", Placeholder = "Select a folder" };
            AppUi.Input(input);
            using var combo = new ComboBox();
            AppUi.Input(combo);
            combo.Items.Add("1080p");
            combo.SelectedIndex = 0;
            using var number = new NumericUpDown { Value = 20 };
            AppUi.Input(number);
            host.Show(number, 200, 40);
            host.ProcessPendingWork();
            var editorPeer = Assert.IsAssignableFrom<Control.ControlAccessibleObject>(number.AccessibilityObject.GetChild(0));
            var editor = Assert.IsAssignableFrom<TextBox>(editorPeer.Owner);

            var surface = AppUi.GetColor(AppThemeTokens.Surface);
            var disabledSurface = AppUi.GetColor(AppThemeTokens.SurfaceSecondary);
            var disabledText = AppUi.GetColor(AppThemeTokens.TextDisabled);
            Assert.Equal(disabledText, Theme.ForegroundDisabledColor);
            Assert.Equal(surface, Theme.ControlLowColor);

            AssertContrast(input.CurrentStyle.GetForegroundColor(), surface, 4.5, "normal TextBox text");
            // MFN TextBoxDocument renders both placeholder and disabled text with this legacy color.
            AssertContrast(Theme.ForegroundDisabledColor, surface, 4.5, "placeholder");
            AssertContrast(Theme.ForegroundDisabledColor, disabledSurface, 4.5, "disabled input text");
            AssertContrast(combo.Style.GetForegroundColor(), combo.Style.GetBackgroundColor(), 4.5, "selected ComboBox text");
            AssertContrast(combo.StyleDisabled.GetForegroundColor(), combo.StyleDisabled.GetBackgroundColor(), 4.5, "disabled ComboBox text");
            AssertContrast(editor.CurrentStyle.GetForegroundColor(), Theme.ControlLowColor, 4.5, "NumericUpDown value");
            AssertContrast(number.CurrentStyle.GetForegroundColor(), surface, 4.5, "NumericUpDown spinner arrows");
            AssertContrast(AppUi.GetColor(AppThemeTokens.TextSecondary), surface, 4.5, "secondary help text");
            AssertContrast(AppUi.GetColor(AppThemeTokens.TextPrimary), surface, 4.5, "CheckBox text");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    private static void AssertContrast(SKColor foreground, SKColor background, double minimum, string description)
    {
        static double Luminance(SKColor color)
        {
            static double Linear(byte component)
            {
                var value = component / 255d;
                return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
            }
            return 0.2126 * Linear(color.Red) + 0.7152 * Linear(color.Green) + 0.0722 * Linear(color.Blue);
        }

        var foregroundLuminance = Luminance(foreground);
        var backgroundLuminance = Luminance(background);
        var ratio = (Math.Max(foregroundLuminance, backgroundLuminance) + 0.05) /
            (Math.Min(foregroundLuminance, backgroundLuminance) + 0.05);
        Assert.True(ratio >= minimum, $"{description}: contrast {ratio:F2}:1 is below {minimum:F1}:1.");
    }

    [Theory]
    [InlineData("en", false)]
    [InlineData("pl", true)]
    public void DetailsWindow_ExposesStableSections_WithFullOrMissingMetadata(string language, bool sparse)
    {
        using var host = ModernFormsTestHost.Create();
        var metadata = new VideoMetadata
        {
            Id = "headless-1",
            Title = sparse ? "" : "Integration sample — 日本語",
            Description = sparse ? null : "Description",
            Channel = sparse ? null : "Channel",
            Formats = sparse ? [] : [new MediaFormat { Id = "137", Extension = "mp4" }]
        };
        using var form = new VideoDetailsForm(metadata, new LocalizationService(language));
        var window = host.Show(form, 980, 740);
        host.ProcessPendingWork();

        Assert.Equal("DetailsWindow", form.AccessibilityObject.AutomationId);
        var tabs = Assert.Single(form.Controls.OfType<TabControl>());
        Assert.Equal("DetailsTabs", tabs.AccessibleAutomationId);
        Assert.Equal(new[]
        {
            "DetailsOverviewContent", "DetailsSourceContent", "DetailsMetadataContent", "DetailsFormatsContent",
            "DetailsSubtitlesContent", "DetailsChaptersContent"
        }, tabs.TabPages.Select(page => page.AccessibleAutomationId));
        Assert.Equal(new[]
        {
            "DetailsOverviewTab", "DetailsSourceTab", "DetailsMetadataTab", "DetailsFormatsTab",
            "DetailsSubtitlesTab", "DetailsChaptersTab"
        }, Enumerable.Range(0, tabs.AccessibilityObject.GetChildCount())
            .Select(tabs.AccessibilityObject.GetChild)
            .Where(peer => peer?.ControlType == ModernFormsNext.Accessibility.AccessibleControlType.TabItem)
            .Select(peer => peer!.AutomationId));
        Assert.All(tabs.TabPages, page => Assert.Single(page.Controls.OfType<TextBox>()));
        Assert.NotEmpty(window.CaptureTree().Children);
    }

    [Fact]
    public void QueueCard_CanBeHostedAndUpdatedWithoutNativeWindow()
    {
        var paths = AppPaths.Create(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ModernTubeDownloader.AutomationTests", Guid.NewGuid().ToString("N")));
        using var thumbnails = new ThumbnailCacheService(paths, new NullAppLogger(), () => false);
        using var host = ModernFormsTestHost.Create();
        var card = new QueueItemCard(thumbnails, new LocalizationService("en"));
        var window = host.Show(card, 900, 180);
        card.UpdateItem(new DownloadQueueItem { Title = "Headless queue item", VideoId = "headless-1" }, false);
        card.SetQueuePosition("1");
        host.ProcessPendingWork();

        Assert.NotEmpty(window.CaptureTree().Children);
        var retrying = new DownloadQueueItem
        {
            Title = "Headless retry item", VideoId = "headless-2", Status = DownloadStatus.Waiting,
            AttemptCount = 1, MaximumAttempts = 3, RetryDelaySeconds = 6
        };
        card.UpdateItem(retrying, false);
        host.ProcessPendingWork();
        Assert.Equal($"QueueStatus-{retrying.Id:N}", card.Controls.OfType<Label>()
            .Single(label => label.AccessibleAutomationId?.StartsWith("QueueStatus-", StringComparison.Ordinal) == true)
            .AccessibleAutomationId);
        Assert.Contains("2/3", card.Controls.OfType<Label>()
            .Single(label => label.AccessibleAutomationId == $"QueueStatus-{retrying.Id:N}").Text);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("pl")]
    public void MediaRangeEditor_ExposesStableAutomationIdsAndValidatesInputs(string language)
    {
        using var host = ModernFormsTestHost.Create();
        using var editor = new MediaRangeEditor(new LocalizationService(language), "Video");
        var window = host.Show(editor, 520, 40);
        host.ProcessPendingWork();
        Assert.NotEmpty(window.CaptureTree().Children);
        Find<ComboBox>(editor, "VideoRangeMode").SelectedIndex = 1;
        Find<TextBox>(editor, "VideoRangeStart").Text = "00:00:02";
        Find<TextBox>(editor, "VideoRangeEnd").Text = "00:00:10";
        Assert.True(editor.TryGetRange(12.5, false, out var range, out var error), error);
        Assert.Equal("*2-10", range.ToYtDlpSection());
        Assert.False(editor.TryGetRange(5, false, out _, out error));
        Assert.Equal("Range.Error.Duration", error);
    }

    [Fact]
    public async Task SettingsExposeRetryAndPacingControlsToTestHost()
    {
        var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ModernTubeDownloader.SettingsAutomation.Tests", Guid.NewGuid().ToString("N"));
        var paths = AppPaths.Create(root);
        paths.EnsureCreated();
        var settings = new SettingsService(paths, new NullAppLogger());
        await settings.LoadAsync();
        settings.Current.UseCustomYtDlp = true;
        settings.Current.CustomYtDlpPath = CoreTests.FakeToolPath();
        settings.Current.UseCustomFfmpeg = true;
        settings.Current.CustomFfmpegPath = CoreTests.FakeToolPath();
        settings.Current.CustomFfprobePath = CoreTests.FakeToolPath();
        var denoDirectory = System.IO.Path.Combine(root, "deno");
        Directory.CreateDirectory(denoDirectory);
        foreach (var source in Directory.EnumerateFiles(AppContext.BaseDirectory, "ModernTubeDownloader.FakeTool*"))
            File.Copy(source, System.IO.Path.Combine(denoDirectory, System.IO.Path.GetFileName(source)));
        var denoPath = System.IO.Path.Combine(denoDirectory, "deno.exe");
        File.Copy(CoreTests.FakeToolPath(), denoPath);
        settings.Current.UseCustomDeno = true;
        settings.Current.CustomDenoPath = denoPath;
        await settings.SaveAsync();

        using (var services = AppServices.Create(paths))
        {
            await services.Tools.Initialization;
            using (var host = ModernFormsTestHost.Create())
            {
                var view = new SettingsView(services);
                var window = host.Show(view, 1200, 800);
                host.ProcessPendingWork();
                Assert.NotEmpty(window.CaptureTree().Children);
                Assert.True(Find<CheckBox>(view, "RetryFailedDownloadsToggle").Checked);
                Assert.Equal(2, Find<NumericUpDown>(view, "AdditionalRetryAttempts").Value);
                Assert.False(Find<CheckBox>(view, "InterDownloadDelayToggle").Checked);
                Assert.Equal(3, Find<NumericUpDown>(view, "MinimumInterDownloadDelay").Value);
                Assert.Equal(20, Find<NumericUpDown>(view, "MaximumInterDownloadDelay").Value);
                Assert.False(Find<CheckBox>(view, "WebRemoteEnable").Checked);
                var authToggle = Find<CheckBox>(view, "WebRemoteRequireAuthentication");
                Assert.True(authToggle.Checked);
                Assert.False(Find<Label>(view, "WebRemoteAuthWarning").Visible);
                Assert.Equal(18765, Find<NumericUpDown>(view, "WebRemotePort").Value);
                Assert.Equal("127.0.0.1", Find<ComboBox>(view, "WebRemoteAddress").SelectedItem?.ToString());
                Assert.False(string.IsNullOrWhiteSpace(Find<TextBox>(view, "WebRemoteToken").Text));
                var scroll = Assert.Single(view.Controls.OfType<AppScrollPanel>());
                scroll.VerticalScrollProperties.Value = 600;
                host.ProcessPendingWork();
                var offsetBeforeAuthChange = scroll.VerticalScrollProperties.Value;
                var maxBeforeAuthChange = scroll.VerticalScrollProperties.Maximum;
                Assert.True(offsetBeforeAuthChange > 0);
                authToggle.Checked = false;
                host.ProcessPendingWork();
                Assert.True(Find<Label>(view, "WebRemoteAuthWarning").Visible);
                Assert.False(Find<TextBox>(view, "WebRemoteToken").Visible);
                Assert.Equal(offsetBeforeAuthChange, scroll.VerticalScrollProperties.Value);
                Assert.Equal(scroll.DisplayRectangle.Top, scroll.Controls[0].Top);
                Assert.Equal(scroll.DisplayRectangle.Left + 24, scroll.Controls[0].Left);
                Assert.Equal(maxBeforeAuthChange - 44, scroll.VerticalScrollProperties.Maximum);
            }
            await services.ShutdownAsync();
        }
        Directory.Delete(root, recursive: true);
    }

    [Theory]
    [InlineData("en", AppThemeMode.Light)]
    [InlineData("pl", AppThemeMode.Light)]
    [InlineData("en", AppThemeMode.Dark)]
    [InlineData("pl", AppThemeMode.Dark)]
    [InlineData("en", AppThemeMode.System)]
    [InlineData("pl", AppThemeMode.System)]
    public async Task VideoCardActionsUseSharedStylesAndCompactLocalizedLabel(string language, AppThemeMode mode)
    {
        var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ModernTubeDownloader.CardStyle.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var paths = AppPaths.Create(root);
            paths.EnsureCreated();
            var settings = new SettingsService(paths, new NullAppLogger());
            await settings.LoadAsync();
            settings.Current.Language = language;
            settings.Current.ThemeMode = mode;
            settings.Current.QueuePaused = true;
            settings.Current.UseCustomYtDlp = settings.Current.UseCustomFfmpeg = settings.Current.UseCustomDeno = true;
            settings.Current.CustomYtDlpPath = settings.Current.CustomFfmpegPath = settings.Current.CustomFfprobePath = CoreTests.FakeToolPath();
            var denoDirectory = System.IO.Path.Combine(root, "deno");
            Directory.CreateDirectory(denoDirectory);
            foreach (var source in Directory.EnumerateFiles(AppContext.BaseDirectory, "ModernTubeDownloader.FakeTool*"))
                File.Copy(source, System.IO.Path.Combine(denoDirectory, System.IO.Path.GetFileName(source)));
            settings.Current.CustomDenoPath = System.IO.Path.Combine(denoDirectory, "deno.exe");
            File.Copy(CoreTests.FakeToolPath(), settings.Current.CustomDenoPath);
            await settings.SaveAsync();

            using var services = AppServices.Create(paths);
            await services.Tools.Initialization;
            using (var host = ModernFormsTestHost.Create())
            {
                services.Appearance.Initialize();
                var view = new DownloadsView(services);
                host.Show(view, 1000, 720);
                host.ProcessPendingWork();
                var more = Find<Button>(view, "AdvancedOptionsButton");
                Assert.Equal(services.Localization["Advanced.MoreOptions"], more.Text);
                foreach (var id in new[] { "AddToQueueButton", "DetailsButton", "AdvancedOptionsButton" })
                {
                    var button = Find<Button>(view, id);
                    var primary = id == "AddToQueueButton";
                    Assert.Equal("Segoe UI", button.Font.FamilyName);
                    // MFN's Control.Font getter exposes its integer style font size.
                    Assert.Equal(10f, button.Font.SizeInPoints);
                    foreach (var style in new[] { button.Style, button.StyleHover, button.StyleFocused, button.StylePressed })
                    {
                        Assert.Equal(10, style.Border.Radius);
                        Assert.Equal(0, style.Border.GetWidth());
                        Assert.Equal(button.Font, style.TextFont);
                        AssertContrast(style.GetForegroundColor(), style.GetBackgroundColor(), 4.5, id);
                    }
                    Assert.Equal(AppUi.GetColor(primary ? AppThemeTokens.Accent : AppThemeTokens.SurfaceSecondary), button.Style.GetBackgroundColor());
                }
            }
            await services.ShutdownAsync();
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    private static T Find<T>(Control root, string automationId) where T : Control
    {
        if (root is T match && root.AccessibleAutomationId == automationId) return match;
        foreach (Control child in root.Controls)
        {
            try { return Find<T>(child, automationId); }
            catch (InvalidOperationException) { }
        }
        throw new InvalidOperationException($"AutomationId not found: {automationId}");
    }
}
