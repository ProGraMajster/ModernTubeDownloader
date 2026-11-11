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
            activeTab.Click += (_, _) => { showRecent = false; ResetLivePage(); }; recentTab.Click += (_, _) => { showRecent = true; ResetLivePage(); };
            scroll.Controls.AddRange([inspector, widgets, activeTab, recentTab]);
            CreateLiveNavigation();
            scroll.Controls.Remove(sourceLive); liveSources!.Controls.Add(sourceLive);
            sourceLive.Margin = new Padding(0, 0, 8, 6);
            inspector.LiveEditRequested += (_, session) => Edit(session);
            inspector.LiveAnalyzeRequested += async (_, session) => await AcceptUrlAsync(session.SourceUrl);
        }
        foreach (var control in new Control?[] { inspector, widgets, activeTab, recentTab, dashboardList, pasteLive, sourceLive, liveLink })
            if (control is not null) { control.Visible = value; if (!value) control.SetBounds(0, 0, 0, 0); }
        if (!value) title.Visible = message.Visible = true;
        SetLiveNavigationVisible(value);
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
        var expanded = !dashboardSubmitted && analysisPresented;
        if (dashboardSubmitted) foreach (var control in new Control[] { quality, container, fromNow, fromStart, start, vod }) control.Visible = false;
        analysis.SetBounds(18 + origin.X, 26 + origin.Y, width, expanded ? AppTypography.TextHeight(start.Visible ? 332 : 218) : line + 8);
        AppUi.BindBackground(analysis); DashboardVisual.Flat(analysis);
        var stackedInput = center < AppTypography.TextHeight(530);
        url.SetBounds(0, 0, stackedInput ? width : Math.Max(1, center - AppTypography.TextHeight(272) - 24), line);
        DashboardVisual.SetPadding(url, new Padding(42, 0, 12, 0));
        liveLink!.SetBounds(12, (line - 20) / 2, 20, 20); liveLink.BringToFront();
        pasteLive!.Text = text["Dashboard.Paste"]; pasteLive.SetBounds(analysis.Left + (stackedInput ? 0 : url.Right + 12), analysis.Top + (stackedInput ? line + 8 : 0), AppTypography.TextHeight(92), line);
        analyze.SetBounds(stackedInput ? pasteLive.Width + 12 : url.Right + AppTypography.TextHeight(92) + 24,
            stackedInput ? line + 8 : 0, Math.Min(AppTypography.TextHeight(180), width - pasteLive.Width - 12), line);
        DownloadsVisual.Button(pasteLive); DownloadsVisual.Button(analyze, true);
        DashboardVisual.Icon(pasteLive, AppIconKind.Clipboard); DashboardVisual.Icon(analyze, AppIconKind.Search);
        title.Visible = message.Visible = expanded;
        title.SetBounds(0, analyze.Bottom + 18, width - 24, AppTypography.TextHeight(30));
        message.SetBounds(0, title.Bottom + 4, width, DashboardVisual.LabelHeight(message, width, 5));
        quality.SetBounds(0, message.Bottom + 8, Math.Min(260, width / 2), AppTypography.TextHeight(36)); container.SetBounds(quality.Right + 12, quality.Top, 120, quality.Height);
        fromNow.SetBounds(0, quality.Bottom + 8, width, AppTypography.TextHeight(30)); fromStart.SetBounds(0, fromNow.Bottom + 4, width, fromNow.Height);
        start.SetBounds(0, fromStart.Bottom + 8, Math.Min(width, AppTypography.TextHeight(280)), line); vod.SetBounds(0, message.Bottom + 10, Math.Min(width, AppTypography.TextHeight(300)), line);
        analysis.Height = expanded ? (start.Visible ? start.Bottom : vod.Visible ? vod.Bottom : message.Bottom) + 10 : analyze.Bottom + 8;
        var top = LayoutLiveSources(analysis.Left, analysis.Bottom + 12, width) + 12;
        activeTab!.Text = services.Localization["Dashboard.ActiveScheduled"] + $" ({liveProjection.ActiveCount})";
        recentTab!.Text = services.Localization["Live.RecentTab"] + $" ({liveProjection.RecentCount})";
        DashboardVisual.Tab(activeTab, !showRecent); DashboardVisual.Tab(recentTab, showRecent);
        var activeWidth = DashboardVisual.ActionWidth(activeTab); var recentWidth = DashboardVisual.ActionWidth(recentTab);
        var stackedTabs = activeWidth + recentWidth + 10 > center - 24;
        activeTab.SetBounds(30 + origin.X, top + 8, Math.Min(center - 24, activeWidth), line);
        recentTab.SetBounds(stackedTabs ? activeTab.Left : activeTab.Right + 10, stackedTabs ? activeTab.Bottom + 4 : activeTab.Top, Math.Min(center - 24, recentWidth), line);
        var listTop = LayoutLiveTools(30 + origin.X, recentTab.Bottom + 10, center - 24) + 12; var next = listTop;
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
        // The list owns only its rows. A stacked inspector must not enlarge the
        // list background or reserve a full details form when nothing is selected.
        var pagingBottom = LayoutLivePaging(30 + origin.X, next, center - 24) + 12;
        var listBottom = inline ? Math.Max(pagingBottom, ClientSize.Height - AppTypography.TextHeight(164)) : pagingBottom;
        dashboardList!.SetBounds(18 + origin.X, top, center, inline ? Math.Max(AppTypography.TextHeight(240), listBottom - top) : listBottom - top);
        DownloadsVisual.Card(dashboardList);
        dashboardList.SendToBack();
        next = dashboardList.Bottom + 12;
        inspector.Visible = inspectorEnabled;
        var inspectorHeight = inline ? Math.Max(AppTypography.TextHeight(300), scroll.ClientSize.Height - top + origin.Y - 18) : AppTypography.TextHeight(500);
        if (inspectorEnabled)
            inspector.SetBounds(inline ? 18 + origin.X + center + 12 : 18 + origin.X, inline ? top : next, inline ? right : width, inspectorHeight);
        else inspector.SetBounds(0, 0, 0, 0);
        var visibleSessions = liveProjection.Items;
        var selected = visibleSessions.FirstOrDefault(s => s.Id == selectedSession) ?? visibleSessions.FirstOrDefault();
        if (selected is not null) SelectSession(selected);
        else
        {
            inspector.Clear();
            if (inspectorEnabled && !inline) inspector.Height = inspector.EmptyContentHeight;
        }
        if (!inline && inspector.Visible) next = inspector.Bottom + 12;
        widgets!.Visible = AppearancePresentation.Current.ModernDashboardShell.Widgets != DashboardWidgetsMode.Hidden;
        if (widgets.Visible)
            widgets.SetBounds(18 + origin.X, Math.Max(next, dashboardList.Bottom + 12), inline ? center : width, widgets.Height);
        else widgets.SetBounds(0, 0, 0, 0);
    }
}

internal sealed partial class LiveSessionCard
{
    private bool dashboard, selected;
    private Label? dashboardChannel, dashboardTiming;
    private AppVectorIcon? dashboardState;
    private Panel? dashboardThumbnail;
    private AppVectorIcon? dashboardThumbnailIcon;
    private Panel? dashboardStatus;
    private SourceVectorIcon? dashboardSource;
    private readonly ToolTip dashboardTips = new();
    public event EventHandler<LiveSession>? SelectionRequested;
    public void SetSelected(bool value) { if (selected == value) return; selected = value; if (dashboard) LayoutDashboardCard(); }
    public void SetDashboard(bool value)
    {
        dashboard = value;
        thumbnail.RoundedPreview = value;
        TabStop = value; SetControlBehavior(ControlBehaviors.Selectable, value);
        AppTypography.Bind(title, value ? DashboardVisual.ItemTitle : 13, FontStyle.Bold);
        AppTypography.Bind(facts, value ? 9.25f : 10.5f); AppTypography.Bind(state, value ? 9.5f : 10.5f);
        AppTypography.SetDashboardPresentation(this, value);
        if (!value)
        {
            DashboardVisual.RestoreTree(this); AppUi.Primary(primary); AppUi.BindForeground(state, AppThemeTokens.TextSecondary);
            title.AutoEllipsis = false; title.Multiline = false;
            state.AutoEllipsis = false;
            if (dashboardChannel is not null) dashboardChannel.Visible = dashboardTiming!.Visible = dashboardState!.Visible = false;
            if (dashboardThumbnail is not null) dashboardThumbnail.Visible = false;
            if (dashboardStatus is not null) dashboardStatus.Visible = false;
            if (dashboardSource is not null) dashboardSource.Visible = false;
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
            dashboardStatus = new Panel { AccessibilityView = ModernFormsNext.Accessibility.AccessibilityView.Hidden };
            DashboardVisual.Flat(dashboardStatus); dashboardStatus.Style.Border.Radius = 6;
            dashboardSource = new SourceVectorIcon { AccessibilityView = ModernFormsNext.Accessibility.AccessibilityView.Hidden };
            dashboardSource.Click += (_, _) => { if (current is not null) SelectionRequested?.Invoke(this, current); };
            facts.Controls.Add(dashboardSource);
            dashboardThumbnail = new Panel(); AppUi.BindBackground(dashboardThumbnail, AppThemeTokens.SurfaceSecondary); DashboardVisual.Flat(dashboardThumbnail);
            dashboardThumbnailIcon = new AppVectorIcon(AppIconKind.Live, 28) { AccessibilityView = ModernFormsNext.Accessibility.AccessibilityView.Hidden };
            dashboardThumbnail.Controls.Add(dashboardThumbnailIcon); dashboardThumbnail.Click += (_, _) => { if (current is not null) SelectionRequested?.Invoke(this, current); };
            dashboardThumbnailIcon.Click += (_, _) => { if (current is not null) SelectionRequested?.Invoke(this, current); };
            Controls.AddRange([dashboardChannel, dashboardTiming, dashboardState, dashboardThumbnail, dashboardStatus]);
        }
        dashboardChannel.Visible = dashboardTiming!.Visible = dashboardState!.Visible = true;
        var line = AppTypography.TextHeight(24); var narrow = Width < AppTypography.TextHeight(780);
        var stackActions = Width < AppTypography.TextHeight(620);
        var imageWidth = AppearancePresentation.ImageWidth(AppearancePresentation.Current.ModernDashboardShell.Thumbnail, 156);
        thumbnail.Visible = imageWidth > 0 && thumbnail.Image is not null; thumbnail.SetBounds(12, 12, imageWidth, imageWidth * 9 / 16);
        dashboardThumbnail!.Visible = imageWidth > 0 && thumbnail.Image is null; dashboardThumbnail.Bounds = thumbnail.Bounds;
        dashboardThumbnailIcon!.SetBounds((imageWidth - 28) / 2, (thumbnail.Height - 28) / 2, 28, 28);
        var left = imageWidth > 0 ? thumbnail.Right + 16 : 14;
        foreach (var button in new[] { primary, secondary, details, folder }) DownloadsVisual.Button(button, button == primary && current?.CanResume == true);
        var actionWidth = Math.Max(AppTypography.TextHeight(146), new[] { primary, secondary, details, folder }.Where(b => b.Visible).Select(b => DashboardVisual.ActionWidth(b, true)).DefaultIfEmpty(0).Max());
        var statusWidth = narrow ? 0 : AppTypography.TextHeight(180);
        var textWidth = Math.Max(1, Width - left - (stackActions ? 12 : actionWidth + statusWidth + 36));
        DashboardVisual.Flat(this, !selected);
        AppUi.BindGradient(this, selected ? DownloadsVisual.Selected : DownloadsVisual.Panel, selected ? DownloadsVisual.SelectedEnd : DownloadsVisual.PanelEnd);
        Style.Border.Width = selected ? 1 : 0; Style.Border.Radius = selected ? 6 : 0; Style.Border.Color = AppUi.GetColor(AppThemeTokens.Accent).WithAlpha(130);
        title.AutoEllipsis = true; title.Multiline = true;
        title.SetBounds(left, 12, textWidth, DashboardVisual.LabelHeight(title, textWidth, 2));
        AppTypography.Bind(dashboardChannel, 9.5f); dashboardChannel.AutoEllipsis = true;
        dashboardChannel.Text = current?.Channel ?? ""; dashboardChannel.SetBounds(left, title.Bottom + 4, textWidth, line);
        facts.Visible = true;
        if (current is not null)
        {
            var t = services.Localization;
            var appearance = AppearancePresentation.Current.Live;
            var identity = SourceBrandLookup.Resolve(current.SourceUrl);
            var source = appearance.ShowSource ? identity.Name + " · " : "";
            facts.Text = source + t["Quality." + current.QualityPresetId] + " · " + current.Container.ToString().ToUpperInvariant();
            dashboardSource!.Brand = identity.Brand; dashboardSource.Kind = identity.Kind;
            dashboardSource.Visible = appearance.ShowSource;
            DashboardVisual.SetPadding(facts, new Padding(appearance.ShowSource ? 22 : 0, 0, 0, 0));
            var measurements = new List<string>();
            if (appearance.ShowElapsed) measurements.Add(current.RecordedDuration.ToString(@"hh\:mm\:ss"));
            if (appearance.ShowBytes) measurements.Add(DashboardStyle.Bytes(current.BytesWritten));
            if (appearance.ShowParts && current.Parts.Count > 0) measurements.Add(t.Get("Live.PartCount", current.Parts.Count));
            if (appearance.ShowReconnectCount && current.ResumeCount > 0) measurements.Add(t["Live.ReconnectCount"] + ": " + current.ResumeCount);
            dashboardTiming.Text = current.State == LiveSessionState.WaitingForLive && current.ScheduledStartAt is { } scheduled
                ? scheduled.ToLocalTime().ToString("g", t.Culture)
                : string.Join(" · ", measurements);
            if (current.FailureMessageKey is { } failure) dashboardTiming.Text = t[failure];
            else if ((current.State == LiveSessionState.WaitingForLive ? current.NextCheckAt : current.RetryAt) is { } next)
                dashboardTiming.Text += "\n" + t.Get("Live.NextCheck", Math.Max(0, (int)(next - DateTimeOffset.UtcNow).TotalSeconds));
        }
        facts.Multiline = true;
        facts.SetBounds(left, dashboardChannel.Bottom + 4, textWidth, DashboardVisual.LabelHeight(facts, textWidth, 2));
        dashboardSource!.SetBounds(0, Math.Max(0, (Math.Min(line, facts.Height) - 16) / 2), 16, 16);
        var statusLeft = narrow ? left : left + textWidth + 12;
        var statusTop = narrow ? facts.Bottom + 4 : 12;
        var availableStatus = narrow ? textWidth : statusWidth;
        var pillWidth = Math.Min(availableStatus, DashboardVisual.TextWidth(state, state.Text) + 42);
        dashboardStatus!.Visible = true; dashboardStatus.SetBounds(statusLeft, statusTop, pillWidth, line);
        dashboardStatus.SendToBack();
        dashboardState.SetBounds(statusLeft + 8, statusTop + (line - 16) / 2, 16, 16);
        state.SetBounds(statusLeft + 30, statusTop, Math.Max(1, pillWidth - 38), line);
        state.AutoEllipsis = true; dashboardTips.SetToolTip(state, state.Text);
        dashboardTiming.Multiline = true; AppTypography.Bind(dashboardTiming, 9.25f);
        dashboardTiming.SetBounds(statusLeft, state.Bottom + 8, availableStatus, DashboardVisual.LabelHeight(dashboardTiming, availableStatus, 6));
        var token = current?.State switch { LiveSessionState.Recording or LiveSessionState.Completed => AppThemeTokens.Success, LiveSessionState.Reconnecting or LiveSessionState.WaitingForLive => AppThemeTokens.Warning, LiveSessionState.Failed => AppThemeTokens.Error, _ => AppThemeTokens.Info };
        AppUi.BindForeground(state, token); dashboardState.SetColorToken(token);
        dashboardStatus.Style.BackgroundColor = AppUi.GetColor(token).WithAlpha(24);
        dashboardStatus.Style.Border.Color = AppUi.GetColor(token).WithAlpha(55); dashboardStatus.Style.Border.Width = 1;
        dashboardState.SetKind(current?.State switch { LiveSessionState.WaitingForLive => AppIconKind.History, LiveSessionState.Completed => AppIconKind.Check, LiveSessionState.Failed => AppIconKind.Close, _ => AppIconKind.Live });
        var actionTop = stackActions ? Math.Max(thumbnail.Bottom, dashboardTiming.Bottom) + 12 : 12;
        if (stackActions) actionWidth = Math.Max(1, Width - left - 12);
        var actionLeft = stackActions ? left : Width - actionWidth - 12; var actionHeight = AppTypography.TextHeight(34);
        if (primary.Visible)
        {
            primary.SetBounds(actionLeft, actionTop, actionWidth, actionHeight); actionTop = primary.Bottom + 7;
            DashboardVisual.Icon(primary, current?.IsActive == true ? AppIconKind.Stop : current?.State == LiveSessionState.WaitingForLive ? AppIconKind.Settings : AppIconKind.Play);
        }
        details.AccessibleName = services.Localization["Common.Details"]; details.Text = "";
        dashboardTips.SetToolTip(details, details.AccessibleName);
        details.SetBounds(actionLeft + actionWidth - 36, actionTop, 36, actionHeight); DashboardVisual.Icon(details, AppIconKind.Info);
        if (secondary.Visible)
        {
            secondary.SetBounds(actionLeft, actionTop, actionWidth - 42, actionHeight);
            // Text retains its native action semantics; the adjacent square opens details.
            DashboardVisual.RestoreTree(secondary); DownloadsVisual.Button(secondary);
        }
        actionTop += actionHeight + 7;
        if (folder.Visible)
        {
            folder.SetBounds(actionLeft, actionTop, actionWidth, actionHeight); actionTop = folder.Bottom + 7;
            DashboardVisual.Icon(folder, AppIconKind.Folder);
        }
        Height = Math.Max(Math.Max(thumbnail.Bottom + 12, Math.Max(facts.Bottom, dashboardTiming.Bottom) + 12), actionTop + 5);
    }
}
