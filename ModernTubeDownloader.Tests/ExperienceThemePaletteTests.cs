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
public sealed class ExperienceThemePaletteTests
{
    [Theory]
    [InlineData(AppThemeMode.Dark)]
    [InlineData(AppThemeMode.Light)]
    public async Task DialogPaletteIsOpaqueAndIdenticalAcrossShellsWithoutChangingClassicColors(AppThemeMode mode)
    {
        using var host = ModernFormsTestHost.Create();
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "mtd-palette-" + Guid.NewGuid().ToString("N")));
        var settings = new SettingsService(AppPaths.Create(root), new NullAppLogger());
        settings.Current.ThemeMode = mode;
        var appearance = new AppAppearanceService(settings, new NullAppLogger());
        try
        {
            appearance.Profile.InterfaceMode = AppInterfaceMode.Classic; appearance.Initialize();
            var dark = mode == AppThemeMode.Dark;
            var expected = new Dictionary<string, string>
            {
                [DownloadsVisual.Background] = dark ? "#10171D" : "#F2F5F8",
                [DownloadsVisual.BackgroundEnd] = dark ? "#0F171E" : "#EDF2F7",
                [DownloadsVisual.Panel] = dark ? "#151E26" : "#FAFBFD",
                [DownloadsVisual.PanelEnd] = dark ? "#161F26" : "#F4F7FA",
                [DownloadsVisual.Selected] = dark ? "#182635" : "#E4EFFB",
                [DownloadsVisual.SelectedEnd] = dark ? "#192736" : "#DDEBFA",
                [DownloadsVisual.Border] = dark ? "#263440" : "#CBD7E3",
                [DownloadsVisual.ButtonTop] = dark ? "#202934" : "#F0F4F8",
                [DownloadsVisual.ButtonEnd] = dark ? "#212B36" : "#E8EEF5"
            };
            var classic = ThemeManager.Current.ActiveSnapshot!.Colors.Where(pair => !pair.Key.StartsWith("Downloads.", StringComparison.Ordinal))
                .ToDictionary(pair => pair.Key, pair => pair.Value.ToArgb());
            Assert.Equal(SKColor.Parse(dark ? "#101521" : "#F5F7FB"), AppUi.GetColor(AppThemeTokens.Background));
            Assert.Equal(SKColor.Parse(dark ? "#18202E" : "#FFFFFF"), AppUi.GetColor(AppThemeTokens.Surface));
            Assert.Equal(SKColor.Parse(dark ? "#7396FF" : "#3867E8"), AppUi.GetColor(AppThemeTokens.Accent));
            AssertPalette();
            var profile = appearance.Profile.Copy(); profile.InterfaceMode = AppInterfaceMode.ModernDashboard;
            await appearance.ApplyProfileAsync(profile);
            AssertPalette();
            profile.InterfaceMode = AppInterfaceMode.Classic; await appearance.ApplyProfileAsync(profile);
            AssertPalette();
            Assert.Equal(AppInterfaceMode.Classic, appearance.InterfaceMode);
            foreach (var (token, color) in classic) Assert.Equal(color, ThemeManager.Current.ActiveSnapshot!.Colors[token].ToArgb());

            void AssertPalette()
            {
                foreach (var (token, hex) in expected)
                {
                    Assert.True(ThemeManager.Current.ActiveSnapshot!.Colors.ContainsKey(token), $"The {appearance.InterfaceMode} palette is missing {token}.");
                    var actual = AppUi.GetColor(token);
                    Assert.Equal(255, actual.Alpha); Assert.Equal(SKColor.Parse(hex), actual);
                }
            }
        }
        finally
        {
            appearance.Dispose(); AppearancePresentation.Current = new();
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}
