using ModernFormsNext;
using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Models;
using ModernTubeDownloader.Theming;

namespace ModernTubeDownloader.Views;

internal sealed partial class DownloadsView
{
    private bool dashboard;
    private bool dashboardDrawer;
    private bool arrangingDashboard;
    private int dashboardPage;
    private System.Drawing.Rectangle classicAnalysisProgressBounds;
    private const int DashboardPageSize = 40;
    private Panel? dashboardQueue;
    private FlowLayoutPanel? dashboardChips;
    private DashboardInspector? dashboardInspector;
    private DashboardWidgets? dashboardWidgets;
    private Button? dashboardAdd, dashboardStart, dashboardClear, dashboardDetails, dashboardPrevious, dashboardNext;
    private Label? dashboardPaging, dashboardStatus;
    private Panel? dashboardStatusDot;
    private AppVectorIcon? dashboardLink;
    private readonly DownloadsContextMenu dashboardMenu = new();
    internal Guid? SelectedItemId => selectedItemId;

    public void SetDashboard(bool value)
    {
        if (rangeEditor.ResponsiveLayout != value) rangeEditor.ResponsiveLayout = value;
        rangeHint.Multiline = value;
        if (dashboard == value)
        {
            if (value && projectedGrouping != AppearancePresentation.Current.ModernDashboardShell.GroupPlaylists) BuildQueueCards();
            else if (value) UpdateLayout();
            return;
        }
        if (value) classicAnalysisProgressBounds = analysisProgress.Bounds;
        dashboard = value;
        playlistPreview.SetDashboard(value);
        ((DashboardButton)analyzeButton).JoinedEdge = value ? 2 : 0;
        analyzeButton.Click -= AnalyzeClicked; analyzeButton.Click -= DashboardAddMenu;
        if (value) analyzeButton.Click += DashboardAddMenu; else analyzeButton.Click += AnalyzeClicked;
        if (value)
        {
            EnsureDashboard();
        }
        if (dashboardQueue is not null) dashboardQueue.Visible = value;
        if (dashboardStatus is not null) dashboardStatus.Visible = value;
        if (dashboardStatusDot is not null) dashboardStatusDot.Visible = value;
        if (dashboardChips is not null) dashboardChips.Visible = value;
        if (dashboardAdd is not null) dashboardAdd.Visible = value;
        if (dashboardInspector is not null) dashboardInspector.Visible = value;
        if (dashboardWidgets is not null) dashboardWidgets.Visible = value;
        foreach (var control in new Control?[] { dashboardStart, dashboardClear, dashboardDetails, dashboardPrevious, dashboardNext, dashboardPaging }) if (control is not null) control.Visible = value;
        if (dashboardLink is not null) dashboardLink.Visible = value;
        urlLabel.Visible = !value; pageTitle.Visible = pageSubtitle.Visible = !value;
        if (!value)
        {
            AppUi.Card(previewPanel); clearPreviewButton.Text = "×";
            foreach (var control in new Control?[] { dashboardQueue, dashboardChips, dashboardAdd, dashboardInspector, dashboardWidgets, dashboardStatus, dashboardStatusDot, dashboardStart, dashboardClear, dashboardDetails, dashboardPrevious, dashboardNext, dashboardPaging, dashboardLink })
                control?.SetBounds(0, 0, 0, 0);
            AppUi.BindBackground(queueHost); AppUi.BindBackground(queueToolbar); AppUi.Card(queueEmpty, true); AppTypography.Bind(queueTitle, 16.5f, FontStyle.Bold);
        }
        RebuildQueuePresentation(); ApplyLocalization(); UpdateLayout();
        if (!value) analysisProgress.Bounds = classicAnalysisProgressBounds;
    }
    private void RebuildQueuePresentation()
    {
        // The rows are replaced on a shell change. Prepare the same queue host off-tree so
        // native accessibility sees the completed collection instead of every intermediate
        // removal and layout of the old/new rows. Ordinary queue updates retain their path.
        var parent = queueHost.Parent;
        if (parent is null) { BuildQueueCards(dashboard ? null : selectedItemId, rebuildPresentation: true); return; }
        var index = parent.Controls.GetChildIndex(queueHost);
        parent.SuspendLayout();
        try
        {
            parent.Controls.Remove(queueHost);
            BuildQueueCards(dashboard ? null : selectedItemId, rebuildPresentation: true);
        }
        finally
        {
            parent.Controls.Add(queueHost);
            parent.Controls.SetChildIndex(queueHost, Math.Min(index, parent.Controls.Count - 1));
            if (queueHost.Visible) queueHost.BringToFront();
            parent.ResumeLayout(true);
        }
    }
    public void SetDashboardStatus(string value, string token)
    {
        if (dashboardStatus is null) return;
        dashboardStatus.Text = value; pasteToolTip.SetToolTip(dashboardStatus, value); AppUi.BindBackground(dashboardStatusDot!, token);
        if (dashboardStatus.Visible) dashboardStatusDot!.Left = dashboardStatus.Right - Math.Min(dashboardStatus.Width, DashboardVisual.TextWidth(dashboardStatus, value)) - 18;
    }
    private void EnsureDashboard()
    {
        playlistPreview.SetDashboard(true);
        // Also runs for direct Modern construction, before SetDashboard(true)'s
        // same-mode early return. Geometry and Invoke must match a shell switch.
        ((DashboardButton)analyzeButton).JoinedEdge = 2;
        analyzeButton.Click -= AnalyzeClicked; analyzeButton.Click -= DashboardAddMenu;
        analyzeButton.Click += DashboardAddMenu;
        if (dashboardQueue is not null) return;
        dashboardMenu.ExpansionChanged = expanded => ((DashboardButton)analyzeButton).Expanded = expanded;
        dashboardLink = new AppVectorIcon(AppIconKind.Link, 20); dashboardLink.AccessibilityView = ModernFormsNext.Accessibility.AccessibilityView.Hidden; urlPanel.Controls.Add(dashboardLink);
        dashboardStatus = AppUi.Muted(); dashboardStatus.AutoEllipsis = true;
        dashboardStatusDot = new Panel { Width = 8, Height = 8 }; dashboardStatusDot.Style.Border.Radius = 4; dashboardStatusDot.Style.Border.Width = 0;
        Controls.AddRange([dashboardStatus, dashboardStatusDot]);
        dashboardQueue = new Panel { AccessibleAutomationId = "DashboardQueuePanel" }; DashboardStyle.Card(dashboardQueue);
        dashboardChips = new FlowLayoutPanel { WrapContents = true }; AppUi.BindBackground(dashboardChips);
        void Chip(string key, Action action)
        {
            var button = new DashboardButton { Text = text[key], AccessibleAutomationId = "DashboardChip" + key.Split('.').Last(), Width = 150, Height = 36, Margin = new Padding(0, 0, 10, 0), Tag = key };
            AppUi.Secondary(button); button.Click += (_, _) => action(); dashboardChips.Controls.Add(button);
        }
        Chip("Dashboard.Source.Video", () => _ = AnalyzeAsync());
        Chip("Playlist.Analyze", () => _ = AnalyzeAsync(true));
        Chip("Dashboard.Source.Channel", () => _ = AnalyzeAsync(true));
        Chip("Dashboard.Source.Shorts", () => _ = AnalyzeAsync());
        Chip("Sources.Link", () => SupportedSourcesRequested?.Invoke(this, EventArgs.Empty));
        dashboardAdd = new DashboardButton { AccessibleAutomationId = "DashboardQuickQueue", JoinedEdge = 1 }; AppUi.Primary(dashboardAdd);
        dashboardAdd.Click += async (_, _) =>
        {
            if (analyzedMetadata is not null) { AddClicked(this, EventArgs.Empty); return; }
            dashboardAdd.Enabled = false;
            try { await AcceptExternalRequestAsync(new(urlInput.Text, true, true, Source: "desktop", Intent: ExternalMediaIntent.QuickQueue)); }
            catch (Exception error) { services.Logger.Error("Dashboard quick queue failed.", error); ShowExternalRequestError(error); }
            finally { if (!disposed) dashboardAdd.Enabled = true; }
        };
        Button Command(string id, Action action)
        {
            var button = new DashboardButton { AccessibleAutomationId = id }; AppUi.Secondary(button); button.Click += (_, _) => action(); queueToolbar.Controls.Add(button); return button;
        }
        dashboardStart = Command("DashboardStartAll", () => _ = services.Queue.ResumeAsync());
        dashboardClear = Command("DashboardClearCompleted", () => { foreach (var item in services.Queue.Snapshot().Where(i => i.Status == DownloadStatus.Completed)) services.Queue.Remove(item.Id); });
        dashboardDetails = Command("DashboardToggleInspector", () => { dashboardDrawer = !dashboardDrawer; UpdateLayout(); });
        dashboardPrevious = Command("DashboardPreviousPage", () => { dashboardPage = Math.Max(0, dashboardPage - 1); BuildQueueCards(); });
        dashboardNext = Command("DashboardNextPage", () => { dashboardPage++; BuildQueueCards(); });
        dashboardPaging = AppUi.Muted(); queueToolbar.Controls.Add(dashboardPaging);
        foreach (var paging in new Control[] { dashboardPrevious, dashboardNext, dashboardPaging })
        { queueToolbar.Controls.Remove(paging); dashboardQueue.Controls.Add(paging); }
        dashboardInspector = new DashboardInspector(services);
        dashboardWidgets = new DashboardWidgets(services) { SelectedPath = () => selectedItemId is { } id ? services.Queue.Find(id)?.FinalFile : null };
        Controls.AddRange([dashboardQueue, dashboardChips, dashboardAdd, dashboardInspector, dashboardWidgets]);
        dashboardQueue.SendToBack(); queueHost.KeyDown += DashboardKeys;
        queueHost.TabStop = true;
    }
    private void DashboardAddMenu(object? sender, EventArgs e)
    {
        dashboardMenu.Items.Clear();
        dashboardMenu.Items.Add(text["Common.Analyze"], null, (_, _) => _ = AnalyzeAsync());
        dashboardMenu.Items.Add(text["Dashboard.AddWithOptions"], null, async (_, _) => { await AnalyzeAsync(); if (analyzedMetadata is not null) AdvancedClicked(this, EventArgs.Empty); });
        dashboardMenu.Show(analyzeButton, analyzeButton.PointToScreen(new System.Drawing.Point(0, analyzeButton.Height + 4)));
    }
    private void DashboardKeys(object? sender, KeyEventArgs e)
    {
        if (!dashboard) return;
        var items = services.Queue.Snapshot(); var index = selectedItemId is { } id ? items.ToList().FindIndex(i => i.Id == id) : -1;
        if (e.KeyCode is Keys.Up or Keys.Down && items.Count > 0)
        {
            index = Math.Clamp(index + (e.KeyCode == Keys.Down ? 1 : -1), 0, items.Count - 1); selectedItemId = items[index].Id;
            BuildQueueCards(selectedItemId);
            RefreshSelection(); ScrollQueueCardIntoView(selectedItemId.Value); e.Handled = true;
        }
        else if (e.KeyCode == Keys.Enter) { dashboardDrawer = true; UpdateLayout(); e.Handled = true; }
        else if (e.KeyCode == Keys.Delete) { RemoveSelected(); e.Handled = true; }
    }
    private sealed class DownloadsContextMenu : ContextMenu
    {
        public Action<bool>? ExpansionChanged { get; set; }
        public override void Show(Control parent, System.Drawing.Point location) { base.Show(parent, location); ExpansionChanged?.Invoke(Visible); }
        protected override void OnParentVisibleChanged(EventArgs e) { base.OnParentVisibleChanged(e); ExpansionChanged?.Invoke(Visible); }
    }
    private void RefreshDashboardSelection()
    {
        if (!dashboard || dashboardInspector is null) return;
        if (selectedItemId is { } id && services.Queue.Find(id) is { } item) dashboardInspector.ShowQueue(item);
        else dashboardInspector.Clear();
    }
    private void LocalizeDashboard()
    {
        if (!dashboard || dashboardStart is null) return;
        dashboardAdd!.Text = text["Downloads.AddToQueue"]; dashboardStart.Text = text["Dashboard.StartAll"];
        dashboardClear!.Text = text["Dashboard.ClearCompleted"]; dashboardDetails!.Text = text["Common.Details"];
        dashboardPrevious!.Text = "‹"; dashboardPrevious.AccessibleName = text["Batch.Previous"];
        dashboardNext!.Text = "›"; dashboardNext.AccessibleName = text["Batch.Next"];
        pasteToolTip.SetToolTip(dashboardPrevious, text["Batch.Previous"]); pasteToolTip.SetToolTip(dashboardNext, text["Batch.Next"]);
        foreach (var chip in dashboardChips!.Controls.OfType<Button>()) if (chip.Tag is string key)
            chip.Text = text[key switch { "Playlist.Analyze" => "Dashboard.Source.Playlist", "Sources.Link" => "Dashboard.Source.Other", _ => key }];
        pasteButton.Text = text["Dashboard.Paste"]; pasteIcon.Visible = false;
        RefreshDashboardSelection();
    }
    private void LayoutDashboard()
    {
        if (arrangingDashboard) return;
        arrangingDashboard = true;
        try
        {
            // Closing a tall playlist removes the outer scroll rail. Reclaim its
            // viewport width before painting the queue and inspector again.
            for (var pass = 0; pass < 3; pass++)
            {
                var viewport = DisplayRectangle;
                SuspendLayout();
                try { ArrangeDashboard(); }
                finally { ResumeLayout(true); }
                if (DisplayRectangle == viewport) break;
            }
        }
        finally { arrangingDashboard = false; }
    }
    private void ArrangeDashboard()
    {
        if (dashboardQueue is null) return;
        AutoScroll = true;
        AppUi.BindGradient(this, DownloadsVisual.Background, DownloadsVisual.BackgroundEnd);
        DownloadsVisual.Card(dashboardQueue);
        var origin = DisplayRectangle.Location; const int margin = 18; const int gap = 14;
        var width = Math.Max(0, DisplayRectangle.Width - margin * 2);
        var p = AppearancePresentation.Current.ModernDashboardShell;
        var inline = p.Inspector != DashboardInspectorMode.Hidden && width >= AppTypography.TextHeight(1080);
        var inspectorWidth = inline ? AppTypography.TextHeight(288) : 0;
        var queueWidth = inline ? width - inspectorWidth - gap : width;
        var inputWidth = inline ? Math.Min(width - 7, queueWidth + 30) : width;
        var row = AppTypography.TextHeight(46); var wideToolbar = queueWidth >= AppTypography.TextHeight(860);
        var hasPages = DashboardPageCount > 1;
        var toolbarRows = wideToolbar ? 1 : 2;
        header.Visible = false; urlLabel.Visible = supportedSourcesButton.Visible = false;
        header.SetBounds(0, 0, 0, 0);
        AppUi.BindGradient(urlPanel, DownloadsVisual.Background, DownloadsVisual.BackgroundEnd); urlPanel.Style.Border.Width = 0;
        var inputLeft = margin + origin.X + (inline ? 7 : 0);
        urlPanel.SetBounds(inputLeft, 38 + origin.Y, inputWidth, row);
        var addWidth = Math.Min(inputWidth / 3, AppTypography.TextHeight(214));
        var dropWidth = AppTypography.TextHeight(38);
        dashboardAdd!.SetBounds(inputLeft + inputWidth - addWidth, urlPanel.Top, addWidth - dropWidth, row);
        analyzeButton.SetBounds(inputWidth - dropWidth, 0, dropWidth, row); analyzeButton.Text = "⌄"; analyzeButton.AccessibleName = text["Common.Analyze"];
        pasteToolTip.SetToolTip(analyzeButton, text["Common.Analyze"]);
        pasteButton.SetBounds(inputWidth - addWidth - AppTypography.TextHeight(102) - 12, 0, AppTypography.TextHeight(102), row);
        urlInput.SetBounds(0, 0, Math.Max(180, pasteButton.Left - 12), row); DashboardVisual.SetPadding(urlInput, new Padding(42, 0, 12, 0)); dashboardLink!.SetBounds(12, (row - 20) / 2, 20, 20); dashboardLink.BringToFront();
        var chipRowHeight = AppTypography.TextHeight(38); var chipRows = 1; var chipX = 0;
        foreach (var chip in dashboardChips!.Controls.OfType<Button>())
        {
            DashboardVisual.Button(chip);
            chip.Width = Math.Min(width, DashboardVisual.ActionWidth(chip, withIcon: true)); chip.Height = chipRowHeight;
            if (chipX > 0 && chipX + chip.Width > width) { chipRows++; chipX = 0; }
            chipX += chip.Width + chip.Margin.Horizontal;
        }
        dashboardChips.SetBounds(margin + origin.X + 7, urlPanel.Bottom + 12, width - 7, chipRows * (chipRowHeight + 4));
        analysisProgress.Visible = false;
        var previewVisible = previewState != PreviewState.Empty;
        previewPanel.Visible = previewVisible && previewState != PreviewState.PlaylistAnalyzed;
        playlistPreview.Visible = previewState == PreviewState.PlaylistAnalyzed;
        var top = dashboardChips.Bottom + 19;
        if (playlistPreview.Visible)
        {
            playlistPreview.SetBounds(margin + origin.X, top, width, playlistPreview.Height);
            playlistPreview.Height = playlistPreview.DashboardHeight;
            top = playlistPreview.Bottom + gap;
        }
        else if (previewPanel.Visible)
        {
            // Measure the visible editor instead of borrowing Classic's fixed card height.
            previewPanel.SetBounds(margin + origin.X, top, width, previewPanel.Height);
            DashboardStyle.Card(previewPanel);
            var imageWidth = Math.Min(176, width / 5); var infoLeft = imageWidth + 32; var infoWidth = Math.Max(0, width - infoLeft - 16);
            previewPlaceholder.SetBounds(16, 16, imageWidth, imageWidth * 9 / 16); previewImage.Bounds = previewPlaceholder.Bounds;
            previewIcon.SetBounds((imageWidth - previewIcon.Width) / 2, (previewPlaceholder.Height - previewIcon.Height) / 2, previewIcon.Width, previewIcon.Height);
            sourceSummary.SetBounds(16, previewPlaceholder.Bottom + 8, imageWidth, DashboardVisual.LabelHeight(sourceSummary, imageWidth, 3));
            previewTitle.SetBounds(infoLeft, 14, Math.Max(0, infoWidth - 38), DashboardVisual.LabelHeight(previewTitle, Math.Max(1, infoWidth - 38), 3));
            previewFacts.SetBounds(infoLeft, previewTitle.Bottom + 4, infoWidth, DashboardVisual.LabelHeight(previewFacts, infoWidth, 2));
            var bottom = previewFacts.Bottom;
            if (previewState == PreviewState.Analyzed)
            {
                var paired = infoWidth >= AppTypography.TextHeight(480); var fieldWidth = paired ? (infoWidth - 12) / 2 : infoWidth;
                var labelHeight = AppTypography.TextHeight(24); var fieldHeight = AppTypography.TextHeight(34);
                qualityLabel.SetBounds(infoLeft, bottom + 10, fieldWidth, labelHeight); quality.SetBounds(infoLeft, qualityLabel.Bottom, fieldWidth, fieldHeight);
                containerLabel.SetBounds(paired ? quality.Right + 12 : infoLeft, paired ? qualityLabel.Top : quality.Bottom + 8, fieldWidth, labelHeight);
                container.SetBounds(containerLabel.Left, containerLabel.Bottom, fieldWidth, fieldHeight);
                rangeEditor.SetBounds(infoLeft, container.Bottom + 10, infoWidth, rangeEditor.Height);
                rangeHint.Visible = rangeEditor.IsCustom; rangeHint.Multiline = true;
                if (rangeHint.Visible) rangeHint.SetBounds(infoLeft, rangeEditor.Bottom + 6, infoWidth, DashboardVisual.LabelHeight(rangeHint, infoWidth, 3));
                else rangeHint.SetBounds(0, 0, 0, 0);
                var actionTop = (rangeHint.Visible ? rangeHint.Bottom : rangeEditor.Bottom) + 10;
                foreach (var button in new[] { detailsButton, advancedButton, addButton }) DashboardVisual.Button(button, button == addButton);
                var actionWidth = new[] { detailsButton, advancedButton, addButton }.Max(b => DashboardVisual.ActionWidth(b));
                var stack = actionWidth * 3 + 16 > infoWidth;
                var x = infoLeft;
                foreach (var button in new[] { detailsButton, advancedButton, addButton })
                {
                    button.SetBounds(x, actionTop, stack ? infoWidth : actionWidth, fieldHeight);
                    DashboardVisual.Button(button, button == addButton);
                    if (stack) actionTop = button.Bottom + 8; else x = button.Right + 8;
                }
                bottom = addButton.Bottom;
            }
            feedback.Visible = !string.IsNullOrWhiteSpace(feedback.Text) && previewState is PreviewState.Analyzed or PreviewState.Failed;
            if (feedback.Visible) { feedback.SetBounds(infoLeft, bottom + 6, infoWidth, DashboardVisual.LabelHeight(feedback, infoWidth, 3)); bottom = feedback.Bottom; }
            analysisProgress.Visible = previewState == PreviewState.Analyzing;
            if (analysisProgress.Visible) { analysisProgress.SetBounds(infoLeft, bottom + 10, infoWidth, 6); bottom = analysisProgress.Bottom; }
            clearPreviewButton.SetBounds(width - 42, 8, 30, 30); DashboardVisual.Button(clearPreviewButton); clearPreviewButton.Text = ""; DashboardVisual.Icon(clearPreviewButton, AppIconKind.Close, true);
            foreach (var child in previewPanel.Controls.Where(c => !c.Visible)) child.SetBounds(0, 0, 0, 0);
            previewPanel.Height = Math.Max(bottom, sourceSummary.Visible ? sourceSummary.Bottom : previewPlaceholder.Bottom) + 16;
            top = previewPanel.Bottom + gap;
        }
        // MFN's default scroll canvas includes hidden children. A previous wider Classic
        // layout must not leave inactive preview/header rectangles outside this viewport.
        if (!previewPanel.Visible) previewPanel.SetBounds(0, 0, 0, 0);
        if (!playlistPreview.Visible) playlistPreview.SetBounds(0, 0, 0, 0);
        dashboardStatus!.Visible = dashboardStatusDot!.Visible = inline;
        if (inline)
        {
            dashboardStatus.SetBounds(margin + origin.X + inputWidth + 28, urlPanel.Top - 22, Math.Max(0, width - inputWidth - 28), row);
            dashboardStatus.TextAlign = ContentAlignment.MiddleRight;
            var statusWidth = Math.Min(dashboardStatus.Width, DashboardVisual.TextWidth(dashboardStatus, dashboardStatus.Text));
            dashboardStatusDot.SetBounds(dashboardStatus.Right - statusWidth - 18, dashboardStatus.Top + (row - 8) / 2, 8, 8);
        }
        else { dashboardStatus.SetBounds(0, 0, 0, 0); dashboardStatusDot.SetBounds(0, 0, 0, 0); }
        var showDetails = !inline && p.Inspector != DashboardInspectorMode.Hidden;
        var toolbarBaseHeight = AppTypography.TextHeight(toolbarRows == 1 ? 50 : 96);
        var toolbarHeight = toolbarBaseHeight + (admissionCountdown.Visible ? AppTypography.TextHeight(28) : 0);
        queueToolbar.SetBounds(margin + origin.X + 12, top + 2, queueWidth - 24, toolbarHeight);
        AppUi.BindGradient(queueToolbar, DownloadsVisual.Panel, DownloadsVisual.PanelEnd);
        queueTitle.Text = text.Get("Dashboard.QueueTitle", services.Queue.Snapshot().Count);
        AppTypography.BindSemibold(queueTitle, 13.5f);
        var buttonTop = wideToolbar ? 11 : AppTypography.TextHeight(44);
        var measuredAction = new[] { dashboardStart!, pauseButton, dashboardClear! }.Max(b => DashboardVisual.ActionWidth(b, true));
        var buttonWidth = Math.Min(Math.Max(AppTypography.TextHeight(146), measuredAction), (queueWidth - 104) / (showDetails ? 4 : 3));
        queueTitle.SetBounds(3, wideToolbar ? 11 : 6, wideToolbar ? Math.Max(160, queueWidth - (showDetails ? 4 : 3) * (buttonWidth + 8) - 64) : queueWidth - 180, AppTypography.TextHeight(30));
        dashboardDetails!.Visible = showDetails;
        dashboardStart!.SetBounds(wideToolbar ? queueTitle.Right + 8 : 0, buttonTop, buttonWidth, AppTypography.TextHeight(34));
        pauseButton.SetBounds(dashboardStart.Right + 8, buttonTop, buttonWidth, dashboardStart.Height);
        dashboardClear!.SetBounds(pauseButton.Right + 8, buttonTop, buttonWidth, dashboardStart.Height);
        dashboardDetails!.SetBounds(dashboardClear.Right + 8, buttonTop, buttonWidth, dashboardStart.Height);
        moreButton.SetBounds(queueToolbar.Width - 36, buttonTop, 34, dashboardStart.Height);
        if (wideToolbar)
        {
            var actionRight = moreButton.Left - 10;
            foreach (var button in new[] { dashboardClear, pauseButton, dashboardStart })
            {
                DashboardVisual.Button(button!, button == dashboardStart);
                AppTypography.Bind(button!, 10);
                // Pause changes caption on admission state changes; reserve both measured states.
                var captionWidth = button == pauseButton ? Math.Max(DashboardVisual.TextWidth(button!, text["Queue.Pause"]), DashboardVisual.TextWidth(button!, text["Queue.Resume"])) : DashboardVisual.TextWidth(button!, button!.Text);
                var actionWidth = captionWidth + 48;
                button.SetBounds(actionRight - actionWidth, buttonTop, actionWidth, AppTypography.TextHeight(34));
                actionRight = button.Left - 10;
            }
            queueTitle.Width = Math.Max(120, dashboardStart.Left - 12);
        }
        var pages = DashboardPageCount;
        dashboardPrevious!.Visible = dashboardNext!.Visible = dashboardPaging!.Visible = pages > 1;
        dashboardPrevious.SetBounds(queueWidth - 174, toolbarBaseHeight - 36, 30, AppTypography.TextHeight(30)); dashboardNext.SetBounds(queueWidth - 74, toolbarBaseHeight - 36, 30, dashboardPrevious.Height);
        dashboardPaging.SetBounds(dashboardPrevious.Right + 4, dashboardPrevious.Top + 2, 62, dashboardPrevious.Height); dashboardPaging.Text = $"{dashboardPage + 1}/{pages}";
        dashboardPrevious.Enabled = dashboardPage > 0; dashboardNext.Enabled = dashboardPage < pages - 1;
        admissionCountdown.SetBounds(0, toolbarBaseHeight - 2, queueToolbar.Width, AppTypography.TextHeight(28));
        var listTop = queueToolbar.Bottom + 4;
        if (services.Queue.Storage.State.IsBlocked) { storageWarning.SetBounds(margin + origin.X + 12, listTop, queueWidth - 24, AppTypography.TextHeight(104)); listTop = storageWarning.Bottom + gap; }
        else storageWarning.SetBounds(0, 0, 0, 0);
        dashboardWidgets!.Visible = p.Widgets != DashboardWidgetsMode.Hidden;
        dashboardWidgets.Width = width; var widgetsHeight = dashboardWidgets.Visible ? dashboardWidgets.Height : 0;
        var queueHeight = Math.Max(AppTypography.TextHeight(250), ClientSize.Height - (top - origin.Y) - Math.Min(widgetsHeight, 160) - 24);
        dashboardQueue.SetBounds(margin + origin.X, top, queueWidth, queueHeight);
        var pagingHeight = hasPages ? AppTypography.TextHeight(36) : 0;
        queueArea.SetBounds(margin + origin.X + 8, listTop, queueWidth - 16, Math.Max(100, dashboardQueue.Bottom - listTop - 12 - pagingHeight));
        dashboardPrevious.SetBounds(queueWidth - 162, queueHeight - pagingHeight, 30, AppTypography.TextHeight(28));
        dashboardPaging.SetBounds(dashboardPrevious.Right + 10, dashboardPrevious.Top + 1, 56, dashboardPrevious.Height);
        dashboardNext.SetBounds(queueWidth - 52, dashboardPrevious.Top, 30, dashboardPrevious.Height);
        dashboardInspector!.Visible = inline || p.Inspector != DashboardInspectorMode.Hidden && (dashboardDrawer || p.Inspector == DashboardInspectorMode.Always);
        if (inline) dashboardInspector.SetBounds(dashboardQueue.Right + gap, top, inspectorWidth, queueHeight);
        else if (dashboardInspector.Visible) dashboardInspector.SetBounds(margin + origin.X, dashboardQueue.Bottom + gap, width, AppTypography.TextHeight(470));
        else dashboardInspector.SetBounds(0, 0, 0, 0);
        if (dashboardWidgets.Visible)
            dashboardWidgets.SetBounds(margin + origin.X, (!inline && dashboardInspector.Visible ? dashboardInspector.Bottom : dashboardQueue.Bottom) + gap, width, widgetsHeight);
        else dashboardWidgets.SetBounds(0, 0, 0, 0);
        var emptyCenter = Math.Max(70, queueArea.Height / 2);
        queueEmptyIcon.SetBounds((queueArea.Width - 42) / 2, emptyCenter - 72, 42, 42);
        queueEmptyTitle.SetBounds(20, queueEmptyIcon.Bottom + 10, queueArea.Width - 40, AppTypography.TextHeight(34));
        queueEmptyBody.SetBounds(20, queueEmptyTitle.Bottom + 6, queueArea.Width - 40, AppTypography.TextHeight(52));
        queueEmpty.Style.Border.Width = 0; AppUi.BindGradient(queueEmpty, DownloadsVisual.Panel, DownloadsVisual.PanelEnd); AppUi.BindGradient(queueHost, DownloadsVisual.Panel, DownloadsVisual.PanelEnd);
        AppUi.BindGradient(dashboardChips, DownloadsVisual.Background, DownloadsVisual.BackgroundEnd);
        foreach (var button in new[] { pasteButton, analyzeButton, dashboardAdd, dashboardStart, pauseButton, dashboardClear, dashboardDetails, moreButton, dashboardPrevious, dashboardNext }.Concat(dashboardChips.Controls.OfType<Button>())) DownloadsVisual.Button(button!, button == dashboardAdd || button == analyzeButton || button == dashboardStart);
        DashboardVisual.Icon(pasteButton, AppIconKind.Clipboard); DashboardVisual.Icon(dashboardAdd, AppIconKind.Plus);
        DashboardVisual.Icon(dashboardStart, AppIconKind.Play); DashboardVisual.Icon(pauseButton, services.Queue.IsPaused ? AppIconKind.Play : AppIconKind.Pause);
        DashboardVisual.Icon(dashboardClear, AppIconKind.Trash);
        foreach (var button in new[] { dashboardStart, pauseButton, dashboardClear })
        {
            AppTypography.Bind(button!, 10);
            DashboardVisual.SetPadding(button!, new Padding(32, 0, 8, 0));
        }
        analyzeButton.Text = ""; DashboardVisual.Icon(analyzeButton, AppIconKind.ChevronDown, true);
        // The adjacent native buttons form one split surface; each keeps its own Invoke action.
        foreach (var button in new[] { dashboardAdd, analyzeButton })
            foreach (var style in new[] { button!.Style, button.StyleHover, button.StylePressed, button.StyleFocused, button.StyleDisabled })
                style.Border.Radius = 0;
        moreButton.Text = ""; DashboardVisual.Icon(moreButton, AppIconKind.More, true);
        DownloadsVisual.Source((Button)dashboardChips.Controls[0], Services.SourceBrand.YouTube);
        DownloadsVisual.Source((Button)dashboardChips.Controls[1], Services.SourceBrand.Other, Services.SourceContentKind.Playlist);
        DownloadsVisual.Source((Button)dashboardChips.Controls[2], Services.SourceBrand.Other, Services.SourceContentKind.Channel);
        DownloadsVisual.Source((Button)dashboardChips.Controls[3], Services.SourceBrand.YouTube, Services.SourceContentKind.Shorts);
        DownloadsVisual.Source((Button)dashboardChips.Controls[4], Services.SourceBrand.Other);
        DashboardVisual.Flat(urlPanel); DashboardVisual.Flat(queueHost); DashboardVisual.Flat(queueToolbar);
        ResizeCards(); dashboardQueue.SendToBack();
    }
}
