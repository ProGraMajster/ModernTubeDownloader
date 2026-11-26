using ModernFormsNext;
using ModernFormsNext.Testing;
using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Services;
using ModernTubeDownloader.Shell;
using ModernTubeDownloader.Theming;
using ModernTubeDownloader.Views;
using Path = System.IO.Path;

namespace ModernTubeDownloader.Tests;

[Collection("ModernFormsNext TestHost")]
public sealed class SettingsVisibilityLifecycleTests
{
    [Theory]
    [InlineData("pl", 100)] [InlineData("ja-JP", 200)]
    public async Task LocalizedOptionSearchFindsHiddenDownloadsCategoryAndClearingRestoresAllCategories(string language, int scale)
    {
        var root = Path.Combine(Path.GetTempPath(), "mtd-settings-search-" + Guid.NewGuid().ToString("N"));
        AppServices? services = null;
        try
        {
            services = await CreateServicesAsync(root, language);
            services.Appearance.Profile.InterfaceMode = AppInterfaceMode.ModernDashboard;
            services.Appearance.Profile.Global.Animations = false;
            services.Appearance.Profile.Typography.AppTextScale = scale;
            using (var host = ModernFormsTestHost.Create())
            using (var form = new MainForm(services, backgroundActivation: true))
            {
                Ui(host, () => host.Show(form, 1536, 1024));
                var settings = form.Controls.SelectMany(control => new[] { control }.Concat(Descendants(control)))
                    .OfType<SettingsView>().Single();
                Ui(host, () => { form.Navigate(AppPageId.Settings); settings.ShowCategory("Authentication"); });
                var query = services.Localization["Settings.TemporaryDirectory"];
                Assert.False(string.IsNullOrWhiteSpace(query));
                if (language == "ja-JP") Assert.Equal("仮ファイル", query);
                var search = Find<TextBox>(settings, "SettingsCategorySearch");
                Ui(host, () => search.Text = query);
                Assert.Equal(query, search.Text);
                var downloads = Find<Button>(settings, "SettingsCategoryDownloads");
                Assert.True(downloads.Visible && downloads.Width > 0 && downloads.Height > 0);
                Assert.False(Find<Button>(settings, "SettingsCategoryGeneral").Visible);
                Ui(host, () => settings.ShowCategory("Downloads"));
                Assert.Contains(Descendants(settings).OfType<Label>(), label => label.Visible && label.Text == query);
                Ui(host, () => search.Text = "");
                AssertSettingsGeometry(settings);
            }
        }
        finally
        {
            if (services is not null) { await services.ShutdownAsync(); services.Dispose(); }
            AppTypography.SetScale(100); AppearancePresentation.Current = new();
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Theory]
    [InlineData("pl", 100)] [InlineData("ja-JP", 200)]
    public async Task ShellRoundtripRestoresCategoryAndCardGeometryWithoutResizeOrCategoryChange(string language, int scale)
    {
        var root = Path.Combine(Path.GetTempPath(), "mtd-settings-visibility-" + Guid.NewGuid().ToString("N"));
        AppServices? services = null;
        try
        {
            services = await CreateServicesAsync(root, language);
            services.Appearance.Profile.InterfaceMode = AppInterfaceMode.ModernDashboard;
            services.Appearance.Profile.Global.Animations = false;
            services.Appearance.Profile.Typography.AppTextScale = scale;
            using (var host = ModernFormsTestHost.Create())
            using (var form = new MainForm(services, backgroundActivation: true))
            {
                Ui(host, () => host.Show(form, 1536, 1024));
                var settings = form.Controls.SelectMany(control => new[] { control }.Concat(Descendants(control)))
                    .OfType<SettingsView>().Single();
                Ui(host, () => { form.Navigate(AppPageId.Settings); settings.ShowCategory("Personalization"); });
                AssertSettingsGeometry(settings);
                var size = settings.ClientSize;
                var retainedCategories = Descendants(settings).OfType<Button>()
                    .Where(button => button.AccessibleAutomationId?.StartsWith("SettingsCategory", StringComparison.Ordinal) == true).ToArray();
                var retainedInterface = Find<Button>(settings, "InterfaceModernDashboard").Parent;
                var processor = services.Processor;
                // Use the same canonical appearance transaction as the actual UI
                // buttons; await persistence without introducing a second runtime.
                for (var cycle = 0; cycle < 2; cycle++)
                {
                    Apply(AppInterfaceMode.Classic);
                    Apply(AppInterfaceMode.ModernDashboard);
                    Assert.Equal(size, settings.ClientSize);
                    Assert.Same(processor, services.Processor);
                    Assert.Same(retainedInterface, Find<Button>(settings, "InterfaceModernDashboard").Parent);
                    Assert.Equal(retainedCategories, Descendants(settings).OfType<Button>()
                        .Where(button => button.AccessibleAutomationId?.StartsWith("SettingsCategory", StringComparison.Ordinal) == true));
                    // No ShowCategory, resize, or synthetic layout after reattachment:
                    // those actions hid the original regression in native automation.
                    AssertSettingsGeometry(settings);
                    Assert.True(Find<Button>(settings, "InterfaceClassic").Visible);
                    Assert.True(retainedInterface!.Width > 0 && retainedInterface.Height > 0);
                }
                Ui(host, () => form.Navigate(AppPageId.Downloads));
                Ui(host, () => form.Navigate(AppPageId.Settings));
                AssertSettingsGeometry(settings);
                host.Dispatcher.ThrowUnhandledExceptions();

                void Apply(AppInterfaceMode mode)
                {
                    Task? change = null;
                    Ui(host, () =>
                    {
                        var profile = services.Appearance.Profile.Copy(); profile.InterfaceMode = mode;
                        change = services.Appearance.ApplyProfileAsync(profile);
                    });
                    Pump(host, () => change!.IsCompleted); change!.GetAwaiter().GetResult();
                    Assert.Equal(mode, form.InterfaceMode);
                }
            }
        }
        finally
        {
            if (services is not null) { await services.ShutdownAsync(); services.Dispose(); }
            AppTypography.SetScale(100); AppearancePresentation.Current = new();
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static void AssertSettingsGeometry(SettingsView settings)
    {
        Assert.True(settings.Visible);
        var categories = Descendants(settings).OfType<Button>()
            .Where(button => button.AccessibleAutomationId?.StartsWith("SettingsCategory", StringComparison.Ordinal) == true).ToArray();
        Assert.Equal(12, categories.Length);
        Assert.Contains(categories, button => button.AccessibleAutomationId == "SettingsCategoryDiagnostics");
        Assert.All(categories, button => Assert.True(button.Visible && button.Width > 0 && button.Height > 0,
            $"Category {button.AccessibleAutomationId} lost its geometry: {button.Bounds}."));
        var scroll = Descendants(settings).Single(control => control.AccessibleAutomationId == "SettingsScroll");
        var cards = scroll.Controls.Where(card => card.Visible).ToArray();
        Assert.NotEmpty(cards);
        Assert.All(cards, card => Assert.True(card.Width > 0 && card.Height > 0, $"Visible card has empty bounds: {card.Bounds}."));
        Assert.Contains(cards, card => card.Bottom > 0 && card.Top < scroll.ClientSize.Height && card.Right > 0 && card.Left < scroll.ClientSize.Width);
    }

    private static async Task<AppServices> CreateServicesAsync(string root, string language)
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
        await settings.SaveAsync(); var services = AppServices.Create(paths); await services.Tools.Initialization; return services;
    }

    private static T Find<T>(Control parent, string id) where T : Control => Assert.Single(Descendants(parent).OfType<T>(), control => control.AccessibleAutomationId == id);
    private static IEnumerable<Control> Descendants(Control parent)
    { foreach (var child in parent.Controls) { yield return child; foreach (var nested in Descendants(child)) yield return nested; } }
    private static void Ui(ModernFormsTestHost host, Action action)
    { host.Dispatcher.Post(action); host.ProcessPendingWork(); host.Dispatcher.ThrowUnhandledExceptions(); }
    private static void Pump(ModernFormsTestHost host, Func<bool> completed)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!completed() && DateTime.UtcNow < deadline) { host.ProcessPendingWork(); Thread.Sleep(5); }
        Assert.True(completed(), "The isolated presentation change did not complete."); host.ProcessPendingWork();
    }
}
