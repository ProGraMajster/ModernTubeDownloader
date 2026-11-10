using ModernFormsNext;
using ModernTubeDownloader.Models;
using ModernTubeDownloader.Services;
using ModernTubeDownloader.Theming;

namespace ModernTubeDownloader.Views;

internal sealed partial class DashboardInspector
{
    private Guid? liveSessionId;
    private readonly Button liveAction = new DashboardButton { AccessibleAutomationId = "InspectorLiveAction" };
    private readonly Button liveAnalyze = new DashboardButton { AccessibleAutomationId = "InspectorLiveAnalyze" };
    public event EventHandler<LiveSession>? LiveEditRequested;
    public event EventHandler<LiveSession>? LiveAnalyzeRequested;

    private void CreateLiveActions()
    {
        Controls.AddRange([liveAction, liveAnalyze]);
        liveAction.Click += (_, _) =>
        {
            // Re-resolve the canonical state; a recording may finish between paint and Invoke.
            if (liveSessionId is not { } id || services.Live.Find(id) is not { } session) return;
            if (session.IsActive) services.Live.StopAndSave(id);
            else if (session.State == LiveSessionState.WaitingForLive) LiveEditRequested?.Invoke(this, session);
            else if (session.CanResume) services.Live.Resume(id);
        };
        liveAnalyze.Click += (_, _) =>
        {
            if (liveSessionId is { } id && services.Live.Find(id) is { } session && !LiveDashboardPage.IsCurrent(session))
                LiveAnalyzeRequested?.Invoke(this, session);
        };
    }

    private void PresentLiveActions(LiveSession session)
    {
        liveAction.Visible = session.IsActive || session.CanResume || session.State == LiveSessionState.WaitingForLive;
        liveAction.Enabled = session.State != LiveSessionState.Finalizing;
        liveAction.Text = services.Localization[session.IsActive ? "Live.StopAndSave" : session.State == LiveSessionState.WaitingForLive ? "Live.Edit" : "Live.Resume"];
        liveAnalyze.Visible = !LiveDashboardPage.IsCurrent(session);
        liveAnalyze.Text = services.Localization["Live.AnalyzeAgain"];
        DownloadsVisual.Button(liveAction, !session.IsActive); DownloadsVisual.Button(liveAnalyze);
        DashboardVisual.Icon(liveAction, session.IsActive ? AppIconKind.Stop : session.State == LiveSessionState.WaitingForLive ? AppIconKind.Settings : AppIconKind.Play);
        DashboardVisual.Icon(liveAnalyze, AppIconKind.Search);
    }

    private void HideInactiveLiveActions()
    {
        if (liveSessionId is not null) return;
        foreach (var action in new[] { liveAction, liveAnalyze }) { action.Visible = false; action.SetBounds(0, 0, 0, 0); }
    }

    private void LayoutLiveActions(int top, int left, int width, int height)
    {
        foreach (var action in new[] { liveAction, liveAnalyze })
        {
            if (!action.Visible) { action.SetBounds(0, 0, 0, 0); continue; }
            action.SetBounds(left, top, width, height); top = action.Bottom + 8;
        }
    }
}
