using ModernTubeDownloader.Localization;
using ModernFormsNext;
using ModernTubeDownloader.Services;
using ModernTubeDownloader.Theming;

namespace ModernTubeDownloader.Views;

internal sealed class StartupUpdateForm : Form
{
    private StartupCoordinator? startup;
    private LocalizationService text;
    private StartupPhase bootstrapPhase;
    private readonly TaskCompletionSource firstPaint = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal Task FirstPaint => firstPaint.Task;
    private readonly Panel surface = new() { Dock = DockStyle.Fill, AutoScroll = true };
    private readonly PictureBox logo = new() { Image = AppBranding.Icon, SizeMode = PictureBoxSizeMode.Zoom };
    private readonly Label title = AppUi.Heading("ModernTubeDownloader", 21);
    private readonly Label version = AppUi.Muted(), status = AppUi.Heading("", 14), detail = AppUi.Muted();
    private readonly DashboardProgress progress = new() { Minimum = 0, Maximum = 100, IsIndeterminate = true };
    private readonly Button continueButton = new DashboardButton();
    private readonly ModernFormsNext.Timer animation = new() { Interval = 40 };
    private bool finished;

    internal StartupUpdateForm(AppServices services, StartupCoordinator startup)
        : this(services.Localization)
    {
        Attach(services.Localization, startup);
    }

    internal StartupUpdateForm(LocalizationService text)
    {
        this.text = text;
        Text = "ModernTubeDownloader";
        Size = new(478, 280);
        MinimumSize = new(410, 265);
        StartPosition = FormStartPosition.CenterScreen;
        UseSystemDecorations = true;
        AccessibilityObject.AutomationId = "StartupUpdateWindow";
        AppBranding.Apply(this);
        ExperienceVisual.Surface(surface);
        AppTypography.SetDashboardPresentation(surface, true);
        status.AccessibleAutomationId = "StartupUpdateStatus";
        progress.AccessibleAutomationId = "StartupUpdateProgress";
        title.Multiline = status.Multiline = detail.Multiline = true;
        continueButton.Text = text["Startup.Continue"];
        continueButton.AccessibleAutomationId = "StartupUpdateContinue";
        ExperienceVisual.Action(continueButton, false);
        continueButton.Click += (_, _) => ContinueWithoutUpdating();
        continueButton.KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) ContinueWithoutUpdating(); };
        Controls.Add(surface);
        surface.Controls.AddRange([logo, title, version, status, detail, progress, continueButton]);
        surface.SizeChanged += (_, _) => LayoutContent();
        AppTypography.RegisterLayout(surface, LayoutContent);
        Shown += (_, _) => { if (AppearancePresentation.Current.Global.Animations) animation.Start(); };
        Closing += (_, e) => { if (!finished) { e.Cancel = true; ContinueWithoutUpdating(); } };
        animation.Tick += (_, _) => { if (progress.IsIndeterminate) progress.Invalidate(); };
        AppTypography.CaptureRecipes(surface).Invoke();
        RefreshState();
    }

    internal void Attach(LocalizationService localization, StartupCoordinator coordinator)
    {
        text = localization;
        startup = coordinator;
        continueButton.Text = text["Startup.Continue"];
        coordinator.Changed += OnStartupChanged;
        RefreshState();
    }

    internal void Report(StartupPhase phase)
    {
        if (finished || startup is not null) return;
        bootstrapPhase = phase;
        RefreshState();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        firstPaint.TrySetResult();
    }

    private void ContinueWithoutUpdating()
    {
        if (!continueButton.Enabled) return;
        continueButton.Enabled = false;
        startup?.Cancellation.Cancel();
    }

    internal void Finish()
    {
        if (finished) return;
        finished = true;
        animation.Stop();
        Close();
    }

    private void OnStartupChanged(object? sender, EventArgs args) =>
        Application.RunOnUIThread(() => { if (!finished) RefreshState(); });

    private void RefreshState()
    {
        version.Text = startup?.TargetVersion is { } target ? $"v{AppBranding.Version}  →  v{target}" : $"v{AppBranding.Version}";
        var phase = startup?.Phase ?? bootstrapPhase;
        status.Text = text[phase switch
        {
            StartupPhase.LoadSettings => "Startup.LoadSettings",
            StartupPhase.LoadData => "Startup.LoadData",
            StartupPhase.CreateServices or StartupPhase.ConstructUi => "Startup.Services",
            StartupPhase.Tools => "Provisioning.Title",
            StartupPhase.Check => "Startup.Checking",
            StartupPhase.Available => "Updates.Available",
            StartupPhase.Download => "Startup.Download",
            StartupPhase.Verify => "Startup.Verify",
            StartupPhase.PrepareInstall => "Updates.StartupVerified",
            StartupPhase.Offline => "Startup.Offline",
            StartupPhase.UpdateFailed => "Startup.UpdateFailed",
            StartupPhase.StartServices => "Startup.Services",
            StartupPhase.Ready => "Startup.Ready",
            _ => "Startup.Load"
        }];
        var transfer = phase == StartupPhase.Download ? startup?.Transfer : null;
        progress.Visible = phase != StartupPhase.Ready;
        progress.IsIndeterminate = transfer?.Percent is null;
        progress.Value = transfer?.Percent ?? 0;
        if (transfer?.Percent is int percent) status.Text += $"  {percent}%";
        progress.AccessibleName = status.Text;
        var canContinue = startup is not null && phase is StartupPhase.Initialize or StartupPhase.Check or StartupPhase.Download or StartupPhase.Verify or StartupPhase.Available;
        continueButton.Visible = canContinue;
        continueButton.Enabled = canContinue && startup?.Cancellation.IsCancellationRequested == false;
        detail.Text = transfer is { } bytes
            ? text.Get("Startup.Transfer", FormatBytes(bytes.Bytes), bytes.Total is { } total ? FormatBytes(total) : "—",
                bytes.BytesPerSecond > 0 ? FormatBytes(bytes.BytesPerSecond) : "—")
            : phase switch
            {
                StartupPhase.StartServices when startup?.Outcome is StartupUpdateOutcome.TimedOut or StartupUpdateOutcome.Offline => text["Startup.Offline"],
                StartupPhase.StartServices when startup?.Outcome == StartupUpdateOutcome.Failed => text["Startup.UpdateFailed"],
                _ => continueButton.Visible ? text["Startup.Detail"] : ""
            };
        LayoutContent();
    }

    private string FormatBytes(long bytes) => (bytes / 1024d / 1024d).ToString("0.0", text.Culture) + " MiB";

    private void LayoutContent()
    {
        const int inset = 24;
        var width = Math.Max(1, surface.Width - 2 * inset);
        logo.SetBounds(inset, 20, 44, 44);
        title.SetBounds(inset + 56, 19, Math.Max(1, width - 56), DashboardVisual.LabelHeight(title, Math.Max(1, width - 56), 4));
        version.SetBounds(title.Left, title.Bottom + 1, Math.Max(1, width - 56), AppTypography.TextHeight(21));
        status.SetBounds(inset, Math.Max(logo.Bottom, version.Bottom) + 20, width, DashboardVisual.LabelHeight(status, width, 6));
        detail.SetBounds(inset, status.Bottom + 6, width, DashboardVisual.LabelHeight(detail, width, 6));
        progress.SetBounds(inset, detail.Bottom + 13, width, 7);
        continueButton.SetBounds(inset, progress.Bottom + 17, Math.Min(width, DashboardVisual.ActionWidth(continueButton)), AppTypography.TextHeight(42));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            finished = true;
            if (startup is not null) startup.Changed -= OnStartupChanged;
            animation.Dispose();
        }
        base.Dispose(disposing);
    }
}
