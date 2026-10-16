using ModernFormsNext;
using ModernTubeDownloader.Models;
using ModernTubeDownloader.Services;
using ModernTubeDownloader.Theming;

namespace ModernTubeDownloader.Views;

internal sealed partial class LiveView
{
    private bool dashboard, showRecent;
    private Guid? selectedSession;
    private DashboardInspector? inspector;
    private DashboardWidgets? widgets;
    private Button? activeTab, recentTab;
    public void SetDashboard(bool value)
    {
        dashboard = value; url.Placeholder = value ? text["Dashboard.LivePlaceholder"] : "";
        if (value && inspector is null)
        {
            inspector = new(services); widgets = new(services, true);
            activeTab = new Button { AccessibleAutomationId = "DashboardLiveActive" }; recentTab = new Button { AccessibleAutomationId = "DashboardLiveRecent" };
            activeTab.Click += (_, _) => { showRecent = false; LayoutPage(); }; recentTab.Click += (_, _) => { showRecent = true; LayoutPage(); };
            scroll.Controls.AddRange([inspector, widgets, activeTab, recentTab]);
        }
        foreach (var control in new Control?[] { inspector, widgets, activeTab, recentTab }) if (control is not null) control.Visible = value;
        if (!value) title.Visible = message.Visible = true;
        foreach (var section in sections) section.Panel.Visible = true;
        foreach (var card in cards.Values) card.SetDashboard(value);
        RefreshSessions(); LayoutPage();
    }
    private void SelectSession(LiveSession value) { selectedSession = value.Id; inspector?.ShowLive(value); }
    private void LayoutDashboard()
    {
        if (inspector is null) return;
        var origin = scroll.DisplayRectangle.Location; var width = Math.Max(560, scroll.ClientSize.Width - 36);
        var line = AppTypography.TextHeight(44); var inline = width >= AppTypography.TextHeight(1050);
        var right = inline ? Math.Clamp(width / 4, 290, AppTypography.TextHeight(320)) : 0;
        var center = inline ? width - right - 12 : width;
        heading.Visible = statistics.Visible = false;
        var expanded = metadata is not null || availability == MediaAvailabilityKind.Upcoming || !analyze.Enabled;
        analysis.SetBounds(18 + origin.X, 26 + origin.Y, width, expanded ? AppTypography.TextHeight(start.Visible ? 332 : 218) : line + 8);
        AppUi.BindBackground(analysis); analysis.Style.Border.Width = 0;
        url.SetBounds(0, 0, Math.Max(180, width - AppTypography.TextHeight(180) - 12), line);
        analyze.SetBounds(url.Right + 12, 0, AppTypography.TextHeight(180), line);
        title.Visible = message.Visible = expanded;
        title.SetBounds(0, line + 18, width - 24, AppTypography.TextHeight(30)); message.SetBounds(0, title.Bottom + 4, width, AppTypography.TextHeight(42));
        quality.SetBounds(0, message.Bottom + 8, Math.Min(260, width / 2), AppTypography.TextHeight(36)); container.SetBounds(quality.Right + 12, quality.Top, 120, quality.Height);
        fromNow.SetBounds(0, quality.Bottom + 8, width, AppTypography.TextHeight(30)); fromStart.SetBounds(0, fromNow.Bottom + 4, width, fromNow.Height);
        start.SetBounds(0, fromStart.Bottom + 8, Math.Min(width, AppTypography.TextHeight(280)), line); vod.SetBounds(0, message.Bottom + 10, Math.Min(width, AppTypography.TextHeight(300)), line);
        analysis.Height = expanded ? (start.Visible ? start.Bottom : vod.Visible ? vod.Bottom : message.Bottom) + 10 : line + 8;
        var top = analysis.Bottom + 20;
        activeTab!.SetBounds(18 + origin.X, top, Math.Min(center / 2 - 6, AppTypography.TextHeight(300)), line);
        recentTab!.SetBounds(activeTab.Right + 10, top, Math.Min(center / 2 - 6, AppTypography.TextHeight(200)), line);
        activeTab.Text = services.Localization["Dashboard.ActiveScheduled"]; recentTab.Text = services.Localization["Dashboard.Completed"];
        AppUi.NavigationButton(activeTab, !showRecent); AppUi.NavigationButton(recentTab, showRecent);
        var listTop = activeTab.Bottom + 12; var next = listTop;
        for (var i = 0; i < sections.Length; i++)
        {
            var section = sections[i]; section.Panel.Visible = showRecent ? i == 2 : i < 2;
            if (!section.Panel.Visible) continue;
            DashboardStyle.Card(section.Panel); AppUi.BindBackground(section.List, AppThemeTokens.Surface);
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
        widgets!.Visible = AppearancePresentation.Current.ModernDashboardShell.Widgets != DashboardWidgetsMode.Hidden;
        widgets.SetBounds(18 + origin.X, Math.Max(next, inline ? Math.Min(inspector.Bottom, ClientSize.Height - widgets.Height - 18) : next), inline ? center : width, widgets.Height);
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
        AppTypography.Bind(title, 11, FontStyle.Bold); title.AutoEllipsis = true; title.SetBounds(left, 10, textWidth, line);
        state.SetBounds(left, title.Bottom + 4, textWidth, line);
        facts.SetBounds(left, state.Bottom + 4, textWidth, narrow ? line * 3 : line * 2);
        primary.SetBounds(Width - actionWidth - 12, 12, actionWidth, AppTypography.TextHeight(34));
        details.SetBounds(primary.Left, primary.Bottom + 8, actionWidth, primary.Height);
        secondary.SetBounds(primary.Left, details.Bottom + 8, actionWidth, primary.Height);
        folder.SetBounds(left, facts.Bottom + 8, Math.Min(actionWidth, textWidth), primary.Height);
        Height = Math.Max(Math.Max(thumbnail.Bottom + 12, (folder.Visible ? folder.Bottom : facts.Bottom) + 12), secondary.Visible ? secondary.Bottom + 12 : details.Bottom + 12);
        AppUi.BindForeground(state, current?.State switch { LiveSessionState.Recording => AppThemeTokens.Success, LiveSessionState.Reconnecting or LiveSessionState.WaitingForLive => AppThemeTokens.Warning, LiveSessionState.Failed => AppThemeTokens.Error, _ => AppThemeTokens.Info });
    }
}
