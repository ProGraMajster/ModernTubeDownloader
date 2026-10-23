using ModernFormsNext;
using ModernFormsNext.Testing;
using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Settings;
using ModernTubeDownloader.Theming;
using Path = System.IO.Path;

namespace ModernTubeDownloader.Tests;

[Collection("ModernFormsNext TestHost")]
public sealed class DashboardThemeInputTests
{
    [Theory]
    [InlineData(AppThemeMode.Dark, AppThemeMode.Light)]
    [InlineData(AppThemeMode.Light, AppThemeMode.Dark)]
    public async Task ThemeSwitchRendersExistingInputExactlyLikeFreshInput(AppThemeMode initial, AppThemeMode target)
    {
        var root = Path.Combine(Path.GetTempPath(), "mtd-input-theme-" + Guid.NewGuid().ToString("N"));
        var paths = AppPaths.Create(root); paths.EnsureCreated();
        Task? write = null;
        try
        {
            using (var host = ModernFormsTestHost.Create())
            {
                var settings = new ModernTubeDownloader.Services.SettingsService(paths, new NullAppLogger());
                settings.Current.ThemeMode = initial;
                using var appearance = new AppAppearanceService(settings, new NullAppLogger());
                appearance.Profile.InterfaceMode = AppInterfaceMode.ModernDashboard;
                appearance.Profile.Global.Animations = true;
                appearance.Initialize();
                var panel = new Panel(); AppUi.Surface(panel);
                var window = host.Show(panel, 600, 100);
                // The real shell exists before its inspectors. Late-created editor default
                // styles subscribe to the legacy theme event after the window's cache refresh.
                var input = Input(); panel.Controls.Add(input);
                using var before = window.CaptureRenderedSnapshot();
                var changes = 0; input.TextChanged += (_, _) => changes++;
                write = appearance.SetModeAsync(target);
                host.Clock.Advance(TimeSpan.FromSeconds(1));
                using var existing = window.CaptureRenderedSnapshot();
                Assert.Equal("00:00:02 — Theme Łódź", input.Text);
                Assert.Equal(0, changes);
                panel.Controls.Remove(input); input.Dispose();
                panel.Controls.Add(Input());
                using var fresh = window.CaptureRenderedSnapshot();
                Assert.False(before.CopyPixels().SequenceEqual(existing.CopyPixels()));
                Assert.True(existing.CopyPixels().SequenceEqual(fresh.CopyPixels()),
                    "Previously rendered text must use the target theme, exactly like a newly created editor.");
            }
            await write;
        }
        finally
        {
            if (write is not null) await write;
            AppearancePresentation.Current = new();
            Directory.Delete(root, true);
        }
    }

    private static TextBox Input()
    {
        var input = new AppThemeTextBox { Text = "00:00:02 — Theme Łódź" };
        input.SetBounds(10, 10, 540, 40); AppUi.Input(input);
        return input;
    }
}
