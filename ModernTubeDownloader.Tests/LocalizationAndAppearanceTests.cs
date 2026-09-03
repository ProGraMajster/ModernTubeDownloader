using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Localization;
using ModernTubeDownloader.Services;
using ModernTubeDownloader.Settings;

namespace ModernTubeDownloader.Tests;

public sealed class LocalizationAndAppearanceTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "ModernTubeDownloader.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void PolishAndEnglishResources_HaveIdenticalKeys()
    {
        var localization = new LocalizationService("pl");

        Assert.Equal(
            localization.GetKeys("en").Order(StringComparer.Ordinal),
            localization.GetKeys("pl").Order(StringComparer.Ordinal));
        Assert.DoesNotContain(localization.GetKeys("pl"), key => localization[key] == $"[{key}]");
    }

    [Fact]
    public void RuntimeLanguageChange_UpdatesCultureAndRaisesOneEvent()
    {
        var localization = new LocalizationService("en");
        var changes = 0;
        localization.LanguageChanged += (_, _) => changes++;

        Assert.True(localization.SetLanguage("pl"));
        Assert.Equal("pl", localization.Language);
        Assert.Equal("pl-PL", localization.Culture.Name);
        Assert.Equal("Pobieranie", localization["Nav.Downloads"]);
        Assert.Equal(1, changes);
        Assert.False(localization.SetLanguage("pl"));
        Assert.Equal(1, changes);
        Assert.True(localization.SetLanguage("en"));
        Assert.Equal("en-US", localization.Culture.Name);
        Assert.Equal("Downloads", localization["Nav.Downloads"]);
        Assert.Equal(2, changes);
    }

    [Theory]
    [InlineData(AppThemeMode.System)]
    [InlineData(AppThemeMode.Light)]
    [InlineData(AppThemeMode.Dark)]
    public async Task ThemeAndLanguagePreferences_SurviveSettingsReload(AppThemeMode themeMode)
    {
        var paths = AppPaths.Create(root);
        paths.EnsureCreated();
        var settings = new SettingsService(paths, new NullAppLogger());
        await settings.LoadAsync();
        settings.Current.ThemeMode = themeMode;
        settings.Current.Language = "pl";
        await settings.SaveAsync();

        var reloaded = new SettingsService(paths, new NullAppLogger());
        await reloaded.LoadAsync();

        Assert.Equal(themeMode, reloaded.Current.ThemeMode);
        Assert.Equal("pl", reloaded.Current.Language);
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
            Directory.Delete(root, recursive: true);
    }
}
