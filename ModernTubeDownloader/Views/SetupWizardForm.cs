using ModernFormsNext;
using ModernTubeDownloader.Localization;
using ModernTubeDownloader.Models;
using ModernTubeDownloader.Services;
using ModernTubeDownloader.Settings;
using ModernTubeDownloader.Theming;
using Path = System.IO.Path;

namespace ModernTubeDownloader.Views;

internal sealed class SetupWizardForm : Form
{
    private readonly AppServices services;
    private readonly bool upgrade;
    private readonly SetupDraft draft;
    private readonly LocalizationService text;
    private readonly Label heading = AppUi.Heading("", 20), subtitle = AppUi.Muted(), progress = AppUi.Muted(), error = AppUi.Muted();
    private readonly AppVectorIcon stepIcon = new(AppIconKind.Settings, 32, AppThemeTokens.Info);
    private readonly DashboardProgress stepProgress = new() { Minimum = 0, Maximum = 6, AccessibleAutomationId = "SetupStepProgress" };
    private readonly ToolTip tips = new();
    private readonly ExperienceScrollPanel body = new() { AutoScroll = true };
    private readonly Panel footer = new();
    private readonly Panel surface = new() { Dock = DockStyle.Fill };
    private readonly Panel pageHeader = new();
    private readonly Button back = new DashboardButton { AccessibleAutomationId = "SetupBack" }, next = new DashboardButton { AccessibleAutomationId = "SetupNext" }, skip = new DashboardButton { AccessibleAutomationId = "SetupSkip" };
    private readonly List<(Control Control, int Height)> rows = [];
    private readonly List<IDisposable> ownedImages = [];
    private bool building, busy, closeAllowed, closeWhenIdle, arrangingRows;
    private Task pendingSave = Task.CompletedTask;
    public bool Accepted { get; private set; }
    public Func<Form, IDisposable?>? RegisterDialog { get; set; }

    internal SetupWizardForm(AppServices services, bool upgrade, SetupDraft draft)
    {
        this.services = services; this.upgrade = upgrade; this.draft = draft; text = new(draft.Language);
        AccessibilityObject.AutomationId = upgrade ? "UpgradeChoiceWindow" : "SetupWizardWindow";
        Size = new(1040, 820); MinimumSize = new(720, 560); AppBranding.Apply(this);
        Controls.Add(surface); ExperienceVisual.Surface(surface); ExperienceVisual.Surface(body); ExperienceVisual.Surface(footer);
        DownloadsVisual.Card(pageHeader); ExperienceVisual.ApplyWindow(this);
        AppTypography.SetDashboardPresentation(surface, true);
        heading.Multiline = subtitle.Multiline = progress.Multiline = error.Multiline = true; AppUi.BindForeground(error, AppThemeTokens.Error);
        heading.AccessibleAutomationId = "SetupHeading"; progress.AccessibleAutomationId = "SetupProgress";
        error.AccessibleAutomationId = "SetupError";
        surface.Controls.AddRange([body, footer]); body.Controls.Add(pageHeader); pageHeader.Controls.AddRange([stepIcon, heading, subtitle, progress, stepProgress]); footer.Controls.AddRange([error, back, skip, next]);
        back.Click += async (_, _) => await RunAsync(async () => { draft.Step = Math.Max(0, draft.Step - 1); await SaveAsync(); BuildStep(); });
        next.Click += async (_, _) => await RunAsync(NextAsync);
        skip.Click += async (_, _) => await RunAsync(async () => { await pendingSave; await services.Experience.SkipAsync(upgrade); closeAllowed = true; Close(); });
        Closing += async (_, e) =>
        {
            if (closeAllowed) return;
            e.Cancel = true;
            if (busy) { closeWhenIdle = true; return; }
            await RunAsync(async () =>
            {
                try { await SaveAsync(); }
                finally { closeAllowed = true; Close(); } // A full disk must not trap the user in setup.
            });
        };
        SizeChanged += (_, _) => LayoutPage(); body.SizeChanged += (_, _) => LayoutRows();
        body.ViewportChanged += (_, _) => LayoutRows();
        surface.SizeChanged += (_, _) => LayoutPage(); AppTypography.RegisterLayout(surface, LayoutPage); BuildStep();
    }

    private async Task RunAsync(Func<Task> action)
    {
        if (busy) return; busy = true; body.Enabled = next.Enabled = back.Enabled = skip.Enabled = false; error.Text = "";
        try { await action(); }
        catch (Exception ex)
        {
            services.Logger.Error("Setup action could not complete; configuration progress remains resumable.", ex);
            error.Text = text[ex is InvalidDataException && ex.Message.StartsWith("Experience.", StringComparison.Ordinal) ? ex.Message : "Experience.SaveError"];
        }
        finally { busy = false; body.Enabled = next.Enabled = skip.Enabled = true; back.Enabled = !upgrade && draft.Step > 0; LayoutPage(); if (closeWhenIdle && !closeAllowed) { closeWhenIdle = false; Close(); } }
    }
    private Task SaveAsync() => pendingSave = services.Experience.SaveDraftAsync(draft);
    private async void Changed()
    {
        if (building) return;
        try { await SaveAsync(); }
        catch (Exception ex) { services.Logger.Error("Setup draft could not be saved.", ex); error.Text = text["Experience.SaveError"]; LayoutPage(); }
    }

    private async Task NextAsync()
    {
        if (!upgrade && draft.Step == 2) await Task.Run(() => UserExperienceService.ValidateFolders(draft));
        if (!upgrade && draft.Step < 5) { draft.Step++; await SaveAsync(); BuildStep(); return; }
        if (!upgrade) await Task.Run(() => UserExperienceService.ValidateFolders(draft));
        await pendingSave;
        var settings = services.Settings.Current;
        if (!upgrade)
        {
            settings.Language = draft.Language; settings.FinalOutputDirectory = Path.GetFullPath(draft.Output);
            settings.TemporaryDirectory = Path.GetFullPath(draft.Temporary); settings.FilenameTemplate = draft.Filename;
            settings.DefaultQualityPresetId = draft.Quality; settings.PreferredVideoContainer = draft.Container;
            settings.ConflictBehavior = draft.Conflict; settings.Notifications.Enabled = draft.Notifications;
            settings.Startup.ShowTrayIcon = draft.Tray;
        }
        // Only explicit completion applies preferences. A crash between the independent
        // atomic stores leaves InProgress; repeating completion is idempotent.
        await services.Updates.ApplyPolicyAsync(draft.AutomaticUpdates, draft.Frequency, draft.UpdateMode, installAtStartup: draft.InstallUpdatesAtStartup);
        var appearance = services.Appearance.Profile.Copy(); appearance.InterfaceMode = draft.Interface;
        await services.Appearance.ApplyProfileAsync(appearance);
        services.Localization.SetLanguage(settings.Language);
        await services.Experience.CompleteAsync(upgrade);
        Accepted = true; closeAllowed = true; Close();
    }

    private void BuildStep()
    {
        building = true;
        try
        {
            rows.Clear();
            foreach (var child in body.Controls.Where(c => c != pageHeader).ToArray()) { body.Controls.Remove(child); child.Dispose(); }
            foreach (var image in ownedImages) image.Dispose(); ownedImages.Clear(); rows.Clear();
            body.VerticalScrollProperties.Value = body.VerticalScrollProperties.Minimum;
            Text = text[upgrade ? "Experience.UpgradeTitle" : "Experience.WelcomeTitle"];
            heading.Text = text[upgrade ? "Experience.UpgradeTitle" : draft.Step == 0 ? "Experience.WelcomeTitle" : "Experience.Step" + draft.Step];
            subtitle.Text = text[upgrade ? "Experience.UpgradeBody" : "Experience.StepDescription" + draft.Step];
            progress.Text = upgrade ? text["Experience.ChangeLater"] : text.Get("Experience.Step", draft.Step + 1, 6, text["Experience.Step" + draft.Step]);
            stepProgress.Visible = !upgrade; stepProgress.Value = draft.Step + 1;
            stepIcon.SetKind(upgrade ? AppIconKind.Media : new[] { AppIconKind.Info, AppIconKind.Media, AppIconKind.Folder, AppIconKind.Download, AppIconKind.Settings, AppIconKind.Check }[draft.Step]);
            back.Text = text["Experience.Back"]; next.Text = text[upgrade ? "Experience.Apply" : draft.Step == 5 ? "Experience.Finish" : "Experience.Next"];
            skip.Text = text["Experience.Skip"]; back.Visible = !upgrade; back.Enabled = draft.Step > 0;
            if (upgrade) { AddInterfaces(); AddUpdates(); }
            else switch (draft.Step)
            {
                case 0:
                    AddText("Experience.Introduction", 70);
                    AddLanguagePicker();
                    AddText("Experience.Privacy", 76); break;
                case 1: AddInterfaces(); break;
                case 2:
                    AddPath("Settings.OutputDirectory", "SetupOutput", draft.Output, value => draft.Output = value);
                    AddPath("Settings.TemporaryDirectory", "SetupTemporary", draft.Temporary, value => draft.Temporary = value);
                    AddInput("Settings.FilenameTemplate", "SetupFilename", draft.Filename, value => draft.Filename = value);
                    AddCombo("Experience.Conflict", "SetupConflict", Enum.GetValues<FileConflictBehavior>().Select(v => text["Experience.Conflict" + v]).ToArray(), (int)draft.Conflict, i => draft.Conflict = (FileConflictBehavior)i);
                    AddText("Experience.DiskHint", 66); break;
                case 3:
                    AddCombo("Settings.DefaultQuality", "SetupQuality", QualityPreset.All.Select(q => text["Quality." + q.Id]).ToArray(), QualityPreset.All.ToList().FindIndex(q => q.Id == draft.Quality), i => draft.Quality = QualityPreset.All[i].Id);
                    AddCombo("Settings.DefaultContainer", "SetupContainer", Enum.GetValues<PreferredVideoContainer>().Select(value => text["Container." + value]).ToArray(), (int)draft.Container, i => draft.Container = (PreferredVideoContainer)i);
                    AddText("Experience.FormatHint", 72); break;
                case 4:
                    AddUpdates();
                    AddCheck("Notification.Preference.Enabled", "SetupNotifications", draft.Notifications, value => draft.Notifications = value);
                    AddCheck("Tray.ShowIcon", "SetupTray", draft.Tray, value => draft.Tray = value);
                    AddText("Experience.Privacy", 72); break;
                case 5:
                    AddText("Experience.SummaryHint", 52);
                    AddValue(text["Settings.Language"], LocaleRegistry.Get(draft.Language).DisplayName(text));
                    AddValue(text["Dashboard.InterfaceStyle"], text[draft.Interface == AppInterfaceMode.Classic ? "Appearance.Value.Classic" : "Appearance.Value.ModernDashboard"]);
                    AddValue(text["Settings.OutputDirectory"], draft.Output); AddValue(text["Settings.TemporaryDirectory"], draft.Temporary);
                    AddValue(text["Settings.FilenameTemplate"], draft.Filename);
                    AddValue(text["Experience.Conflict"], text["Experience.Conflict" + draft.Conflict]);
                    AddValue(text["Settings.DefaultQuality"], text["Quality." + draft.Quality] + " · " + text["Container." + draft.Container]);
                    AddValue(text["Updates.Automatic"], draft.AutomaticUpdates ? text["Experience.Mode" + draft.UpdateMode] : text["Dashboard.Disabled"]);
                    AddValue(text["Updates.StartupInstall"], text[draft.AutomaticUpdates && draft.InstallUpdatesAtStartup ? "Dashboard.Enabled" : "Dashboard.Disabled"]);
                    AddValue(text["Notification.Preference.Enabled"], text[draft.Notifications ? "Dashboard.Enabled" : "Dashboard.Disabled"]);
                    AddValue(text["Tray.ShowIcon"], text[draft.Tray ? "Dashboard.Enabled" : "Dashboard.Disabled"]);
                    break;
            }
            AppTypography.CaptureRecipes(body).Invoke(); LayoutPage();
        }
        finally { building = false; LayoutPage(); }
    }

    private void AddLanguagePicker()
    {
        var card = new ExperienceCard(); var label = ExperienceVisual.Paragraph(text["Settings.Language"]);
        var picker = new LocalePickerButton(text) { AccessibleAutomationId = "SetupLanguage", RegisterDialog = dialog => RegisterDialog?.Invoke(dialog) }; picker.SelectLanguage(draft.Language);
        picker.SelectedLanguageChanged += (_, _) => { draft.Language = picker.SelectedLanguage; text.SetLanguage(draft.Language); Changed(); BuildStep(); };
        card.Controls.AddRange([label, picker]); body.Controls.Add(card); rows.Add((card, 0));
        card.ArrangeContent = width =>
        {
            var inside = Math.Max(1, width - 32); label.SetBounds(16, 12, inside, DashboardVisual.LabelHeight(label, inside, 12));
            picker.SetBounds(16, label.Bottom + 8, inside, AppTypography.TextHeight(40)); return picker.Bottom + 14;
        };
    }

    private void AddInterfaces()
    {
        var container = new ExperienceCard(); DashboardVisual.Flat(container); ExperienceVisual.Surface(container);
        rows.Add((container, 0)); body.Controls.Add(container);
        var options = new ExperienceCard[2]; var buttons = new DashboardButton[2];
        var previews = new DownloadsPictureBox[2]; var descriptions = new Label[2]; var checks = new AppVectorIcon[2];
        for (var i = 0; i < 2; i++)
        {
            var index = i; var option = options[i] = new ExperienceCard();
            var button = buttons[i] = new DashboardButton { Text = text[i == 0 ? "Appearance.Value.Classic" : "Appearance.Value.ModernDashboard"], AccessibleAutomationId = i == 0 ? "SetupClassic" : "SetupModern" };
            var description = descriptions[i] = ExperienceVisual.Paragraph(text[i == 0 ? "Experience.ClassicDescription" : "Experience.ModernDescription"]);
            var picture = previews[i] = new DownloadsPictureBox { SizeMode = PictureBoxSizeMode.Zoom, RoundedPreview = true,
                AccessibleAutomationId = i == 0 ? "SetupPreviewClassic" : "SetupPreviewModern", AccessibleName = text["Experience.RealPreview"] };
            AppUi.BindBackground(picture, DownloadsVisual.Background);
            using var stream = typeof(SetupWizardForm).Assembly.GetManifestResourceStream("ModernTubeDownloader.Assets.Preview" + (i == 0 ? "Classic" : "Modern") + ".png");
            if (stream is not null) { var bitmap = SkiaSharp.SKBitmap.Decode(stream); picture.Image = bitmap; ownedImages.Add(bitmap); }
            var check = checks[i] = new AppVectorIcon(AppIconKind.Check, 20, AppThemeTokens.Success)
                { AccessibilityView = ModernFormsNext.Accessibility.AccessibilityView.Hidden };
            option.Controls.AddRange([picture, button, description, check]); container.Controls.Add(option);
            button.Click += (_, _) => { draft.Interface = index == 0 ? AppInterfaceMode.Classic : AppInterfaceMode.ModernDashboard; Present(); Changed(); };
            picture.Click += (_, _) => button.PerformClick(); description.Click += (_, _) => button.PerformClick();
        }
        void Present()
        {
            for (var i = 0; i < 2; i++)
            {
                var selected = draft.Interface == (i == 0 ? AppInterfaceMode.Classic : AppInterfaceMode.ModernDashboard);
                StyleAction(buttons[i], selected); checks[i].Visible = selected;
                buttons[i].AccessibleName = buttons[i].Text + (selected ? " · " + text["Experience.Selected"] : "");
                AppUi.BindGradient(options[i], selected ? DownloadsVisual.Selected : DownloadsVisual.Panel, selected ? DownloadsVisual.SelectedEnd : DownloadsVisual.PanelEnd);
                AppUi.BindBorder(options[i], selected ? AppThemeTokens.Accent : DownloadsVisual.Border);
                options[i].Style.Border.Width = selected ? 2 : 1;
            }
        }
        container.ArrangeContent = width =>
        {
            var columns = width >= AppTypography.TextHeight(620) ? 2 : 1;
            var cardWidth = columns == 2 ? (width - 14) / 2 : width;
            var cardHeight = 0;
            for (var i = 0; i < 2; i++)
            {
                var inside = Math.Max(1, cardWidth - 28); var line = AppTypography.TextHeight(40);
                buttons[i].SetBounds(14, 14, inside, line);
                var previewHeight = Math.Clamp(inside * 2 / 3, 150, 300);
                previews[i].SetBounds(14, buttons[i].Bottom + 12, inside, previewHeight);
                descriptions[i].SetBounds(14, previews[i].Bottom + 12, Math.Max(1, inside - 30), DashboardVisual.LabelHeight(descriptions[i], Math.Max(1, inside - 30), 12));
                checks[i].SetBounds(cardWidth - 36, descriptions[i].Top + 2, 20, 20);
                cardHeight = Math.Max(cardHeight, descriptions[i].Bottom + 16);
            }
            for (var i = 0; i < 2; i++) options[i].SetBounds(columns == 2 ? i * (cardWidth + 14) : 0, columns == 2 ? 0 : i * (cardHeight + 14), cardWidth, cardHeight);
            return columns == 2 ? cardHeight : cardHeight * 2 + 14;
        };
        Present(); AddText("Experience.RealPreview", 44);
    }
    private void AddUpdates()
    {
        ComboBox? mode = null;
        AddCheck("Experience.Automatic", "SetupAutomaticUpdates", draft.AutomaticUpdates, value =>
        { draft.AutomaticUpdates = value; if (mode is not null) mode.Enabled = value; });
        mode = AddCombo("Experience.UpdateMode", "SetupUpdateMode", Enum.GetValues<UpdateInstallMode>().Select(value => text["Experience.Mode" + value]).ToArray(), (int)draft.UpdateMode, i => draft.UpdateMode = (UpdateInstallMode)i);
        mode.Enabled = draft.AutomaticUpdates;
        AddCheck("Updates.StartupInstall", "SetupStartupUpdate", draft.InstallUpdatesAtStartup, value => draft.InstallUpdatesAtStartup = value);
        AddText("Updates.StartupHelp", 84);
        AddText(services.Settings.UpdatePolicyNeedsConsent ? "Experience.LegacyUpdates" : "Experience.UpdateSafety", 84);
    }
    private void AddText(string key, int height)
    {
        var card = new ExperienceCard(); var label = ExperienceVisual.Paragraph(text[key]);
        var icon = new AppVectorIcon(key == "Experience.SummaryHint" ? AppIconKind.Check : AppIconKind.Info, 20, AppThemeTokens.Info);
        card.Controls.AddRange([icon, label]); body.Controls.Add(card); rows.Add((card, height));
        card.ArrangeContent = width =>
        {
            icon.SetBounds(14, 16, 20, 20); var textWidth = Math.Max(1, width - 64);
            label.SetBounds(46, 14, textWidth, DashboardVisual.LabelHeight(label, textWidth, 30));
            return Math.Max(52, label.Bottom + 14);
        };
    }
    private void AddValue(string name, string value)
    {
        var card = new ExperienceCard(); var key = ExperienceVisual.Paragraph(name); var detail = ExperienceVisual.Paragraph(value, true);
        AppTypography.Bind(detail, 10.5f); AppUi.BindForeground(detail); tips.SetToolTip(detail, value);
        card.Controls.AddRange([key, detail]); body.Controls.Add(card); rows.Add((card, 0));
        card.ArrangeContent = width =>
        {
            var inside = Math.Max(1, width - 32); var inline = width >= AppTypography.TextHeight(620);
            var keyWidth = inline ? Math.Min(AppTypography.TextHeight(230), inside / 3) : inside;
            key.SetBounds(16, 14, keyWidth, DashboardVisual.LabelHeight(key, keyWidth, 15));
            var detailWidth = inline ? Math.Max(1, inside - keyWidth - 18) : inside;
            detail.SetBounds(inline ? key.Right + 18 : 16, inline ? 14 : key.Bottom + 8, detailWidth, DashboardVisual.LabelHeight(detail, detailWidth, 100));
            return Math.Max(key.Bottom, detail.Bottom) + 14;
        };
    }
    private void AddCheck(string key, string id, bool value, Action<bool> set)
    {
        var card = new ExperienceCard();
        var control = new DashboardToggle { Text = text[key], Checked = value, AccessibleAutomationId = id };
        AppUi.BindForeground(control); control.CheckedChanged += (_, _) => { set(control.Checked); Changed(); };
        card.Controls.Add(control); body.Controls.Add(card); rows.Add((card, 0));
        card.ArrangeContent = width =>
        {
            var inside = Math.Max(1, width - 32); var style = control.CurrentStyle;
            var measured = TextMeasurer.MeasureText(control.Text, style.GetFont(), style.GetFontSize(), new System.Drawing.Size(Math.Max(1, inside - 52), int.MaxValue), AppTypography.TextStyle(control));
            var height = Math.Max(AppTypography.TextHeight(34), (int)Math.Ceiling(measured.Height) + 8);
            control.SetBounds(16, 12, inside, height); return height + 24;
        };
    }
    private ComboBox AddCombo(string key, string id, string[] items, int selected, Action<int> set)
    {
        var panel = new ExperienceCard(); var label = ExperienceVisual.Paragraph(text[key]); var combo = new ComboBox { AccessibleAutomationId = id, AccessibleName = text[key] };
        foreach (var item in items) combo.Items.Add(item); combo.SelectedIndex = Math.Max(0, selected); AppUi.Input(combo);
        combo.SelectedIndexChanged += (_, _) => { if (combo.SelectedIndex >= 0) { set(combo.SelectedIndex); Changed(); } };
        panel.Controls.AddRange([label, combo]); body.Controls.Add(panel); rows.Add((panel, 0));
        panel.ArrangeContent = width =>
        {
            var inside = Math.Max(1, width - 32); label.SetBounds(16, 12, inside, DashboardVisual.LabelHeight(label, inside, 12));
            combo.SetBounds(16, label.Bottom + 8, inside, AppTypography.TextHeight(40)); return combo.Bottom + 14;
        };
        return combo;
    }
    private void AddInput(string key, string id, string value, Action<string> set) => AddPath(key, id, value, set, false);
    private void AddPath(string key, string id, string value, Action<string> set, bool browse = true)
    {
        var panel = new ExperienceCard(); var label = ExperienceVisual.Paragraph(text[key]); var input = new TextBox { Text = value, AccessibleAutomationId = id, AccessibleName = text[key] };
        AppUi.Input(input); tips.SetToolTip(input, input.Text);
        input.TextChanged += (_, _) => { set(input.Text); tips.SetToolTip(input, input.Text); Changed(); };
        var pick = new DashboardButton { AccessibleAutomationId = id + "Browse", AccessibleName = text["Settings.Browse"], Visible = browse }; DownloadsVisual.Button(pick);
        pick.Click += async (_, _) =>
        {
            var dialog = new FolderBrowserDialog { Title = text[key], InitialDirectory = input.Text };
            if (await dialog.ShowDialog(this) == DialogResult.OK && dialog.SelectedPath is { } path) input.Text = path;
        };
        panel.Controls.AddRange([label, input, pick]); body.Controls.Add(panel); rows.Add((panel, 0));
        panel.ArrangeContent = width =>
        {
            var inside = Math.Max(1, width - 32); var line = AppTypography.TextHeight(40);
            label.SetBounds(16, 12, inside, DashboardVisual.LabelHeight(label, inside, 12));
            input.SetBounds(16, label.Bottom + 8, Math.Max(1, inside - (browse ? line + 8 : 0)), line);
            pick.SetBounds(input.Right + 8, input.Top, browse ? line : 0, browse ? line : 0); DashboardVisual.Icon(pick, AppIconKind.Folder, true);
            return input.Bottom + 14;
        };
    }
    private void LayoutPage()
    {
        var width = Math.Max(1, ClientSize.Width - 48); var line = AppTypography.TextHeight(42);
        var errorHeight = string.IsNullOrEmpty(error.Text) ? 0 : DashboardVisual.LabelHeight(error, width, 4);
        footer.SetBounds(24, ClientSize.Height - line - errorHeight - 30, width, line + errorHeight + 6);
        error.SetBounds(0, 0, width, errorHeight);
        StyleAction(next, true); StyleAction(back); StyleAction(skip);
        var nextWidth = Math.Min(width / 2, DashboardVisual.ActionWidth(next)); var buttonY = error.Bottom + 6;
        next.SetBounds(width - nextWidth, buttonY, nextWidth, line);
        back.SetBounds(0, buttonY, Math.Min(width / 5, DashboardVisual.ActionWidth(back)), line);
        var skipLeft = back.Visible ? back.Right + 10 : 0;
        skip.SetBounds(skipLeft, buttonY, Math.Min(Math.Max(1, next.Left - skipLeft - 10), DashboardVisual.ActionWidth(skip)), line);
        body.SetBounds(24, 18, width, Math.Max(1, footer.Top - 30)); LayoutRows();
    }
    private void LayoutRows()
    {
        if (building || arrangingRows) return;
        arrangingRows = true;
        try
        {
            for (var pass = 0; pass < 3; pass++)
            {
                var width = body.DisplayRectangle.Width;
                ArrangeRows();
                if (body.DisplayRectangle.Width == width) break;
            }
        }
        finally { arrangingRows = false; }
    }
    private void ArrangeRows()
    {
        var viewport = body.DisplayRectangle; var width = Math.Max(1, viewport.Width - 10);
        var textWidth = Math.Max(1, width - 88);
        stepIcon.SetBounds(18, 20, 32, 32);
        heading.SetBounds(66, 16, textWidth, DashboardVisual.LabelHeight(heading, textWidth, 10));
        subtitle.SetBounds(66, heading.Bottom + 8, textWidth, DashboardVisual.LabelHeight(subtitle, textWidth, 15));
        progress.SetBounds(18, subtitle.Bottom + 14, Math.Max(1, width - 36), DashboardVisual.LabelHeight(progress, Math.Max(1, width - 36), 10));
        stepProgress.SetBounds(18, progress.Bottom + 12, Math.Max(1, width - 36), upgrade ? 0 : 5);
        pageHeader.SetBounds(viewport.X, viewport.Y, width, (upgrade ? progress.Bottom : stepProgress.Bottom) + 18); var y = pageHeader.Bottom + 14;
        foreach (var (control, height) in rows)
        {
            var measuredHeight = control is ExperienceCard card ? card.FitWidth(width) : AppTypography.TextHeight(height);
            if (control is Label label) measuredHeight = Math.Max(AppTypography.TextHeight(28), DashboardVisual.LabelHeight(label, width, 40));
            if (control is CheckBox check)
            {
                var style = check.CurrentStyle;
                var measured = TextMeasurer.MeasureText(check.Text, style.GetFont(), style.GetFontSize(), new System.Drawing.Size(Math.Max(1, width - 48), int.MaxValue), AppTypography.TextStyle(check));
                measuredHeight = Math.Max(measuredHeight, (int)Math.Ceiling(measured.Height) + 12);
            }
            control.SetBounds(viewport.X, y, width, measuredHeight); y = control.Bottom + 14;
        }
    }
    private static void StyleAction(Button button, bool primary = false) => ExperienceVisual.Action(button, primary);
    protected override void Dispose(bool disposing)
    { if (disposing) { tips.Dispose(); foreach (var image in ownedImages) image.Dispose(); ownedImages.Clear(); } base.Dispose(disposing); }
}

// Native modal visibility can reveal a vertical scrollbar without resizing the
// panel itself. Reflow against the canonical viewport after that transition;
// otherwise rows retain the pre-scrollbar width and create a horizontal rail.
internal sealed class ExperienceScrollPanel : AppScrollPanel
{
    internal event EventHandler? ViewportChanged;
    protected override void OnScrollViewportChanged(EventArgs e)
    {
        base.OnScrollViewportChanged(e);
        ViewportChanged?.Invoke(this, e);
    }
}
