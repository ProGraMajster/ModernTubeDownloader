using ModernFormsNext;
using ModernTubeDownloader.Services;
using ModernTubeDownloader.Settings;
using ModernTubeDownloader.Theming;

namespace ModernTubeDownloader.Views;

internal sealed class UpdateSettingsPanel : Panel
{
    private readonly AppServices services;
    private readonly Label heading = AppUi.Heading("", 15), versions = AppUi.Muted(), frequencyLabel = AppUi.Muted(), modeLabel = AppUi.Muted(), status = AppUi.Muted();
    private readonly DashboardToggle automatic = new(), onExit = new();
    private readonly ComboBox frequency = new(), mode = new();
    private readonly Button save = new(), check = new(), download = new(), install = new(), notes = new(), later = new(), skip = new();
    private readonly ProgressBar progress = new() { Minimum = 0, Maximum = 100 };
    private bool disposed, busy;
    public event EventHandler? InstallRequested;

    public UpdateSettingsPanel(AppServices services)
    {
        this.services = services;
        Height = 452;
        AccessibleAutomationId = "ApplicationUpdatesSection";
        AppUi.Card(this);
        automatic.Checked = services.Settings.Current.Updates.AutomaticChecks;
        AppUi.BindForeground(automatic); AppUi.BindForeground(onExit);
        AppUi.Input(frequency); AppUi.Input(mode);
        foreach (var button in new[] { save, check, download, install, notes, later, skip }) AppUi.Secondary(button);
        AppUi.Primary(install);
        Controls.AddRange([heading, versions, automatic, frequencyLabel, frequency, modeLabel, mode, save, check, status, progress, download, install, notes, later, skip, onExit]);
        frequency.AccessibleAutomationId = "ApplicationUpdateFrequency"; mode.AccessibleAutomationId = "ApplicationUpdateMode";
        check.AccessibleAutomationId = "ApplicationUpdateCheck"; status.AccessibleAutomationId = "ApplicationUpdateStatus";
        download.AccessibleAutomationId = "ApplicationUpdateDownload"; install.AccessibleAutomationId = "ApplicationUpdateInstall";
        ApplyLocalization();
        frequency.SelectedIndex = (int)services.Settings.Current.Updates.Frequency;
        mode.SelectedIndex = (int)services.Settings.Current.Updates.Mode;
        save.Click += (_, _) => _ = RunAsync(async () =>
        {
            services.Settings.Current.Updates.AutomaticChecks = automatic.Checked;
            services.Settings.Current.Updates.Frequency = (UpdateCheckFrequency)Math.Max(0, frequency.SelectedIndex);
            services.Settings.Current.Updates.Mode = (UpdateInstallMode)Math.Max(0, mode.SelectedIndex);
            await services.Settings.SaveAsync();
        });
        check.Click += (_, _) => _ = RunAsync(() => services.Updates.CheckAsync());
        download.Click += (_, _) => _ = RunAsync(() => services.Updates.DownloadAsync());
        install.Click += (_, _) => InstallRequested?.Invoke(this, EventArgs.Empty);
        later.Click += (_, _) => _ = RunAsync(() => services.Updates.DeferAsync(false));
        skip.Click += (_, _) => _ = RunAsync(() => services.Updates.DeferAsync(true));
        onExit.CheckedChanged += (_, _) => { if (!busy && services.Updates.Ready) _ = RunAsync(() => services.Updates.SetInstallOnExitAsync(onExit.Checked)); };
        notes.Click += NotesClicked;
        SizeChanged += (_, _) => LayoutForWidth(Width);
        services.Updates.Changed += UpdateChanged;
        services.Localization.LanguageChanged += LanguageChanged;
        Refresh();
    }

    private async Task RunAsync(Func<Task> action)
    {
        if (busy || disposed) return;
        busy = true; Refresh();
        try { await action(); }
        catch (OperationCanceledException) { }
        catch (Exception error) { services.Logger.Error("The update settings action failed.", error); status.Text = services.Localization["Updates.Failed"]; }
        finally { busy = false; if (!disposed) Refresh(); }
    }
    private async void NotesClicked(object? sender, EventArgs e)
    {
        if (services.Updates.State.Available is not { } release || FindForm() is not { } owner) return;
        try
        {
            using var window = new Form { Text = services.Localization["Updates.Notes"], Size = new System.Drawing.Size(820, 620) };
            AppBranding.Apply(window);
            var content = new TextBox { Dock = DockStyle.Fill, MultiLine = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
                TextAlign = ContentAlignment.TopLeft, Text = release.Notes };
            AppUi.Input(content); window.Controls.Add(content);
            await window.ShowDialog(owner);
        }
        catch (Exception error) { services.Logger.Error("Release notes could not be opened.", error); }
    }
    private void ApplyLocalization()
    {
        var text = services.Localization;
        heading.Text = text["Updates.Title"]; automatic.Text = text["Updates.Automatic"];
        frequencyLabel.Text = text["Updates.Frequency"]; modeLabel.Text = text["Updates.Mode"];
        var frequencyIndex = frequency.SelectedIndex; var modeIndex = mode.SelectedIndex;
        frequency.Items.Clear(); mode.Items.Clear();
        foreach (var key in new[] { "StartupDaily", "Daily", "Weekly" }) frequency.Items.Add(text["Updates." + key]);
        foreach (var key in new[] { "NotifyOnly", "AutoDownload", "InstallOnExit" }) mode.Items.Add(text["Updates." + key]);
        frequency.SelectedIndex = Math.Max(0, frequencyIndex); mode.SelectedIndex = Math.Max(0, modeIndex);
        save.Text = text["Updates.Save"]; check.Text = text["Updates.Check"]; download.Text = text["Updates.Download"];
        install.Text = text["Updates.Install"]; notes.Text = text["Updates.Notes"]; later.Text = text["Updates.Later"]; skip.Text = text["Updates.Skip"];
        onExit.Text = text["Updates.OnExit"];
    }
    private void Refresh()
    {
        if (disposed) return;
        var state = services.Updates.State; var ready = services.Updates.Ready;
        versions.Text = services.Localization.Get("Updates.Versions", services.Updates.CurrentVersion, state.Available?.Version ?? "—");
        status.Text = services.Localization[services.Updates.StatusKey];
        progress.Value = (int)Math.Clamp(services.Updates.Progress, 0, 100);
        download.Enabled = !busy && state.Available is not null && !ready;
        install.Enabled = !busy && ready;
        check.Enabled = save.Enabled = !busy;
        notes.Enabled = later.Enabled = skip.Enabled = !busy && state.Available is not null;
        onExit.Enabled = !busy && ready;
        if (onExit.Checked != state.InstallOnExit)
        { var previous = busy; busy = true; onExit.Checked = state.InstallOnExit; busy = previous; }
    }
    public void LayoutForWidth(int width)
    {
        if (AppTypography.IsDashboard(this) && width < 540)
        {
            var innerWidth = width - 32; var line = AppTypography.TextHeight(36); var label = AppTypography.TextHeight(24);
            heading.SetBounds(16,16,innerWidth,label); versions.SetBounds(16,heading.Bottom+8,innerWidth,label); automatic.SetBounds(16,versions.Bottom+10,innerWidth,line);
            frequencyLabel.SetBounds(16,automatic.Bottom+10,innerWidth,label); frequency.SetBounds(16,frequencyLabel.Bottom+4,innerWidth,line);
            modeLabel.SetBounds(16,frequency.Bottom+10,innerWidth,label); mode.SetBounds(16,modeLabel.Bottom+4,innerWidth,line);
            status.Multiline = true; status.SetBounds(16,mode.Bottom+12,innerWidth,AppTypography.TextHeight(48)); progress.SetBounds(16,status.Bottom+6,innerWidth,5);
            var buttons = new[] { save,check,download,install,notes,later,skip }; var y = progress.Bottom+12;
            for (var i=0;i<buttons.Length;i++) buttons[i].SetBounds(16+(i%2)*(innerWidth/2+4),y+(i/2)*(line+8),(innerWidth-8)/2,line);
            onExit.SetBounds(16,buttons[^1].Bottom+10,innerWidth,AppTypography.TextHeight(44)); Height=onExit.Bottom+16; return;
        }

        var inner = Math.Max(480, width - 48);
        heading.SetBounds(24, 20, inner, 30); versions.SetBounds(24, 56, inner, 28);
        automatic.SetBounds(24, 94, inner, 30);
        frequencyLabel.SetBounds(24, 138, 190, 30); frequency.SetBounds(220, 136, inner - 196, 36);
        modeLabel.SetBounds(24, 182, 190, 30); mode.SetBounds(220, 180, inner - 196, 36);
        save.SetBounds(24, 230, 208, 38); check.SetBounds(244, 230, 208, 38);
        status.SetBounds(24, 282, inner, 30); progress.SetBounds(24, 316, inner, 8);
        download.SetBounds(24, 340, 136, 38); install.SetBounds(170, 340, 148, 38); notes.SetBounds(328, 340, 160, 38);
        later.SetBounds(24, 390, 136, 38); skip.SetBounds(170, 390, 148, 38); onExit.SetBounds(328, 390, Math.Max(150, inner - 304), 38);
    }
    private void UpdateChanged(object? sender, EventArgs e) => Application.RunOnUIThread(Refresh);
    private void LanguageChanged(object? sender, EventArgs e) => Application.RunOnUIThread(() => { if (!disposed) { ApplyLocalization(); Refresh(); } });
    protected override void Dispose(bool disposing)
    {
        if (disposing) { disposed = true; services.Updates.Changed -= UpdateChanged; services.Localization.LanguageChanged -= LanguageChanged; }
        base.Dispose(disposing);
    }
}
