using ModernFormsNext;
using ModernTubeDownloader.Services;
using ModernTubeDownloader.Theming;

namespace ModernTubeDownloader.Views;

/// <summary>One short foreground preparation, before the canonical workers start.</summary>
internal sealed class StartupUpdateForm : Form
{
    private readonly AppServices services;
    private readonly CancellationTokenSource cancellation;
    private readonly Label title = AppUi.Heading("", 18), detail = AppUi.Muted(), status = AppUi.Muted();
    private readonly ProgressBar progress = new() { Minimum = 0, Maximum = 100 };
    private readonly Button continueButton = new DashboardButton();
    private bool finished, started, continueRequested;
    internal StartupUpdateOutcome Outcome { get; private set; } = StartupUpdateOutcome.Deferred;

    internal StartupUpdateForm(AppServices services, CancellationTokenSource cancellation,
        Func<CancellationToken, Task<StartupUpdateOutcome>> prepare, Func<bool>? wasInterrupted = null)
    {
        this.services = services; this.cancellation = cancellation;
        Text = services.Localization["Updates.StartupTitle"]; Size = new(780, 460); MinimumSize = new(640, 390);
        AccessibilityObject.AutomationId = "StartupUpdateWindow";
        AppBranding.Apply(this); ExperienceVisual.ApplyWindow(this);
        var surface = new Panel { Dock = DockStyle.Fill }; ExperienceVisual.Surface(surface);
        AppTypography.SetDashboardPresentation(surface, true); Controls.Add(surface);
        title.Text = Text; title.Multiline = detail.Multiline = status.Multiline = true;
        detail.Text = services.Localization["Updates.StartupHelp"];
        continueButton.Text = services.Localization["Updates.ContinueStartup"];
        continueButton.AccessibleAutomationId = "StartupUpdateContinue"; status.AccessibleAutomationId = "StartupUpdateStatus";
        ExperienceVisual.Action(continueButton, true);
        continueButton.Click += (_, _) => { continueRequested = true; continueButton.Enabled = false; cancellation.Cancel(); };
        surface.Controls.AddRange([title, detail, status, progress, continueButton]);
        void LayoutContent()
        {
            var width = Math.Max(1, surface.Width - 48);
            title.SetBounds(24, 20, width, DashboardVisual.LabelHeight(title, width, 3));
            detail.SetBounds(24, title.Bottom + 16, width, DashboardVisual.LabelHeight(detail, width, 8));
            status.SetBounds(24, detail.Bottom + 18, width, DashboardVisual.LabelHeight(status, width, 5));
            progress.SetBounds(24, status.Bottom + 10, width, 6);
            continueButton.SetBounds(24, Math.Max(progress.Bottom + 16, surface.Height - AppTypography.TextHeight(42) - 24), width, AppTypography.TextHeight(42));
            surface.AutoScroll = true;
        }
        surface.SizeChanged += (_, _) => LayoutContent(); AppTypography.RegisterLayout(surface, LayoutContent);
        services.Updates.Changed += Changed;
        Shown += async (_, _) =>
        {
            if (started) return; started = true;
            try { Outcome = await prepare(cancellation.Token); }
            catch (OperationCanceledException)
            {
                Outcome = continueRequested || wasInterrupted?.Invoke() == true ? StartupUpdateOutcome.Deferred : StartupUpdateOutcome.TimedOut;
                await services.Updates.RecordStartupOutcomeAsync(Outcome);
            }
            catch (Exception error)
            {
                services.Logger.Error("Startup update preflight could not complete.", error);
                Outcome = StartupUpdateOutcome.Failed;
                await services.Updates.RecordStartupOutcomeAsync(Outcome);
            }
            finally { finished = true; Close(); }
        };
        Closing += (_, e) => { if (!finished) { continueRequested = true; cancellation.Cancel(); e.Cancel = true; } };
        AppTypography.CaptureRecipes(surface).Invoke(); RefreshState(); LayoutContent();
        void RefreshState()
        {
            status.Text = services.Localization[services.Updates.StatusKey];
            progress.Value = (int)Math.Clamp(services.Updates.Progress, 0, 100);
            LayoutContent();
        }
        refresh = RefreshState;
    }
    private readonly Action refresh;
    private void Changed(object? sender, EventArgs e) => Application.RunOnUIThread(() => { if (!finished) refresh(); });
    protected override void Dispose(bool disposing)
    {
        if (disposing) services.Updates.Changed -= Changed;
        base.Dispose(disposing);
    }
}
