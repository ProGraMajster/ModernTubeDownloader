using ModernFormsNext;
using ModernTubeDownloader.Models;
using ModernTubeDownloader.Settings;
using ModernTubeDownloader.Theming;
using ModernTubeDownloader.Views;

namespace ModernTubeDownloader.Services;

/// <summary>UI-thread owner of native tray resources and window visibility. Workers only mark the menu dirty.</summary>
internal sealed class TrayApplicationController : IDisposable
{
    private readonly Form form;
    private readonly AppServices services;
    private readonly Action shutdown;
    private readonly Action openLive;
    private readonly ModernFormsNext.Timer timer = new() { Interval = 1000 };
    private NotifyIcon? icon;
    private NotifyIconContextMenu? menu;
    private NotifyIconMenuItem? openItem, queuedItem, downloadingItem, liveItem, pauseItem, openLiveItem, exitItem, updateItem;
    private int dirty = 1;
    private bool disposed, deciding;

    public TrayApplicationController(Form form, AppServices services, Action shutdown, Action openLive)
    {
        this.form = form;
        this.services = services;
        this.shutdown = shutdown;
        this.openLive = openLive;
        timer.Tick += TimerTick;
        form.WindowStateChanged += WindowStateChanged;
        services.Settings.Changed += SettingsChanged;
        services.Localization.LanguageChanged += SettingsChanged;
        services.Queue.Changed += QueueChanged;
        services.Live.Changed += WorkChanged;
        services.Updates.Changed += WorkChanged;
    }

    public bool Available => icon?.Visible == true;
    public Func<Form, IDisposable?>? RegisterDialog { get; set; }
    public bool HasActiveWork => services.WebRemote.IsRunning || services.Queue.NotificationSnapshot().Any(item =>
        item.Status is DownloadStatus.Queued or DownloadStatus.Waiting or DownloadStatus.DownloadingVideo or
            DownloadStatus.DownloadingAudio or DownloadStatus.Merging or DownloadStatus.Finalizing) ||
        services.Live.Snapshot().Any(item => item.IsActive || item.State is LiveSessionState.Pending or LiveSessionState.WaitingForLive);

    public void Start(bool autostart, bool hiddenArgument)
    {
        ApplySettings();
        timer.Start();
        if (!TrayPolicy.StartHidden(services.Settings.Current.Startup, Available, autostart, hiddenArgument)) Restore();
        UpdateMenu();
    }

    public void Restore()
    {
        if (disposed) return;
        if (form.WindowState == FormWindowState.Minimized) form.WindowState = FormWindowState.Normal;
        form.Show();
        form.Activate();
    }

    public void RequestClose(bool explicitExit = false)
    {
        if (disposed || deciding) return;
        switch (TrayPolicy.Close(services.Settings.Current.Startup, Available, HasActiveWork, explicitExit))
        {
            case TrayCloseDecision.Hide: Hide(); break;
            case TrayCloseDecision.Exit: shutdown(); break;
            case TrayCloseDecision.Confirm: _ = ConfirmAsync(); break;
        }
    }

    public async Task<bool> ConfirmExitAsync()
    {
        if (deciding || disposed) return false;
        deciding = true;
        try
        {
            Restore();
            using var dialog = new ShutdownChoiceForm(services.Localization, HasActiveWork, Available);
            using var registration = RegisterDialog?.Invoke(dialog);
            var result = await dialog.ShowDialog(form);
            if (result == DialogResult.OK) return true;
            if (result == DialogResult.Ignore) Hide();
        }
        catch (Exception error) { services.Logger.Error("The shutdown confirmation could not be opened; shutdown was cancelled.", error); }
        finally { deciding = false; }
        return false;
    }

    private async Task ConfirmAsync() { if (await ConfirmExitAsync()) shutdown(); }

    private void Hide()
    {
        if (!Available) return; // Never leave an inaccessible background process.
        form.Hide();
        var settings = services.Settings.Current.Startup;
        if (!settings.FirstHideNotice || settings.FirstHideNoticeShown) return;
        try
        {
            icon!.ShowBalloonTip(5000, "ModernTubeDownloader", services.Localization["Tray.FirstHide"], NotifyIconBalloonIcon.Info);
            settings.FirstHideNoticeShown = true;
            _ = SaveNoticeAsync();
        }
        catch (Exception error) { services.Logger.Error("The optional first-hide tray notice failed.", error); }
    }

    private async Task SaveNoticeAsync()
    {
        try { await services.Settings.SaveAsync(); }
        catch (Exception error) { services.Logger.Error("Saving the tray notice preference failed.", error); }
    }

    private void ApplySettings()
    {
        if (disposed) return;
        if (!services.Settings.Current.Startup.ShowTrayIcon)
        {
            if (!form.Visible) Restore();
            ReleaseIcon();
            return;
        }
        try
        {
            if (icon is null)
            {
                menu = new NotifyIconContextMenu();
                menu.Items.Add("ModernTubeDownloader", (_, _) => { }).Enabled = false;
                openItem = menu.Items.Add("", (_, _) => Restore());
                menu.Items.AddSeparator();
                queuedItem = menu.Items.Add("", (_, _) => { }); queuedItem.Enabled = false;
                downloadingItem = menu.Items.Add("", (_, _) => { }); downloadingItem.Enabled = false;
                liveItem = menu.Items.Add("", (_, _) => { }); liveItem.Enabled = false;
                menu.Items.AddSeparator();
                pauseItem = menu.Items.Add("", (_, _) => services.Queue.SetPaused(!services.Queue.IsPaused));
                openLiveItem = menu.Items.Add("", (_, _) => { Restore(); openLive(); });
                menu.Items.AddSeparator();
                updateItem = menu.Items.Add("", (_, _) => { }); updateItem.Enabled = false;
                exitItem = menu.Items.Add("", (_, _) => RequestClose(true));
                icon = new NotifyIcon { Icon = AppBranding.Icon, Text = "ModernTubeDownloader", ContextMenu = menu, Visible = true };
                icon.Click += IconClick;
                icon.DoubleClick += IconClick;
                services.Logger.Info("Native tray icon registered.");
            }
            Interlocked.Exchange(ref dirty, 1);
        }
        catch (Exception error)
        {
            ReleaseIcon();
            services.Logger.Error("The native tray is unavailable; the main window remains accessible.", error);
            Restore();
        }
    }

    private void IconClick(object? sender, MouseEventArgs e) { if (e.Button == MouseButtons.Left) Restore(); }
    private void WindowStateChanged(object? sender, WindowStateChangedEventArgs e)
    {
        if (form.WindowState == FormWindowState.Minimized && services.Settings.Current.Startup.MinimizeBehavior == TrayMinimizeBehavior.HideToTray)
            Hide();
    }
    private void TimerTick(object? sender, EventArgs e) { if (Interlocked.Exchange(ref dirty, 0) != 0) UpdateMenu(); }
    private void SettingsChanged(object? sender, EventArgs e) => Application.RunOnUIThread(ApplySettings);
    private void QueueChanged(object? sender, QueueChangedEventArgs e) => Interlocked.Exchange(ref dirty, 1);
    private void WorkChanged(object? sender, EventArgs e) => Interlocked.Exchange(ref dirty, 1);
    private void UpdateMenu()
    {
        if (!Available) return;
        var text = services.Localization;
        var items = services.Queue.NotificationSnapshot();
        openItem!.Text = text["Tray.Open"];
        queuedItem!.Text = text.Get("Tray.Queued", items.Count(item => item.Status == DownloadStatus.Queued));
        downloadingItem!.Text = text.Get("Tray.Downloading", items.Count(item => item.Status is DownloadStatus.Waiting or
            DownloadStatus.DownloadingVideo or DownloadStatus.DownloadingAudio or DownloadStatus.Merging or DownloadStatus.Finalizing));
        liveItem!.Text = text.Get("Tray.Live", services.Live.Snapshot().Count(item => item.IsActive || item.State is LiveSessionState.Pending or LiveSessionState.WaitingForLive));
        pauseItem!.Text = text[services.Queue.IsPaused ? "Tray.Resume" : "Tray.Pause"];
        openLiveItem!.Text = text["Tray.OpenLive"];
        exitItem!.Text = text["Tray.Exit"];
        updateItem!.Text = services.Updates.Ready ? text.Get("Updates.TrayReady", services.Updates.State.Available?.Version ?? "") : text["Updates.TrayIdle"];
    }

    private void ReleaseIcon()
    {
        icon?.Dispose(); icon = null;
        if (menu is not null)
        {
            foreach (var item in menu.Items.ToArray()) item.Dispose();
            menu.Dispose(); menu = null;
        }
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        timer.Dispose();
        form.WindowStateChanged -= WindowStateChanged;
        services.Settings.Changed -= SettingsChanged;
        services.Localization.LanguageChanged -= SettingsChanged;
        services.Queue.Changed -= QueueChanged;
        services.Live.Changed -= WorkChanged;
        services.Updates.Changed -= WorkChanged;
        ReleaseIcon();
    }
}
