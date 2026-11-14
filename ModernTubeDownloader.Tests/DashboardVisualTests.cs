using ModernFormsNext;
using ModernFormsNext.Accessibility;
using ModernFormsNext.Testing;
using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Localization;
using ModernTubeDownloader.Models;
using ModernTubeDownloader.Services;
using ModernTubeDownloader.Theming;
using ModernTubeDownloader.Views;
using SkiaSharp;
using Path = System.IO.Path;

namespace ModernTubeDownloader.Tests;

[Collection("ModernFormsNext TestHost")]
public sealed class DashboardVisualTests
{
    [Theory]
    [InlineData(238, 1f)]
    [InlineData(32, 1f)]
    [InlineData(200, 0.6f)]
    public void SolidDashboardTextBrushPreservesNativeUnicodeColorsAndOpacity(byte channel, float opacity)
    {
        using var host = ModernFormsTestHost.Create();
        var panel = new Panel(); AppTypography.SetDashboardPresentation(panel, true);
        var label = new DashboardLabel { Text = "Zażółć 日本語 🌄 🎬", Width = 800, Height = 90, Font = new Font("Segoe UI", 20) };
        var brush = new ModernFormsNext.Drawing.SolidColorBrush(new SKColor(channel, channel, channel, 230)) { Opacity = opacity };
        label.Style.ForegroundColor = brush.Color.WithAlpha((byte)MathF.Round(230 * opacity));
        panel.Controls.Add(label); var window = host.Show(panel, 820, 110);
        using var native = window.CaptureRenderedSnapshot();
        label.TextBrush = brush;
        using var solid = window.CaptureRenderedSnapshot();
        Assert.True(native.CopyPixels().SequenceEqual(solid.CopyPixels()), "A solid theme brush must preserve shaped color glyphs and ordinary text, including alpha.");
        Assert.Equal("Zażółć 日本語 🌄 🎬", label.Text);
    }

    [Theory]
    [InlineData(100)] [InlineData(150)] [InlineData(200)]
    public void MetricValuesRemainVisibleAtEveryTextScale(int scale)
    {
        using var host = ModernFormsTestHost.Create();
        AppTypography.SetScale(scale);
        try
        {
            using var root = new Panel(); AppTypography.SetDashboardPresentation(root, true);
            var metric = new DashboardMetric(AppThemeTokens.Success); root.Controls.Add(metric);
            metric.Present("Gotowe", 7);
            metric.SetBounds(0, 0, 173, metric.DesiredHeight(173));
            var window = host.Show(root, 200, metric.Height + 20);
            using var first = window.CaptureRenderedSnapshot();
            metric.Present("Gotowe", 9);
            using var second = window.CaptureRenderedSnapshot();
            Assert.False(first.CopyPixels().SequenceEqual(second.CopyPixels()), "Changing the value must change rendered pixels, not only accessible text.");
        }
        finally { AppTypography.SetScale(100); }
    }

    [Fact]
    public void DisplayedOutputFormatUsesTheExecutorSelectionAndCompletedFile()
    {
        var item = new DownloadQueueItem { QualityPresetId = "audio", PreferredVideoContainer = PreferredVideoContainer.Mp4 };
        Assert.Equal("—", DashboardStyle.OutputFormat(item));
        item.SelectedFormats = new(null, "140", "", "m4a", true, false);
        Assert.Equal("M4A", DashboardStyle.OutputFormat(item));
        item.Status = DownloadStatus.Completed; item.FinalFile = "finished.ogg";
        Assert.Equal("OGG", DashboardStyle.OutputFormat(item));
        item.Status = DownloadStatus.Queued; item.QualityPresetId = "1080";
        item.SelectedFormats = new("137", "140", "mp4", "m4a", false, true);
        Assert.Equal("MP4", DashboardStyle.OutputFormat(item));
    }
    [Fact]
    public void DashboardButtonGradientsAreReusedAndRestoreEveryClassicState()
    {
        using var host = ModernFormsTestHost.Create();
        using var button = new Button(); AppUi.Secondary(button);
        var disabled = new ModernFormsNext.Drawing.SolidColorBrush(SKColors.Orange);
        button.StyleDisabled.BackgroundBrush = disabled;
        DashboardVisual.Button(button);
        var normal = button.Style.BackgroundBrush;
        Assert.IsType<ModernFormsNext.Drawing.LinearGradientBrush>(normal);
        Assert.IsType<ModernFormsNext.Drawing.LinearGradientBrush>(button.StyleDisabled.BackgroundBrush);
        DashboardVisual.Button(button);
        Assert.Same(normal, button.Style.BackgroundBrush);
        DashboardVisual.RestoreTree(button);
        Assert.Null(button.Style.BackgroundBrush);
        Assert.Null(button.StyleHover.BackgroundBrush);
        Assert.Null(button.StylePressed.BackgroundBrush);
        Assert.Null(button.StyleFocused.BackgroundBrush);
        Assert.Same(disabled, button.StyleDisabled.BackgroundBrush);
    }

    [Theory]
    [InlineData(100)] [InlineData(150)] [InlineData(200)]
    public void ReapplyingTheSameTextRecipeDoesNotNotifyAFontChange(int scale)
    {
        using var host = ModernFormsTestHost.Create();
        AppTypography.SetScale(scale);
        try
        {
            using var root = new Panel();
            var label = AppUi.Muted("Stable typography — Łódź 日本語"); root.Controls.Add(label);
            foreach (var modern in new[] { false, true, false })
            {
                AppTypography.SetDashboardPresentation(root, modern);
                AppTypography.Bind(label, 9.5f);
                var before = label.Font;
                var changes = 0;
                EventHandler changed = (_, _) => changes++;
                label.FontChanged += changed;
                for (var i = 0; i < 20; i++)
                {
                    AppTypography.Bind(label, 9.5f);
                    AppTypography.SetDashboardPresentation(root, modern);
                }
                label.FontChanged -= changed;
                Assert.Equal(before, label.Font);
                Assert.Equal(0, changes);
            }
        }
        finally { AppTypography.SetScale(100); }
    }

    [Theory]
    [InlineData(100)] [InlineData(150)] [InlineData(200)]
    public void FittingUnicodeTextHasIdenticalPixelsWithAndWithoutEllipsis(int scale)
    {
        using var host = ModernFormsTestHost.Create();
        AppTypography.SetScale(scale);
        try
        {
            var panel = new Panel(); AppTypography.SetDashboardPresentation(panel, true);
            var label = new DashboardLabel { Text = "Integration Sample — Łódź 日本語", Width = 1500, Height = 90, Font = new Font("Segoe UI", 20 * scale / 100f) };
            panel.Controls.Add(label); var window = host.Show(panel, 1520, 140);
            using var without = window.CaptureRenderedSnapshot();
            label.AutoEllipsis = true;
            using var with = window.CaptureRenderedSnapshot();
            Assert.True(without.CopyPixels().SequenceEqual(with.CopyPixels()), "Fitting Unicode must not acquire a premature ellipsis.");
            label.Text = new string('W', 400); label.Width = 180;
            using var truncated = window.CaptureRenderedSnapshot(); label.AutoEllipsis = false;
            using var clipped = window.CaptureRenderedSnapshot();
            Assert.False(truncated.CopyPixels().SequenceEqual(clipped.CopyPixels()), "Actual overflow must visibly expose the ellipsis.");
        }
        finally { AppTypography.SetScale(100); }
    }

    [Fact]
    public void DynamicallyAttachedTextScalesAndRestoresItsClassicStateRecipe()
    {
        using var host = ModernFormsTestHost.Create();
        var classic = new Panel(); var modern = new Panel(); AppTypography.SetDashboardPresentation(modern, true);
        var label = AppUi.Muted("Dynamic facts"); var original = label.Font.Size; var hoverSize = label.StyleHover.FontSize;
        modern.Controls.Add(label); Assert.True(label.Font.Size > original); Assert.Equal(label.Font.Size, label.StyleHover.GetFontSize());
        modern.Controls.Remove(label); classic.Controls.Add(label);
        Assert.Equal(original, label.Font.Size); Assert.Equal(hoverSize, label.StyleHover.FontSize);
        classic.Dispose(); modern.Dispose();
    }

    [Fact]
    public void ModernSwitchRetainsTheCanonicalToggleActionAndCheckedState()
    {
        using var host = ModernFormsTestHost.Create();
        var root = new Panel(); AppTypography.SetDashboardPresentation(root, true);
        var toggle = new DashboardToggle { Text = "Notifications", Width = 300, Height = 36 }; root.Controls.Add(toggle); host.Show(root, 400, 140);
        var provider = toggle.AccessibilityObject;
        Assert.Equal(AccessibleControlType.CheckBox, provider.ControlType);
        Assert.True(provider.PerformAction(AccessibleActions.Toggle)); Assert.True(toggle.Checked);
        Assert.True(provider.PerformAction(AccessibleActions.Toggle)); Assert.False(toggle.Checked);
    }

    [Fact]
    public void RemovingModernPresentationRestoresBorderRecipes()
    {
        var button = new Button(); var width = button.Style.Border.Width; var radius = button.Style.Border.Radius; var color = button.Style.Border.Color;
        DashboardVisual.Flat(button, true); DashboardVisual.RestoreTree(button);
        Assert.Equal(width, button.Style.Border.Width); Assert.Equal(radius, button.Style.Border.Radius); Assert.Equal(color, button.Style.Border.Color);
        button.Dispose();
    }

    [Theory]
    [InlineData(100)] [InlineData(150)] [InlineData(200)]
    public void QueueStatusKeepsClassicTypographyAndModernIconsDoNotSurviveRecovery(int scale)
    {
        var directory = Path.Combine(Path.GetTempPath(), "mtd-classic-row-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var host = ModernFormsTestHost.Create();
            AppTypography.SetScale(scale);
            var paths = AppPaths.Create(directory); paths.EnsureCreated();
            using var thumbnails = new ThumbnailCacheService(paths, new NullAppLogger(), () => false);
            var localization = new LocalizationService("en");
            var row = new QueueItemCard(thumbnails, localization) { Width = 1200 };
            var item = new DownloadQueueItem { Title = "Classic status recipe", Status = DownloadStatus.Queued, SourceUrl = "https://example.test/video" };
            row.UpdateItem(item, false);
            host.Show(row, 1200, row.Height);
            var status = row.Controls.OfType<Label>().Single(label => label.Text == localization["Status.Queued"]);
            var originalFont = status.Font;
            Assert.True(originalFont.Bold);

            // Fresh Classic also goes through SetDashboard(false) when queue rows are created.
            row.SetDashboard(false); row.UpdateItem(item, false);
            Assert.Equal(originalFont, status.Font);
            row.SetDashboard(true); row.UpdateItem(item, false);
            var icons = row.Controls.OfType<Button>().SelectMany(button => button.Controls.OfType<AppVectorIcon>()).ToArray();
            Assert.NotEmpty(icons);
            Assert.All(icons, icon => Assert.True(icon.Visible));
            Assert.False(status.Font.Bold);

            DashboardVisual.RestoreTree(row);
            row.SetDashboard(false); row.UpdateItem(item, false);
            Assert.Equal(originalFont, status.Font);
            Assert.All(icons, icon => Assert.False(icon.Visible));
            Assert.Equal(localization["Common.Remove"], row.Controls.OfType<Button>().First().Text);
        }
        finally
        {
            AppTypography.SetScale(100);
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ModernButtonIconRestoresClassicPaddingAndIsReusedAcrossPresentationSwitches()
    {
        using var host = ModernFormsTestHost.Create();
        var button = new Button { Text = "Paste", Width = 130, Height = 38, Padding = new Padding(4) };
        host.Show(button, 130, 38);
        button.Style.Padding = new Padding(6, 1, 7, 1);
        var originalPadding = button.Padding;
        var originalStylePadding = button.Style.Padding;
        DashboardVisual.Button(button);
        DashboardVisual.Icon(button, AppIconKind.Clipboard);
        var icon = Assert.Single(button.Controls.OfType<AppVectorIcon>());
        Assert.True(icon.Visible);
        Assert.Equal(AccessibilityView.Hidden, icon.AccessibilityView);

        DashboardVisual.RestoreTree(button);
        Assert.False(icon.Visible);
        Assert.Equal(originalPadding, button.Padding);
        Assert.Equal(originalStylePadding, button.Style.Padding);
        Assert.Equal("Paste", button.Text);

        DashboardVisual.Button(button);
        DashboardVisual.Icon(button, AppIconKind.Clipboard);
        Assert.Same(icon, Assert.Single(button.Controls.OfType<AppVectorIcon>()));
        Assert.True(icon.Visible);
    }

    [Fact]
    public void ModernButtonIconIsPublishedBeforeControlAddedCanReenterPresentation()
    {
        using var host = ModernFormsTestHost.Create();
        var button = new Button { Width = 130, Height = 38 };
        host.Show(button, 130, 38);
        var reentered = false;
        button.ControlAdded += (_, _) =>
        {
            if (reentered) return;
            reentered = true;
            DashboardVisual.Icon(button, AppIconKind.Clipboard);
        };

        DashboardVisual.Icon(button, AppIconKind.Clipboard);

        Assert.True(reentered);
        Assert.Single(button.Controls.OfType<AppVectorIcon>());
    }

    [Theory]
    [InlineData(100)] [InlineData(150)] [InlineData(200)]
    public void MeasuredModernActionFitsLocalizedCaptionWithoutPrematureEllipsis(int scale)
    {
        var directory = Path.Combine(Path.GetTempPath(), "mtd-action-raster-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var host = ModernFormsTestHost.Create();
            var paths = AppPaths.Create(directory); paths.EnsureCreated();
            var settings = new SettingsService(paths, new NullAppLogger());
            using var appearance = new AppAppearanceService(settings, new NullAppLogger());
            appearance.Profile.Global.Animations = false; appearance.Initialize();
            AppTypography.SetScale(scale);
            var root = new Panel(); AppTypography.SetDashboardPresentation(root, true);
            var button = new DashboardButton { Text = "Usuń zakończone · Łódź 日本語", Height = AppTypography.TextHeight(42), TabStop = false };
            root.Controls.Add(button);
            DashboardVisual.Button(button);
            DashboardVisual.Icon(button, AppIconKind.Trash);
            button.Width = DashboardVisual.ActionWidth(button, withIcon: true);
            var window = host.Show(root, button.Width + 32, button.Height + 32);
            Assert.Equal(255, AppUi.GetColor(AppThemeTokens.TextPrimary).Alpha);
            using var without = window.CaptureRenderedSnapshot();
            button.AutoEllipsis = true;
            using var with = window.CaptureRenderedSnapshot();
            Assert.True(without.CopyPixels().SequenceEqual(with.CopyPixels()), "Measured localized action must fit its available field without ellipsis.");

            button.Text = new string('W', 200);
            using var overflow = window.CaptureRenderedSnapshot();
            button.AutoEllipsis = false;
            using var clipped = window.CaptureRenderedSnapshot();
            Assert.False(overflow.CopyPixels().SequenceEqual(clipped.CopyPixels()), "Actual action overflow must still render an ellipsis.");
        }
        finally
        {
            AppTypography.SetScale(100); AppearancePresentation.Current = new();
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void DashboardActionDelegatesClassicAndImageRenderingToNativeButton(bool modernImage)
    {
        using var host = ModernFormsTestHost.Create();
        using var image = new SKBitmap(16, 16); image.Erase(SKColors.Coral);
        var root = new Panel();
        var native = new Button { Text = "Native action Łódź", Width = 330, Height = 54, TabStop = false, AutoEllipsis = true, Font = new Font("Segoe UI", 18) };
        var dashboard = new DashboardButton { Text = native.Text, Width = native.Width, Height = native.Height, TabStop = false, AutoEllipsis = true, Font = native.Font, Visible = false };
        // Production actions participate in the typography binding, including invalidation
        // and state-font restoration when presentation changes.
        AppTypography.Bind(native, 18); AppTypography.Bind(dashboard, 18);
        if (modernImage) native.Image = dashboard.Image = image;
        root.Controls.AddRange([native, dashboard]);
        AppTypography.SetDashboardPresentation(root, modernImage);
        var window = host.Show(root, 370, 90);
        using var expected = window.CaptureRenderedSnapshot();
        native.Visible = false; dashboard.Visible = true;
        if (!modernImage)
        {
            AppTypography.SetDashboardPresentation(root, true);
            using var modern = window.CaptureRenderedSnapshot();
            AppTypography.SetDashboardPresentation(root, false);
        }
        using var actual = window.CaptureRenderedSnapshot();
        Assert.True(expected.CopyPixels().SequenceEqual(actual.CopyPixels()), "Classic recovery and image actions must retain the native Button renderer.");
    }

    [Fact]
    public void DashboardActionRetainsCanonicalInvokeAndDisabledBehavior()
    {
        using var host = ModernFormsTestHost.Create();
        var root = new Panel(); AppTypography.SetDashboardPresentation(root, true);
        var button = new DashboardButton { Text = "Queue", Width = 150, Height = 42 };
        var clicks = 0; button.Click += (_, _) => clicks++;
        root.Controls.Add(button); host.Show(root, 200, 100);
        var provider = button.AccessibilityObject;
        Assert.Equal(AccessibleControlType.Button, provider.ControlType);
        Assert.True(provider.PerformAction(AccessibleActions.Invoke)); Assert.Equal(1, clicks);
        button.Enabled = false;
        Assert.False(provider.PerformAction(AccessibleActions.Invoke)); Assert.Equal(1, clicks);
        button.Enabled = true; AppTypography.SetDashboardPresentation(root, false);
        Assert.Same(provider, button.AccessibilityObject);
        Assert.True(provider.PerformAction(AccessibleActions.Invoke)); Assert.Equal(2, clicks);
    }

    [Fact]
    public void DashboardActionPaintsChildVisualsThroughTheNativeCompositionPath()
    {
        using var host = ModernFormsTestHost.Create();
        var root = new Panel(); AppTypography.SetDashboardPresentation(root, true);
        var button = new DashboardButton { Text = "Action", Width = 200, Height = 50, TabStop = false };
        var child = new Panel { Left = 8, Top = 15, Width = 20, Height = 20 };
        child.Style.BackgroundColor = SKColors.Magenta; child.Style.Border.Width = 0;
        button.Controls.Add(child); root.Controls.Add(button);
        var window = host.Show(root, 240, 90);
        using var visible = window.CaptureRenderedSnapshot();
        child.Visible = false;
        using var hidden = window.CaptureRenderedSnapshot();
        Assert.False(visible.CopyPixels().SequenceEqual(hidden.CopyPixels()), "The custom text renderer must preserve native child composition used by action icons.");
    }

    [Fact]
    public void ActionIconReflectsInheritedDisabledStateAndRestoresItsSemanticColor()
    {
        var directory = Path.Combine(Path.GetTempPath(), "mtd-icon-state-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var host = ModernFormsTestHost.Create();
            var paths = AppPaths.Create(directory); paths.EnsureCreated();
            using var appearance = new AppAppearanceService(new SettingsService(paths, new NullAppLogger()), new NullAppLogger());
            appearance.Initialize();
            var button = new DashboardButton { Width = 42, Height = 42 };
            DashboardVisual.Icon(button, AppIconKind.Play, iconOnly: true);
            host.Show(button, 80, 80);
            var icon = Assert.Single(button.Controls.OfType<AppVectorIcon>());
            var shape = Assert.Single(icon.Controls.OfType<Shape>());
            var enabled = ((ModernFormsNext.Drawing.SolidColorBrush)shape.Stroke!).Color;
            button.Enabled = false;
            var disabled = ((ModernFormsNext.Drawing.SolidColorBrush)shape.Stroke!).Color;
            var expected = AppUi.GetColor(AppThemeTokens.TextDisabled);
            Assert.NotEqual(enabled, disabled);
            Assert.Equal(expected, disabled);
            button.Enabled = true;
            Assert.Equal(enabled, ((ModernFormsNext.Drawing.SolidColorBrush)shape.Stroke!).Color);
        }
        finally
        {
            AppearancePresentation.Current = new();
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }
}
