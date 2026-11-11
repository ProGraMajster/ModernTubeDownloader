using ModernFormsNext;
using ModernFormsNext.Accessibility;
using ModernFormsNext.Testing;
using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Localization;
using ModernTubeDownloader.Models;
using ModernTubeDownloader.Services;
using ModernTubeDownloader.Settings;
using ModernTubeDownloader.Theming;
using ModernTubeDownloader.Views;
using Path = System.IO.Path;

namespace ModernTubeDownloader.Tests;

[Collection("ModernFormsNext TestHost")]
public sealed class ExperiencePresentationTests
{
    [Theory]
    [InlineData(100, AppThemeMode.Dark)] [InlineData(150, AppThemeMode.Dark)] [InlineData(200, AppThemeMode.Dark)]
    [InlineData(100, AppThemeMode.Light)] [InlineData(150, AppThemeMode.Light)] [InlineData(200, AppThemeMode.Light)]
    public async Task AllSixStepsAndWhatsNewFitBothWindowSizesWithRealPreviews(int scale, AppThemeMode theme)
    {
        using var fixture = new Fixture(); using var services = await fixture.Services(theme);
        var draft = SetupDraft.From(services, true); draft.Language = "pl";
        draft.Output = Path.Combine(fixture.Root, new string('a', 100), new string('b', 100), "output");
        draft.Temporary = Path.Combine(fixture.Root, "temporary");
        var originalOutput = services.Settings.Current.FinalOutputDirectory;
        await services.Experience.BeginAsync(draft);
        using (var host = ModernFormsTestHost.Create())
        {
            services.Appearance.Initialize(); AppTypography.SetScale(scale);
            using var wizard = new SetupWizardForm(services, false, draft); var window = host.Show(wizard, 1040, 820);
            host.ProcessPendingWork(); var root = Root(wizard); var text = new LocalizationService("pl");
            Assert.Equal("SetupWizardWindow", wizard.AccessibilityObject.AutomationId);
            Assert.Equal(text["Experience.WelcomeTitle"], Find<Label>(root, "SetupHeading").Text);
            Ui(host, () => Find<ComboBox>(root, "SetupLanguage").SelectedIndex = 1);
            Assert.Equal(new LocalizationService("en")["Experience.WelcomeTitle"], Find<Label>(root, "SetupHeading").Text);
            Ui(host, () => Find<ComboBox>(root, "SetupLanguage").SelectedIndex = 0);
            Assert.Equal(text["Experience.WelcomeTitle"], Find<Label>(root, "SetupHeading").Text);

            for (var step = 0; step < 6; step++)
            {
                AssertNoSetupError(root, fixture.Root);
                Assert.Equal(step, draft.Step);
                Assert.Contains(text["Experience.Step" + step], Find<Label>(root, "SetupProgress").Text);
                foreach (var size in new[] { (1040, 820), (720, 560) })
                {
                    window.Resize(size.Item1, size.Item2); host.ProcessPendingWork();
                    AssertGeometry(root, $"setup step {step}, scale {scale}, {theme}, {size}");
                }
                if (step == 1)
                {
                    var classic = Find<PictureBox>(root, "SetupPreviewClassic"); var modern = Find<PictureBox>(root, "SetupPreviewModern");
                    Assert.NotNull(classic.Image); Assert.NotNull(modern.Image);
                    Assert.True(classic.Image.Width >= 640 && classic.Image.Height >= 360);
                    Assert.True(modern.Image.Width >= 640 && modern.Image.Height >= 360);
                    Assert.NotSame(classic.Image, modern.Image);
                    Click(host, root, "SetupClassic"); Assert.Equal(AppInterfaceMode.Classic, draft.Interface);
                    Click(host, root, "SetupModern"); Assert.Equal(AppInterfaceMode.ModernDashboard, draft.Interface);
                }
                if (step == 3)
                    Assert.Equal(Enum.GetNames<PreferredVideoContainer>().Select(name => text["Container." + name]),
                        Find<ComboBox>(root, "SetupContainer").Items.Cast<object>().Select(value => value.ToString()));
                if (step < 5)
                {
                    Click(host, root, "SetupNext");
                    Pump(host, () => draft.Step == step + 1 && Find<Button>(root, "SetupNext").Enabled);
                }
            }
            Click(host, root, "SetupBack");
            Pump(host, () => draft.Step == 4 && Find<Button>(root, "SetupNext").Enabled);
            var persisted = UserExperienceService.OpenAsync(services.Paths, new NullAppLogger(), services.Experience.Version);
            Pump(host, () => persisted.IsCompleted); Assert.Equal(4, (await persisted).State.Draft!.Step);
            Ui(host, wizard.Close); Pump(host, () => window.IsClosed);
            Assert.False(wizard.Accepted); Assert.Equal(originalOutput, services.Settings.Current.FinalOutputDirectory);
            Assert.Equal(SetupProgress.InProgress, services.Experience.State.Setup);

            using var news = new WhatsNewForm(services); var newsWindow = host.Show(news, 1040, 820); host.ProcessPendingWork();
            var newsRoot = Root(news);
            foreach (var size in new[] { (1040, 820), (720, 560) })
            {
                newsWindow.Resize(size.Item1, size.Item2); host.ProcessPendingWork(); AssertGeometry(newsRoot, $"WhatsNew, {scale}, {theme}, {size}");
            }
            foreach (var key in ReleaseNotesCatalog.Cards)
            {
                Assert.Contains(Descendants(newsRoot).OfType<Label>(), label => label.Text == services.Localization[key]);
                Assert.Contains(Descendants(newsRoot).OfType<Label>(), label => label.Text == services.Localization[key + "Body"]);
            }
            Assert.Contains(Descendants(newsRoot).OfType<Label>(), label => label.Text.Contains(services.Experience.Version, StringComparison.Ordinal));
            Click(host, newsRoot, "WhatsNewDone"); Pump(host, () => newsWindow.IsClosed);
            Assert.Equal(services.Experience.Version, services.Experience.State.LastWhatsNewAcknowledgedVersion);
        }
        await services.ShutdownAsync();
    }

    [Fact]
    public async Task InvalidFolderBlocksAdvancingAndModalCompletionDurablyAppliesOnlyChosenPreferences()
    {
        using var fixture = new Fixture(); using var services = await fixture.Services(AppThemeMode.Dark);
        var draft = SetupDraft.From(services, true); draft.Step = 2; draft.Output = "relative-output";
        var originalOutput = services.Settings.Current.FinalOutputDirectory;
        var originalRemote = services.Settings.Current.EnableWebRemote;
        await services.Experience.BeginAsync(draft);
        using (var host = ModernFormsTestHost.Create())
        {
            services.Appearance.Initialize();
            using var ownerForm = new Form(); var owner = host.Show(ownerForm, 1000, 800);
            using var wizard = new SetupWizardForm(services, false, draft);
            var dialog = host.ShowDialog(wizard, owner, new TestViewport(1040, 820)); host.ProcessPendingWork();
            var root = Root(wizard);
            Assert.NotNull(dialog.DialogCompletion); Assert.False(dialog.DialogCompletion.IsCompleted);
            Click(host, root, "SetupNext");
            Pump(host, () => Find<Button>(root, "SetupNext").Enabled && Find<Label>(root, "SetupError").Text.Length > 0);
            Assert.Equal(2, draft.Step); Assert.Equal(originalOutput, services.Settings.Current.FinalOutputDirectory);
            Assert.Equal(services.Localization["Experience.InvalidFolder"], Find<Label>(root, "SetupError").Text);
            Ui(host, () => Find<TextBox>(root, "SetupOutput").Text = Path.Combine(fixture.Root, "chosen-output"));
            Ui(host, () => Find<TextBox>(root, "SetupTemporary").Text = Path.Combine(fixture.Root, "chosen-temp"));
            Ui(host, () => Find<TextBox>(root, "SetupFilename").Text = "%(title)s.%(ext)s");
            Click(host, root, "SetupNext"); Pump(host, () => draft.Step == 3 && Find<Button>(root, "SetupNext").Enabled);
            AssertNoSetupError(root, fixture.Root);
            Ui(host, () => Find<ComboBox>(root, "SetupContainer").SelectedIndex = (int)PreferredVideoContainer.Mkv);
            Click(host, root, "SetupNext"); Pump(host, () => draft.Step == 4 && Find<Button>(root, "SetupNext").Enabled);
            Ui(host, () => Find<CheckBox>(root, "SetupAutomaticUpdates").Checked = false);
            Ui(host, () => Find<CheckBox>(root, "SetupNotifications").Checked = false);
            Ui(host, () => Find<CheckBox>(root, "SetupTray").Checked = false);
            Click(host, root, "SetupNext"); Pump(host, () => draft.Step == 5 && Find<Button>(root, "SetupNext").Enabled);
            Click(host, root, "SetupNext"); Pump(host, () => dialog.DialogCompletion!.IsCompleted);
            await dialog.DialogCompletion!;
            Assert.True(wizard.Accepted); Assert.True(dialog.IsClosed); Assert.False(owner.IsClosed);
            Assert.Equal(SetupProgress.Completed, services.Experience.State.Setup);
            Assert.Equal(AppInterfaceMode.ModernDashboard, services.Appearance.Profile.InterfaceMode);
            Assert.False(services.Settings.Current.Updates.AutomaticChecks);
            Assert.False(services.Settings.Current.Notifications.Enabled); Assert.False(services.Settings.Current.Startup.ShowTrayIcon);
            Assert.Equal(originalRemote, services.Settings.Current.EnableWebRemote); Assert.Empty(services.Queue.Snapshot());
        }
        var settings = new SettingsService(services.Paths, new NullAppLogger()); await settings.LoadAsync();
        Assert.Equal(Path.Combine(fixture.Root, "chosen-output"), settings.Current.FinalOutputDirectory);
        Assert.Equal(Path.Combine(fixture.Root, "chosen-temp"), settings.Current.TemporaryDirectory);
        Assert.Equal("%(title)s.%(ext)s", settings.Current.FilenameTemplate); Assert.Equal(PreferredVideoContainer.Mkv, settings.Current.PreferredVideoContainer);
        var state = await UserExperienceService.OpenAsync(services.Paths, new NullAppLogger(), services.Experience.Version);
        Assert.Equal(SetupProgress.Completed, state.State.Setup); Assert.Null(state.State.Draft);
        await services.ShutdownAsync();
    }

    [Theory]
    [InlineData(100)] [InlineData(200)]
    public async Task UpgradeChoiceAndWhatsNewDestinationKeepExistingDownloadDefaults(int scale)
    {
        using var fixture = new Fixture(); using var services = await fixture.Services(AppThemeMode.Light);
        var draft = SetupDraft.From(services, false); var originalOutput = draft.Output; var originalQuality = draft.Quality;
        draft.Output = "this is deliberately ignored by upgrade choice"; draft.Quality = "2160";
        await services.Experience.BeginAsync(draft);
        using (var host = ModernFormsTestHost.Create())
        {
            services.Appearance.Initialize();
            AppTypography.SetScale(scale);
            using var wizard = new SetupWizardForm(services, true, draft); var window = host.Show(wizard, 1040, 820); host.ProcessPendingWork();
            var root = Root(wizard); Assert.False(Find<Button>(root, "SetupBack").Visible);
            AssertGeometry(root, $"upgrade choice, scale {scale}");
            Assert.DoesNotContain(Descendants(root), control => control.AccessibleAutomationId == "SetupOutput");
            Click(host, root, "SetupModern"); Click(host, root, "SetupNext"); Pump(host, () => window.IsClosed);
            Assert.True(wizard.Accepted); Assert.Equal(originalOutput, services.Settings.Current.FinalOutputDirectory);
            Assert.Equal(originalQuality, services.Settings.Current.DefaultQualityPresetId);
            Assert.Equal(UserExperienceService.ChoiceFamily, services.Experience.State.InterfaceChoiceFamily);
            using var news = new WhatsNewForm(services); var newsWindow = host.Show(news, 1040, 820); host.ProcessPendingWork();
            Click(host, Root(news), "WhatsNewUpdates"); Pump(host, () => newsWindow.IsClosed);
            Assert.Equal("Updates", news.Destination); Assert.Equal(services.Experience.Version, services.Experience.State.LastWhatsNewAcknowledgedVersion);
        }
        await services.ShutdownAsync();
    }

    private static Panel Root(Form form) => Assert.Single(form.Controls.OfType<Panel>());
    private static T Find<T>(Control root, string id) where T : Control => Assert.Single(Descendants(root).OfType<T>(), control => control.AccessibleAutomationId == id);
    private static void AssertNoSetupError(Control root, string directory)
    {
        var error = Find<Label>(root, "SetupError").Text;
        if (string.IsNullOrEmpty(error)) return;
        var logDirectory = Path.Combine(directory, "logs");
        var logs = Directory.Exists(logDirectory) ? string.Join(Environment.NewLine,
            Directory.EnumerateFiles(logDirectory, "*.log").Select(ReadActiveLog)) : "No log file was produced.";
        Assert.Fail(error + Environment.NewLine + logs);
        static string ReadActiveLog(string path)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream); return reader.ReadToEnd();
        }
    }
    private static IEnumerable<Control> Descendants(Control root)
    { foreach (var child in root.Controls) { yield return child; foreach (var nested in Descendants(child)) yield return nested; } }
    private static void Click(ModernFormsTestHost host, Control root, string id) => Ui(host, () => Find<Button>(root, id).PerformClick());
    private static void Ui(ModernFormsTestHost host, Action action)
    {
        // Posted jobs run under the production dispatcher synchronization context.
        // Inline xUnit event calls would capture its worker-pool context instead.
        host.Dispatcher.Post(action); host.ProcessPendingWork(); host.Dispatcher.ThrowUnhandledExceptions();
    }
    private static void Pump(ModernFormsTestHost host, Func<bool> completed)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!completed() && DateTime.UtcNow < deadline) { host.ProcessPendingWork(); Thread.Sleep(5); }
        Assert.True(completed(), "The isolated presentation action did not complete."); host.ProcessPendingWork();
    }
    private static void AssertGeometry(Control root, string context)
    {
        void Visit(Control parent)
        {
            if (parent is AppScrollPanel scrollPanel)
                Assert.False(Assert.IsType<AccessibleScrollInfo>(scrollPanel.AccessibilityObject.ScrollInfo).Horizontal.IsScrollable,
                    $"{context}: horizontal scroll appeared after the vertical viewport transition.");
            foreach (var child in parent.Controls.Where(control => control.Visible))
            {
                // Empty feedback is intentionally allocated no height. Scrolled content
                // may lie beyond the viewport; its children must still fit their card.
                if (child is Label empty && string.IsNullOrEmpty(empty.Text)) continue;
                Assert.True(child.Width > 0 && child.Height > 0, $"{context}: {child.AccessibleAutomationId} has empty bounds.");
                if (parent is not AppScrollPanel)
                    Assert.True(child.Left >= 0 && child.Top >= 0 && child.Right <= parent.Width && child.Bottom <= parent.Height,
                        $"{context}: {child.AccessibleAutomationId} {child.Text} escapes {parent.GetType().Name}.");
                else Assert.True(child.Left >= parent.DisplayRectangle.Left && child.Right <= parent.DisplayRectangle.Right,
                    $"{context}: scroll content exceeds the actual viewport width.");
                Assert.False(child.Text.StartsWith("Experience.", StringComparison.Ordinal), $"Untranslated text in {context}: {child.Text}");
                if (child is Label label && !string.IsNullOrWhiteSpace(label.Text))
                {
                    var style = label.CurrentStyle;
                    var measured = TextMeasurer.MeasureText(label.Text, style.GetFont(), style.GetFontSize(),
                        new System.Drawing.Size(label.Multiline ? label.Width : int.MaxValue, int.MaxValue), AppTypography.TextStyle(label));
                    Assert.True(measured.Height <= label.Height + 1, $"{context}: text clipped vertically: {label.Text}");
                    if (!label.Multiline) Assert.True(measured.Width <= label.Width + 1, $"{context}: text clipped horizontally: {label.Text}");
                }
                Visit(child);
            }
        }
        Visit(root);
    }

    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "mtd-experience-ui-" + Guid.NewGuid().ToString("N"));
        public async Task<AppServices> Services(AppThemeMode theme)
        {
            var paths = AppPaths.Create(Root); paths.EnsureCreated(); var settings = new SettingsService(paths, new NullAppLogger()); await settings.LoadAsync();
            settings.Current.Language = "pl"; settings.Current.ThemeMode = theme;
            settings.Current.QueuePaused = true; settings.Current.EnableWebRemote = false; settings.Current.Startup.ShowTrayIcon = false;
            settings.Current.Updates.AutomaticChecks = false; settings.Current.FinalOutputDirectory = Path.Combine(Root, "output"); settings.Current.TemporaryDirectory = Path.Combine(Root, "temp");
            settings.Current.UseCustomYtDlp = settings.Current.UseCustomFfmpeg = settings.Current.UseCustomDeno = true;
            settings.Current.CustomYtDlpPath = settings.Current.CustomFfmpegPath = settings.Current.CustomFfprobePath = CoreTests.FakeToolPath();
            var tools = Path.Combine(Root, "fake"); Directory.CreateDirectory(tools);
            foreach (var file in Directory.EnumerateFiles(AppContext.BaseDirectory, "ModernTubeDownloader.FakeTool.*")) File.Copy(file, Path.Combine(tools, Path.GetFileName(file)));
            settings.Current.CustomDenoPath = Path.Combine(tools, "deno.exe"); File.Copy(CoreTests.FakeToolPath(), settings.Current.CustomDenoPath);
            await settings.SaveAsync(); var services = AppServices.Create(paths); await services.Tools.Initialization; return services;
        }
        public void Dispose() { AppTypography.SetScale(100); AppearancePresentation.Current = new(); if (Directory.Exists(Root)) Directory.Delete(Root, true); }
    }
}
