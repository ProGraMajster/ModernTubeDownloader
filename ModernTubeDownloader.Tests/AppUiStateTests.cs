using System.Drawing;
using ModernFormsNext;
using ModernFormsNext.Testing;
using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Services;
using ModernTubeDownloader.Settings;
using ModernTubeDownloader.Theming;
using SkiaSharp;
using Path = System.IO.Path;

namespace ModernTubeDownloader.Tests;

[Collection("ModernFormsNext TestHost")]
public sealed class AppUiStateTests
{
    [Theory]
    [InlineData(false, AppThemeMode.Dark)]
    [InlineData(false, AppThemeMode.Light)]
    [InlineData(true, AppThemeMode.Dark)]
    [InlineData(true, AppThemeMode.Light)]
    public void InformationalTextDoesNotAcquireBackgroundOnPointerOrClick(bool modern, AppThemeMode theme)
    {
        WithAppearance(theme, host =>
        {
            var root = new Panel(); AppUi.BindBackground(root); AppTypography.SetDashboardPresentation(root, modern);
            var labels = new[] { AppUi.Heading("Kolejka jest pusta"), AppUi.Heading("Wykres szybkości", 14), AppUi.Muted("Miejsce na dysku 日本語") };
            for (var i = 0; i < labels.Length; i++) { labels[i].SetBounds(12, 12 + i * 55, 460, 45); root.Controls.Add(labels[i]); }
            var form = new Form(); root.Dock = DockStyle.Fill; form.Controls.Add(root);
            var window = host.Show(form, 500, 240);
            using var normal = window.CaptureRenderedSnapshot();
            foreach (var label in labels)
            {
                var text = label.Text;
                host.Input.Move(label);
                using (var hover = window.CaptureRenderedSnapshot())
                    Assert.True(normal.CopyPixels().SequenceEqual(hover.CopyPixels()), "A static label must not gain a hover background.");
                var point = label.PointToScreen(new Point(label.Width / 2, label.Height / 2));
                point.Offset(-form.Location.X, -form.Location.Y);
                host.Input.PointerDown(point);
                using (var pressed = window.CaptureRenderedSnapshot())
                    Assert.True(normal.CopyPixels().SequenceEqual(pressed.CopyPixels()), "Pointer activation must not paint a selected static label.");
                host.Input.PointerUp(point);
                host.Input.Move(new Point(490, 190));
                using var released = window.CaptureRenderedSnapshot();
                Assert.True(normal.CopyPixels().SequenceEqual(released.CopyPixels()));
                Assert.Equal(text, label.Text); Assert.False(label.TabStop);
            }
        });
    }

    [Theory]
    [InlineData(AppThemeMode.Dark)]
    [InlineData(AppThemeMode.Light)]
    public void InputFocusUsesSurfaceAndVisibleBorderWithoutChangingValue(AppThemeMode theme)
    {
        WithAppearance(theme, host =>
        {
            var root = new Panel(); AppUi.BindBackground(root);
            var input = new AppThemeTextBox { Text = "unchanged" }; input.SetBounds(15, 15, 300, 42); AppUi.Input(input);
            var combo = new ComboBox(); combo.Items.Add("one"); combo.Items.Add("two"); combo.SelectedIndex = 0;
            combo.SetBounds(15, 70, 300, 42); AppUi.Input(combo); root.Controls.AddRange([input, combo]);
            var window = host.Show(root, 400, 160);
            foreach (var field in new Control[] { input, combo })
            {
                host.Input.Move(new Point(390, 150)); host.Input.Focus(field);
                Assert.Equal(AppUi.GetColor(AppThemeTokens.Surface), field.CurrentStyle.GetBackgroundColor());
                Assert.Equal(AppUi.GetColor(AppThemeTokens.Accent), field.CurrentStyle.Border.GetColor());
                Assert.True(field.CurrentStyle.Border.GetWidth() > 0);
                Assert.Equal(field, host.FocusedControl);
            }
            Assert.Equal("unchanged", input.Text); Assert.Equal(0, combo.SelectedIndex);
            combo.Enabled = false;
            Assert.NotEqual(combo.Style.GetForegroundColor(), combo.CurrentStyle.GetForegroundColor());
        });
    }

    private static void WithAppearance(AppThemeMode theme, Action<ModernFormsTestHost> test)
    {
        var root = Path.Combine(Path.GetTempPath(), "mtd-ui-states-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var host = ModernFormsTestHost.Create();
            var paths = AppPaths.Create(root); paths.EnsureCreated();
            var settings = new SettingsService(paths, new NullAppLogger()); settings.Current.ThemeMode = theme;
            using var appearance = new AppAppearanceService(settings, new NullAppLogger()); appearance.Initialize();
            test(host);
        }
        finally { AppearancePresentation.Current = new(); Directory.Delete(root, true); }
    }
}
