using ModernFormsNext;
using ModernFormsNext.Animations;
using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Services;
using ModernTubeDownloader.Settings;
using ModernTubeDownloader.Theming;
using SkiaSharp;

namespace ModernTubeDownloader.Tests;

public sealed class SidebarNavigationStyleTests : IDisposable
{
    private readonly string root = System.IO.Path.Combine(
        System.IO.Path.GetTempPath(),
        "ModernTubeDownloader.Tests",
        Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData(AppThemeMode.Light, false, "History")]
    [InlineData(AppThemeMode.Light, true, "Downloads")]
    [InlineData(AppThemeMode.Dark, false, "Ustawienia")]
    [InlineData(AppThemeMode.Dark, true, "Pobieranie")]
    [InlineData(AppThemeMode.System, false, "History")]
    [InlineData(AppThemeMode.System, true, "Pobieranie")]
    public async Task NavigationButton_KeepsReadableTextAndConsistentMetricsAcrossStates(
        AppThemeMode themeMode,
        bool selected,
        string label)
    {
        var paths = AppPaths.Create(root);
        paths.EnsureCreated();
        var logger = new NullAppLogger();
        var settings = new SettingsService(paths, logger);
        await settings.LoadAsync();
        settings.Current.ThemeMode = themeMode;

        using var appearance = new AppAppearanceService(settings, logger);
        appearance.Initialize();
        using var button = new NavigationButtonProbe
        {
            Text = label,
            Width = 192,
            Height = 46,
            Padding = new Padding(48, 0, 10, 0),
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI", 11f)
        };

        AppUi.NavigationButton(button, selected);

        var restingTextToken = selected ? AppThemeTokens.NavigationSelectedText : AppThemeTokens.TextPrimary;
        AssertState(button.Style, button.Font, restingTextToken);
        AssertState(button.StyleHover, button.Font, restingTextToken);
        AssertState(button.StyleFocused, button.Font, restingTextToken);
        AssertState(button.StylePressed, button.Font, AppThemeTokens.NavigationSelectedText);
        AssertState(button.StyleDisabled, button.Font, AppThemeTokens.TextSecondary);

        foreach (var stateStyle in new[]
                 {
                     button.Style,
                     button.StyleHover,
                     button.StyleFocused,
                     button.StylePressed,
                     button.StyleDisabled
                 })
        {
            Assert.True(button.RenderedTextPixelCount(stateStyle) > 0);
        }

        if (selected)
        {
            var contrast = ContrastRatio(
                AppUi.GetColor(AppThemeTokens.NavigationSelectedText),
                AppUi.GetColor(AppThemeTokens.NavigationSelected));
            Assert.True(contrast >= 4.5, $"Selected navigation contrast was only {contrast:F2}:1.");
        }

        button.EnterForTest();

        Assert.Equal(VisualState.Hover, button.VisualState);
        Assert.True(button.RenderedTextPixelCount() > 0);

        button.LeaveForTest();
        button.FocusForTest();

        Assert.Equal(VisualState.Focused, button.VisualState);
        Assert.True(button.RenderedTextPixelCount() > 0);

        button.Enabled = false;

        Assert.Equal(VisualState.Disabled, button.VisualState);
        Assert.True(button.RenderedTextPixelCount() > 0);
    }

    private static double ContrastRatio(SKColor foreground, SKColor background)
    {
        static double Luminance(SKColor color)
        {
            static double Channel(byte value)
            {
                var normalized = value / 255d;
                return normalized <= 0.04045
                    ? normalized / 12.92
                    : Math.Pow((normalized + 0.055) / 1.055, 2.4);
            }

            return (0.2126 * Channel(color.Red)) + (0.7152 * Channel(color.Green)) + (0.0722 * Channel(color.Blue));
        }

        var lighter = Math.Max(Luminance(foreground), Luminance(background));
        var darker = Math.Min(Luminance(foreground), Luminance(background));
        return (lighter + 0.05) / (darker + 0.05);
    }

    private static void AssertState(ControlStyle style, Font expectedFont, string foregroundToken)
    {
        Assert.Equal(expectedFont.FamilyName, style.GetFontFamily());
        Assert.Equal((int)Math.Round(expectedFont.SizeInPoints), style.GetFontSize());
        Assert.Equal(expectedFont.Style, style.GetFontStyle());
        Assert.Equal(AppUi.GetColor(foregroundToken), style.GetForegroundColor());
        Assert.Null(style.ForegroundBrush);
        Assert.Equal(10, style.Border.Radius);
        Assert.Equal(0, style.Border.GetWidth());
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
            Directory.Delete(root, recursive: true);
    }

    private sealed class NavigationButtonProbe : Button
    {
        private ControlStyle? styleOverride;

        public override ControlStyle CurrentStyle => styleOverride ?? base.CurrentStyle;

        public void EnterForTest()
            => OnMouseEnter(new MouseEventArgs(MouseButtons.None, 0, 0, 0, System.Drawing.Point.Empty));

        public void LeaveForTest() => OnMouseLeave(EventArgs.Empty);

        public void FocusForTest() => OnGotFocus(EventArgs.Empty);

        public int RenderedTextPixelCount(ControlStyle? style = null)
        {
            styleOverride = style;
            using var bitmap = new SKBitmap(Width, Height);
            bitmap.Erase(SKColors.Transparent);
            using var canvas = new SKCanvas(bitmap);
            try
            {
                OnPaint(new PaintEventArgs(bitmap.Info, canvas, 1d));
                canvas.Flush();
            }
            finally
            {
                styleOverride = null;
            }

            var pixels = 0;
            for (var y = 0; y < bitmap.Height; y++)
            {
                for (var x = 0; x < bitmap.Width; x++)
                {
                    if (bitmap.GetPixel(x, y).Alpha > 0)
                        pixels++;
                }
            }
            return pixels;
        }
    }
}
