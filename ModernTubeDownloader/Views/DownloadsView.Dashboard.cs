using ModernFormsNext;
using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Models;
using ModernTubeDownloader.Theming;

namespace ModernTubeDownloader.Views;

internal sealed partial class DownloadsView
{
    private bool dashboard;
    private bool dashboardDrawer;
    private int dashboardPage;
    private const int DashboardPageSize = 40;
    private Panel? dashboardQueue;
    private FlowLayoutPanel? dashboardChips;
    private DashboardInspector? dashboardInspector;
    private DashboardWidgets? dashboardWidgets;
    private Button? dashboardAdd, dashboardStart, dashboardClear, dashboardDetails, dashboardPrevious, dashboardNext;
    private Label? dashboardPaging, dashboardStatus;
    private Panel? dashboardStatusDot;
    private readonly ContextMenu dashboardMenu = new();
    internal Guid? SelectedItemId => selectedItemId;

    public void SetDashboard(bool value)
    {
        if (dashboard == value) { if (value) UpdateLayout(); return; }
        dashboard = value;
        analyzeButton.Click -= AnalyzeClicked; analyzeButton.Click -= DashboardAddMenu;
        if (value) analyzeButton.Click += DashboardAddMenu; else analyzeButton.Click += AnalyzeClicked;
        if (value)
        {
            EnsureDashboard();
            if (selectedItemId is { } id) { var index = services.Queue.Snapshot().ToList().FindIndex(i => i.Id == id); if (index >= 0) dashboardPage = index / DashboardPageSize; }
        }
        if (dashboardQueue is not null) dashboardQueue.Visible = value;
        if (dashboardStatus is not null) dashboardStatus.Visible = value;
        if (dashboardStatusDot is not null) dashboardStatusDot.Visible = value;
        if (dashboardChips is not null) dashboardChips.Visible = value;
        if (dashboardAdd is not null) dashboardAdd.Visible = value;
        if (dashboardInspector is not null) dashboardInspector.Visible = value;
        if (dashboardWidgets is not null) dashboardWidgets.Visible = value;
        foreach (var control in new Control?[] { dashboardStart, dashboardClear, dashboardDetails, dashboardPrevious, dashboardNext, dashboardPaging }) if (control is not null) control.Visible = value;
        foreach (var card in cards.Values) card.SetDashboard(value);
        urlLabel.Visible = !value; pageTitle.Visible = pageSubtitle.Visible = !value;
        if (!value) { AppUi.BindBackground(queueHost); AppUi.BindBackground(queueToolbar); AppUi.Card(queueEmpty, true); }
        BuildQueueCards(); ApplyLocalization(); UpdateLayout();
    }
    public void SetDashboardStatus(string value, string token) { if (dashboardStatus is not null) { dashboardStatus.Text = value; pasteToolTip.SetToolTip(dashboardStatus, value); AppUi.BindBackground(dashboardStatusDot!, token); } }
    private void EnsureDashboard()
    {
        if (dashboardQueue is not null) return;
        dashboardStatus = AppUi.Muted(); dashboardStatus.AutoEllipsis = true;
        dashboardStatusDot = new Panel { Width = 8, Height = 8 }; dashboardStatusDot.Style.Border.Radius = 4; dashboardStatusDot.Style.Border.Width = 0;
        Controls.AddRange([dashboardStatus, dashboardStatusDot]);
        dashboardQueue = new Panel { AccessibleAutomationId = "DashboardQueuePanel" }; DashboardStyle.Card(dashboardQueue);
        dashboardChips = new FlowLayoutPanel { WrapContents = true }; AppUi.BindBackground(dashboardChips);
        void Chip(string key, Action action)
        {
            var button = new Button { Text = text[key], AccessibleAutomationId = "DashboardChip" + key.Split('.').Last(), Width = 150, Height = 36, Margin = new Padding(0, 0, 10, 0), Tag = key };
            AppUi.Secondary(button); button.Click += (_, _) => action(); dashboardChips.Controls.Add(button);
        }
        Chip("Sources.Link", () => SupportedSourcesRequested?.Invoke(this, EventArgs.Empty));
        Chip("Playlist.Analyze", () => _ = AnalyzeAsync(true));
        dashboardAdd = new Button { AccessibleAutomationId = "DashboardQuickQueue" }; AppUi.Primary(dashboardAdd);
        dashboardAdd.Click += async (_, _) =>
        {
            if (analyzedMetadata is not null) { AddClicked(this, EventArgs.Empty); return; }
            dashboardAdd.Enabled = false;
            try { await AcceptExternalRequestAsync(new(urlInput.Text, true, true, Source: "desktop", Intent: ExternalMediaIntent.QuickQueue)); }
            catch (Exception error) { ShowExternalRequestError(error); }
            finally { if (!disposed) dashboardAdd.Enabled = true; }
        };
        Button Command(string id, Action action)
        {
            var button = new Button { AccessibleAutomationId = id }; AppUi.Secondary(button); button.Click += (_, _) => action(); queueToolbar.Controls.Add(button); return button;
        }
        dashboardStart = Command("DashboardStartAll", () => _ = services.Queue.ResumeAsync());
        dashboardClear = Command("DashboardClearCompleted", () => { foreach (var item in services.Queue.Snapshot().Where(i => i.Status == DownloadStatus.Completed)) services.Queue.Remove(item.Id); });
        dashboardDetails = Command("DashboardToggleInspector", () => { dashboardDrawer = !dashboardDrawer; UpdateLayout(); });
        dashboardPrevious = Command("DashboardPreviousPage", () => { dashboardPage = Math.Max(0, dashboardPage - 1); BuildQueueCards(); });
        dashboardNext = Command("DashboardNextPage", () => { dashboardPage++; BuildQueueCards(); });
        dashboardPaging = AppUi.Muted(); queueToolbar.Controls.Add(dashboardPaging);
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
            var page = index / DashboardPageSize; if (page != dashboardPage) { dashboardPage = page; BuildQueueCards(); }
            RefreshSelection(); ScrollQueueCardIntoView(selectedItemId.Value); e.Handled = true;
        }
        else if (e.KeyCode == Keys.Enter) { dashboardDrawer = true; UpdateLayout(); e.Handled = true; }
        else if (e.KeyCode == Keys.Delete) { RemoveSelected(); e.Handled = true; }
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
        foreach (var chip in dashboardChips!.Controls.OfType<Button>()) if (chip.Tag is string key) chip.Text = text[key];
        pasteButton.Text = text["Dashboard.Paste"]; pasteIcon.Visible = false;
        RefreshDashboardSelection();
    }
    private void LayoutDashboard()
    {
        if (dashboardQueue is null) return;
        AutoScroll = true;
        var origin = DisplayRectangle.Location; const int margin = 26; const int gap = 12;
        var width = Math.Max(560, ClientSize.Width - margin * 2 - (AppTypography.Scale > 1.25 ? 18 : 0));
        var p = AppearancePresentation.Current.ModernDashboardShell;
        var inline = p.Inspector != DashboardInspectorMode.Hidden && width >= AppTypography.TextHeight(1050);
        var inspectorWidth = inline ? Math.Clamp(width / 4, 290, AppTypography.TextHeight(320)) : 0;
        var queueWidth = inline ? width - inspectorWidth - gap : width;
        var inputWidth = inline ? Math.Min(width, queueWidth + 36) : width;
        var row = AppTypography.TextHeight(46); var wideToolbar = queueWidth >= AppTypography.TextHeight(820) && services.Queue.Snapshot().Count <= DashboardPageSize;
        var toolbarRows = wideToolbar ? 1 : 2;
        header.Visible = false; urlLabel.Visible = supportedSourcesButton.Visible = false;
        AppUi.BindBackground(urlPanel); urlPanel.Style.Border.Width = 0;
        urlPanel.SetBounds(margin + origin.X, 38 + origin.Y, inputWidth, row);
        var addWidth = Math.Min(inputWidth / 3, AppTypography.TextHeight(212));
        dashboardAdd!.SetBounds(margin + origin.X + inputWidth - addWidth, urlPanel.Top, addWidth, row);
        analyzeButton.SetBounds(inputWidth - addWidth - 46, 0, 38, row); analyzeButton.Text = "⌄"; analyzeButton.AccessibleName = text["Common.Analyze"];
        pasteToolTip.SetToolTip(analyzeButton, text["Common.Analyze"]);
        pasteButton.SetBounds(analyzeButton.Left - AppTypography.TextHeight(100) - 8, 0, AppTypography.TextHeight(100), row);
        urlInput.SetBounds(0, 0, Math.Max(180, pasteButton.Left - 12), row);
        dashboardChips!.SetBounds(margin + origin.X, urlPanel.Bottom + 12, width, AppTypography.TextHeight(38));
        foreach (var chip in dashboardChips.Controls) { chip.Width = AppTypography.TextHeight(186); chip.Height = AppTypography.TextHeight(36); }
        analysisProgress.Visible = false;
        var previewVisible = previewState != PreviewState.Empty;
        previewPanel.Visible = previewVisible && previewState != PreviewState.PlaylistAnalyzed;
        playlistPreview.Visible = previewState == PreviewState.PlaylistAnalyzed;
        var top = dashboardChips.Bottom + 24;
        // Analysis uses the same complete editor, temporarily expanded above the queue.
        if (previewVisible)
        {
            var previewHeight = AppTypography.TextHeight(GetPreviewHeight(false));
            previewPanel.SetBounds(margin + origin.X, top, width, previewHeight); playlistPreview.SetBounds(previewPanel.Left, top, width, previewHeight);
            var infoLeft = Math.Min(220, width / 4); var infoWidth = width - infoLeft - 28;
            previewPlaceholder.SetBounds(14, 18, infoLeft - 30, (infoLeft - 30) * 9 / 16); previewImage.Bounds = previewPlaceholder.Bounds;
            previewTitle.SetBounds(infoLeft, 10, infoWidth - 32, AppTypography.TextHeight(34)); previewFacts.SetBounds(infoLeft, previewTitle.Bottom + 4, infoWidth, AppTypography.TextHeight(32));
            if (previewState == PreviewState.Analyzed)
            {
                qualityLabel.SetBounds(infoLeft, previewFacts.Bottom + 4, infoWidth / 2 - 8, AppTypography.TextHeight(22)); containerLabel.SetBounds(infoLeft + infoWidth / 2, qualityLabel.Top, infoWidth / 2, qualityLabel.Height);
                quality.SetBounds(infoLeft, qualityLabel.Bottom, infoWidth / 2 - 8, AppTypography.TextHeight(34)); container.SetBounds(containerLabel.Left, quality.Top, infoWidth / 2, quality.Height);
                rangeEditor.SetBounds(infoLeft, quality.Bottom + 8, infoWidth, AppTypography.TextHeight(rangeEditor.IsCustom ? 80 : 34));
                detailsButton.SetBounds(infoLeft, rangeEditor.Bottom + 8, Math.Max(90, infoWidth / 3 - 8), AppTypography.TextHeight(34));
                advancedButton.SetBounds(detailsButton.Right + 8, detailsButton.Top, detailsButton.Width, detailsButton.Height); addButton.SetBounds(advancedButton.Right + 8, detailsButton.Top, detailsButton.Width, detailsButton.Height);
                if (AppTypography.Scale > 1.25)
                {
                    detailsButton.Width = advancedButton.Width = addButton.Width = infoWidth;
                    advancedButton.Left = addButton.Left = infoLeft; advancedButton.Top = detailsButton.Bottom + 8; addButton.Top = advancedButton.Bottom + 8;
                }
                feedback.SetBounds(infoLeft, Math.Max(detailsButton.Bottom, addButton.Bottom) + 6, infoWidth, AppTypography.TextHeight(28)); previewPanel.Height = Math.Max(previewPanel.Height, feedback.Bottom + 12);
            }
            else { feedback.SetBounds(infoLeft, previewFacts.Bottom + 8, infoWidth, AppTypography.TextHeight(38)); analysisProgress.SetBounds(infoLeft, feedback.Bottom + 6, infoWidth, 6); }
            clearPreviewButton.SetBounds(width - 42, 8, 30, 30);
            top = Math.Max(previewPanel.Visible ? previewPanel.Bottom : 0, playlistPreview.Visible ? playlistPreview.Bottom : 0) + gap;
        }
        dashboardStatus!.Visible = dashboardStatusDot!.Visible = inline;
        dashboardStatus.SetBounds(margin + origin.X + inputWidth + 28, urlPanel.Top, Math.Max(0, width - inputWidth - 28), row);
        dashboardStatusDot.SetBounds(dashboardStatus.Left - 16, urlPanel.Top + (row - 8) / 2, 8, 8);
        var showDetails = !inline && p.Inspector != DashboardInspectorMode.Hidden;
        var toolbarHeight = AppTypography.TextHeight(toolbarRows == 1 ? 54 : 96);
        queueToolbar.SetBounds(margin + origin.X + 12, top + 2, queueWidth - 24, toolbarHeight);
        AppUi.BindBackground(queueToolbar, AppThemeTokens.Surface);
        queueTitle.Text = text.Get("Dashboard.QueueTitle", services.Queue.Snapshot().Count);
        AppTypography.Bind(queueTitle, 13, FontStyle.Bold);
        var buttonTop = wideToolbar ? 6 : AppTypography.TextHeight(44); var buttonWidth = Math.Min(AppTypography.TextHeight(146), (queueWidth - 104) / (showDetails ? 4 : 3));
        queueTitle.SetBounds(0, 6, wideToolbar ? Math.Max(160, queueWidth - (showDetails ? 4 : 3) * (buttonWidth + 8) - 64) : queueWidth - 180, AppTypography.TextHeight(30));
        dashboardDetails!.Visible = showDetails;
        dashboardStart!.SetBounds(wideToolbar ? queueTitle.Right + 8 : 0, buttonTop, buttonWidth, AppTypography.TextHeight(34));
        pauseButton.SetBounds(dashboardStart.Right + 8, buttonTop, buttonWidth, dashboardStart.Height);
        dashboardClear!.SetBounds(pauseButton.Right + 8, buttonTop, buttonWidth, dashboardStart.Height);
        dashboardDetails!.SetBounds(dashboardClear.Right + 8, buttonTop, buttonWidth, dashboardStart.Height);
        moreButton.SetBounds(queueWidth - 64, buttonTop, 34, dashboardStart.Height);
        var pages = Math.Max(1, (services.Queue.Snapshot().Count + DashboardPageSize - 1) / DashboardPageSize);
        dashboardPrevious!.Visible = dashboardNext!.Visible = dashboardPaging!.Visible = pages > 1;
        dashboardPrevious.SetBounds(queueWidth - 162, 4, 30, AppTypography.TextHeight(30)); dashboardNext.SetBounds(queueWidth - 62, 4, 30, dashboardPrevious.Height);
        dashboardPaging.SetBounds(dashboardPrevious.Right + 4, 6, 62, dashboardPrevious.Height); dashboardPaging.Text = $"{dashboardPage + 1}/{pages}";
        dashboardPrevious.Enabled = dashboardPage > 0; dashboardNext.Enabled = dashboardPage < pages - 1;
        var listTop = queueToolbar.Bottom + 4;
        if (services.Queue.Storage.State.IsBlocked) { storageWarning.SetBounds(margin + origin.X + 12, listTop, queueWidth - 24, AppTypography.TextHeight(104)); listTop = storageWarning.Bottom + gap; }
        dashboardWidgets!.Visible = p.Widgets != DashboardWidgetsMode.Hidden;
        dashboardWidgets.Width = width; var widgetsHeight = dashboardWidgets.Visible ? dashboardWidgets.Height : 0;
        var queueHeight = Math.Max(AppTypography.TextHeight(250), ClientSize.Height - (top - origin.Y) - Math.Min(widgetsHeight, 160) - 42);
        dashboardQueue.SetBounds(margin + origin.X, top, queueWidth, queueHeight);
        queueArea.SetBounds(margin + origin.X + 8, listTop, queueWidth - 16, Math.Max(100, dashboardQueue.Bottom - listTop - 12));
        dashboardInspector!.Visible = inline || p.Inspector != DashboardInspectorMode.Hidden && (dashboardDrawer || p.Inspector == DashboardInspectorMode.Always);
        if (inline) dashboardInspector.SetBounds(dashboardQueue.Right + gap, top, inspectorWidth, queueHeight);
        else if (dashboardInspector.Visible) dashboardInspector.SetBounds(margin + origin.X, dashboardQueue.Bottom + gap, width, AppTypography.TextHeight(470));
        dashboardWidgets.SetBounds(margin + origin.X, (!inline && dashboardInspector.Visible ? dashboardInspector.Bottom : dashboardQueue.Bottom) + gap, width, widgetsHeight);
        var emptyCenter = Math.Max(70, queueArea.Height / 2);
        queueEmptyIcon.SetBounds((queueArea.Width - 42) / 2, emptyCenter - 72, 42, 42);
        queueEmptyTitle.SetBounds(20, queueEmptyIcon.Bottom + 10, queueArea.Width - 40, AppTypography.TextHeight(34));
        queueEmptyBody.SetBounds(20, queueEmptyTitle.Bottom + 6, queueArea.Width - 40, AppTypography.TextHeight(52));
        queueEmpty.Style.Border.Width = 0; AppUi.BindBackground(queueEmpty, AppThemeTokens.Surface); AppUi.BindBackground(queueHost, AppThemeTokens.Surface);
        ResizeCards(); dashboardQueue.SendToBack();
    }
}
