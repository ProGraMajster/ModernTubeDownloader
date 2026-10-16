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
using ModernTubeDownloader.Shell;

namespace ModernTubeDownloader;

public sealed class MainForm : Form
{
    private readonly AppServices services;
    private readonly LocalizationService text;
    private readonly DownloadsView downloadsView;
    private readonly LiveView liveView;
    private readonly HistoryView historyView;
    private readonly SettingsView settingsView;
    private readonly OtherView otherView;
    private readonly AppShellHost shellHost;
    private readonly CaptionLabel caption = new();
    private readonly Button captionTheme = new();
    private bool fallbackNotice;
    private readonly Panel content;
    private readonly BatchImportView batchView;
    private readonly InformationView helpView;
    private readonly InformationView aboutView;
    private readonly Panel preparationOverlay;
    private readonly Panel preparationCard;
    private readonly Label preparationTitle;
    private readonly Label preparationDetails;
    private readonly Label ytDlpPreparation;
    private readonly Label ffmpegPreparation;
    private readonly Label denoPreparation;
    private readonly Button preparationRetry;
    private readonly ProgressBar preparationProgress;
    private readonly Dictionary<Control, AppPageId> pages = [];
    private Control? currentView;
    private bool readyPresentationShown;
    private bool uiLoopReady;
    private bool shutdownStarted;
    private bool shutdownCompleted;
    private bool updateInstallRequested;
    private readonly TrayApplicationController tray;
    private readonly ExternalMediaRequest? initialExternalRequest;
    private readonly HashSet<Guid> externalRequestIds = [];
    private readonly Queue<Guid> externalRequestOrder = [];
#if DEBUG
    private readonly DevelopmentAutomationHost? automation;
#endif

    public MainForm(AppServices services, bool enableAutomation = false, ExternalMediaRequest? initialExternalRequest = null, bool classicUi = false)
    {
        this.services = services;
        if (classicUi) services.Appearance.ForceClassicForSession();
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
        AppUi.BindForeground(caption); AppTypography.Bind(caption, 12, FontStyle.Bold);
        AppUi.Secondary(captionTheme); captionTheme.AccessibleAutomationId = "CaptionTheme";
        captionTheme.Click += async (_, _) => await services.Appearance.SetModeAsync(
            services.Appearance.EffectiveVariant == ThemeVariant.Dark ? Settings.AppThemeMode.Light : Settings.AppThemeMode.Dark);
        TitleBar.Controls.AddRange([caption, captionTheme]); TitleBar.SizeChanged += (_, _) => LayoutCaption();
        Size = new System.Drawing.Size(1440, 900);
        MinimumSize = new System.Drawing.Size(1000, 700);

        content = new Panel { Dock = DockStyle.Fill };
        AppUi.BindBackground(content);
        downloadsView = CreatePage(modern => new DownloadsView(services, modern)); downloadsView.Visible = true;
#if DEBUG
        if (automation is not null)
        {
            downloadsView.RegisterDetailsForAutomation = automation.RegisterWindow;
            downloadsView.RegisterAdvancedForAutomation = automation.RegisterWindow;
        }
#endif
        historyView = CreatePage(modern => new HistoryView(services, modern)); historyView.Visible = false;
        historyView.RetryRequested += async (_, url) =>
        {
            ShowView(downloadsView);
            try { await downloadsView.AcceptExternalRequestAsync(new(url, true, true, Source: "history", Intent: ExternalMediaIntent.QuickQueue)); }
            catch (Exception error) { downloadsView.ShowExternalRequestError(error); }
        };
        liveView = new LiveView(services) { Visible = false };
#if DEBUG
        if (automation is not null) liveView.RegisterDetailsForAutomation = automation.RegisterWindow;
#endif
        downloadsView.LiveRequested += (_, request) => { ShowView(liveView); liveView.Present(request); };
        liveView.OpenDownloadsRequested += async (_, url) =>
        { ShowView(downloadsView); await downloadsView.AcceptDesktopUrlAsync(url); };
        settingsView = new SettingsView(services) { Visible = false };
        otherView = new OtherView(services) { Visible = false };
        otherView.RegisterTool("Batch", () => _ = OpenBatchAsync());
        otherView.ResumeBatch += (_, session) => _ = OpenBatchAsync(session);
        settingsView.InstallUpdateRequested += (_, _) => _ = RequestUpdateInstallationAsync();
        downloadsView.SupportedSourcesRequested += OpenSupportedSources;
        settingsView.SupportedSourcesRequested += OpenSupportedSources;
        batchView = new BatchImportView(services) { Visible = false };
#if DEBUG
        if (automation is not null) batchView.RegisterDialog = automation.RegisterWindow;
#endif
        helpView = new InformationView(services, false) { Visible = false };
        aboutView = new InformationView(services, true) { Visible = false };
        content.Controls.AddRange([downloadsView, liveView, historyView, otherView, settingsView, batchView, helpView, aboutView]);
        pages[downloadsView] = AppPageId.Downloads; pages[liveView] = AppPageId.Live;
        pages[historyView] = AppPageId.History; pages[otherView] = AppPageId.Other;
        pages[settingsView] = AppPageId.Settings; pages[batchView] = AppPageId.Batch;
        pages[helpView] = AppPageId.Help; pages[aboutView] = AppPageId.About;

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

        shellHost = new AppShellHost(content, text, services.Logger);
        shellHost.Navigate += (_, page) => Navigate(page);
        shellHost.ThemeRequested += async (_, _) => await services.Appearance.SetModeAsync(
            services.Appearance.EffectiveVariant == ThemeVariant.Dark ? Settings.AppThemeMode.Light : Settings.AppThemeMode.Dark);
        shellHost.UpdateRequested += (_, _) => { Navigate(AppPageId.Settings); settingsView.ShowCategory("Updates"); };
        shellHost.Fallback += (_, _) =>
        {
            services.Appearance.ForceClassicForSession();
            fallbackNotice = true;
        };
        Controls.Add(shellHost);
        shellHost.Apply(services.Appearance.InterfaceMode);
        ApplyInterface();
        services.Appearance.ModeChanged += AppearanceChanged;
        tray = new TrayApplicationController(this, services, BeginShutdown, () => ShowView(liveView));
#if DEBUG
        if (automation is not null) tray.RegisterDialog = automation.RegisterWindow;
#endif
        currentView = downloadsView;
        UiCrashDiagnostics.SetActiveView("Downloads");
        UpdateNavigationState();

        services.Queue.Changed += StateChanged;
        services.Tools.Changed += StateChanged;
        services.Live.Changed += StateChanged; services.Updates.Changed += StateChanged;
        text.LanguageChanged += LanguageChanged;
        ThemeManager.Current.ThemeChanged += ThemeChanged;
        AppTypography.RegisterLayout(shellHost, () => { UpdateLayout(); shellHost.RefreshChrome(); });
        Closing += FormClosing;
        Shown += FormShown;
        content.SizeChanged += LayoutChanged;
        UpdateLayout();
        ApplyLocalization();
        UpdateStatus();
        _ = InitializeAsync();
    }

    private T CreatePage<T>(Func<bool, T> factory) where T : Control
    {
        try { return factory(services.Appearance.InterfaceMode == AppInterfaceMode.ModernDashboard); }
        catch (Exception error) when (services.Appearance.InterfaceMode == AppInterfaceMode.ModernDashboard)
        {
            services.Logger.Error("Modern page construction failed; recovering Classic presentation.", error);
            services.Appearance.ForceClassicForSession(); fallbackNotice = true; return factory(false);
        }
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

    private async Task OpenBatchAsync(ModernTubeDownloader.Batch.BatchImportSession? session = null)
    {
        try
        {
            if (shellHost.Mode == AppInterfaceMode.ModernDashboard)
            {
                if (session is not null) batchView.Present(session);
                Navigate(AppPageId.Batch); return;
            }
            using var window = new BatchImportForm(services, session);
#if DEBUG
            if (automation is not null) window.RegisterDialog = automation.RegisterWindow;
            using var registration = automation?.RegisterWindow(window);
#endif
            await window.ShowDialog(this);
        }
        catch (Exception ex) { services.Logger.Error("Batch window failed.", ex); }
    }

    public AppPageId CurrentPage => shellHost.CurrentPage;
    public AppInterfaceMode InterfaceMode => shellHost.Mode;
    public void Navigate(AppPageId page)
    {
        if (page == AppPageId.Batch && shellHost.Mode == AppInterfaceMode.Classic) { ShowView(otherView); _ = OpenBatchAsync(); return; }
        if (pages.FirstOrDefault(pair => pair.Value == page).Key is { } view) ShowView(view);
    }

    private void AppearanceChanged(object? sender, EventArgs e) => Application.RunOnUIThread(ApplyInterface);
    private void ApplyInterface()
    {
        try { ApplyInterfaceCore(); if (uiLoopReady) ShowFallbackNotice(); }
        catch (Exception error) when (services.Appearance.InterfaceMode == AppInterfaceMode.ModernDashboard)
        {
            services.Logger.Error("Modern Dashboard presentation failed; recovering Classic without restarting services.", error);
            services.Appearance.ForceClassicForSession(); fallbackNotice = true;
            ApplyInterfaceCore();
            if (uiLoopReady) ShowFallbackNotice();
        }
    }
    private async void ShowFallbackNotice()
    {
        if (!fallbackNotice) return; fallbackNotice = false;
        await SystemMessageBox.ShowAsync(this, text["Dashboard.Fallback"], text["App.Title"]);
    }
    private void ApplyInterfaceCore()
    {
        shellHost.Apply(services.Appearance.InterfaceMode);
        var modern = shellHost.Mode == AppInterfaceMode.ModernDashboard;
        downloadsView.SetDashboard(modern); liveView.SetDashboard(modern);
        historyView.SetDashboard(modern); otherView.SetDashboard(modern); settingsView.SetDashboard(modern); batchView.SetDashboard(modern);
        Text = modern ? "ModernTubeDownloader   v" + AppBranding.Version : text["App.Title"];
        TitleBar.Height = modern ? AppTypography.TextHeight(48) : 34;
        AppUi.BindBackground(TitleBar, modern ? AppThemeTokens.Background : AppThemeTokens.Surface);
        foreach (var view in pages.Keys) AppTypography.SetDashboardPresentation(view, modern);
        if (!modern && currentView == batchView) ShowView(otherView);
        LayoutCaption();
        UpdateLayout(); UpdateStatus();
    }

    private void LayoutCaption()
    {
        if (shellHost is null) return;
        var modern = shellHost.Mode == AppInterfaceMode.ModernDashboard;
        caption.Visible = captionTheme.Visible = modern;
        if (modern)
        {
            TitleBar.Text = ""; caption.Text = "ModernTubeDownloader   v" + AppBranding.Version;
            caption.SetBounds(52, 0, Math.Max(180, TitleBar.Width - 330), TitleBar.Height);
            captionTheme.Text = text["Dashboard.Theme"]; captionTheme.SetBounds(Math.Max(52, TitleBar.Width - 252), 8, 96, Math.Max(28, TitleBar.Height - 16));
        }
        else TitleBar.Text = Text;
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
        if (ReferenceEquals(currentView, view)) { shellHost.Select(pages[view]); return; }

        var previous = currentView;
        currentView = view;
        UiCrashDiagnostics.SetActiveView(pages[view].ToString());
        view.Visible = true;
        view.Opacity = 0f;
        view.TranslationY = 8f;
        view.BringToFront();
        UiCrashDiagnostics.Record("view.show", "content", content.Controls.Count);
        preparationOverlay.BringToFront();
        UpdateNavigationState();
        if (!AppearancePresentation.Current.Global.Animations)
        {
            view.Opacity = 1; view.TranslationY = 0; if (previous is not null) previous.Visible = false; return;
        }


        try
        {
            await Task.WhenAll(
                view.FadeToAsync(1f, AppearancePresentation.AnimationMilliseconds(180), Easings.CubicOut),
                view.TranslateToAsync(0f, 0f, AppearancePresentation.AnimationMilliseconds(180), Easings.CubicOut));
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
        if (currentView is not null) shellHost.Select(pages[currentView]);
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
        shellHost.SetStatus(string.Join("  •  ", segments) +
            (services.Queue.IsPaused ? $"  •  {text["Footer.Paused"]}" : string.Empty));

        var generalStatus = DashboardStatus.Read(services);
        if (shellHost.Mode == AppInterfaceMode.ModernDashboard) shellHost.SetStatus(generalStatus.Text, generalStatus.Token);
        downloadsView.SetDashboardStatus(generalStatus.Text, generalStatus.Token);
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
        Text = shellHost.Mode == AppInterfaceMode.ModernDashboard ? "ModernTubeDownloader   v" + AppBranding.Version : text["App.Title"];
        shellHost.RefreshChrome(); LayoutCaption();
        preparationRetry.Text = text["Provisioning.Retry"];
        UpdateStatus();
    }

    private void LanguageChanged(object? sender, EventArgs e) => Application.RunOnUIThread(ApplyLocalization);

    private void ThemeChanged(object? sender, ThemeChangedEventArgs e) => Application.RunOnUIThread(UpdateNavigationState);

    private void FormShown(object? sender, EventArgs e)
    {
        InitializeRuntime(); ShowFallbackNotice();
    }

    public void StartRuntime(bool autostart, bool hiddenArgument)
    {
        InitializeRuntime();
        tray.Start(autostart, hiddenArgument);
    }

    public void RestoreWindow() { if (!shutdownStarted) tray.Restore(); }

    private void InitializeRuntime()
    {
        if (uiLoopReady) return;
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
        services.Updates.Start();
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

    public void HandleNotification(NotificationNavigation request)
    {
        if (shutdownStarted || !NotificationNavigation.TryParse(request.Encode(), out _)) return;
        try
        {
            RestoreWindow();
            Navigate(AppNavigation.ForNotification(request));
            // Resolve only our own persisted state. Native/IPC payload never supplies a path or URL.
            if (request.Group == "downloads")
            {
                var item = services.Queue.Find(request.ItemId);
                var history = services.History.Snapshot().FirstOrDefault(entry => entry.QueueItemId == request.ItemId);
                var path = history?.FinalPath ?? item?.FinalFile;
                if (request.View == "downloads") downloadsView.RevealQueueItem(request.ItemId);
                var action = NotificationActionPolicy.Resolve(request, item?.Status, path, System.IO.File.Exists, null);
                if (action == "open-file") Utilities.ShellService.OpenFile(path);
                else if (action == "open-folder") Utilities.ShellService.OpenFolderForFile(path);
                else if (action == "retry") services.Queue.Retry(request.ItemId);
            }
            else if (request.Group == "live" && NotificationActionPolicy.Resolve(request, null, null, System.IO.File.Exists, services.Live.Find(request.ItemId)?.State) == "stop-save")
                services.Live.StopAndSave(request.ItemId);
        }
        catch (Exception error) { services.Logger.Error("Notification action could not be completed.", error); }
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
            RestoreWindow();
            if (OperatingSystem.IsWindows() && PlatformHandle.HandleDescriptor == "HWND")
                SetForegroundWindow(PlatformHandle.Handle);
            if (AppNavigation.ForExternalRequest(request with { SourceUrl = sourceUrl }) == AppPageId.Live)
            { ShowView(liveView); await liveView.AcceptUrlAsync(sourceUrl); }
            else
            { ShowView(downloadsView); await downloadsView.AcceptExternalRequestAsync(request with { SourceUrl = sourceUrl }); }
        }
        catch (Exception exception)
        {
            if (request.RequestId is { } failedId) externalRequestIds.Remove(failedId);
            services.Logger.Error("Handling an external media link failed.", exception);
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
        tray.RequestClose();
    }

    private void BeginShutdown()
    {
        if (shutdownStarted) return;
        shutdownStarted = true;
        shellHost.SetStatus(text["Footer.Stopping"]);
        _ = ShutdownAndCloseAsync();
    }

    private async Task RequestUpdateInstallationAsync()
    {
        if (shutdownStarted || !services.Updates.Ready) return;
        if (tray.HasActiveWork && !await tray.ConfirmExitAsync()) return;
        updateInstallRequested = true;
        BeginShutdown();
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
            if (services.Updates.Ready && (updateInstallRequested || services.Updates.State.InstallOnExit))
            {
                try { await services.Updates.LaunchInstallerAsync(restart: updateInstallRequested); }
                catch (Exception error) { services.Logger.Error("The staged updater could not be started; the installation was left unchanged.", error); }
            }
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
            tray.Dispose();
            services.Queue.Changed -= StateChanged;
            services.Tools.Changed -= StateChanged;
            services.Live.Changed -= StateChanged; services.Updates.Changed -= StateChanged;
            text.LanguageChanged -= LanguageChanged;
            ThemeManager.Current.ThemeChanged -= ThemeChanged;
            Closing -= FormClosing;
            Shown -= FormShown;
            content.SizeChanged -= LayoutChanged;
            services.Appearance.ModeChanged -= AppearanceChanged;
        }
        base.Dispose(disposing);
    }
}
