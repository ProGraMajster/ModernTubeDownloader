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
    private AppVectorIcon? liveLink;
    public event EventHandler? SupportedSourcesRequested;
    public void SetDashboard(bool value)
    {
        if (!value && dashboardSubmitted) { dashboardSubmitted = false; PopulateAnalysis(); }
        dashboard = value; url.Placeholder = value ? text["Dashboard.LivePlaceholder"] : "";
        if (value && inspector is null)
        {
            inspector = new(services); widgets = new(services, true);
            dashboardList = new Panel(); DashboardStyle.Card(dashboardList);
            pasteLive = new DashboardButton { AccessibleAutomationId = "LivePaste" }; sourceLive = new DashboardButton { AccessibleAutomationId = "LiveSources" };
            liveLink = new AppVectorIcon(AppIconKind.Link, 20) { AccessibilityView = ModernFormsNext.Accessibility.AccessibilityView.Hidden };
            analysis.Controls.Add(liveLink);
            pasteLive.Click += async (_, _) => { try { url.Text = await Clipboard.GetTextAsync() ?? ""; url.Select(); } catch (Exception error) { services.Logger.Error("LIVE clipboard could not be read.", error); } };
            sourceLive.Click += (_, _) => SupportedSourcesRequested?.Invoke(this, EventArgs.Empty);
            scroll.Controls.AddRange([dashboardList, pasteLive, sourceLive]);
            activeTab = new DashboardButton { AccessibleAutomationId = "DashboardLiveActive" }; recentTab = new DashboardButton { AccessibleAutomationId = "DashboardLiveRecent" };
            activeTab.Click += (_, _) => { showRecent = false; LayoutPage(); }; recentTab.Click += (_, _) => { showRecent = true; LayoutPage(); };
            scroll.Controls.AddRange([inspector, widgets, activeTab, recentTab]);
        }
        foreach (var control in new Control?[] { inspector, widgets, activeTab, recentTab, dashboardList, pasteLive, sourceLive, liveLink })
            if (control is not null) { control.Visible = value; if (!value) control.SetBounds(0, 0, 0, 0); }
        if (!value) title.Visible = message.Visible = true;
        foreach (var section in sections) { section.Panel.Visible = true; section.Heading.Visible = !value; AppTypography.Bind(section.Heading, value ? 11 : 16, FontStyle.Bold); }
        foreach (var card in cards.Values) card.SetDashboard(value);
        RefreshSessions(); LayoutPage();
    }
    private void SelectSession(LiveSession value)
    {
        selectedSession = value.Id; inspector?.ShowLive(value);
        foreach (var card in cards) card.Value.SetSelected(card.Key == value.Id);
    }
    private void LayoutDashboard()
    {
        if (inspector is null) return;
        var origin = scroll.DisplayRectangle.Location; var width = Math.Max(0, scroll.DisplayRectangle.Width - 36);
        var inspectorEnabled = AppearancePresentation.Current.ModernDashboardShell.Inspector != DashboardInspectorMode.Hidden;
        var line = AppTypography.TextHeight(44); var inline = inspectorEnabled && width >= AppTypography.TextHeight(1050);
        var right = inline ? Math.Clamp(width * 25 / 100, 300, AppTypography.TextHeight(330)) : 0;
        var center = inline ? width - right - 12 : width;
        heading.Visible = statistics.Visible = false;
        var expanded = !dashboardSubmitted && (metadata is not null || availability == MediaAvailabilityKind.Upcoming || !analyze.Enabled);
        if (dashboardSubmitted) foreach (var control in new Control[] { quality, container, fromNow, fromStart, start, vod }) control.Visible = false;
        analysis.SetBounds(18 + origin.X, 26 + origin.Y, width, expanded ? AppTypography.TextHeight(start.Visible ? 332 : 218) : line + 8);
        AppUi.BindBackground(analysis); DashboardVisual.Flat(analysis);
        url.SetBounds(0, 0, Math.Max(180, center - AppTypography.TextHeight(272) - 24), line);
        DashboardVisual.SetPadding(url, new Padding(42, 0, 12, 0));
        liveLink!.SetBounds(12, (line - 20) / 2, 20, 20); liveLink.BringToFront();
        pasteLive!.Text = text["Dashboard.Paste"]; pasteLive.SetBounds(analysis.Left + url.Right + 12, analysis.Top, AppTypography.TextHeight(92), line);
        analyze.SetBounds(url.Right + AppTypography.TextHeight(92) + 24, 0, AppTypography.TextHeight(180), line);
        sourceLive!.Text = text["Sources.Link"]; sourceLive.SetBounds(analysis.Left, analysis.Bottom + 10, AppTypography.TextHeight(240), AppTypography.TextHeight(36));
        DashboardVisual.Button(pasteLive); DashboardVisual.Button(analyze, true); DashboardVisual.Button(sourceLive);
        DashboardVisual.Icon(pasteLive, AppIconKind.Clipboard); DashboardVisual.Icon(analyze, AppIconKind.Search); DashboardVisual.Icon(sourceLive, AppIconKind.Link);
        sourceLive.Width = DashboardVisual.ActionWidth(sourceLive, true);
        title.Visible = message.Visible = expanded;
        title.SetBounds(0, line + 18, width - 24, AppTypography.TextHeight(30)); message.SetBounds(0, title.Bottom + 4, width, AppTypography.TextHeight(42));
        quality.SetBounds(0, message.Bottom + 8, Math.Min(260, width / 2), AppTypography.TextHeight(36)); container.SetBounds(quality.Right + 12, quality.Top, 120, quality.Height);
        fromNow.SetBounds(0, quality.Bottom + 8, width, AppTypography.TextHeight(30)); fromStart.SetBounds(0, fromNow.Bottom + 4, width, fromNow.Height);
        start.SetBounds(0, fromStart.Bottom + 8, Math.Min(width, AppTypography.TextHeight(280)), line); vod.SetBounds(0, message.Bottom + 10, Math.Min(width, AppTypography.TextHeight(300)), line);
        analysis.Height = expanded ? (start.Visible ? start.Bottom : vod.Visible ? vod.Bottom : message.Bottom) + 10 : line + 8;
        sourceLive!.Top = analysis.Bottom + 12;
        var top = sourceLive.Bottom + 18;
        activeTab!.SetBounds(30 + origin.X, top + 8, Math.Min(center / 2 - 6, AppTypography.TextHeight(300)), line);
        recentTab!.SetBounds(activeTab.Right + 10, activeTab.Top, Math.Min(center / 2 - 6, AppTypography.TextHeight(200)), line);
        activeTab.Text = services.Localization["Dashboard.ActiveScheduled"]; recentTab.Text = services.Localization["Dashboard.Completed"];
        activeTab.Text += $" ({services.Live.Snapshot().Count(s => s.IsActive || s.State is LiveSessionState.Pending or LiveSessionState.WaitingForLive)})";
        recentTab.Text += $" ({services.Live.Snapshot().Count(s => !s.IsActive && s.State is not LiveSessionState.Pending and not LiveSessionState.WaitingForLive)})";
        DashboardVisual.Tab(activeTab, !showRecent); DashboardVisual.Tab(recentTab, showRecent);
        var listTop = activeTab.Bottom + 12; var next = listTop;
        for (var i = 0; i < sections.Length; i++)
        {
            var section = sections[i]; section.Panel.Visible = showRecent ? i == 2 : i < 2;
            var hasRows = section.List.Controls.Count > 0;
            if (!showRecent && !hasRows && (i == 1 || sections[1].List.Controls.Count > 0)) section.Panel.Visible = false;
            if (!section.Panel.Visible) { section.Panel.SetBounds(0, 0, 0, 0); continue; }
            AppUi.BindBackground(section.Panel, AppThemeTokens.Surface); DashboardVisual.Flat(section.Panel); AppUi.BindBackground(section.List, AppThemeTokens.Surface);
            foreach (var card in section.List.Controls.OfType<LiveSessionCard>()) card.Width = center - 20;
            var listHeight = section.List.Controls.Sum(c => c.Height + c.Margin.Bottom);
            section.Panel.SetBounds(18 + origin.X, next, center, Math.Max(AppTypography.TextHeight(64), listHeight + 8));
            section.Heading.Visible = false; section.Heading.SetBounds(0, 0, 0, 0);
            section.List.SetBounds(8, 4, center - 16, Math.Max(1, listHeight));
            section.Empty.SetBounds(14, 16, center - 28, AppTypography.TextHeight(36)); section.Empty.Visible = listHeight == 0;
            next = section.Panel.Bottom + 12;
        }
        inspector.Visible = inspectorEnabled;
        var inspectorHeight = inline ? Math.Max(AppTypography.TextHeight(300), scroll.ClientSize.Height - top + origin.Y - 18) : AppTypography.TextHeight(500);
        if (inspectorEnabled)
            inspector.SetBounds(inline ? 18 + origin.X + center + 12 : 18 + origin.X, inline ? top : next, inline ? right : width, inspectorHeight);
        else inspector.SetBounds(0, 0, 0, 0);
        if (!inline && inspector.Visible) next = inspector.Bottom + 12;
        var listBottom = Math.Max(next + 24, ClientSize.Height - AppTypography.TextHeight(164));
        dashboardList!.SetBounds(18 + origin.X, top, center, Math.Max(AppTypography.TextHeight(240), listBottom - top)); dashboardList.SendToBack();
        widgets!.Visible = AppearancePresentation.Current.ModernDashboardShell.Widgets != DashboardWidgetsMode.Hidden;
        if (widgets.Visible)
            widgets.SetBounds(18 + origin.X, Math.Max(next, dashboardList.Bottom + 12), inline ? center : width, widgets.Height);
        else widgets.SetBounds(0, 0, 0, 0);
        var selected = selectedSession is { } id ? services.Live.Find(id) : null;
        selected ??= services.Live.Snapshot().FirstOrDefault();
        if (selected is not null) SelectSession(selected);
        else inspector.Clear();
    }
}

internal sealed partial class LiveSessionCard
{
    private bool dashboard, selected;
    private Label? dashboardChannel, dashboardTiming;
    private AppVectorIcon? dashboardState;
    private Panel? dashboardThumbnail;
    private AppVectorIcon? dashboardThumbnailIcon;
    public event EventHandler<LiveSession>? SelectionRequested;
    public void SetSelected(bool value) { if (selected == value) return; selected = value; if (dashboard) LayoutDashboardCard(); }
    public void SetDashboard(bool value)
    {
        dashboard = value;
        AppTypography.Bind(title, value ? DashboardVisual.ItemTitle : 13, FontStyle.Bold);
        AppTypography.Bind(facts, value ? 9.25f : 10.5f); AppTypography.Bind(state, value ? 9.5f : 10.5f);
        AppTypography.SetDashboardPresentation(this, value);
        if (!value)
        {
            DashboardVisual.RestoreTree(this); AppUi.Primary(primary); AppUi.BindForeground(state, AppThemeTokens.TextSecondary);
            title.AutoEllipsis = false;
            if (dashboardChannel is not null) dashboardChannel.Visible = dashboardTiming!.Visible = dashboardState!.Visible = false;
            if (dashboardThumbnail is not null) dashboardThumbnail.Visible = false;
            if (current is not null) UpdateSession(current);
        }
        LayoutCard();
    }
    private void LayoutDashboardCard()
    {
        if (dashboardChannel is null)
        {
            dashboardChannel = AppUi.Muted(); dashboardTiming = AppUi.Muted();
            dashboardState = new AppVectorIcon(AppIconKind.Live, 16) { AccessibilityView = ModernFormsNext.Accessibility.AccessibilityView.Hidden };
            dashboardThumbnail = new Panel(); AppUi.BindBackground(dashboardThumbnail, AppThemeTokens.SurfaceSecondary); DashboardVisual.Flat(dashboardThumbnail);
            dashboardThumbnailIcon = new AppVectorIcon(AppIconKind.Live, 28) { AccessibilityView = ModernFormsNext.Accessibility.AccessibilityView.Hidden };
            dashboardThumbnail.Controls.Add(dashboardThumbnailIcon); dashboardThumbnail.Click += (_, _) => { if (current is not null) SelectionRequested?.Invoke(this, current); };
            dashboardThumbnailIcon.Click += (_, _) => { if (current is not null) SelectionRequested?.Invoke(this, current); };
            Controls.AddRange([dashboardChannel, dashboardTiming, dashboardState, dashboardThumbnail]);
        }
        dashboardChannel.Visible = dashboardTiming!.Visible = dashboardState!.Visible = true;
        var line = AppTypography.TextHeight(24); var narrow = Width < AppTypography.TextHeight(860);
        var imageWidth = AppearancePresentation.ImageWidth(AppearancePresentation.Current.ModernDashboardShell.Thumbnail, 144);
        thumbnail.Visible = imageWidth > 0 && thumbnail.Image is not null; thumbnail.SetBounds(12, 12, imageWidth, imageWidth * 9 / 16);
        dashboardThumbnail!.Visible = imageWidth > 0 && thumbnail.Image is null; dashboardThumbnail.Bounds = thumbnail.Bounds;
        dashboardThumbnailIcon!.SetBounds((imageWidth - 28) / 2, (thumbnail.Height - 28) / 2, 28, 28);
        var left = imageWidth > 0 ? thumbnail.Right + 16 : 14;
        foreach (var button in new[] { primary, secondary, details, folder }) DashboardVisual.Button(button, button == primary && current?.CanResume == true);
        var actionWidth = Math.Max(AppTypography.TextHeight(146), new[] { primary, secondary, details, folder }.Where(b => b.Visible).Select(b => DashboardVisual.ActionWidth(b, true)).DefaultIfEmpty(0).Max());
        var statusWidth = narrow ? 0 : AppTypography.TextHeight(180);
        var textWidth = Math.Max(140, Width - left - actionWidth - statusWidth - 36);
        DashboardVisual.Flat(this, !selected);
        AppUi.BindBackground(this, selected ? AppThemeTokens.RowSelected : AppThemeTokens.Surface);
        Style.Border.Width = selected ? 1 : 0; Style.Border.Radius = selected ? 6 : 0; Style.Border.Color = AppUi.GetColor(AppThemeTokens.Accent).WithAlpha(130);
        title.AutoEllipsis = true; title.SetBounds(left, 12, textWidth, line);
        AppTypography.Bind(dashboardChannel, 9.5f); dashboardChannel.AutoEllipsis = true;
        dashboardChannel.Text = current?.Channel ?? ""; dashboardChannel.SetBounds(left, title.Bottom + 4, textWidth, line);
        facts.Visible = true; facts.SetBounds(left, dashboardChannel.Bottom + 4, textWidth, line * 2);
        if (current is not null)
        {
            var t = services.Localization;
            var appearance = AppearancePresentation.Current.Live;
            var source = appearance.ShowSource ? SourcePresentation.Host(current.SourceUrl) + " · " : "";
            facts.Text = source + t["Quality." + current.QualityPresetId] + " · " + current.Container;
            var measurements = new List<string>();
            if (appearance.ShowElapsed) measurements.Add(current.RecordedDuration.ToString(@"hh\:mm\:ss"));
            if (appearance.ShowBytes) measurements.Add(DashboardStyle.Bytes(current.BytesWritten));
            if (appearance.ShowParts) measurements.Add(t.Get("Live.PartCount", current.Parts.Count));
            if (appearance.ShowReconnectCount) measurements.Add(t["Live.ReconnectCount"] + ": " + current.ResumeCount);
            dashboardTiming.Text = current.State == LiveSessionState.WaitingForLive && current.ScheduledStartAt is { } scheduled
                ? scheduled.ToLocalTime().ToString("g", t.Culture)
                : string.Join(" · ", measurements);
            if (current.FailureMessageKey is { } failure) dashboardTiming.Text = t[failure];
            else if ((current.State == LiveSessionState.WaitingForLive ? current.NextCheckAt : current.RetryAt) is { } next)
                dashboardTiming.Text += "\n" + t.Get("Live.NextCheck", Math.Max(0, (int)(next - DateTimeOffset.UtcNow).TotalSeconds));
        }
        var statusLeft = narrow ? left : left + textWidth + 12;
        var statusTop = narrow ? facts.Bottom + 4 : 12;
        var availableStatus = narrow ? textWidth : statusWidth;
        dashboardState.SetBounds(statusLeft, statusTop + (line - 16) / 2, 16, 16);
        state.SetBounds(statusLeft + 22, statusTop, Math.Max(100, availableStatus - 22), line);
        dashboardTiming.Multiline = true; AppTypography.Bind(dashboardTiming, 9.25f);
        dashboardTiming.SetBounds(statusLeft, state.Bottom + 8, availableStatus, DashboardVisual.LabelHeight(dashboardTiming, availableStatus, 6));
        var token = current?.State switch { LiveSessionState.Recording or LiveSessionState.Completed => AppThemeTokens.Success, LiveSessionState.Reconnecting or LiveSessionState.WaitingForLive => AppThemeTokens.Warning, LiveSessionState.Failed => AppThemeTokens.Error, _ => AppThemeTokens.Info };
        AppUi.BindForeground(state, token); dashboardState.SetColorToken(token);
        dashboardState.SetKind(current?.State switch { LiveSessionState.WaitingForLive => AppIconKind.History, LiveSessionState.Completed => AppIconKind.Check, LiveSessionState.Failed => AppIconKind.Close, _ => AppIconKind.Live });
        var actionTop = 12;
        foreach (var button in new[] { primary, details, secondary, folder })
        {
            if (!button.Visible) continue;
            button.SetBounds(Width - actionWidth - 12, actionTop, actionWidth, AppTypography.TextHeight(34)); actionTop = button.Bottom + 7;
            DashboardVisual.Icon(button, button == details ? AppIconKind.Info : button == folder ? AppIconKind.Folder : button == secondary ? (current?.IsActive == true || current?.State is LiveSessionState.Pending or LiveSessionState.WaitingForLive ? AppIconKind.Close : AppIconKind.Play) : current?.IsActive == true ? AppIconKind.Pause : current?.State == LiveSessionState.WaitingForLive ? AppIconKind.Settings : AppIconKind.Play);
        }
        Height = Math.Max(Math.Max(thumbnail.Bottom + 12, Math.Max(facts.Bottom, dashboardTiming.Bottom) + 12), actionTop + 5);
    }
}
