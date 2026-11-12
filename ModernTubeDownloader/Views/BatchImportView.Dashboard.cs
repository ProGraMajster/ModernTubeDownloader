using ModernFormsNext;
using ModernTubeDownloader.Batch;
using ModernTubeDownloader.Models;
using ModernTubeDownloader.Services;
using ModernTubeDownloader.Theming;

namespace ModernTubeDownloader.Views;

internal sealed partial class BatchImportView
{
    private static readonly string[] SourceActions = ["Browse", "Paste", "Import", "Examples"];
    private static readonly string[] PrimaryActions = ["Add", "Analyze", "Pause", "Cancel", "Export"];
    private static readonly string[] SelectionActions = ["SelectPage", "ClearSelection", "Apply", "Remove"];
    private bool dashboard, dashboardOptionsExpanded;
    private Panel? sourceCard, resultCard, dashboardOptions, dashboardHeader;
    private FlowLayoutPanel? dashboardSelectionActions;
    private Label? sourceTitle, sourceHint, resultTitle, counters, dashboardSubtitle, parserDetails;
    private Label[] dashboardHeadings = [], dashboardOptionLabels = [];
    private Button? dashboardOptionsButton;
    private CheckBox? dashboardThumbnails;
    private bool settingThumbnailPreference;
    private readonly List<DashboardMetric> metrics = [];
    private AppVectorIcon? sourceIcon;
    private Panel? sourceTabs;
    private DashboardDropZone? dropZone;
    private Button? pickFile;
    private Panel? summaryCard;
    private Panel? resultStatusFrame;
    private AppVectorIcon? resultStatusIcon;
    private Label? summaryTitle, summaryFacts;
    private Action? restoreDashboardTypography, restoreDashboardLabels;
    private readonly ToolTip dashboardTips = new();
    private sealed record DashboardBatchRow(CheckBox Selection, Label Title, Label Url, Label Status,
        Label Quality, Button[] Actions, PictureBox? Thumbnail, SourceVectorIcon SourceIcon, AppVectorIcon Placeholder);

    public void SetDashboard(bool value)
    {
        if (dashboard == value) return;
        if (value) { restoreDashboardTypography = AppTypography.CaptureRecipes(this); restoreDashboardLabels = DashboardVisual.CaptureLabels(this); }
        dashboard = value;
        buttons["Paste"].Visible = value;
        range.ResponsiveLayout = value;
        AppTypography.SetDashboardPresentation(this, value);
        if (value && sourceCard is null) CreateDashboardChrome();
        if (value)
        {
            foreach (var card in new[] { sourceCard!, resultCard!, dashboardOptions!, summaryCard!, resultStatusFrame! }) DownloadsVisual.Card(card);
            AppUi.BindGradient(dashboardHeader!, DownloadsVisual.ButtonTop, DownloadsVisual.ButtonEnd);
            DashboardVisual.Flat(dashboardHeader!, true);
            foreach (var metric in metrics)
            {
                DownloadsVisual.Card(metric);
                AppUi.BindGradient(metric, DownloadsVisual.ButtonTop, DownloadsVisual.ButtonEnd);
            }
        }
        foreach (var key in SourceActions) MoveDashboardControl(buttons[key], value ? sourceTabs! : toolbar);
        foreach (var key in SelectionActions) MoveDashboardControl(buttons[key], value ? dashboardSelectionActions! : toolbar);
        foreach (var key in new[] { "Previous", "Next" }) MoveDashboardControl(buttons[key], value ? body : toolbar);
        MoveDashboardControl(overall, value ? summaryCard! : body);
        MoveDashboardControl(downloadProgress, value ? summaryCard! : body);
        MoveDashboardControl(path, value ? sourceCard! : body);
        MoveDashboardControl(status, value ? resultStatusFrame! : body);
        foreach (var control in new Control[] { quality, container, workers, range }) MoveDashboardControl(control, value ? dashboardOptions! : body);
        // Explicit ordering avoids depending on the previous shell's reparenting order.
        foreach (var key in value ? PrimaryActions : buttons.Keys.ToArray())
        {
            if (buttons[key].Parent != toolbar) continue;
            toolbar.Controls.Remove(buttons[key]); toolbar.Controls.Add(buttons[key]);
        }
        foreach (var chrome in new Control?[] { sourceCard, sourceTabs, resultCard, dashboardHeader, dashboardOptionsButton, summaryCard, dashboardSubtitle })
            if (chrome is not null) { chrome.Visible = value; if (!value) chrome.SetBounds(0, 0, 0, 0); }
        if (dashboardOptions is not null) dashboardOptions.Visible = value && dashboardOptionsExpanded;
        buttons["Close"].Visible = !value;
        if (!value)
        {
            DashboardVisual.RestoreTree(this); restoreDashboardTypography?.Invoke(); restoreDashboardLabels?.Invoke();
            heading.Text = services.Localization["Other.Batch.Title"];
            foreach (var button in buttons.Values) { AppUi.Secondary(button); button.Width = 160; button.Height = 38; button.Margin = new Padding(0, 0, 8, 8); }
            AppUi.BindBackground(rows); AppUi.BindForeground(status, AppThemeTokens.TextSecondary);
            dashboardOptions?.SetBounds(0, 0, 0, 0);
            status.Multiline = true;
        }
        signature = ""; Render(); LayoutPage();
    }

    private static void MoveDashboardControl(Control control, Control parent)
    { control.Parent?.Controls.Remove(control); parent.Controls.Add(control); }

    private void CreateDashboardChrome()
    {
        sourceCard = new(); resultCard = new(); dashboardOptions = new(); dashboardHeader = new() { AccessibleAutomationId = "BatchTableHeader" };
        sourceTabs = new Panel(); AppUi.BindBackground(sourceTabs);
        dashboardSubtitle = AppUi.Muted(); dashboardSubtitle.AutoEllipsis = true;
        dashboardSubtitle.AccessibleAutomationId = "BatchSubtitle";
        summaryCard = new Panel { AccessibleAutomationId = "BatchImportSummary" }; DashboardStyle.Card(summaryCard);
        summaryTitle = AppUi.Heading("", 11); summaryFacts = AppUi.Muted(); summaryFacts.Multiline = true;
        summaryFacts.AccessibleAutomationId = "BatchImportSummaryFacts";
        summaryCard.Controls.AddRange([summaryTitle, summaryFacts]);
        dropZone = new DashboardDropZone { AccessibleAutomationId = "BatchDropZone" };
        pickFile = new DashboardButton { AccessibleAutomationId = "BatchPickFile" };
        pickFile.Click += (_, _) => buttons["Browse"].PerformClick();
        foreach (var card in new[] { sourceCard, resultCard, dashboardOptions }) DashboardStyle.Card(card);
        AppUi.BindGradient(dashboardHeader, DownloadsVisual.ButtonTop, DownloadsVisual.ButtonEnd); DashboardVisual.Flat(dashboardHeader, true);
        sourceTitle = AppUi.Heading("", 12); sourceHint = AppUi.Muted(); sourceHint.Multiline = true;
        resultTitle = AppUi.Heading("", 12); counters = AppUi.Muted(); counters.AutoEllipsis = true;
        counters.AccessibleAutomationId = "BatchSourceFileName";
        parserDetails = AppUi.Muted(); parserDetails.AutoEllipsis = true; parserDetails.AccessibleAutomationId = "BatchParserDetails";
        dropZone.Controls.AddRange([sourceTitle, sourceHint, pickFile]); sourceCard.Controls.Add(dropZone); resultCard.Controls.AddRange([resultTitle, counters, parserDetails]);
        resultStatusFrame = new Panel(); DownloadsVisual.Card(resultStatusFrame);
        resultStatusIcon = new AppVectorIcon(AppIconKind.Info, 20, AppThemeTokens.Info)
            { AccessibilityView = ModernFormsNext.Accessibility.AccessibilityView.Hidden };
        resultStatusFrame.Controls.Add(resultStatusIcon); resultCard.Controls.Add(resultStatusFrame);
        sourceIcon = new AppVectorIcon(AppIconKind.Media, 38, AppThemeTokens.TextSecondary); dropZone.Controls.Add(sourceIcon);
        foreach (var token in new[] { AppThemeTokens.Info, AppThemeTokens.Success, AppThemeTokens.Warning, AppThemeTokens.Error })
        {
            var metric = new DashboardMetric(token); DownloadsVisual.Card(metric);
            AppUi.BindGradient(metric, DownloadsVisual.ButtonTop, DownloadsVisual.ButtonEnd);
            metric.Style.Border.Radius = 8;
            if (token is AppThemeTokens.Warning or AppThemeTokens.Error) AppUi.BindForeground(metric.Controls[2], token);
            metrics.Add(metric); resultCard.Controls.Add(metric);
        }
        dashboardOptionsButton = new DashboardButton { AccessibleAutomationId = "BatchOptions", AutoEllipsis = true };
        AppUi.Secondary(dashboardOptionsButton);
        dashboardOptionsButton.Click += (_, _) => { dashboardOptionsExpanded = !dashboardOptionsExpanded; LayoutPage(); };
        dashboardSelectionActions = new() { WrapContents = true }; AppUi.BindBackground(dashboardSelectionActions, AppThemeTokens.Surface);
        foreach (var key in new[] { "Downloads.Quality", "Downloads.Container", "Dashboard.BatchWorkers" })
        { var label = AppUi.Muted(); label.Tag = key; dashboardOptionLabels = [.. dashboardOptionLabels, label]; dashboardOptions.Controls.Add(label); }
        dashboardThumbnails = new CheckBox { AccessibleAutomationId = "BatchShowThumbnails" };
        AppUi.BindForeground(dashboardThumbnails);
        dashboardThumbnails.CheckedChanged += (_, _) =>
        {
            if (settingThumbnailPreference) return;
            var showThumbnails = dashboardThumbnails.Checked;
            _ = RunAsync(async () =>
            {
                var profile = services.Appearance.Profile.Copy(); profile.BatchImport.Thumbnail = showThumbnails;
                await services.Appearance.ApplyProfileAsync(profile); signature = "";
            });
        };
        dashboardOptions.Controls.AddRange([dashboardSelectionActions, dashboardThumbnails]);
        foreach (var key in new[] { "#", "URL", "Dashboard.Title", "Dashboard.Status", "Downloads.Quality", "Dashboard.MoreActions" })
        {
            var label = AppUi.Muted(); label.Tag = key; label.AutoEllipsis = true;
            label.AccessibleAutomationId = "BatchColumn" + new[] { "Selection", "Url", "Title", "Status", "Quality", "Actions" }[dashboardHeadings.Length];
            AppTypography.Bind(label, 9); dashboardHeadings = [.. dashboardHeadings, label]; dashboardHeader.Controls.Add(label);
        }
        body.Controls.AddRange([dashboardSubtitle, sourceTabs, sourceCard, resultCard, dashboardOptions, dashboardOptionsButton, dashboardHeader, summaryCard]);
        // Labels were composed while their cards were detached. Apply their existing
        // typography recipes after attachment so the first frame uses Modern sizes.
        AppTypography.CaptureRecipes(body).Invoke();
        range.ModeChanged += (_, _) => { if (dashboard && dashboardOptionsExpanded) LayoutPage(); };
    }

    private void RefreshDashboardCounters()
    {
        if (!dashboard || counters is null) return;
        var t = services.Localization;
        heading.Text = t["Dashboard.Batch"]; dashboardSubtitle!.Text = t["Dashboard.BatchHint"]; sourceTitle!.Text = t["Dashboard.DropFile"]; sourceHint!.Text = t["Dashboard.DropFormats"];
        pickFile!.Text = t["Batch.Browse"];
        resultTitle!.Text = t["Dashboard.ImportResults"];
        summaryTitle!.Text = t["Dashboard.ImportSummary"];
        dashboardOptionsButton!.Text = t["Dashboard.OptionsShort"];
        dashboardThumbnails!.Text = t["Batch.ShowThumbnails"];
        dashboardThumbnails.Enabled = !busy;
        settingThumbnailPreference = true;
        try { dashboardThumbnails.Checked = services.Appearance.Profile.BatchImport.Thumbnail; }
        finally { settingThumbnailPreference = false; }
        foreach (var label in dashboardHeadings) label.Text = label.Tag is "#" or "URL" ? (string)label.Tag : t[(string)label.Tag!];
        foreach (var label in dashboardOptionLabels) label.Text = t[(string)label.Tag!];
        if (session is not { } s)
        {
            counters.Text = t["Batch.NoFile"];
            summaryFacts!.Text = t.Get("Dashboard.ImportSummaryFacts", 0, 0, 0, 0);
            parserDetails!.Text = $"{t["Batch.Status.Pending"]}: 0   ·   {t["Batch.Status.Analyzing"]}: 0   ·   {t["Batch.Status.Skipped"]}: 0";
            for (var i = 0; i < 4; i++) metrics[i].Present(t[i switch { 0 => "Dashboard.All", 1 => "Batch.ValidLinks", 2 => "Batch.Filter.Duplicates", _ => "Dashboard.Errors" }], 0);
            return;
        }
        lock (s.Sync)
        {
            metrics[0].Present(t["Dashboard.All"], s.ImportedCount);
            metrics[1].Present(t["Batch.ValidLinks"], s.Entries.Count(e => !e.Removed && e.Status != BatchEntryStatus.Duplicate && !string.IsNullOrEmpty(e.Identity)));
            metrics[2].Present(t["Batch.Filter.Duplicates"], s.Entries.Count(e => !e.Removed && e.Status == BatchEntryStatus.Duplicate));
            metrics[3].Present(t["Dashboard.Errors"], s.Entries.Count(e => !e.Removed && e.Status is BatchEntryStatus.Failed or BatchEntryStatus.Unsupported));
            counters.Text = s.FileName;
            parserDetails!.Text = $"{t["Batch.Status.Pending"]}: {s.Entries.Count(e => !e.Removed && e.Status == BatchEntryStatus.Pending)}   ·   {t["Batch.Status.Analyzing"]}: {s.Entries.Count(e => !e.Removed && e.Status == BatchEntryStatus.Analyzing)}   ·   {t["Batch.Status.Skipped"]}: {s.Entries.Count(e => !e.Removed && e.Status == BatchEntryStatus.Skipped)}";
            summaryFacts!.Text = t.Get("Dashboard.ImportSummaryFacts", selection.Count,
                s.Entries.Count(e => !e.Removed && e.Status == BatchEntryStatus.Ready),
                s.Entries.Count(e => !e.Removed && e.QueueItemId is not null),
                s.Entries.Count(e => !e.Removed && e.Status == BatchEntryStatus.Completed));
            dashboardTips.SetToolTip(counters, counters.Text);
        }
    }

    private void RefreshDashboardStatus()
    {
        if (!dashboard) return;
        if (session is null && !busy)
            overall.Text = services.Localization.Get("Batch.Overall", 0, 0, 0, 0,
                0d.ToString("N2", services.Localization.Culture), 0, "—");
        var token = lastErrorKey is null ? AppThemeTokens.Info : AppThemeTokens.Error;
        var icon = lastErrorKey is null && (busy || services.Batch.IsAnalyzing) ? AppIconKind.Retry : AppIconKind.Info;
        if (lastErrorKey is null && !busy && session is { } current && !services.Batch.IsAnalyzing)
        {
            lock (current.Sync)
            {
                var ready = current.Entries.Count(entry => !entry.Removed && entry.Status == BatchEntryStatus.Ready);
                status.Text = services.Localization[ready > 0 ? "Batch.ReviewReady" : "Batch.ReviewPending"];
                token = ready > 0 ? AppThemeTokens.Success : AppThemeTokens.TextSecondary;
                icon = ready > 0 ? AppIconKind.Check : AppIconKind.Info;
            }
        }
        AppUi.BindForeground(status, token);
        resultStatusIcon?.SetKind(icon); resultStatusIcon?.SetColorToken(token);
        if (resultStatusFrame is not null) AppUi.BindBorder(resultStatusFrame, token == AppThemeTokens.TextSecondary ? DownloadsVisual.Border : token);
        dashboardTips.SetToolTip(status, status.Text);
        status.AutoEllipsis = true;
    }

    private void LayoutDashboard()
    {
        if (sourceCard is null) return;
        var origin = body.DisplayRectangle.Location; var width = Math.Max(1, body.DisplayRectangle.Width - 36);
        var line = AppTypography.TextHeight(36); var gap = 10;
        var inline = width >= AppTypography.TextHeight(900);
        heading.SetBounds(18 + origin.X, 12 + origin.Y, width,
            Math.Max(AppTypography.TextHeight(40), DashboardVisual.LabelHeight(heading, width, 1)));
        var left = inline ? width * 42 / 100 - 6 : width;
        var tabsHeight = LayoutDashboardActions(SourceActions.Select(k => buttons[k]), width, 0, 0, line);
        dashboardSubtitle!.SetBounds(heading.Left, heading.Bottom + 2, width, AppTypography.TextHeight(24));
        sourceTabs!.SetBounds(heading.Left, dashboardSubtitle.Bottom + 8, width, tabsHeight);
        sourceCard.SetBounds(18 + origin.X, sourceTabs.Bottom + 10, left, 220);
        dropZone!.SetBounds(10, 10, left - 20, 1);
        AppUi.BindGradient(dropZone, DownloadsVisual.ButtonTop, DownloadsVisual.PanelEnd);
        sourceIcon!.SetIconSize(32); sourceIcon.SetBounds((dropZone.Width - 32) / 2, 10, 32, 32);
        sourceTitle!.TextAlign = sourceHint!.TextAlign = ContentAlignment.MiddleCenter;
        sourceTitle.SetBounds(16, sourceIcon.Bottom + 6, dropZone.Width - 32, AppTypography.TextHeight(24));
        sourceHint.SetBounds(16, sourceTitle.Bottom + 2, dropZone.Width - 32, DashboardVisual.LabelHeight(sourceHint, dropZone.Width - 32, 2));
        DownloadsVisual.Button(pickFile!, true); pickFile!.Enabled = !busy;
        var pickWidth = Math.Min(dropZone.Width - 32, DashboardVisual.ActionWidth(pickFile));
        pickFile.SetBounds((dropZone.Width - pickWidth) / 2, sourceHint.Bottom + 8, pickWidth, line);
        dropZone.Height = pickFile.Bottom + 10;
        path.SetBounds(10, dropZone.Bottom + 8, left - 20, line);
        DashboardVisual.SetPadding(path, new Padding(10, 0, 10, 0));
        dashboardTips.SetToolTip(path, path.Text);
        sourceCard.Height = path.Bottom + 10;
        resultCard!.SetBounds(inline ? sourceCard.Right + 12 : sourceCard.Left, inline ? sourceCard.Top : sourceCard.Bottom + 12,
            inline ? width - left - 12 : width, sourceCard.Height);
        resultTitle!.SetBounds(16, 12, resultCard.Width - 32, AppTypography.TextHeight(28));
        var metricColumns = resultCard.Width >= AppTypography.TextHeight(440) ? 4 : 2;
        var metricWidth = (resultCard.Width - 24 - (metricColumns - 1) * 8) / metricColumns;
        var metricHeight = metrics.Max(metric => metric.DesiredHeight(metricWidth));
        for (var i = 0; i < metrics.Count; i++)
        {
            metrics[i].Style.Border.Radius = 8;
            metrics[i].SetBounds(12 + i % metricColumns * (metricWidth + 8),
                resultTitle.Bottom + 10 + i / metricColumns * (metricHeight + 8), metricWidth, metricHeight);
        }
        counters!.SetBounds(16, metrics[^1].Bottom + 8, resultCard.Width - 32, AppTypography.TextHeight(24));
        parserDetails!.SetBounds(16, counters.Bottom + 2, resultCard.Width - 32, AppTypography.TextHeight(24));
        dashboardTips.SetToolTip(parserDetails, parserDetails.Text);
        AppTypography.Bind(counters, 9);
        status.Multiline = true; AppTypography.Bind(status, 9);
        var statusWidth = Math.Max(1, resultCard.Width - 76);
        status.SetBounds(34, 8, statusWidth, DashboardVisual.LabelHeight(status, statusWidth, lastErrorKey is null ? 2 : 4));
        resultStatusFrame!.SetBounds(12, parserDetails.Bottom + 6, resultCard.Width - 24, status.Bottom + 8);
        resultStatusIcon!.SetBounds(8, (resultStatusFrame.Height - 20) / 2, 20, 20);
        resultCard.Height = Math.Max(inline ? sourceCard.Height : 0, resultStatusFrame.Bottom + 12);
        if (inline) sourceCard.Height = resultCard.Height;

        var top = Math.Max(sourceCard.Bottom, resultCard.Bottom) + 12;
        var toolbarHeight = LayoutDashboardActions(PrimaryActions.Select(k => buttons[k]), width, 0, 0, line);
        DownloadsVisual.Button(dashboardOptionsButton!); AppTypography.Bind(dashboardOptionsButton!, 9.5f);
        var optionsWidth = Math.Min(width / 3, DashboardVisual.ActionWidth(dashboardOptionsButton!, true));
        // FlowLayoutPanel can already have wrapped these controls under its previous
        // allocation. Sum widths, not their transient positions, and include margins.
        var actionsWidth = PrimaryActions.Sum(key => buttons[key].Width + buttons[key].Margin.Horizontal);
        var filterWidth = Math.Min(AppTypography.TextHeight(140), width / 3);
        var combineActions = toolbarHeight == line && actionsWidth + filterWidth + AppTypography.TextHeight(180) + optionsWidth + 3 * gap <= width;
        toolbar.SetBounds(18 + origin.X, top, combineActions ? actionsWidth : width, toolbarHeight);
        if (!combineActions) top = toolbar.Bottom + gap;
        dashboardOptionsButton!.SetBounds(18 + origin.X + width - optionsWidth, top, optionsWidth, line);
        DashboardVisual.Icon(dashboardOptionsButton, AppIconKind.Settings);
        dashboardOptionsButton.AccessibleName = services.Localization["Dashboard.OptionsShort"];
        filter.SetBounds(combineActions ? toolbar.Right + gap : 18 + origin.X, top, filterWidth, line);
        var searchWidth = dashboardOptionsButton.Left - filter.Right - 20;
        if (searchWidth >= AppTypography.TextHeight(180)) search.SetBounds(filter.Right + gap, top, searchWidth, line);
        else { search.SetBounds(18 + origin.X, top + line + gap, width, line); }
        top = Math.Max(search.Bottom, filter.Bottom) + gap;
        dashboardOptions!.Visible = dashboardOptionsExpanded;
        if (dashboardOptionsExpanded)
        {
            dashboardOptions.SetBounds(18 + origin.X, top, width, 100);
            LayoutDashboardOptions(line); top = dashboardOptions.Bottom + gap;
        }
        else dashboardOptions.SetBounds(0, 0, 0, 0); // MFN includes hidden default-layout children in scroll extent.
        dashboardHeader!.SetBounds(18 + origin.X, top, width, AppTypography.TextHeight(34));
        AppUi.BindGradient(rows, DownloadsVisual.Panel, DownloadsVisual.PanelEnd); DashboardVisual.Flat(rows);
        summaryTitle!.SetBounds(16, 12, width - 32, AppTypography.TextHeight(24));
        summaryFacts!.SetBounds(16, summaryTitle.Bottom + 6, width - 32, DashboardVisual.LabelHeight(summaryFacts, width - 32, 3));
        overall.SetBounds(16, summaryFacts.Bottom + 4, width - 32, DashboardVisual.LabelHeight(overall, width - 32, 2));
        downloadProgress.SetBounds(16, overall.Bottom + 4, width - 32, 4);
        var summaryHeight = downloadProgress.Bottom + 12;
        rows.SetBounds(18 + origin.X, dashboardHeader.Bottom, width,
            Math.Max(AppTypography.TextHeight(180), body.ClientSize.Height - (dashboardHeader.Bottom - origin.Y) - line - summaryHeight - 42));
        progress.SetBounds(rows.Left, dashboardHeader.Top - 3, width, 3);
        var next = buttons["Next"]; var previous = buttons["Previous"];
        foreach (var button in new[] { previous, next }) { DashboardVisual.Button(button); button.Margin = Padding.Empty; }
        next.SetBounds(rows.Right - Math.Min(width / 3, DashboardVisual.ActionWidth(next)), rows.Bottom + 10, Math.Min(width / 3, DashboardVisual.ActionWidth(next)), line);
        previous.SetBounds(next.Left - gap - Math.Min(width / 3, DashboardVisual.ActionWidth(previous)), next.Top,
            Math.Min(width / 3, DashboardVisual.ActionWidth(previous)), line);
        pageText.SetBounds(rows.Left, next.Top, Math.Max(80, previous.Left - rows.Left - gap), line);
        summaryCard!.SetBounds(rows.Left, next.Bottom + 10, width, summaryHeight);
        LayoutRows(); LayoutDashboardHeader();
    }

    private int LayoutDashboardActions(IEnumerable<Button> actions, int width, int top, int left, int line)
    {
        var x = left; var y = top;
        foreach (var button in actions)
        {
            DownloadsVisual.Button(button, button == buttons["Add"] || button == buttons["Browse"]);
            AppTypography.Bind(button, 9.5f);
            var icon = button == buttons["Add"] ? AppIconKind.Download : button == buttons["Analyze"] ? AppIconKind.Search :
                button == buttons["Browse"] ? AppIconKind.Folder : button == buttons["Paste"] ? AppIconKind.Clipboard : (AppIconKind?)null;
            var actionWidth = Math.Min(width, DashboardVisual.ActionWidth(button, icon is not null));
            if (x > left && x + actionWidth > left + width) { x = left; y += line + 8; }
            button.SetBounds(x, y, actionWidth, line); button.Margin = new Padding(0, 0, 8, 8);
            if (icon is { } kind) DashboardVisual.Icon(button, kind);
            x += actionWidth + 8;
        }
        return y + line;
    }

    private void LayoutDashboardOptions(int line)
    {
        var width = dashboardOptions!.Width - 28; var fieldWidths = new[] { AppTypography.TextHeight(230), AppTypography.TextHeight(210), AppTypography.TextHeight(176) };
        var fields = new Control[] { quality, container, workers }; var x = 14; var y = 12;
        var captionHeight = AppTypography.TextHeight(26);
        for (var i = 0; i < fields.Length; i++)
        {
            var fieldWidth = Math.Min(width, fieldWidths[i]);
            if (x > 14 && x + fieldWidth > width + 14) { x = 14; y += captionHeight + line + 14; }
            dashboardOptionLabels[i].SetBounds(x, y, fieldWidth, captionHeight);
            fields[i].SetBounds(x, y + captionHeight + 2, fieldWidth, line); x += fieldWidth + 12;
        }
        dashboardThumbnails!.SetBounds(14, y + captionHeight + line + 10, width, AppTypography.TextHeight(28));
        range.SetBounds(14, dashboardThumbnails.Bottom + 10, width, range.Height);
        var actionHeight = LayoutDashboardActions(SelectionActions.Select(k => buttons[k]), width, 0, 0, line);
        dashboardSelectionActions!.SetBounds(14, range.Bottom + 10, width, actionHeight);
        dashboardOptions.Height = dashboardSelectionActions.Bottom + 14;
    }

    private void PrepareDashboardRow(Panel panel, BatchImportEntry entry)
    {
        var c = panel.Controls.ToArray(); var appearance = AppearancePresentation.Current.BatchImport;
        var title = (Label)c[1]; var url = (Label)c[2]; var state = (Label)c[3];
        title.Text = appearance.ShowTitle && !string.IsNullOrWhiteSpace(entry.Title) ? entry.Title : appearance.ShowSource ? entry.Source : "";
        AppTypography.Bind(c[0], 9.5f); DashboardVisual.Flat(c[0]); AppUi.BindBackground(c[0], AppThemeTokens.Surface);
        url.AccessibleAutomationId = "BatchEntryUrl" + entry.Ordinal;
        var qualityText = string.Join(" · ", new[] { appearance.ShowQuality ? services.Localization["Quality." + entry.Quality] : null,
            appearance.ShowContainer ? services.Localization["Container." + entry.Container] : null }.Where(v => !string.IsNullOrEmpty(v)));
        var qualityLabel = AppUi.Muted(qualityText); qualityLabel.Multiline = true; qualityLabel.AccessibleAutomationId = "BatchEntryQuality" + entry.Ordinal;
        state.Text = services.Localization["Batch.Status." + entry.Status];
        AppUi.BindForeground(state, BatchStatusToken(entry.Status)); AppUi.BindForeground(url, AppThemeTokens.Info);
        // Preserve full parser details without turning the status column into a paragraph.
        var detail = state.Text + " · " + qualityText + " " + entry.Range.DisplayText;
        if (appearance.StatusDetails && entry.ErrorKey is { } errorKey) detail += " · " + services.Localization[errorKey];
        dashboardTips.SetToolTip(state, detail); state.AccessibleName = detail;
        dashboardTips.SetToolTip(title, title.Text); dashboardTips.SetToolTip(url, entry.Url); dashboardTips.SetToolTip(qualityLabel, qualityText);
        foreach (var label in new[] { title, url, state, qualityLabel }) { label.AutoEllipsis = true; AppTypography.Bind(label, 9.5f); }
        AppTypography.Bind(title, 9.5f); title.Multiline = false;
        var actions = c.Skip(4).Take(4).Cast<Button>().ToArray();
        for (var i = 0; i < actions.Length; i++)
        {
            actions[i].AccessibleName = actions[i].Text; actions[i].AccessibleAutomationId = "BatchEntry" + new[] { "Analyze", "Add", "Details", "Remove" }[i] + entry.Ordinal;
            dashboardTips.SetToolTip(actions[i], actions[i].Text); actions[i].Text = "";
        }
        var sourceIcon = new SourceVectorIcon(SourceBrandLookup.Resolve(entry.Url).Brand) { AccessibilityView = ModernFormsNext.Accessibility.AccessibilityView.Hidden };
        var placeholder = new AppVectorIcon(AppIconKind.Media, 20, AppThemeTokens.TextSecondary) { AccessibilityView = ModernFormsNext.Accessibility.AccessibilityView.Hidden };
        panel.Controls.AddRange([qualityLabel, sourceIcon, placeholder]);
        panel.Tag = new DashboardBatchRow((CheckBox)c[0], title, url, state, qualityLabel, actions, c.OfType<PictureBox>().FirstOrDefault(), sourceIcon, placeholder);
        DashboardVisual.Flat(panel, true);
        void UpdateSelection() => AppUi.BindGradient(panel, ((CheckBox)c[0]).Checked ? DownloadsVisual.Selected : DownloadsVisual.Panel,
            ((CheckBox)c[0]).Checked ? DownloadsVisual.SelectedEnd : DownloadsVisual.PanelEnd);
        ((CheckBox)c[0]).CheckedChanged += (_, _) => UpdateSelection(); UpdateSelection();
        panel.Margin = new Padding(0, 0, 0, 1);
    }

    internal static string BatchStatusToken(BatchEntryStatus value) => value switch
    {
        BatchEntryStatus.Ready or BatchEntryStatus.Completed => AppThemeTokens.Success,
        BatchEntryStatus.Failed or BatchEntryStatus.Unsupported => AppThemeTokens.Error,
        BatchEntryStatus.Duplicate or BatchEntryStatus.AuthRequired => AppThemeTokens.Warning,
        BatchEntryStatus.Validating or BatchEntryStatus.Analyzing or BatchEntryStatus.AddedToQueue or BatchEntryStatus.Downloading => AppThemeTokens.Info,
        _ => AppThemeTokens.TextSecondary
    };

    private sealed record BatchColumns(bool Compact, int Selection, int Url, int Title, int Status, int Quality, int Actions, int Right);
    private static BatchColumns DashboardColumns(int width)
    {
        var compact = width < AppTypography.TextHeight(990);
        var selection = AppTypography.TextHeight(54);
        var actionWidth = AppTypography.TextHeight(!compact && AppTypography.TextScale == 100 ? 26 : 32);
        var actions = width - 12 - 4 * (actionWidth + 6);
        var quality = actions - AppTypography.TextHeight(170); var status = quality - AppTypography.TextHeight(150);
        var content = status - selection;
        return new(compact, 10, selection, selection + Math.Max(160, content * 43 / 100), status, quality, actions, width - 12);
    }

    private void LayoutDashboardHeader()
    {
        if (dashboardHeader is null) return;
        var columns = DashboardColumns(Math.Max(1, rows.DisplayRectangle.Width - 6)); var line = dashboardHeader.Height;
        var positions = new[] { columns.Selection, columns.Url, columns.Title, columns.Status, columns.Quality, columns.Actions, columns.Right };
        for (var i = 0; i < dashboardHeadings.Length; i++)
        {
            var heading = dashboardHeadings[i]; heading.Visible = !columns.Compact || i is 0 or 2;
            var inset = !columns.Compact ? i == 1 ? 24 : i == 2 ? 48 : 0 : 0;
            heading.SetBounds(columns.Compact && i == 2 ? columns.Url + 76 : positions[i] + inset, 0,
                columns.Compact && i == 2 ? columns.Right - columns.Url - 76 : Math.Max(0, positions[i + 1] - positions[i] - inset - 8), line);
        }
    }

    private void LayoutDashboardRow(Control panel)
    {
        if (panel.Tag is not DashboardBatchRow row) return;
        panel.Width = Math.Max(1, rows.DisplayRectangle.Width - 6);
        var columns = DashboardColumns(panel.Width); var line = AppTypography.TextHeight(24);
        var dense = !columns.Compact && AppTypography.TextScale == 100;
        var action = AppTypography.TextHeight(dense ? 26 : 32);
        panel.Margin = dense ? Padding.Empty : new Padding(0, 0, 0, 1);
        var titleWidth = columns.Compact ? columns.Right - columns.Url - 76 : columns.Status - columns.Title - 48 - 12;
        row.Title.Multiline = columns.Compact;
        row.Quality.Multiline = columns.Compact;
        var titleHeight = DashboardVisual.LabelHeight(row.Title, titleWidth, columns.Compact ? 2 : 1);
        var height = Math.Max(AppTypography.TextHeight(dense ? 32 : 40), action + (dense ? 6 : 8));
        if (dense)
        {
            // Keep native text metrics authoritative if a font/fallback needs more room.
            var textHeight = new[] { row.Title, row.Url, row.Status, row.Quality }
                .Max(label => DashboardVisual.LabelHeight(label, int.MaxValue, 1));
            height = Math.Max(height, textHeight + 8);
        }
        var contentLeft = columns.Url;
        row.Selection.SetBounds(10, 4, columns.Url - 18, Math.Max(action, line));
        row.SourceIcon.Visible = !columns.Compact;
        row.Placeholder.Visible = row.Thumbnail?.Image is null;
        if (!columns.Compact)
        {
            row.SourceIcon.SetBounds(columns.Url, (height - 16) / 2, 16, 16);
            row.Url.SetBounds(columns.Url + 24, 4, columns.Title - columns.Url - 36, height - 8);
            row.Thumbnail?.SetBounds(columns.Title, 6, 40, height - 12);
            row.Placeholder.SetBounds(columns.Title + 10, (height - 20) / 2, 20, 20);
            row.Title.SetBounds(columns.Title + 48, 4, columns.Status - columns.Title - 60, height - 8);
            row.Status.SetBounds(columns.Status, 4, columns.Quality - columns.Status - 12, height - 8);
            row.Quality.SetBounds(columns.Quality, 4, columns.Actions - columns.Quality - 12, height - 8);
            for (var i = 0; i < row.Actions.Length; i++) row.Actions[i].SetBounds(columns.Actions + i * (action + 6), (height - action) / 2, action, action);
        }
        else
        {
            row.Thumbnail?.SetBounds(contentLeft, 8, 68, 44);
            row.Placeholder.SetBounds(contentLeft + 24, 20, 20, 20);
            row.Title.SetBounds(contentLeft + 76, 7, columns.Right - contentLeft - 76, titleHeight);
            row.Url.SetBounds(contentLeft + 76, row.Title.Bottom + 2, columns.Right - contentLeft - 76, line);
            var factsTop = Math.Max(row.Url.Bottom, 52) + 6; var factsWidth = (columns.Right - contentLeft - 12) / 2;
            row.Status.SetBounds(contentLeft, factsTop, factsWidth, line);
            row.Quality.SetBounds(contentLeft + factsWidth + 12, factsTop, factsWidth, DashboardVisual.LabelHeight(row.Quality, factsWidth, 3));
            var actionTop = Math.Max(row.Status.Bottom, row.Quality.Bottom) + 8;
            for (var i = 0; i < row.Actions.Length; i++) row.Actions[i].SetBounds(contentLeft + i * (action + 8), actionTop, action, action);
            height = actionTop + action + 10;
        }
        for (var i = 0; i < row.Actions.Length; i++)
        {
            DashboardVisual.Button(row.Actions[i]);
            DashboardVisual.Icon(row.Actions[i], i switch { 0 => AppIconKind.Search, 1 => AppIconKind.Download, 2 => AppIconKind.More, _ => AppIconKind.Close }, true);
        }
        panel.Height = height;
    }
}
