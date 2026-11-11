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
    private readonly Label heading = AppUi.Heading("", 23), subtitle = AppUi.Muted(), progress = AppUi.Muted(), error = AppUi.Muted();
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

    internal SetupWizardForm(AppServices services, bool upgrade, SetupDraft draft)
    {
        this.services = services; this.upgrade = upgrade; this.draft = draft; text = new(draft.Language);
        AccessibilityObject.AutomationId = upgrade ? "UpgradeChoiceWindow" : "SetupWizardWindow";
        Size = new(1040, 820); MinimumSize = new(720, 560); AppBranding.Apply(this);
        Controls.Add(surface); AppUi.BindBackground(surface); AppUi.BindBackground(body); AppUi.BindBackground(footer);
        AppUi.BindBackground(pageHeader);
        AppTypography.SetDashboardPresentation(surface, true);
        heading.Multiline = subtitle.Multiline = progress.Multiline = error.Multiline = true; AppUi.BindForeground(error, AppThemeTokens.Error);
        heading.AccessibleAutomationId = "SetupHeading"; progress.AccessibleAutomationId = "SetupProgress";
        error.AccessibleAutomationId = "SetupError";
        surface.Controls.AddRange([body, footer]); body.Controls.Add(pageHeader); pageHeader.Controls.AddRange([heading, subtitle, progress]); footer.Controls.AddRange([error, back, skip, next]);
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
        catch (Exception ex) { services.Logger.Error("Setup draft could not be saved.", ex); error.Text = text["Experience.SaveError"]; }
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
        await services.Updates.ApplyPolicyAsync(draft.AutomaticUpdates, draft.Frequency, draft.UpdateMode);
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
            heading.Text = Text;
            subtitle.Text = text[upgrade ? "Experience.UpgradeBody" : "Experience.WelcomeBody"];
            progress.Text = upgrade ? text["Experience.ChangeLater"] : text.Get("Experience.Step", draft.Step + 1, 6, text["Experience.Step" + draft.Step]);
            back.Text = text["Experience.Back"]; next.Text = text[upgrade ? "Experience.Apply" : draft.Step == 5 ? "Experience.Finish" : "Experience.Next"];
            skip.Text = text["Experience.Skip"]; back.Visible = !upgrade; back.Enabled = draft.Step > 0;
            if (upgrade) { AddInterfaces(); AddUpdates(); }
            else switch (draft.Step)
            {
                case 0:
                    AddText("Experience.Introduction", 70);
                    AddCombo("Settings.Language", "SetupLanguage", new[] { "Polski", "English" }, draft.Language == "pl" ? 0 : 1, index => { draft.Language = index == 0 ? "pl" : "en"; text.SetLanguage(draft.Language); Changed(); BuildStep(); });
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
                    AddValue(text["Settings.Language"], draft.Language == "pl" ? "Polski" : "English");
                    AddValue(text["Dashboard.InterfaceStyle"], text[draft.Interface == AppInterfaceMode.Classic ? "Appearance.Value.Classic" : "Appearance.Value.ModernDashboard"]);
                    AddValue(text["Settings.OutputDirectory"], draft.Output); AddValue(text["Settings.TemporaryDirectory"], draft.Temporary);
                    AddValue(text["Settings.DefaultQuality"], text["Quality." + draft.Quality] + " · " + text["Container." + draft.Container]);
                    AddValue(text["Updates.Automatic"], draft.AutomaticUpdates ? text["Experience.Mode" + draft.UpdateMode] : text["Dashboard.Disabled"]);
                    break;
            }
            AppTypography.CaptureRecipes(body).Invoke(); LayoutPage();
        }
        finally { building = false; LayoutPage(); }
    }

    private void AddInterfaces()
    {
        if (!upgrade) AddText("Experience.ChangeLater", 46);
        var card = new Panel(); AppUi.BindBackground(card); rows.Add((card, 310)); body.Controls.Add(card);
        var classic = new DashboardButton { Text = text["Appearance.Value.Classic"], AccessibleAutomationId = "SetupClassic" };
        var modern = new DashboardButton { Text = text["Appearance.Value.ModernDashboard"], AccessibleAutomationId = "SetupModern" };
        var previews = new PictureBox[2];
        for (var i = 0; i < 2; i++)
        {
            var picture = new PictureBox { SizeMode = PictureBoxSizeMode.Zoom, AccessibleAutomationId = i == 0 ? "SetupPreviewClassic" : "SetupPreviewModern", AccessibleName = text["Experience.RealPreview"] };
            AppUi.BindBackground(picture);
            using var stream = typeof(SetupWizardForm).Assembly.GetManifestResourceStream("ModernTubeDownloader.Assets.Preview" + (i == 0 ? "Classic" : "Modern") + ".png");
            if (stream is not null) { var bitmap = SkiaSharp.SKBitmap.Decode(stream); picture.Image = bitmap; ownedImages.Add(bitmap); }
            previews[i] = picture; card.Controls.Add(picture);
        }
        card.Controls.AddRange([classic, modern]);
        void Present() { StyleAction(classic, draft.Interface == AppInterfaceMode.Classic); StyleAction(modern, draft.Interface == AppInterfaceMode.ModernDashboard); }
        classic.Click += (_, _) => { draft.Interface = AppInterfaceMode.Classic; Present(); Changed(); };
        modern.Click += (_, _) => { draft.Interface = AppInterfaceMode.ModernDashboard; Present(); Changed(); };
        previews[0].Click += (_, _) => classic.PerformClick(); previews[1].Click += (_, _) => modern.PerformClick();
        card.SizeChanged += (_, _) =>
        {
            var half = Math.Max(1, (card.Width - 16) / 2); var line = AppTypography.TextHeight(42);
            classic.SetBounds(0, 0, half, line); modern.SetBounds(half + 16, 0, half, line);
            previews[0].SetBounds(0, line + 10, half, Math.Max(1, card.Height - line - 10));
            previews[1].SetBounds(half + 16, line + 10, half, Math.Max(1, card.Height - line - 10));
        };
        Present(); AddText("Experience.RealPreview", 44);
    }
    private void AddUpdates()
    {
        AddCheck("Experience.Automatic", "SetupAutomaticUpdates", draft.AutomaticUpdates, value => draft.AutomaticUpdates = value);
        AddCombo("Experience.UpdateMode", "SetupUpdateMode", Enum.GetValues<UpdateInstallMode>().Select(mode => text["Experience.Mode" + mode]).ToArray(), (int)draft.UpdateMode, i => draft.UpdateMode = (UpdateInstallMode)i);
        AddText(services.Settings.UpdatePolicyNeedsConsent ? "Experience.LegacyUpdates" : "Experience.UpdateSafety", 84);
    }
    private void AddText(string key, int height)
    { var label = AppUi.Muted(text[key]); label.Multiline = true; body.Controls.Add(label); rows.Add((label, height)); }
    private void AddValue(string name, string value)
    { var label = AppUi.Muted(name + "\n" + value); label.Multiline = true; body.Controls.Add(label); rows.Add((label, 76)); }
    private void AddCheck(string key, string id, bool value, Action<bool> set)
    {
        var control = new CheckBox { Text = text[key], Checked = value, AccessibleAutomationId = id };
        AppUi.BindForeground(control); AppUi.BindBackground(control); control.CheckedChanged += (_, _) => { set(control.Checked); Changed(); };
        body.Controls.Add(control); rows.Add((control, 48));
    }
    private void AddCombo(string key, string id, string[] items, int selected, Action<int> set)
    {
        var panel = new Panel(); var label = AppUi.Muted(text[key]); var combo = new ComboBox { AccessibleAutomationId = id, AccessibleName = text[key] };
        AppUi.BindBackground(panel);
        foreach (var item in items) combo.Items.Add(item); combo.SelectedIndex = Math.Max(0, selected); AppUi.Input(combo);
        combo.SelectedIndexChanged += (_, _) => { if (combo.SelectedIndex >= 0) { set(combo.SelectedIndex); Changed(); } };
        panel.Controls.AddRange([label, combo]); body.Controls.Add(panel); rows.Add((panel, 90));
        panel.SizeChanged += (_, _) => { label.SetBounds(0, 0, panel.Width, AppTypography.TextHeight(28)); combo.SetBounds(0, label.Bottom + 4, panel.Width, AppTypography.TextHeight(42)); };
    }
    private void AddInput(string key, string id, string value, Action<string> set) => AddPath(key, id, value, set, false);
    private void AddPath(string key, string id, string value, Action<string> set, bool browse = true)
    {
        var panel = new Panel(); var label = AppUi.Muted(text[key]); var input = new TextBox { Text = value, AccessibleAutomationId = id, AccessibleName = text[key] };
        AppUi.BindBackground(panel);
        AppUi.Input(input); input.TextChanged += (_, _) => { set(input.Text); Changed(); };
        var pick = new DashboardButton { Text = "…", AccessibleAutomationId = id + "Browse", AccessibleName = text["Settings.Browse"], Visible = browse }; DownloadsVisual.Button(pick);
        pick.Click += async (_, _) =>
        {
            var dialog = new FolderBrowserDialog { Title = text[key], InitialDirectory = input.Text };
            if (await dialog.ShowDialog(this) == DialogResult.OK && dialog.SelectedPath is { } path) input.Text = path;
        };
        panel.Controls.AddRange([label, input, pick]); body.Controls.Add(panel); rows.Add((panel, 90));
        panel.SizeChanged += (_, _) =>
        { var line = AppTypography.TextHeight(42); label.SetBounds(0, 0, panel.Width, AppTypography.TextHeight(28)); input.SetBounds(0, label.Bottom + 4, Math.Max(1, panel.Width - (browse ? line + 8 : 0)), line); pick.SetBounds(input.Right + 8, input.Top, browse ? line : 0, browse ? line : 0); };
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
        heading.SetBounds(0, 0, width, DashboardVisual.LabelHeight(heading, width, 6));
        subtitle.SetBounds(0, heading.Bottom + 8, width, DashboardVisual.LabelHeight(subtitle, width, 8));
        progress.SetBounds(0, subtitle.Bottom + 12, width, DashboardVisual.LabelHeight(progress, width, 6));
        pageHeader.SetBounds(viewport.X, viewport.Y, width, progress.Bottom + 8); var y = pageHeader.Bottom + 12;
        foreach (var (control, height) in rows)
        {
            var measuredHeight = AppTypography.TextHeight(height);
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
    private static void StyleAction(Button button, bool primary = false)
    {
        DownloadsVisual.Button(button, primary);
        button.TextBrush = primary ? new ModernFormsNext.Drawing.SolidColorBrush(SkiaSharp.SKColors.White) : null;
    }
    protected override void Dispose(bool disposing)
    { if (disposing) { foreach (var image in ownedImages) image.Dispose(); ownedImages.Clear(); } base.Dispose(disposing); }
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
