using System.Globalization;
using System.Text.Json;
using ModernFormsNext;
using ModernFormsNext.Testing;
using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Services;
using ModernTubeDownloader.Theming;
using ModernTubeDownloader.Views;
using Path = System.IO.Path;

namespace ModernTubeDownloader.Tests;

[Collection("ModernFormsNext TestHost")]
public sealed class SettingsOptionSearchTests
{
    [Theory]
    [InlineData("pl", "JEZYK", 100, 1536)]
    [InlineData("pl", "język", 150, 1100)]
    [InlineData("pl", "jezyk", 200, 900)]
    [InlineData("en", "LANGUAGE", 100, 1536)]
    [InlineData("de-DE", "SPRACHE", 150, 1100)]
    [InlineData("ja-JP", "言語", 200, 900)]
    public async Task ExactOptionNavigationKeepsValuesAndSemanticsAndResetsWithEscape(string locale, string query, int scale, int width)
    {
        await InSettings(locale, scale, width, (services, host, view) =>
        {
            var search = Find<TextBox>(view, "SettingsCategorySearch");
            var target = Find<Control>(view, "LanguageSelector");
            var originalText = target.Text; var originalName = target.AccessibleName;
            var originalSettings = JsonSerializer.Serialize(services.Settings.Current);
            var originalControls = SettingsSearchIndex.Descendants(view).ToArray();
            search.Select(); search.Text = query; host.ProcessPendingWork();
            Assert.True(search.Focused);
            Assert.True(view.SettingsSearchMatchCount > 0);
            var result = Find<SettingsSearchResultButton>(view, "SettingsSearchResult0");
            Assert.Equal("Settings.Language", result.Entry?.Key);
            Assert.Equal(services.Localization["Settings.Language"], result.Text);
            Assert.Contains(services.Localization["Settings.Category.General"], result.AccessibleName, StringComparison.Ordinal);
            Assert.All(SettingsSearchIndex.Descendants(view).OfType<SettingsSearchResultButton>().Where(row => row.Visible), row =>
            {
                Assert.True(row.Width > 0 && row.Height > 0);
                Assert.True(row.Left >= 0 && row.Right <= row.Parent!.Width && row.Bottom <= row.Parent.Height);
            });
            host.Input.PressKey(Keys.Enter); host.ProcessPendingWork();
            Assert.Same(target, view.SettingsSearchHighlightedTarget);
            Assert.True(target.Focused);
            var viewport = Find<Control>(view, "SettingsScroll");
            var bounds = SettingsSearchIndex.BoundsIn(target, viewport);
            Assert.True(bounds.Top < viewport.Height && bounds.Bottom > 0, $"Target is outside viewport: {bounds}; {viewport.Size}");
            Assert.Equal(originalText, target.Text); Assert.Equal(originalName, target.AccessibleName);
            Assert.Equal(originalSettings, JsonSerializer.Serialize(services.Settings.Current));
            // Reveal must not invoke the destination's language chooser on Enter release.
            Assert.Single(host.Windows);
            host.Input.PressKey(Keys.Escape); host.ProcessPendingWork();
            Assert.Equal(string.Empty, search.Text); Assert.True(search.Focused);
            Assert.Null(view.SettingsSearchHighlightedTarget);
            Assert.False(Find<Control>(view, "SettingsSearchResults").Visible);
            Assert.All(SettingsSearchIndex.Descendants(view).OfType<Button>().Where(button =>
                button.AccessibleAutomationId?.StartsWith("SettingsCategory", StringComparison.Ordinal) == true), button => Assert.True(button.Visible));
            Assert.Equal(originalControls, SettingsSearchIndex.Descendants(view).ToArray());
            Assert.Equal(originalSettings, JsonSerializer.Serialize(services.Settings.Current));
        });
    }

    [Theory]
    [InlineData(100, 1536)]
    [InlineData(200, 900)]
    public async Task ClearSearchIsPointerAccessibleAndDoesNotEditSettings(int scale, int width)
    {
        await InSettings("pl", scale, width, (services, host, view) =>
        {
            var search = Find<TextBox>(view, "SettingsCategorySearch");
            var clear = Find<Button>(view, "SettingsSearchClear");
            var originalSettings = JsonSerializer.Serialize(services.Settings.Current);
            search.Select(); search.Text = services.Localization["Settings.Language"]; host.ProcessPendingWork();
            Assert.True(clear.Visible); Assert.True(clear.Enabled);
            Assert.True(view.SettingsSearchMatchCount > 0);
            // Route through the production pointer hit test; PerformClick cannot detect an obscured button.
            host.Input.Click(clear); host.ProcessPendingWork();
            Assert.Equal(string.Empty, search.Text); Assert.True(search.Focused);
            Assert.False(Find<Control>(view, "SettingsSearchResults").Visible);
            Assert.False(clear.Visible);
            Assert.All(SettingsSearchIndex.Descendants(view).OfType<Button>().Where(button =>
                button.AccessibleAutomationId?.StartsWith("SettingsCategory", StringComparison.Ordinal) == true), button => Assert.True(button.Visible));
            Assert.Equal(originalSettings, JsonSerializer.Serialize(services.Settings.Current));
        });
    }

    [Fact]
    public async Task SearchNeverIndexesPrivateValuesAndResultsRemainBoundedAcrossPagingAndLocaleChange()
    {
        await InSettings("pl", 100, 1536, (services, host, view) =>
        {
            var search = Find<TextBox>(view, "SettingsCategorySearch");
            const string privateValue = "private-value-73f89c-token-path-cookie";
            Find<TextBox>(view, "SettingsOutputDirectory").Text = privateValue;
            var originalSettings = JsonSerializer.Serialize(services.Settings.Current);
            search.Select(); search.Text = privateValue; host.ProcessPendingWork();
            Assert.Equal(0, view.SettingsSearchMatchCount);
            Assert.True(Find<Label>(view, "SettingsSearchEmpty").Visible);
            Assert.DoesNotContain(SettingsSearchIndex.Descendants(view).OfType<SettingsSearchResultButton>(), row => row.Visible);
            search.Text = services.Localization["Settings.Category.Downloads"]; host.ProcessPendingWork();
            Assert.True(view.SettingsSearchMatchCount > 3);
            var rows = SettingsSearchIndex.Descendants(view).OfType<SettingsSearchResultButton>().ToArray();
            Assert.Equal(3, rows.Length);
            var first = rows[0].Entry;
            Find<Button>(view, "SettingsSearchNext").PerformClick(); host.ProcessPendingWork();
            Assert.NotEqual(first, rows[0].Entry); Assert.True(Find<Button>(view, "SettingsSearchPrevious").Enabled);
            Find<Button>(view, "SettingsSearchPrevious").PerformClick(); host.ProcessPendingWork(); Assert.Equal(first, rows[0].Entry);
            search.Text = services.Localization["Settings.TemporaryDirectory"]; host.ProcessPendingWork();
            Assert.Equal("Settings.TemporaryDirectory", rows[0].Entry?.Key);
            search.Select(); host.Input.PressKey(Keys.Down); host.ProcessPendingWork();
            Assert.True(rows[0].Focused); Assert.Null(view.SettingsSearchHighlightedTarget); Assert.Single(host.Windows);
            host.Input.PressKey(Keys.Enter); host.ProcessPendingWork();
            Assert.Same(Find<TextBox>(view, "SettingsTemporaryDirectory"), view.SettingsSearchHighlightedTarget);
            host.Clock.Advance(TimeSpan.FromSeconds(4)); host.ProcessPendingWork();
            Assert.Null(view.SettingsSearchHighlightedTarget);
            host.Input.PressKey(Keys.Escape); host.ProcessPendingWork();
            Assert.Equal(string.Empty, search.Text); Assert.True(search.Focused);
            Assert.Equal(originalSettings, JsonSerializer.Serialize(services.Settings.Current));
            services.Localization.SetLanguage("ja-JP"); host.ProcessPendingWork();
            search.Text = services.Localization["Settings.TemporaryDirectory"]; host.ProcessPendingWork();
            Assert.Equal("Settings.TemporaryDirectory", rows[0].Entry?.Key);
            Assert.Equal(services.Localization["Settings.TemporaryDirectory"], rows[0].Text);
            Find<Button>(view, "SettingsSearchClear").PerformClick(); host.ProcessPendingWork();
            Assert.Equal(string.Empty, search.Text); Assert.True(search.Focused);
            view.SetDashboard(false); host.ProcessPendingWork();
            Assert.False(search.Visible); Assert.False(Find<Control>(view, "SettingsSearchResults").Visible);
            Assert.Equal(privateValue, Find<TextBox>(view, "SettingsOutputDirectory").Text);
        });
    }

    [Theory]
    [InlineData("Live", "ShowReconnectCount")]
    [InlineData("BatchImport", "UrlColumnWidth")]
    [InlineData("ModernDashboardShell", "ActiveProcesses")]
    public async Task LazyPersonalizationOptionsAreFoundAndRevealedWithoutEditingProfile(string section, string field)
    {
        await InSettings("pl", 200, 900, (services, host, view) =>
        {
            var id = "Appearance" + section + field;
            var captionKey = "Appearance.Field." + field;
            var search = Find<TextBox>(view, "SettingsCategorySearch");
            var selector = Find<ComboBox>(view, "AppearanceSection");
            var originalSection = selector.SelectedIndex;
            var originalControls = SettingsSearchIndex.Descendants(view).ToArray();
            var originalSettings = JsonSerializer.Serialize(services.Settings.Current);
            var originalProfile = JsonSerializer.Serialize(services.Appearance.Profile);
            Assert.DoesNotContain(originalControls, control => control.AccessibleAutomationId == id);
            search.Select(); search.Text = services.Localization[captionKey]; host.ProcessPendingWork();
            Assert.True(search.Focused);
            Assert.Equal(originalSection, selector.SelectedIndex);
            Assert.Equal(originalControls, SettingsSearchIndex.Descendants(view).ToArray());
            Assert.Equal(originalProfile, JsonSerializer.Serialize(services.Appearance.Profile));

            var rows = SettingsSearchIndex.Descendants(view).OfType<SettingsSearchResultButton>().ToArray();
            SettingsSearchResultButton? match = null;
            for (var page = 0; page < view.SettingsSearchMatchCount; page++)
            {
                match = rows.FirstOrDefault(row => row.Visible && row.Entry?.Personalization?.AutomationId == id);
                if (match is not null) break;
                var next = Find<Button>(view, "SettingsSearchNext");
                if (!next.Enabled) break;
                next.PerformClick(); host.ProcessPendingWork();
            }
            Assert.NotNull(match);
            Assert.Contains(services.Localization["Appearance.Section." + section], match.AccessibleName, StringComparison.Ordinal);
            match.PerformClick(); host.ProcessPendingWork();
            var target = Find<Control>(view, id);
            Assert.Same(target, view.SettingsSearchHighlightedTarget); Assert.True(target.Focused);
            Assert.NotEqual(originalSection, selector.SelectedIndex);
            var viewport = Find<Control>(view, "SettingsScroll");
            var bounds = SettingsSearchIndex.BoundsIn(target, viewport);
            Assert.True(bounds.Top >= 0 && bounds.Bottom <= viewport.Height,
                $"Revealed option must fit the viewport: {bounds}; {viewport.Size}");
            host.Clock.Advance(TimeSpan.FromMilliseconds(100)); host.ProcessPendingWork();
            Assert.Equal(originalProfile, JsonSerializer.Serialize(services.Appearance.Profile));
            Assert.Equal(originalSettings, JsonSerializer.Serialize(services.Settings.Current));
            host.Input.PressKey(Keys.Escape); host.ProcessPendingWork();
            Assert.Equal(string.Empty, search.Text); Assert.True(search.Focused);
            Assert.Null(view.SettingsSearchHighlightedTarget);
        });
    }

    [Theory]
    [InlineData("pl", "Zażółć gęślą jaźń", "ZAZOLC GESLA JAZN")]
    [InlineData("de", "Größe", "GROSSE")]
    [InlineData("ja-JP", "画質の設定", "画質")]
    public void CaptionIndexUsesCultureAndNeverReadsTargetText(string locale, string caption, string query)
    {
        using var target = new TextBox { Text = "not-searchable-secret-123" };
        var entry = new SettingsSearchEntry("safe", caption, "Downloads", "category", "card", "description", target, target, target);
        Assert.Single(SettingsSearchIndex.Find([entry], query, CultureInfo.GetCultureInfo(locale)));
        Assert.Empty(SettingsSearchIndex.Find([entry], target.Text, CultureInfo.GetCultureInfo(locale)));
        Assert.Empty(SettingsSearchIndex.Find([entry], "  ", CultureInfo.GetCultureInfo(locale)));
    }

    private static async Task InSettings(string language, int scale, int width, Action<AppServices, ModernFormsTestHost, SettingsView> test)
    {
        var root = Path.Combine(Path.GetTempPath(), "mtd-exact-setting-search-" + Guid.NewGuid().ToString("N"));
        AppServices? services = null;
        try
        {
            var paths = AppPaths.Create(root); paths.EnsureCreated();
            var settings = new SettingsService(paths, new NullAppLogger()); await settings.LoadAsync();
            settings.Current.Language = language; settings.Current.QueuePaused = true; settings.Current.EnableWebRemote = false;
            settings.Current.Startup.ShowTrayIcon = false; settings.Current.Updates.AutomaticChecks = false;
            settings.Current.FinalOutputDirectory = Path.Combine(root, "output"); settings.Current.TemporaryDirectory = Path.Combine(root, "temp");
            settings.Current.UseCustomYtDlp = settings.Current.UseCustomFfmpeg = settings.Current.UseCustomDeno = true;
            settings.Current.CustomYtDlpPath = settings.Current.CustomFfmpegPath = settings.Current.CustomFfprobePath = CoreTests.FakeToolPath();
            var tools = Path.Combine(root, "fake"); Directory.CreateDirectory(tools);
            foreach (var file in Directory.EnumerateFiles(AppContext.BaseDirectory, "ModernTubeDownloader.FakeTool.*"))
                File.Copy(file, Path.Combine(tools, Path.GetFileName(file)));
            settings.Current.CustomDenoPath = Path.Combine(tools, "deno.exe"); File.Copy(CoreTests.FakeToolPath(), settings.Current.CustomDenoPath);
            await settings.SaveAsync(); services = AppServices.Create(paths); await services.Tools.Initialization;
            using (var host = ModernFormsTestHost.Create())
            {
                services.Appearance.Initialize(); AppTypography.SetScale(scale);
                var view = new SettingsView(services); AppTypography.SetDashboardPresentation(view, true); view.SetDashboard(true);
                host.Show(view, width, 1024); host.ProcessPendingWork(); test(services, host, view);
            }
        }
        finally
        {
            if (services is not null) { await services.ShutdownAsync(); services.Dispose(); }
            AppTypography.SetScale(100); AppearancePresentation.Current = new();
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static T Find<T>(Control root, string id) where T : Control =>
        Assert.Single(SettingsSearchIndex.Descendants(root).OfType<T>(), control => control.AccessibleAutomationId == id);
}
