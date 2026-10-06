using System.ComponentModel;
using System.Runtime.InteropServices;
using ModernFormsNext;
using ModernFormsNext.Animations;
using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Localization;
using ModernTubeDownloader.Models;
using ModernTubeDownloader.Services;
using ModernTubeDownloader.Theming;
using ModernTubeDownloader.Views;

namespace ModernTubeDownloader;

public sealed class MainForm : Form
{
    private readonly AppServices services;
    private readonly LocalizationService text;
    private readonly DownloadsView downloadsView;
    private readonly LiveView liveView;
    private readonly HistoryView historyView;
    private readonly SettingsView settingsView;
    private readonly Panel sidebar;
    private readonly Panel header;
    private readonly Panel content;
    private readonly Label brandLabel;
    private readonly Label queueStatus;
    private readonly Panel preparationOverlay;
    private readonly Panel preparationCard;
    private readonly Label preparationTitle;
    private readonly Label preparationDetails;
    private readonly Label ytDlpPreparation;
    private readonly Label ffmpegPreparation;
    private readonly Label denoPreparation;
    private readonly Button preparationRetry;
    private readonly ProgressBar preparationProgress;
    private readonly Dictionary<Control, Button> navigation = [];
    private readonly Dictionary<Button, AppVectorIcon> navigationIcons = [];
    private Control? currentView;
    private bool readyPresentationShown;
    private bool uiLoopReady;
    private bool shutdownStarted;
    private bool shutdownCompleted;
    private readonly ExternalMediaRequest? initialExternalRequest;
    private readonly HashSet<Guid> externalRequestIds = [];
    private readonly Queue<Guid> externalRequestOrder = [];
#if DEBUG
    private readonly DevelopmentAutomationHost? automation;
#endif

    public MainForm(AppServices services, bool enableAutomation = false, ExternalMediaRequest? initialExternalRequest = null)
    {
        this.services = services;
        this.initialExternalRequest = initialExternalRequest;
        text = services.Localization;
        services.Appearance.Initialize();

        Text = text["App.Title"];
        Name = "MainForm";
        AccessibilityObject.AutomationId = "MainWindow";
#if DEBUG
        if (enableAutomation)
            automation = new DevelopmentAutomationHost();
#endif
        AppBranding.Apply(this);
        Size = new System.Drawing.Size(1440, 900);
        MinimumSize = new System.Drawing.Size(1000, 700);

        sidebar = new Panel { Dock = DockStyle.Left, Width = 236, Padding = new Padding(18, 20, 18, 18) };
        AppUi.BindBackground(sidebar, AppThemeTokens.Navigation);

        var logo = new Panel { Left = 18, Top = 18, Width = 56, Height = 56, Padding = new Padding(6) };
        AppUi.Card(logo, secondary: true);
        logo.Controls.Add(new PictureBox
        {
            Dock = DockStyle.Fill,
            Image = AppBranding.Icon,
            SizeMode = PictureBoxSizeMode.Zoom
        });
        sidebar.Controls.Add(logo);

        brandLabel = AppUi.Heading("ModernTube\nDownloader", 12.5f);
        brandLabel.Left = 86;
        brandLabel.Top = 22;
        brandLabel.Width = 142;
        brandLabel.Height = 50;
        brandLabel.Multiline = true;
        sidebar.Controls.Add(brandLabel);

        var downloadsButton = AddNavigationButton(text["Nav.Downloads"], "NavDownloads", 120, AppIconKind.Download);
        var liveButton = AddNavigationButton(text["Nav.Live"], "NavLive", 174, AppIconKind.Live);
        var historyButton = AddNavigationButton(text["Nav.History"], "NavHistory", 228, AppIconKind.History);
        var settingsButton = AddNavigationButton(text["Nav.Settings"], "NavSettings", 282, AppIconKind.Settings);

        header = new Panel { Dock = DockStyle.Top, Height = 12 };
        AppUi.BindBackground(header, AppThemeTokens.Surface);

        var footer = new Panel { Dock = DockStyle.Bottom, Height = 44, Padding = new Padding(26, 10, 26, 8) };
        AppUi.BindBackground(footer, AppThemeTokens.SurfaceSecondary);
        queueStatus = AppUi.Muted();
        queueStatus.Dock = DockStyle.Fill;
        footer.Controls.Add(queueStatus);

        content = new Panel { Dock = DockStyle.Fill };
        AppUi.BindBackground(content);
        downloadsView = new DownloadsView(services) { Visible = true };
#if DEBUG
        if (automation is not null)
        {
            downloadsView.RegisterDetailsForAutomation = automation.RegisterWindow;
            downloadsView.RegisterAdvancedForAutomation = automation.RegisterWindow;
        }
#endif
        historyView = new HistoryView(services) { Visible = false };
        liveView = new LiveView(services) { Visible = false };
#if DEBUG
        if (automation is not null) liveView.RegisterDetailsForAutomation = automation.RegisterWindow;
#endif
        downloadsView.LiveRequested += (_, request) => { ShowView(liveView); liveView.Present(request); };
        liveView.OpenDownloadsRequested += async (_, url) =>
        { ShowView(downloadsView); await downloadsView.AcceptDesktopUrlAsync(url); };
        settingsView = new SettingsView(services) { Visible = false };
        downloadsView.SupportedSourcesRequested += OpenSupportedSources;
        settingsView.SupportedSourcesRequested += OpenSupportedSources;
        content.Controls.AddRange([downloadsView, liveView, historyView, settingsView]);

        preparationOverlay = new Panel { Dock = DockStyle.Fill, Visible = true };
        AppUi.BindBackground(preparationOverlay, AppThemeTokens.Background);
        preparationCard = new Panel { Width = 660, Height = 358, Padding = new Padding(32) };
        AppUi.Card(preparationCard);
        preparationTitle = AppUi.Heading(text["Provisioning.Title"], 23f);
        preparationTitle.Left = 32;
        preparationTitle.Top = 30;
        preparationTitle.Width = 596;
        preparationTitle.Height = 44;
        preparationTitle.TextAlign = ContentAlignment.MiddleCenter;
        preparationDetails = AppUi.Muted(text["Provisioning.Body"]);
        preparationDetails.Left = 54;
        preparationDetails.Top = 82;
        preparationDetails.Width = 552;
        preparationDetails.Height = 50;
        preparationDetails.Multiline = true;
        preparationDetails.TextAlign = ContentAlignment.TopCenter;
        ytDlpPreparation = CreatePreparationRow(148);
        ffmpegPreparation = CreatePreparationRow(190);
        denoPreparation = CreatePreparationRow(232);
        preparationProgress = new ProgressBar { Left = 54, Top = 286, Width = 552, Height = 8, Minimum = 0, Maximum = 100 };
        preparationRetry = new Button { Left = 244, Top = 308, Width = 172, Height = 42, Visible = false };
        AppUi.Primary(preparationRetry);
        preparationRetry.Click += RetryPreparation;
        preparationCard.Controls.AddRange([
            preparationTitle,
            preparationDetails,
            ytDlpPreparation,
            ffmpegPreparation,
            denoPreparation,
            preparationProgress,
            preparationRetry]);
        preparationOverlay.Controls.Add(preparationCard);
        content.Controls.Add(preparationOverlay);
        preparationOverlay.BringToFront();

        var mainHost = new Panel { Dock = DockStyle.Fill };
        AppUi.BindBackground(mainHost);
        mainHost.Controls.Add(content);
        mainHost.Controls.Add(footer);
        mainHost.Controls.Add(header);
        Controls.Add(mainHost);
        Controls.Add(sidebar);

        navigation[downloadsView] = downloadsButton;
        navigation[liveView] = liveButton;
        navigation[historyView] = historyButton;
        navigation[settingsView] = settingsButton;
        downloadsButton.Click += (_, _) => ShowView(downloadsView);
        liveButton.Click += (_, _) => ShowView(liveView);
        historyButton.Click += (_, _) => ShowView(historyView);
        settingsButton.Click += (_, _) => ShowView(settingsView);
        currentView = downloadsView;
        UiCrashDiagnostics.SetActiveView("Downloads");
        UpdateNavigationState();

        services.Queue.Changed += StateChanged;
        services.Tools.Changed += StateChanged;
        text.LanguageChanged += LanguageChanged;
        ThemeManager.Current.ThemeChanged += ThemeChanged;
        Closing += FormClosing;
        Shown += FormShown;
        header.SizeChanged += LayoutChanged;
        UpdateLayout();
        ApplyLocalization();
        UpdateStatus();
        _ = InitializeAsync();
    }

    private async void OpenSupportedSources(object? sender, EventArgs e)
    {
        try
        {
            using var window = new SupportedSourcesForm(services.Sources, services.SourceCheck, text);
#if DEBUG
            using var registration = automation?.RegisterWindow(window);
#endif
            if (await window.ShowDialog(this) != DialogResult.OK || window.NavigationRequest is not { } request) return;
            if (request.OpenLive)
            {
                ShowView(liveView);
                if (request.Metadata is { } metadata)
                    liveView.Present(new LiveNavigationRequest(request.SourceUrl, metadata, MediaAvailabilityPolicy.Classify(metadata)));
                else await liveView.AcceptUrlAsync(request.SourceUrl);
            }
            else { ShowView(downloadsView); await downloadsView.AcceptCheckedSourceAsync(request); }
        }
        catch (Exception ex) { services.Logger.Error("Supported sources window failed.", ex); }
    }

    private Button AddNavigationButton(string label, string automationId, int top, AppIconKind iconKind)
    {
        var button = new Button
        {
            Text = label,
            Left = 18,
            Top = top,
            Width = 192,
            Height = 46,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(48, 0, 10, 0)
        };
        AppUi.NavigationButton(button, selected: false);
        button.Font = new Font("Segoe UI", 11f);
        button.Name = $"Navigation{label}";
        button.AccessibleAutomationId = automationId;
        var icon = new AppVectorIcon(iconKind, 24, AppThemeTokens.TextPrimary)
        {
            Left = 15,
            Top = 11
        };
        icon.Click += (_, _) => button.PerformClick();
        button.Controls.Add(icon);
        navigationIcons[button] = icon;
        sidebar.Controls.Add(button);
        return button;
    }

    private Label CreatePreparationRow(int top)
    {
        var label = AppUi.Muted();
        label.Left = 54;
        label.Top = top;
        label.Width = 552;
        label.Height = 28;
        return label;
    }

    private async void ShowView(Control view)
    {
        UiCrashDiagnostics.VerifyUiThread("main.view-switch");
        if (ReferenceEquals(currentView, view))
            return;

        var previous = currentView;
        currentView = view;
        UiCrashDiagnostics.SetActiveView(ReferenceEquals(view, downloadsView) ? "Downloads" :
            ReferenceEquals(view, liveView) ? "Live" : ReferenceEquals(view, historyView) ? "History" : "Settings");
        view.Visible = true;
        view.Opacity = 0f;
        view.TranslationY = 8f;
        view.BringToFront();
        UiCrashDiagnostics.Record("view.show", "content", content.Controls.Count);
        preparationOverlay.BringToFront();
        UpdateNavigationState();
        var selectedButton = navigation[view];
        selectedButton.ScaleX = 0.98f;
        selectedButton.ScaleY = 0.98f;
        _ = selectedButton.ScaleToAsync(1f, 170, Easings.CubicOut);

        try
        {
            await Task.WhenAll(
                view.FadeToAsync(1f, 180, Easings.CubicOut),
                view.TranslateToAsync(0f, 0f, 180, Easings.CubicOut));
        }
        finally
        {
            if (previous is not null && !ReferenceEquals(previous, currentView))
            {
                previous.Visible = false;
                UiCrashDiagnostics.Record("view.hide", "content", content.Controls.Count);
            }
        }
    }

    private void UpdateNavigationState()
    {
        foreach (var (view, button) in navigation)
        {
            var selected = ReferenceEquals(view, currentView);
            AppUi.NavigationButton(button, selected);
            button.Font = new Font("Segoe UI", 11f, selected ? FontStyle.Bold : FontStyle.Regular);
            navigationIcons[button].SetColorToken(selected ? AppThemeTokens.NavigationSelectedText : AppThemeTokens.TextPrimary);
        }
    }

    private async Task InitializeAsync()
    {
        try
        {
            await services.Tools.Initialization;
        }
        catch (Exception ex)
        {
            services.Logger.Error("Initial tool preparation failed.", ex);
        }
        Application.RunOnUIThread(UpdateStatus);
    }

    private async void RetryPreparation(object? sender, EventArgs e)
    {
        preparationRetry.Enabled = false;
        try
        {
            await services.Tools.RetryAsync();
        }
        catch (Exception ex)
        {
            services.Logger.Error("Retrying tool preparation failed.", ex);
        }
        finally
        {
            Application.RunOnUIThread(() =>
            {
                preparationRetry.Enabled = true;
                UpdateStatus();
            });
        }
    }

    private void StateChanged(object? sender, EventArgs e) => Application.RunOnUIThread(UpdateStatus);

    private void UpdateStatus()
    {
        var items = services.Queue.Snapshot();
        var queued = items.Count(item => item.Status is DownloadStatus.Queued or DownloadStatus.Waiting);
        var active = items.Count(item =>
            item.Status is (DownloadStatus.DownloadingVideo or DownloadStatus.DownloadingAudio or DownloadStatus.Merging or DownloadStatus.Finalizing));
        var completed = items.Count(item => item.Status == DownloadStatus.Completed);
        var segments = new List<string>
        {
            text.Get("Footer.Queue", queued),
            text.Get("Footer.Downloading", active),
            text.Get("Footer.Completed", completed)
        };
        queueStatus.Text = string.Join("  •  ", segments) +
            (services.Queue.IsPaused ? $"  •  {text["Footer.Paused"]}" : string.Empty);

        var engine = services.Tools.EngineStatus;

        if (services.Tools.CanAnalyze)
        {
            if (engine == DownloadEngineStatus.Ready && !readyPresentationShown && uiLoopReady)
                _ = ShowReadyThenDismissAsync();
            else if (engine == DownloadEngineStatus.Degraded)
            {
                preparationOverlay.Visible = false;
                currentView?.BringToFront();
            }
            return;
        }

        readyPresentationShown = false;
        preparationOverlay.Visible = true;
        preparationOverlay.Opacity = 1f;
        preparationCard.Opacity = 1f;
        preparationRetry.Visible = engine == DownloadEngineStatus.Failed;
        preparationTitle.Text = engine == DownloadEngineStatus.Failed
            ? text["Provisioning.AttentionTitle"]
            : text["Provisioning.Title"];
        preparationDetails.Text = engine == DownloadEngineStatus.Failed
            ? text["Provisioning.AttentionBody"]
            : text["Provisioning.Body"];
        ytDlpPreparation.Text = $"yt-dlp     {FormatPreparationStatus(services.Tools.YtDlp)}";
        ffmpegPreparation.Text = $"FFmpeg     {FormatPreparationStatus(services.Tools.Ffmpeg)}";
        denoPreparation.Text = $"Deno     {FormatPreparationStatus(services.Tools.Deno)}";
        var percentages = new[] { services.Tools.YtDlp.ProgressPercent, services.Tools.Ffmpeg.ProgressPercent, services.Tools.Deno.ProgressPercent }
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .ToArray();
        preparationProgress.Value = percentages.Length == 0 ? 0 : (int)Math.Clamp(percentages.Average(), 0, 100);
        preparationProgress.Visible = engine == DownloadEngineStatus.Preparing;
        preparationOverlay.BringToFront();
    }

    private async Task ShowReadyThenDismissAsync()
    {
        readyPresentationShown = true;
        preparationOverlay.Visible = true;
        preparationOverlay.Opacity = 1f;
        preparationCard.Opacity = 1f;
        preparationRetry.Visible = false;
        preparationProgress.Visible = false;
        preparationTitle.Text = text["Provisioning.ReadyTitle"];
        preparationDetails.Text = text["Provisioning.ReadyBody"];
        ytDlpPreparation.Text = $"yt-dlp     {FormatPreparationStatus(services.Tools.YtDlp)}";
        ffmpegPreparation.Text = $"FFmpeg     {FormatPreparationStatus(services.Tools.Ffmpeg)}";
        denoPreparation.Text = $"Deno     {FormatPreparationStatus(services.Tools.Deno)}";
        preparationOverlay.BringToFront();
        await Task.Delay(650);
        await preparationCard.FadeToAsync(0f, 200, Easings.CubicOut);
        if (services.Tools.CanAnalyze)
        {
            preparationOverlay.Visible = false;
            currentView?.BringToFront();
        }
    }

    private string FormatPreparationStatus(ToolInfo tool)
    {
        var status = text[$"ToolStatus.{tool.Status}"];
        return tool.ProgressPercent is { } percent
            ? $"{status}  {text.Get("Tools.Progress", percent.ToString("F0", text.Culture))}"
            : status;
    }

    private void ApplyLocalization()
    {
        Text = text["App.Title"];
        brandLabel.Text = "ModernTube\nDownloader";
        navigation[downloadsView].Text = text["Nav.Downloads"];
        navigation[liveView].Text = text["Nav.Live"];
        navigation[historyView].Text = text["Nav.History"];
        navigation[settingsView].Text = text["Nav.Settings"];
        preparationRetry.Text = text["Provisioning.Retry"];
        UpdateStatus();
    }

    private void LanguageChanged(object? sender, EventArgs e) => Application.RunOnUIThread(ApplyLocalization);

    private void ThemeChanged(object? sender, ThemeChangedEventArgs e) => Application.RunOnUIThread(UpdateNavigationState);

    private void FormShown(object? sender, EventArgs e)
    {
        // The constructor runs before Application.Run installs the UI synchronization context.
        // Do not start the asynchronous ready transition until its continuations can return to it.
        uiLoopReady = true;
        UiCrashDiagnostics.RegisterStateProvider(onUiThread =>
        {
            var items = services.Queue.Snapshot();
            var state = $"Queue: total={items.Count}, queued={items.Count(item => item.Status == DownloadStatus.Queued)}, " +
                $"active={items.Count(item => item.Status is DownloadStatus.Waiting or DownloadStatus.DownloadingVideo or DownloadStatus.DownloadingAudio or DownloadStatus.Merging or DownloadStatus.Finalizing)}, " +
                $"paused={services.Queue.IsPaused}; Web Remote: enabled={services.Settings.Current.EnableWebRemote}, running={services.WebRemote.IsRunning}";
            return onUiThread ? state + $"; focused={FindDiagnosticControl(Controls, control => control.Focused)}; " +
                $"captured={FindDiagnosticControl(Controls, control => control.Capture)}; content-children={content.Controls.Count}" : state;
        });
        services.Appearance.RefreshSystemTheme();
        UpdateStatus();
        if (initialExternalRequest is not null)
            _ = HandleExternalUrlAsync(initialExternalRequest);
#if DEBUG
        if (automation is not null)
        {
            try { automation.Start(this); }
            catch (Exception ex)
            {
                services.Logger.Error("Starting the development automation bridge failed.", ex);
                FatalErrorReporter.ShowMessage("ModernTubeDownloader automation", ex.Message);
                Close();
            }
        }
#endif
    }

    private static string FindDiagnosticControl(IEnumerable<Control> roots, Func<Control, bool> predicate)
    {
        try
        {
            foreach (var child in roots.ToArray())
            {
                var nested = FindDiagnosticControl(child.Controls, predicate);
                if (nested != "none") return nested;
                if (predicate(child)) return child.AccessibleAutomationId ?? child.GetType().Name;
            }
            return "none";
        }
        catch (Exception error) { return "unavailable:" + error.GetType().Name; }
    }

    public async Task HandleExternalUrlAsync(ExternalMediaRequest request)
    {
        if (shutdownStarted || !services.Settings.Current.BrowserIntegrationEnabled ||
            !ExternalMediaRequest.TryValidateSource(request.SourceUrl, out var sourceUrl))
            return;
        if (request.RequestId is { } id)
        {
            if (!externalRequestIds.Add(id)) return;
            externalRequestOrder.Enqueue(id);
            while (externalRequestOrder.Count > 256)
                externalRequestIds.Remove(externalRequestOrder.Dequeue());
        }

        try
        {
            services.Logger.Info($"Opening external request id={request.RequestId?.ToString("D") ?? "legacy"}, source={request.Source}, queue={request.AutoQueue}, open={request.OpenInApp}, quality={request.RequestedQualityPresetId ?? "default"}, container={request.RequestedContainer}.");
            if (WindowState == FormWindowState.Minimized)
                WindowState = FormWindowState.Normal;
            if (OperatingSystem.IsWindows() && PlatformHandle.HandleDescriptor == "HWND")
                SetForegroundWindow(PlatformHandle.Handle);
            if (new Uri(sourceUrl).AbsolutePath.StartsWith("/live/", StringComparison.OrdinalIgnoreCase))
            { ShowView(liveView); await liveView.AcceptUrlAsync(sourceUrl); }
            else
            { ShowView(downloadsView); await downloadsView.AcceptExternalRequestAsync(request with { SourceUrl = sourceUrl }); }
        }
        catch (Exception exception)
        {
            if (request.RequestId is { } failedId) externalRequestIds.Remove(failedId);
            services.Logger.Error("Handling an external YouTube link failed.", exception);
            if (DownloadFailureClassifier.Classify(exception).Category == DownloadFailureCategory.Upcoming)
            { ShowView(liveView); liveView.Present(new LiveNavigationRequest(sourceUrl, null, MediaAvailabilityKind.Upcoming)); }
            else downloadsView.ShowExternalRequestError(exception);
        }
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr window);

    private void LayoutChanged(object? sender, EventArgs e) => UpdateLayout();

    private void UpdateLayout()
    {
        sidebar.Width = 236;
        brandLabel.Visible = true;
        foreach (var button in navigation.Values)
            button.Width = sidebar.Width - 36;
        preparationCard.Width = Math.Min(660, Math.Max(520, content.ClientSize.Width - 96));
        preparationCard.Left = Math.Max(24, (preparationOverlay.ClientSize.Width - preparationCard.Width) / 2);
        preparationCard.Top = Math.Max(24, (preparationOverlay.ClientSize.Height - preparationCard.Height) / 2);
        var innerWidth = preparationCard.Width - 108;
        preparationTitle.Width = preparationCard.Width - 64;
        preparationDetails.Width = innerWidth;
        ytDlpPreparation.Width = innerWidth;
        ffmpegPreparation.Width = innerWidth;
        denoPreparation.Width = innerWidth;
        preparationProgress.Width = innerWidth;
        preparationRetry.Left = (preparationCard.Width - preparationRetry.Width) / 2;
    }

    private void FormClosing(object? sender, CancelEventArgs e)
    {
        if (shutdownCompleted)
            return;
        e.Cancel = true;
        if (shutdownStarted)
            return;
        shutdownStarted = true;
        queueStatus.Text = text["Footer.Stopping"];
        _ = ShutdownAndCloseAsync();
    }

    private async Task ShutdownAndCloseAsync()
    {
#if DEBUG
        if (automation is not null)
        {
            try { await automation.StopAsync(); }
            catch (Exception ex) { services.Logger.Error("Stopping the development automation bridge failed.", ex); }
        }
#endif
        try
        {
            await services.ShutdownAsync();
        }
        catch (Exception ex)
        {
            services.Logger.Error("Application shutdown encountered an error.", ex);
        }
        finally
        {
            shutdownCompleted = true;
            Application.RunOnUIThread(Close);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            services.Queue.Changed -= StateChanged;
            services.Tools.Changed -= StateChanged;
            text.LanguageChanged -= LanguageChanged;
            ThemeManager.Current.ThemeChanged -= ThemeChanged;
            Closing -= FormClosing;
            Shown -= FormShown;
            header.SizeChanged -= LayoutChanged;
        }
        base.Dispose(disposing);
    }
}
