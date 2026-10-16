using ModernFormsNext;
using ModernTubeDownloader.Localization;
using ModernTubeDownloader.Services;
using ModernTubeDownloader.Settings;
using ModernTubeDownloader.Theming;

namespace ModernTubeDownloader.Views;

internal sealed class StartupSettingsPanel : Panel
{
    private readonly AppServices services;
    private readonly WindowsStartupService startup;
    private readonly DashboardToggle showTray = new(), notice = new(), startWithWindows = new(), startHidden = new();
    private readonly ComboBox minimize = new(), close = new();
    private readonly Label title = AppUi.Heading("", 15), minimizeLabel = AppUi.Muted(), closeLabel = AppUi.Muted(), status = AppUi.Muted();
    private readonly Button apply = new();
    private bool disposed;

    public StartupSettingsPanel(AppServices services)
    {
        this.services = services;
        startup = new(new CurrentUserStartupRegistration(), System.IO.Path.Combine(AppContext.BaseDirectory, "ModernTubeDownloader.exe"));
        Height = 420;
        AppUi.Card(this);
        minimize.AccessibleAutomationId = "TrayMinimizeBehavior";
        close.AccessibleAutomationId = "TrayCloseBehavior";
        showTray.AccessibleAutomationId = "TrayEnabled";
        startWithWindows.AccessibleAutomationId = "WindowsAutostartEnabled";
        startHidden.AccessibleAutomationId = "WindowsAutostartHidden";
        AppUi.Input(minimize); AppUi.Input(close); AppUi.Secondary(apply);
        foreach (var check in new[] { showTray, notice, startWithWindows, startHidden }) AppUi.BindForeground(check);
        apply.AccessibleAutomationId = "StartupApply";
        status.Multiline = true;
        Controls.AddRange([title, showTray, minimizeLabel, minimize, closeLabel, close, notice, startWithWindows, startHidden, apply, status]);
        var settings = services.Settings.Current.Startup;
        showTray.Checked = settings.ShowTrayIcon; notice.Checked = settings.FirstHideNotice;
        startWithWindows.Checked = settings.StartWithWindows; startHidden.Checked = settings.AutostartInTray;
        ApplyLocalization();
        minimize.SelectedIndex = (int)settings.MinimizeBehavior;
        close.SelectedIndex = (int)settings.CloseBehavior;
        apply.Click += ApplyClicked;
        SizeChanged += LayoutChanged;
        services.Localization.LanguageChanged += LanguageChanged;
        LayoutForWidth(Width);
        RefreshStatus();
    }

    public void LayoutForWidth(int width)
    {
        if (AppTypography.IsDashboard(this) && width < 540)
        {
            var innerWidth = width - 32; var line = AppTypography.TextHeight(36); var label = AppTypography.TextHeight(24);
            title.SetBounds(16,16,innerWidth,label); showTray.SetBounds(16,title.Bottom+14,innerWidth,AppTypography.TextHeight(42));
            minimizeLabel.SetBounds(16,showTray.Bottom+8,innerWidth,label); minimize.SetBounds(16,minimizeLabel.Bottom+4,innerWidth,line);
            closeLabel.SetBounds(16,minimize.Bottom+12,innerWidth,label); close.SetBounds(16,closeLabel.Bottom+4,innerWidth,line);
            notice.SetBounds(16,close.Bottom+12,innerWidth,AppTypography.TextHeight(44)); startWithWindows.SetBounds(16,notice.Bottom+8,innerWidth,notice.Height);
            startHidden.SetBounds(16,startWithWindows.Bottom+8,innerWidth,notice.Height); apply.SetBounds(16,startHidden.Bottom+12,innerWidth,line);
            status.Multiline = true; status.SetBounds(16,apply.Bottom+12,innerWidth,AppTypography.TextHeight(54)); Height = status.Bottom+16; return;
        }

        var inner = Math.Max(320, width - 48);
        title.SetBounds(24, 20, inner, 30);
        showTray.SetBounds(24, 66, inner, 30);
        minimizeLabel.SetBounds(24, 108, 190, 30); minimize.SetBounds(220, 106, Math.Max(280, inner - 196), 36);
        closeLabel.SetBounds(24, 152, 190, 30); close.SetBounds(220, 150, Math.Max(280, inner - 196), 36);
        notice.SetBounds(24, 198, inner, 30);
        startWithWindows.SetBounds(24, 242, inner, 30);
        startHidden.SetBounds(24, 278, inner, 30);
        apply.SetBounds(24, 320, 220, 40);
        status.SetBounds(24, 372, inner, 38);
    }

    private void ApplyLocalization()
    {
        var text = services.Localization;
        title.Text = text["Tray.SettingsTitle"]; showTray.Text = text["Tray.ShowIcon"]; notice.Text = text["Tray.Notice"];
        minimizeLabel.Text = text["Tray.OnMinimize"]; closeLabel.Text = text["Tray.OnClose"];
        startWithWindows.Text = text["Startup.Enable"]; startHidden.Text = text["Startup.Hidden"];
        apply.Text = text["Startup.Apply"];
        var minimizeIndex = minimize.SelectedIndex; var closeIndex = close.SelectedIndex;
        minimize.Items.Clear(); close.Items.Clear();
        minimize.Items.AddRange(new object[] { text["Tray.NormalMinimize"], text["Tray.Hide"] });
        close.Items.AddRange(new object[] { text["Tray.Exit"], text["Tray.Hide"], text["Tray.Ask"] });
        minimize.SelectedIndex = Math.Max(0, minimizeIndex); close.SelectedIndex = Math.Max(0, closeIndex);
    }

    private void RefreshStatus()
    {
        try { status.Text = services.Localization["Startup.Status." + startup.Inspect(startHidden.Checked)]; }
        catch (Exception error) { status.Text = services.Localization["Startup.Failed"]; services.Logger.Error("Inspecting the current-user startup entry failed.", error); }
    }

    private async void ApplyClicked(object? sender, EventArgs e)
    {
        apply.Enabled = false;
        try
        {
            startup.Apply(startWithWindows.Checked, startHidden.Checked); // Explicit user action only; never performed at startup.
            var settings = services.Settings.Current.Startup;
            settings.ShowTrayIcon = showTray.Checked;
            settings.MinimizeBehavior = (TrayMinimizeBehavior)Math.Max(0, minimize.SelectedIndex);
            settings.CloseBehavior = (TrayCloseBehavior)Math.Max(0, close.SelectedIndex);
            settings.FirstHideNotice = notice.Checked;
            settings.StartWithWindows = startWithWindows.Checked; settings.AutostartInTray = startHidden.Checked;
            await services.Settings.SaveAsync();
            RefreshStatus();
        }
        catch (Exception error) { services.Logger.Error("Applying startup/tray settings failed.", error); status.Text = services.Localization["Startup.Failed"]; }
        finally { if (!disposed) apply.Enabled = true; }
    }
    private void LayoutChanged(object? sender, EventArgs e) => LayoutForWidth(Width);
    private void LanguageChanged(object? sender, EventArgs e) => Application.RunOnUIThread(() => { if (!disposed) { ApplyLocalization(); RefreshStatus(); } });
    protected override void Dispose(bool disposing)
    {
        if (disposing) { disposed = true; services.Localization.LanguageChanged -= LanguageChanged; }
        base.Dispose(disposing);
    }
}
