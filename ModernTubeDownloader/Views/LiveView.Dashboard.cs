using ModernFormsNext;
using ModernTubeDownloader.Models;
using ModernTubeDownloader.Services;
using ModernTubeDownloader.Theming;

namespace ModernTubeDownloader.Views;

internal sealed partial class LiveView
{
    private bool dashboardSubmitted;
    private bool dashboard, showRecent;
    private Guid? selectedSession;
    private DashboardInspector? inspector;
    private DashboardWidgets? widgets;
    private Button? activeTab, recentTab;
    private Panel? dashboardList;
    private Button? pasteLive, sourceLive;
    public event EventHandler? SupportedSourcesRequested;
    public void SetDashboard(bool value)
    {
        if (!value && dashboardSubmitted) { dashboardSubmitted = false; PopulateAnalysis(); }
        dashboard = value; url.Placeholder = value ? text["Dashboard.LivePlaceholder"] : "";
        if (value && inspector is null)
        {
            inspector = new(services); widgets = new(services, true);
            dashboardList = new Panel(); DashboardStyle.Card(dashboardList);
            pasteLive = new Button { AccessibleAutomationId = "LivePaste" }; sourceLive = new Button { AccessibleAutomationId = "LiveSources" };
            pasteLive.Click += async (_, _) => { try { url.Text = await Clipboard.GetTextAsync() ?? ""; url.Select(); } catch (Exception error) { services.Logger.Error("LIVE clipboard could not be read.", error); } };
            sourceLive.Click += (_, _) => SupportedSourcesRequested?.Invoke(this, EventArgs.Empty);
            scroll.Controls.AddRange([dashboardList, pasteLive, sourceLive]);
            activeTab = new Button { AccessibleAutomationId = "DashboardLiveActive" }; recentTab = new Button { AccessibleAutomationId = "DashboardLiveRecent" };
            activeTab.Click += (_, _) => { showRecent = false; LayoutPage(); }; recentTab.Click += (_, _) => { showRecent = true; LayoutPage(); };
            scroll.Controls.AddRange([inspector, widgets, activeTab, recentTab]);
        }
        foreach (var control in new Control?[] { inspector, widgets, activeTab, recentTab, dashboardList, pasteLive, sourceLive }) if (control is not null) control.Visible = value;
        if (!value) title.Visible = message.Visible = true;
        foreach (var section in sections) { section.Panel.Visible = true; AppTypography.Bind(section.Heading, value ? 11 : 16, FontStyle.Bold); }
        foreach (var card in cards.Values) card.SetDashboard(value);
        RefreshSessions(); LayoutPage();
    }
    private void SelectSession(LiveSession value) { selectedSession = value.Id; inspector?.ShowLive(value); }
    private void LayoutDashboard()
    {
        if (inspector is null) return;
        var origin = scroll.DisplayRectangle.Location; var width = Math.Max(560, scroll.ClientSize.Width - 36);
        var line = AppTypography.TextHeight(44); var inline = width >= AppTypography.TextHeight(1050);
        var right = inline ? Math.Clamp(width * 25 / 100, 300, AppTypography.TextHeight(330)) : 0;
        var center = inline ? width - right - 12 : width;
        heading.Visible = statistics.Visible = false;
        var expanded = !dashboardSubmitted && (metadata is not null || availability == MediaAvailabilityKind.Upcoming || !analyze.Enabled);
        if (dashboardSubmitted) foreach (var control in new Control[] { quality, container, fromNow, fromStart, start, vod }) control.Visible = false;
        analysis.SetBounds(18 + origin.X, 26 + origin.Y, width, expanded ? AppTypography.TextHeight(start.Visible ? 332 : 218) : line + 8);
        AppUi.BindBackground(analysis); DashboardVisual.Flat(analysis);
        url.SetBounds(0, 0, Math.Max(180, center - AppTypography.TextHeight(272) - 24), line);
        pasteLive!.Text = text["Dashboard.Paste"]; pasteLive.SetBounds(analysis.Left + url.Right + 12, analysis.Top, AppTypography.TextHeight(92), line);
        analyze.SetBounds(url.Right + AppTypography.TextHeight(92) + 24, 0, AppTypography.TextHeight(180), line);
        sourceLive!.Text = text["Sources.Link"]; sourceLive.SetBounds(analysis.Left, analysis.Bottom + 10, AppTypography.TextHeight(240), AppTypography.TextHeight(36));
        DashboardVisual.Button(pasteLive); DashboardVisual.Button(analyze, true); DashboardVisual.Button(sourceLive);
        title.Visible = message.Visible = expanded;
        title.SetBounds(0, line + 18, width - 24, AppTypography.TextHeight(30)); message.SetBounds(0, title.Bottom + 4, width, AppTypography.TextHeight(42));
        quality.SetBounds(0, message.Bottom + 8, Math.Min(260, width / 2), AppTypography.TextHeight(36)); container.SetBounds(quality.Right + 12, quality.Top, 120, quality.Height);
        fromNow.SetBounds(0, quality.Bottom + 8, width, AppTypography.TextHeight(30)); fromStart.SetBounds(0, fromNow.Bottom + 4, width, fromNow.Height);
        start.SetBounds(0, fromStart.Bottom + 8, Math.Min(width, AppTypography.TextHeight(280)), line); vod.SetBounds(0, message.Bottom + 10, Math.Min(width, AppTypography.TextHeight(300)), line);
        analysis.Height = expanded ? (start.Visible ? start.Bottom : vod.Visible ? vod.Bottom : message.Bottom) + 10 : line + 8;
        sourceLive!.Top = analysis.Bottom + 12;
        var top = sourceLive.Bottom + 18;
        activeTab!.SetBounds(30 + origin.X, top + 8, Math.Min(center / 2 - 6, AppTypography.TextHeight(300)), line);
        recentTab!.SetBounds(activeTab.Right + 10, top, Math.Min(center / 2 - 6, AppTypography.TextHeight(200)), line);
        activeTab.Text = services.Localization["Dashboard.ActiveScheduled"]; recentTab.Text = services.Localization["Dashboard.Completed"];
        activeTab.Text += $" ({services.Live.Snapshot().Count(s => s.IsActive || s.State is LiveSessionState.Pending or LiveSessionState.WaitingForLive)})";
        recentTab.Text += $" ({services.Live.Snapshot().Count(s => !s.IsActive && s.State is not LiveSessionState.Pending and not LiveSessionState.WaitingForLive)})";
        DashboardVisual.Tab(activeTab, !showRecent); DashboardVisual.Tab(recentTab, showRecent);
        var listTop = activeTab.Bottom + 12; var next = listTop;
        for (var i = 0; i < sections.Length; i++)
        {
            var section = sections[i]; section.Panel.Visible = showRecent ? i == 2 : i < 2;
            if (!section.Panel.Visible) continue;
            AppUi.BindBackground(section.Panel, AppThemeTokens.Surface); DashboardVisual.Flat(section.Panel); AppUi.BindBackground(section.List, AppThemeTokens.Surface);
            foreach (var card in section.List.Controls.OfType<LiveSessionCard>()) card.Width = center - 20;
            var listHeight = section.List.Controls.Sum(c => c.Height + c.Margin.Bottom);
            var header = AppTypography.TextHeight(36); section.Panel.SetBounds(18 + origin.X, next, center, Math.Max(header + AppTypography.TextHeight(44), listHeight + header + 12));
            section.Heading.SetBounds(12, 6, center - 24, header); section.List.SetBounds(8, header + 4, center - 16, Math.Max(1, listHeight));
            section.Empty.SetBounds(14, header + 4, center - 28, AppTypography.TextHeight(36)); section.Empty.Visible = listHeight == 0;
            next = section.Panel.Bottom + 12;
        }
        inspector.Visible = AppearancePresentation.Current.ModernDashboardShell.Inspector != DashboardInspectorMode.Hidden;
        inspector.SetBounds(inline ? 18 + origin.X + center + 12 : 18 + origin.X, inline ? top : next, inline ? right : width, AppTypography.TextHeight(inline ? 580 : 380));
        if (!inline && inspector.Visible) next = inspector.Bottom + 12;
        var listBottom = Math.Max(next + 24, ClientSize.Height - AppTypography.TextHeight(164));
        dashboardList!.SetBounds(18 + origin.X, top, center, Math.Max(AppTypography.TextHeight(240), listBottom - top)); dashboardList.SendToBack();
        widgets!.Visible = AppearancePresentation.Current.ModernDashboardShell.Widgets != DashboardWidgetsMode.Hidden;
        widgets.SetBounds(18 + origin.X, Math.Max(next, dashboardList.Bottom + 12), inline ? center : width, widgets.Height);
        var selected = selectedSession is { } id ? services.Live.Find(id) : null;
        selected ??= services.Live.Snapshot().FirstOrDefault();
        if (selected is not null) { selectedSession = selected.Id; inspector.ShowLive(selected); }
    }
}

internal sealed partial class LiveSessionCard
{
    private bool dashboard;
    public event EventHandler<LiveSession>? SelectionRequested;
    public void SetDashboard(bool value) { dashboard = value; AppTypography.Bind(title, value ? 11 : 13, FontStyle.Bold); AppTypography.SetDashboardPresentation(this, value); LayoutCard(); }
    private void LayoutDashboardCard()
    {
        var line = AppTypography.TextHeight(24); var narrow = Width < 850 || AppTypography.Scale > 1.25;
        var imageWidth = AppearancePresentation.ImageWidth(AppearancePresentation.Current.ModernDashboardShell.Thumbnail, 144);
        thumbnail.Visible = imageWidth > 0; thumbnail.SetBounds(12, 12, imageWidth, imageWidth * 9 / 16);
        var left = imageWidth > 0 ? thumbnail.Right + 16 : 14; var actionWidth = AppTypography.TextHeight(140);
        var textWidth = Math.Max(140, Width - left - actionWidth - 28);
        AppTypography.Bind(title, 11, FontStyle.Bold); AppTypography.Bind(facts, 9.25f); AppTypography.Bind(state, 9.5f); DashboardVisual.Flat(this, true); AppUi.BindBackground(this, AppThemeTokens.Surface); title.AutoEllipsis = true; title.SetBounds(left, 10, textWidth, line);
        state.SetBounds(left, title.Bottom + 4, textWidth, line);
        facts.SetBounds(left, state.Bottom + 4, textWidth, narrow ? line * 3 : line * 2);
        primary.SetBounds(Width - actionWidth - 12, 12, actionWidth, AppTypography.TextHeight(34));
        details.SetBounds(primary.Left, primary.Bottom + 8, actionWidth, primary.Height);
        secondary.SetBounds(primary.Left, details.Bottom + 8, actionWidth, primary.Height);
        foreach (var button in new[] { primary, secondary, details, folder }) DashboardVisual.Button(button, button == primary && current?.State == LiveSessionState.Pending);
        folder.SetBounds(left, facts.Bottom + 8, Math.Min(actionWidth, textWidth), primary.Height);
        Height = Math.Max(Math.Max(thumbnail.Bottom + 12, (folder.Visible ? folder.Bottom : facts.Bottom) + 12), secondary.Visible ? secondary.Bottom + 12 : details.Bottom + 12);
        AppUi.BindForeground(state, current?.State switch { LiveSessionState.Recording => AppThemeTokens.Success, LiveSessionState.Reconnecting or LiveSessionState.WaitingForLive => AppThemeTokens.Warning, LiveSessionState.Failed => AppThemeTokens.Error, _ => AppThemeTokens.Info });
    }
}
