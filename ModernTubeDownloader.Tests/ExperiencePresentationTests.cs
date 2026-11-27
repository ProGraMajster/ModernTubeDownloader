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
public sealed partial class ExperiencePresentationTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task SettingsLanguageWriteFailureIsContainedAndCanBeRetriedWithoutLosingPreferences(bool modern)
    {
        using var fixture = new Fixture(); using var services = await fixture.Services(AppThemeMode.Dark);
        using (var host = ModernFormsTestHost.Create())
        {
            services.Appearance.Initialize();
            using var view = new SettingsView(services); AppTypography.SetDashboardPresentation(view, modern); view.SetDashboard(modern);
            host.Show(view, 1320, 1000); host.ProcessPendingWork();
            var selector = Find<LocalePickerButton>(view, "LanguageSelector");
            var feedback = Find<Label>(view, "SettingsFeedback");
            var original = File.ReadAllBytes(services.Paths.SettingsFile);
            var output = services.Settings.Current.FinalOutputDirectory;
            using (var held = new FileStream(services.Paths.SettingsFile, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                Ui(host, () => selector.SelectLanguage("en", true));
                Pump(host, () => selector.Enabled && feedback.Text == services.Localization["Error.Generic"]);
                host.Dispatcher.ThrowUnhandledExceptions();
                Assert.Equal("pl", selector.SelectedLanguage); Assert.Equal("pl", services.Localization.Language);
                Assert.Equal("pl", services.Settings.Current.Language); Assert.Equal(output, services.Settings.Current.FinalOutputDirectory);
            }
            Assert.Equal(original, File.ReadAllBytes(services.Paths.SettingsFile));
            Ui(host, () => selector.SelectLanguage("en", true));
            Pump(host, () => selector.Enabled && services.Localization.Language == "en");
            Assert.Equal(services.Localization["Settings.Saved"], feedback.Text);
            var restored = new SettingsService(services.Paths, new NullAppLogger()); var reload = restored.LoadAsync();
            Pump(host, () => reload.IsCompleted); await reload;
            Assert.Equal("en", restored.Current.Language); Assert.Equal(output, restored.Current.FinalOutputDirectory);
        }
        await services.ShutdownAsync();
    }

    [Fact]
    public async Task OtherRecentMetadataRelocalizesInPlaceWithoutReopeningOrReplacingActions()
    {
        using var fixture = new Fixture(); using var services = await fixture.Services(AppThemeMode.Dark);
        var session = await services.Batch.ImportTextAsync("https://example.invalid/synthetic-video");
        using (var host = ModernFormsTestHost.Create())
        {
            services.Appearance.Initialize();
            using var view = new OtherView(services); view.RegisterTool("Batch", () => { });
            AppTypography.SetDashboardPresentation(view, true); view.SetDashboard(true); host.Show(view, 1320, 1000);
            var refresh = view.RefreshToolsAsync(); Pump(host, () => refresh.IsCompleted); await refresh;
            var recent = Find<Button>(view, "BatchRecent" + session.Id.ToString("N"));
            var title = recent.Parent!.Controls.OfType<Label>().First();
            Assert.Equal(services.Localization["Other.RecentTools"], title.Text);
            Ui(host, () => services.Localization.SetLanguage("en"));
            Assert.Same(recent, Find<Button>(view, "BatchRecent" + session.Id.ToString("N")));
            Assert.Equal(services.Localization["Other.RecentTools"], title.Text);
            Assert.Contains(session.CreatedAt.ToLocalTime().ToString("g", services.Localization.Culture), recent.AccessibleName);
            Assert.Empty(services.Queue.Snapshot());
        }
        await services.ShutdownAsync();
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task FailedWizardNavigationKeepsTheVisibleAndDurableStepUntilRetry(bool forward)
    {
        using var fixture = new Fixture(); using var services = await fixture.Services(AppThemeMode.Dark);
        var draft = SetupDraft.From(services, true); draft.Step = 1;
        await services.Experience.BeginAsync(draft);
        using (var host = ModernFormsTestHost.Create())
        {
            services.Appearance.Initialize();
            using var wizard = new SetupWizardForm(services, false, draft);
            var window = host.Show(wizard, 1040, 820); host.ProcessPendingWork();
            var root = Root(wizard); var button = forward ? "SetupNext" : "SetupBack";
            var heading = Find<Label>(root, "SetupHeading").Text;
            using (var held = new FileStream(Path.Combine(fixture.Root, "experience.json"), FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                Click(host, root, button);
                Pump(host, () => Find<Button>(root, button).Enabled && Find<Label>(root, "SetupError").Text.Length > 0);
                Assert.Equal(1, draft.Step); Assert.Equal(1, services.Experience.State.Draft!.Step);
                Assert.Equal(heading, Find<Label>(root, "SetupHeading").Text);
                Assert.Equal(2, Find<ProgressBar>(root, "SetupStepProgress").Value);
            }
            Click(host, root, button);
            var expected = forward ? 2 : 0;
            Pump(host, () => draft.Step == expected && Find<Button>(root, "SetupNext").Enabled);
            AssertNoSetupError(root, fixture.Root);
            Assert.Equal(expected, services.Experience.State.Draft!.Step);
            Assert.Equal(expected + 1, Find<ProgressBar>(root, "SetupStepProgress").Value);
            Ui(host, wizard.Close); Pump(host, () => window.IsClosed);
        }
        await services.ShutdownAsync();
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task FailedAutosaveDoesNotPoisonSkipOrUpgradeCompletionAfterStorageRecovers(bool upgrade)
    {
        using var fixture = new Fixture(); using var services = await fixture.Services(AppThemeMode.Dark);
        var draft = SetupDraft.From(services, true); await services.Experience.BeginAsync(draft);
        using (var host = ModernFormsTestHost.Create())
        {
            services.Appearance.Initialize();
            using var wizard = new SetupWizardForm(services, upgrade, draft);
            var window = host.Show(wizard, 1040, 820); host.ProcessPendingWork(); var root = Root(wizard);
            using (var held = new FileStream(Path.Combine(fixture.Root, "experience.json"), FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (upgrade) Ui(host, () => Find<CheckBox>(root, "SetupAutomaticUpdates").Checked = !draft.AutomaticUpdates);
                else Ui(host, () => Find<LocalePickerButton>(root, "SetupLanguage").SelectLanguage("en", true));
                Pump(host, () => Find<Label>(root, "SetupError").Text.Length > 0);
            }
            Click(host, root, upgrade ? "SetupNext" : "SetupSkip"); Pump(host, () => window.IsClosed);
            Assert.Equal(upgrade, wizard.Accepted);
            if (upgrade) Assert.Equal(UserExperienceService.ChoiceFamily, services.Experience.State.InterfaceChoiceFamily);
            else Assert.Equal(SetupProgress.Skipped, services.Experience.State.Setup);
        }
        await services.ShutdownAsync();
    }

    [Theory]
    [InlineData("pl", 100, true)] [InlineData("en", 150, false)]
    [InlineData("de-DE", 200, true)] [InlineData("ja-JP", 200, false)]
    public async Task StartupPreparationDialogCancelsWithoutInstallingAndKeepsContinueReachable(string language, int scale, bool userContinue)
    {
        using var fixture = new Fixture(); using var services = await fixture.Services(AppThemeMode.Dark);
        services.Localization.SetLanguage(language);
        using var host = ModernFormsTestHost.Create();
        services.Appearance.Initialize(); AppTypography.SetScale(scale);
        using var startup = new StartupCoordinator(services.Updates, services.Logger, Guid.NewGuid(),
            userContinue ? TimeSpan.FromSeconds(8) : TimeSpan.FromMilliseconds(500));
        using var form = new StartupUpdateForm(services, startup);
        TestWindowHost? window = null;
        Ui(host, () => window = host.Show(form, 510, 300));
        var root = Root(form);
        var button = Find<Button>(root, "StartupUpdateContinue");
        Assert.True(button.Enabled && button.Width > 0 && button.Height >= AppTypography.TextHeight(42));
        Assert.True(button.Left >= 0 && button.Right <= button.Parent!.Width);
        Task? preparation = null;
        Ui(host, () => preparation = RunAsync());
        if (userContinue) Ui(host, button.PerformClick);
        Pump(host, () => window!.IsClosed);
        await preparation!;
        Assert.Equal(userContinue ? StartupUpdateOutcome.Deferred : StartupUpdateOutcome.TimedOut, startup.Outcome);
        Assert.False(services.Updates.InstallationPending);
        Assert.Null(services.Updates.State.LastStartupAttemptVersion);

        async Task RunAsync()
        {
            await startup.PrepareAsync(true, async token =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return false;
            }, () => false);
            form.Finish();
        }
    }

    [Fact]
    public async Task InstallOnExitCheckboxPersistsTheNewChoiceAcrossPanelRefresh()
    {
        using var fixture = new Fixture();
        using var services = await fixture.Services(AppThemeMode.Dark);
        await services.Updates.InitializeAsync();
        const string version = "99.0.0";
        var root = Path.Combine(services.Paths.RootDirectory, "Updates");
        Directory.CreateDirectory(root);
        var filename = $"ModernTubeDownloader-{version}-win-x64.zip";
        var staged = Path.Combine(root, filename);
        using (var zip = System.IO.Compression.ZipFile.Open(staged, System.IO.Compression.ZipArchiveMode.Create))
        using (var writer = new StreamWriter(zip.CreateEntry("ModernTubeDownloader.exe").Open()))
            writer.Write("inert update fixture");
        var repository = GitHubApplicationReleaseSource.Repository;
        services.Updates.State.Available = new(version, "", new(repository + "/releases/tag/v" + version),
            new(repository + "/releases/download/v" + version + "/" + filename),
            new(repository + "/releases/download/v" + version + "/" + filename + ".sha256"));
        services.Updates.State.StagedArchive = staged;
        services.Updates.State.StagedHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(staged)));
        using (var host = ModernFormsTestHost.Create())
        {
            services.Appearance.Initialize();
            using var panel = new UpdateSettingsPanel(services);
            host.Show(panel, 1100, 900);
            var checkbox = Find<CheckBox>(panel, "ApplicationUpdateOnExit");
            Assert.True(checkbox.Enabled);
            Assert.False(checkbox.Checked);
            foreach (var enabled in new[] { true, false })
            {
                Ui(host, () => checkbox.Checked = enabled);
                Pump(host, () => checkbox.Enabled && services.Updates.State.InstallOnExit == enabled);
                Assert.Equal(enabled, checkbox.Checked);
                var state = AtomicJsonFile.ReadAsync<ApplicationUpdateState>(Path.Combine(root, "state.json"));
                Pump(host, () => state.IsCompleted);
                Assert.Equal(enabled, (await state)!.InstallOnExit);
            }
        }
        await services.ShutdownAsync();
    }

    [Theory]
    [InlineData(false, 100)] [InlineData(true, 100)]
    [InlineData(false, 200)] [InlineData(true, 200)]
    public async Task StartupUpdateSettingsCommitOneStableHeightAcrossNativeLayout(bool modern, int scale)
    {
        using var fixture = new Fixture(); using var services = await fixture.Services(AppThemeMode.Dark);
        using var host = ModernFormsTestHost.Create();
        services.Appearance.Initialize(); AppTypography.SetScale(scale);
        using var form = new Form(); using var panel = new UpdateSettingsPanel(services);
        AppTypography.SetDashboardPresentation(panel, modern); form.Controls.Add(panel);
        var window = host.Show(form, 1100, 900); host.ProcessPendingWork();
        foreach (var width in new[] { 1050, 640, 960 })
        {
            var heightChanges = 0;
            void Changed(object? sender, EventArgs args) => heightChanges++;
            panel.SizeChanged += Changed;
            Ui(host, () => { panel.Width = width; panel.LayoutForWidth(width); });
            panel.SizeChanged -= Changed;
            Assert.InRange(heightChanges, 0, 4);
            var result = panel.Controls.Single(control => control.AccessibleAutomationId == "ApplicationUpdateStartupResult");
            Assert.True(result.Bottom <= panel.Height);
            var settled = panel.Height;
            Ui(host, () => panel.LayoutForWidth(width));
            Assert.Equal(settled, panel.Height);
        }
    }

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
            Ui(host, () => Find<LocalePickerButton>(root, "SetupLanguage").SelectLanguage("en", true));
            Assert.Equal(new LocalizationService("en")["Experience.WelcomeTitle"], Find<Label>(root, "SetupHeading").Text);
            Ui(host, () => Find<LocalePickerButton>(root, "SetupLanguage").SelectLanguage("pl", true));
            Assert.Equal(text["Experience.WelcomeTitle"], Find<Label>(root, "SetupHeading").Text);

            for (var step = 0; step < 6; step++)
            {
                AssertNoSetupError(root, fixture.Root);
                Assert.Equal(step, draft.Step);
                Assert.Contains(text["Experience.Step" + step], Find<Label>(root, "SetupProgress").Text);
                Assert.Equal(step + 1, Find<ProgressBar>(root, "SetupStepProgress").Value);
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
                    Assert.Contains(text["Experience.Selected"], Find<Button>(root, "SetupModern").AccessibleName);
                    Assert.DoesNotContain(text["Experience.Selected"], Find<Button>(root, "SetupClassic").AccessibleName);
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
            var installedNotes = Assert.IsType<InstalledReleaseNotes>(ReleaseNotesCatalog.ForInstalled(services.Experience.Version, services.Localization.Language));
            Assert.Equal(4, installedNotes.Sections.Count);
            foreach (var section in installedNotes.Sections)
            {
                Assert.Contains(Descendants(newsRoot).OfType<Label>(), label => label.Text == section.Title);
                var content = Find<TextBox>(newsRoot, "WhatsNewText-" + section.Category);
                Assert.True(content.ReadOnly && content.MultiLine);
                Assert.Equal(string.Join("\n\n", section.Items.Select(item => "• " + item)), content.Text);
                foreach (var item in section.Items) Assert.Contains(item, content.Text, StringComparison.Ordinal);
            }
            Assert.Contains(Descendants(newsRoot).OfType<Label>(), label => label.Text.Contains(services.Experience.Version, StringComparison.Ordinal));
            Click(host, newsRoot, "WhatsNewDone"); Pump(host, () => newsWindow.IsClosed);
            Assert.Equal(services.Experience.Version, services.Experience.State.LastWhatsNewAcknowledgedVersion);
        }
        await services.ShutdownAsync();
    }

    [Fact]
    public async Task FailedDraftAutosaveImmediatelyRevealsFeedbackWithoutResizing()
    {
        using var fixture = new Fixture(); using var services = await fixture.Services(AppThemeMode.Dark);
        var draft = SetupDraft.From(services, true); draft.Step = 2;
        await services.Experience.BeginAsync(draft);
        using (var host = ModernFormsTestHost.Create())
        {
            services.Appearance.Initialize();
            using var wizard = new SetupWizardForm(services, false, draft); var window = host.Show(wizard, 1040, 820); host.ProcessPendingWork();
            var root = Root(wizard); var feedback = Find<Label>(root, "SetupError"); Assert.Equal(0, feedback.Height);
            // Deny replacement of the existing atomic marker, as a real sharing violation would.
            using (var lockedMarker = new FileStream(Path.Combine(fixture.Root, "experience.json"), FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                Ui(host, () => Find<TextBox>(root, "SetupFilename").Text = "%(title)s - revised.%(ext)s");
                Pump(host, () => feedback.Text.Length > 0);
                Assert.Equal(services.Localization["Experience.SaveError"], feedback.Text);
                Assert.True(feedback.Height > 0);
                AssertGeometry(root, "Failed draft autosave");
            }
            Ui(host, wizard.Close); Pump(host, () => window.IsClosed);
            Assert.False(wizard.Accepted);
        }
        await services.ShutdownAsync();
    }

    [Fact]
    public async Task ReadLaterLeavesNotesUnacknowledgedAndThemedWindowCanBeReopened()
    {
        using var fixture = new Fixture(); using var services = await fixture.Services(AppThemeMode.Light);
        using (var host = ModernFormsTestHost.Create())
        {
            services.Appearance.Initialize();
            using var news = new WhatsNewForm(services); var window = host.Show(news, 1040, 820); host.ProcessPendingWork();
            Assert.Equal(services.Localization["Experience.WhatsNew"], news.Text);
            Assert.Empty(news.TitleBar.Text); // The mouse-transparent themed caption replaces only MFN's decorative painter.
            Assert.Equal(AppUi.GetColor(DownloadsVisual.Border), news.Style.Border.Color);
            AssertGeometry(Root(news), "Read later");
            Click(host, Root(news), "WhatsNewLater"); Pump(host, () => window.IsClosed);
            Assert.Null(services.Experience.State.LastWhatsNewAcknowledgedVersion);
            Assert.Null(news.Destination);
            using var reopened = new WhatsNewForm(services); var second = host.Show(reopened, 1040, 820); host.ProcessPendingWork();
            Click(host, Root(reopened), "WhatsNewDone"); Pump(host, () => second.IsClosed);
            Assert.Equal(services.Experience.Version, services.Experience.State.LastWhatsNewAcknowledgedVersion);
        }
        var saved = await UserExperienceService.OpenAsync(services.Paths, new NullAppLogger(), services.Experience.Version);
        Assert.Equal(services.Experience.Version, saved.State.LastWhatsNewAcknowledgedVersion);
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
        public async Task<AppServices> Services(AppThemeMode theme, HttpClient? httpClient = null)
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
            // Presentation fixtures must remain deterministic after the public tag exists.
            // Individual online cases supply their own controlled handler.
            await settings.SaveAsync(); var services = AppServices.CreateCore(paths, httpClient ?? new HttpClient(new UnavailableGitHub()), null, null, null, false, NotesVersion); await services.Tools.Initialization; return services;
        }
        public void Dispose() { AppTypography.SetScale(100); AppearancePresentation.Current = new(); if (Directory.Exists(Root)) Directory.Delete(Root, true); }
    }
}
