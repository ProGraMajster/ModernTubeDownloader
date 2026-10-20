using ModernFormsNext;
using ModernTubeDownloader.Batch;
using ModernTubeDownloader.Models;
using ModernTubeDownloader.Theming;

namespace ModernTubeDownloader.Views;

internal sealed partial class BatchImportView
{
    private static readonly string[] SourceActions = ["Browse", "Import", "Examples"];
    private static readonly string[] PrimaryActions = ["Add", "Analyze", "Pause", "Cancel", "Export"];
    private static readonly string[] SelectionActions = ["SelectPage", "ClearSelection", "Apply", "Remove"];
    private bool dashboard, dashboardOptionsExpanded;
    private Panel? sourceCard, resultCard, dashboardOptions, dashboardHeader;
    private FlowLayoutPanel? dashboardSelectionActions;
    private Label? sourceTitle, sourceHint, resultTitle, counters;
    private Label[] dashboardHeadings = [], dashboardOptionLabels = [];
    private Button? dashboardOptionsButton;
    private readonly List<DashboardMetric> metrics = [];
    private AppVectorIcon? sourceIcon;
    private Panel? sourceTabs;
    private DashboardDropZone? dropZone;
    private Button? pickFile;
    private Panel? summaryCard;
    private Label? summaryTitle, summaryFacts;
    private Action? restoreDashboardTypography, restoreDashboardLabels;
    private readonly ToolTip dashboardTips = new();
    private sealed record DashboardBatchRow(CheckBox Selection, Label Title, Label Url, Label Status,
        Label Quality, Button[] Actions, PictureBox? Thumbnail);

    public void SetDashboard(bool value)
    {
        if (dashboard == value) return;
        if (value) { restoreDashboardTypography = AppTypography.CaptureRecipes(this); restoreDashboardLabels = DashboardVisual.CaptureLabels(this); }
        dashboard = value;
        range.ResponsiveLayout = value;
        AppTypography.SetDashboardPresentation(this, value);
        if (value && sourceCard is null) CreateDashboardChrome();
        if (value) foreach (var card in new[] { sourceCard!, resultCard!, dashboardOptions! }) DashboardStyle.Card(card);
        foreach (var key in SourceActions) MoveDashboardControl(buttons[key], value ? sourceTabs! : toolbar);
        foreach (var key in SelectionActions) MoveDashboardControl(buttons[key], value ? dashboardSelectionActions! : toolbar);
        foreach (var key in new[] { "Previous", "Next" }) MoveDashboardControl(buttons[key], value ? body : toolbar);
        MoveDashboardControl(path, value ? sourceCard! : body);
        MoveDashboardControl(status, value ? resultCard! : body);
        foreach (var control in new Control[] { quality, container, workers, range }) MoveDashboardControl(control, value ? dashboardOptions! : body);
        // Explicit ordering avoids depending on the previous shell's reparenting order.
        foreach (var key in value ? PrimaryActions : buttons.Keys.ToArray())
        {
            if (buttons[key].Parent != toolbar) continue;
            toolbar.Controls.Remove(buttons[key]); toolbar.Controls.Add(buttons[key]);
        }
        foreach (var chrome in new Control?[] { sourceCard, sourceTabs, resultCard, dashboardHeader, dashboardOptionsButton, summaryCard })
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
        summaryCard = new Panel { AccessibleAutomationId = "BatchImportSummary" }; DashboardStyle.Card(summaryCard);
        summaryTitle = AppUi.Heading("", 11); summaryFacts = AppUi.Muted(); summaryFacts.Multiline = true;
        summaryFacts.AccessibleAutomationId = "BatchImportSummaryFacts";
        summaryCard.Controls.AddRange([summaryTitle, summaryFacts]);
        dropZone = new DashboardDropZone { AccessibleAutomationId = "BatchDropZone" };
        pickFile = new DashboardButton { AccessibleAutomationId = "BatchPickFile" };
        pickFile.Click += (_, _) => buttons["Browse"].PerformClick();
        foreach (var card in new[] { sourceCard, resultCard, dashboardOptions }) DashboardStyle.Card(card);
        AppUi.BindBackground(dashboardHeader, AppThemeTokens.SurfaceSecondary); DashboardVisual.Flat(dashboardHeader, true);
        sourceTitle = AppUi.Heading("", 12); sourceHint = AppUi.Muted(); sourceHint.Multiline = true;
        resultTitle = AppUi.Heading("", 12); counters = AppUi.Muted(); counters.AutoEllipsis = true;
        dropZone.Controls.AddRange([sourceTitle, sourceHint, pickFile]); sourceCard.Controls.Add(dropZone); resultCard.Controls.AddRange([resultTitle, counters]);
        sourceIcon = new AppVectorIcon(AppIconKind.Media, 38, AppThemeTokens.TextSecondary); dropZone.Controls.Add(sourceIcon);
        foreach (var token in new[] { AppThemeTokens.Info, AppThemeTokens.Success, AppThemeTokens.Warning, AppThemeTokens.Error })
        { var metric = new DashboardMetric(token); metrics.Add(metric); resultCard.Controls.Add(metric); }
        dashboardOptionsButton = new DashboardButton { AccessibleAutomationId = "BatchOptions", AutoEllipsis = true };
        AppUi.Secondary(dashboardOptionsButton);
        dashboardOptionsButton.Click += (_, _) => { dashboardOptionsExpanded = !dashboardOptionsExpanded; LayoutPage(); };
        dashboardSelectionActions = new() { WrapContents = true }; AppUi.BindBackground(dashboardSelectionActions, AppThemeTokens.Surface);
        foreach (var key in new[] { "Downloads.Quality", "Downloads.Container", "Dashboard.BatchWorkers" })
        { var label = AppUi.Muted(); label.Tag = key; dashboardOptionLabels = [.. dashboardOptionLabels, label]; dashboardOptions.Controls.Add(label); }
        dashboardOptions.Controls.Add(dashboardSelectionActions);
        foreach (var key in new[] { "#", "URL", "Dashboard.Title", "Dashboard.Status", "Downloads.Quality", "Dashboard.MoreActions" })
        {
            var label = AppUi.Muted(); label.Tag = key; label.AutoEllipsis = true;
            label.AccessibleAutomationId = "BatchColumn" + new[] { "Selection", "Url", "Title", "Status", "Quality", "Actions" }[dashboardHeadings.Length];
            AppTypography.Bind(label, 9); dashboardHeadings = [.. dashboardHeadings, label]; dashboardHeader.Controls.Add(label);
        }
        body.Controls.AddRange([sourceTabs, sourceCard, resultCard, dashboardOptions, dashboardOptionsButton, dashboardHeader, summaryCard]);
        range.ModeChanged += (_, _) => { if (dashboard && dashboardOptionsExpanded) LayoutPage(); };
    }

    private void RefreshDashboardCounters()
    {
        if (!dashboard || counters is null) return;
        var t = services.Localization;
        heading.Text = t["Dashboard.Batch"]; sourceTitle!.Text = t["Dashboard.DropFile"]; sourceHint!.Text = t["Dashboard.DropFormats"];
        pickFile!.Text = t["Batch.Browse"];
        resultTitle!.Text = t["Dashboard.ImportResults"];
        summaryTitle!.Text = t["Dashboard.ImportSummary"];
        dashboardOptionsButton!.Text = t["Dashboard.OptionsShort"];
        foreach (var label in dashboardHeadings) label.Text = label.Tag is "#" or "URL" ? (string)label.Tag : t[(string)label.Tag!];
        foreach (var label in dashboardOptionLabels) label.Text = t[(string)label.Tag!];
        if (session is not { } s)
        {
            counters.Text = t["Batch.Empty"];
            summaryFacts!.Text = t["Batch.Empty"];
            for (var i = 0; i < 4; i++) metrics[i].Present(t[i switch { 0 => "Dashboard.All", 1 => "Batch.Filter.Ready", 2 => "Batch.Filter.Duplicates", _ => "Dashboard.Errors" }], 0);
            return;
        }
        lock (s.Sync)
        {
            metrics[0].Present(t["Dashboard.All"], s.ImportedCount);
            metrics[1].Present(t["Batch.Filter.Ready"], s.Entries.Count(e => !e.Removed && e.Status == BatchEntryStatus.Ready));
            metrics[2].Present(t["Batch.Filter.Duplicates"], s.Entries.Count(e => !e.Removed && e.Status == BatchEntryStatus.Duplicate));
            metrics[3].Present(t["Dashboard.Errors"], s.Entries.Count(e => !e.Removed && e.Status is BatchEntryStatus.Failed or BatchEntryStatus.Unsupported));
            counters.Text = s.FileName + " · " + t["Dashboard.All"] + ": " + s.ImportedCount;
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
        AppUi.BindForeground(status, lastErrorKey is null ? AppThemeTokens.TextSecondary : AppThemeTokens.Error);
        dashboardTips.SetToolTip(status, status.Text);
        status.AutoEllipsis = true;
    }

    private void LayoutDashboard()
    {
        if (sourceCard is null) return;
        var origin = body.DisplayRectangle.Location; var width = Math.Max(1, body.DisplayRectangle.Width - 36);
        var line = AppTypography.TextHeight(36); var gap = 10;
        var inline = width >= AppTypography.TextHeight(900);
        heading.SetBounds(18 + origin.X, 16 + origin.Y, width, AppTypography.TextHeight(44));
        var left = inline ? width * 42 / 100 - 6 : width;
        var tabsHeight = LayoutDashboardActions(SourceActions.Select(k => buttons[k]), width, 0, 0, line);
        sourceTabs!.SetBounds(heading.Left, heading.Bottom + 12, width, tabsHeight);
        sourceCard.SetBounds(18 + origin.X, sourceTabs.Bottom + 12, left, 220);
        dropZone!.SetBounds(10, 10, left - 20, 1);
        sourceIcon!.SetIconSize(32); sourceIcon.SetBounds((dropZone.Width - 32) / 2, 10, 32, 32);
        sourceTitle!.TextAlign = sourceHint!.TextAlign = ContentAlignment.MiddleCenter;
        sourceTitle.SetBounds(16, sourceIcon.Bottom + 6, dropZone.Width - 32, AppTypography.TextHeight(24));
        sourceHint.SetBounds(16, sourceTitle.Bottom + 2, dropZone.Width - 32, DashboardVisual.LabelHeight(sourceHint, dropZone.Width - 32, 2));
        DashboardVisual.Button(pickFile!, true); pickFile!.Enabled = !busy;
        var pickWidth = Math.Min(dropZone.Width - 32, DashboardVisual.ActionWidth(pickFile));
        pickFile.SetBounds((dropZone.Width - pickWidth) / 2, sourceHint.Bottom + 8, pickWidth, line);
        dropZone.Height = pickFile.Bottom + 10;
        path.SetBounds(10, dropZone.Bottom + 8, left - 20, line);
        DashboardVisual.SetPadding(path, new Padding(10, 0, 10, 0));
        sourceCard.Height = path.Bottom + 10;
        resultCard!.SetBounds(inline ? sourceCard.Right + 12 : sourceCard.Left, inline ? sourceCard.Top : sourceCard.Bottom + 12,
            inline ? width - left - 12 : width, sourceCard.Height);
        resultTitle!.SetBounds(16, 12, resultCard.Width - 32, AppTypography.TextHeight(28));
        var metricColumns = resultCard.Width >= AppTypography.TextHeight(440) ? 4 : 2;
        var metricWidth = (resultCard.Width - 24 - (metricColumns - 1) * 8) / metricColumns;
        var metricHeight = metrics.Max(metric => metric.DesiredHeight(metricWidth));
        for (var i = 0; i < metrics.Count; i++) metrics[i].SetBounds(12 + i % metricColumns * (metricWidth + 8),
            resultTitle.Bottom + 10 + i / metricColumns * (metricHeight + 8), metricWidth, metricHeight);
        counters!.SetBounds(16, metrics[^1].Bottom + 10, resultCard.Width - 32, AppTypography.TextHeight(30));
        AppTypography.Bind(counters, 9);
        status.Multiline = true; AppTypography.Bind(status, 9);
        status.SetBounds(16, counters.Bottom + 8, resultCard.Width - 32, DashboardVisual.LabelHeight(status, resultCard.Width - 32, lastErrorKey is null ? 2 : 4));
        resultCard.Height = Math.Max(inline ? sourceCard.Height : 0, status.Bottom + 14);
        if (inline) sourceCard.Height = resultCard.Height;

        var top = Math.Max(sourceCard.Bottom, resultCard.Bottom) + 12;
        var toolbarHeight = LayoutDashboardActions(PrimaryActions.Select(k => buttons[k]), width, 0, 0, line);
        toolbar.SetBounds(18 + origin.X, top, width, toolbarHeight);
        top = toolbar.Bottom + gap;
        dashboardOptionsButton!.SetBounds(18 + origin.X + width - Math.Min(width / 3, DashboardVisual.ActionWidth(dashboardOptionsButton, true)),
            top, Math.Min(width / 3, DashboardVisual.ActionWidth(dashboardOptionsButton, true)), line);
        DashboardVisual.Button(dashboardOptionsButton); DashboardVisual.Icon(dashboardOptionsButton, AppIconKind.Settings);
        dashboardOptionsButton.AccessibleName = services.Localization["Dashboard.OptionsShort"];
        filter.SetBounds(18 + origin.X, top, Math.Min(AppTypography.TextHeight(190), width / 3), line);
        var searchWidth = dashboardOptionsButton.Left - filter.Right - 20;
        if (searchWidth >= AppTypography.TextHeight(220)) search.SetBounds(filter.Right + gap, top, searchWidth, line);
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
        AppUi.BindBackground(rows, AppThemeTokens.Surface); DashboardVisual.Flat(rows);
        summaryTitle!.SetBounds(16, 12, width - 32, AppTypography.TextHeight(24));
        summaryFacts!.SetBounds(16, summaryTitle.Bottom + 6, width - 32, DashboardVisual.LabelHeight(summaryFacts, width - 32, 3));
        var summaryHeight = summaryFacts.Bottom + 12;
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
            DashboardVisual.Button(button, button == buttons["Add"] || button == buttons["Browse"]);
            var icon = button == buttons["Add"] ? AppIconKind.Download : button == buttons["Analyze"] ? AppIconKind.Search :
                button == buttons["Browse"] ? AppIconKind.Folder : (AppIconKind?)null;
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
        range.SetBounds(14, y + captionHeight + line + 14, width, range.Height);
        var actionHeight = LayoutDashboardActions(SelectionActions.Select(k => buttons[k]), width, 0, 0, line);
        dashboardSelectionActions!.SetBounds(14, range.Bottom + 10, width, actionHeight);
        dashboardOptions.Height = dashboardSelectionActions.Bottom + 14;
    }

    private void PrepareDashboardRow(Panel panel, BatchImportEntry entry)
    {
        var c = panel.Controls.ToArray(); var appearance = AppearancePresentation.Current.BatchImport;
        var title = (Label)c[1]; var url = (Label)c[2]; var state = (Label)c[3];
        title.Text = string.Join(" · ", new[] { appearance.ShowTitle ? entry.Title : null, appearance.ShowSource ? entry.Source : null }
            .Where(value => !string.IsNullOrWhiteSpace(value)));
        AppTypography.Bind(c[0], 9.5f); DashboardVisual.Flat(c[0]); AppUi.BindBackground(c[0], AppThemeTokens.Surface);
        url.AccessibleAutomationId = "BatchEntryUrl" + entry.Ordinal;
        var qualityText = string.Join("\n", new[] { appearance.ShowQuality ? services.Localization["Quality." + entry.Quality] : null,
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
        AppTypography.Bind(title, DashboardVisual.ItemTitle); title.Multiline = true;
        var actions = c.Skip(4).Take(4).Cast<Button>().ToArray();
        for (var i = 0; i < actions.Length; i++)
        {
            actions[i].AccessibleName = actions[i].Text; actions[i].AccessibleAutomationId = "BatchEntry" + new[] { "Analyze", "Add", "Details", "Remove" }[i] + entry.Ordinal;
            dashboardTips.SetToolTip(actions[i], actions[i].Text); actions[i].Text = "";
        }
        panel.Controls.Add(qualityLabel);
        panel.Tag = new DashboardBatchRow((CheckBox)c[0], title, url, state, qualityLabel, actions, c.OfType<PictureBox>().FirstOrDefault());
        DashboardVisual.Flat(panel, true); AppUi.BindBackground(panel, AppThemeTokens.Surface);
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
        var selection = AppTypography.TextHeight(54); var actionWidth = AppTypography.TextHeight(32); var actions = width - 12 - 4 * (actionWidth + 6);
        var compact = width < AppTypography.TextHeight(990);
        var quality = actions - AppTypography.TextHeight(208); var status = quality - AppTypography.TextHeight(174);
        var content = status - selection;
        return new(compact, 10, selection, selection + Math.Max(160, content * 46 / 100), status, quality, actions, width - 12);
    }

    private void LayoutDashboardHeader()
    {
        if (dashboardHeader is null) return;
        var columns = DashboardColumns(Math.Max(1, rows.DisplayRectangle.Width - 6)); var line = dashboardHeader.Height;
        var positions = new[] { columns.Selection, columns.Url, columns.Title, columns.Status, columns.Quality, columns.Actions, columns.Right };
        for (var i = 0; i < dashboardHeadings.Length; i++)
        {
            var heading = dashboardHeadings[i]; heading.Visible = !columns.Compact || i is 0 or 2;
            heading.SetBounds(columns.Compact && i == 2 ? columns.Url : positions[i], 0,
                columns.Compact && i == 2 ? columns.Right - columns.Url : Math.Max(0, positions[i + 1] - positions[i] - 8), line);
        }
    }

    private void LayoutDashboardRow(Control panel)
    {
        if (panel.Tag is not DashboardBatchRow row) return;
        panel.Width = Math.Max(1, rows.DisplayRectangle.Width - 6);
        var columns = DashboardColumns(panel.Width); var line = AppTypography.TextHeight(24); var action = AppTypography.TextHeight(32);
        var qualityStyle = row.Quality.CurrentStyle;
        var qualityWidth = columns.Compact ? Math.Max(1, (columns.Right - columns.Url - 12) / 2) : columns.Actions - columns.Quality - 12;
        var measuredQuality = TextMeasurer.MeasureText(row.Quality.Text, qualityStyle.GetFont(), qualityStyle.GetFontSize(),
            new System.Drawing.Size(qualityWidth, int.MaxValue), qualityStyle.GetFontStyle());
        var titleWidth = columns.Compact ? columns.Right - columns.Url - (row.Thumbnail is null ? 0 : 76) :
            columns.Status - columns.Title - (row.Thumbnail is null ? 0 : 48) - 12;
        var titleHeight = DashboardVisual.LabelHeight(row.Title, titleWidth, 2);
        var cellHeight = Math.Max(titleHeight, Math.Max(AppTypography.TextHeight(42), (int)Math.Ceiling(measuredQuality.Height) + 4));
        var height = Math.Max(action + 14, cellHeight + 12); var contentLeft = columns.Url;
        row.Selection.SetBounds(10, 7, columns.Url - 18, Math.Max(action, line));
        if (!columns.Compact)
        {
            row.Url.SetBounds(columns.Url, 7, columns.Title - columns.Url - 12, cellHeight);
            var imageWidth = row.Thumbnail is null ? 0 : 48;
            row.Thumbnail?.SetBounds(columns.Title, 8, 40, 28);
            row.Title.SetBounds(columns.Title + imageWidth, 7, columns.Status - columns.Title - imageWidth - 12, cellHeight);
            row.Status.SetBounds(columns.Status, 7, columns.Quality - columns.Status - 12, cellHeight);
            row.Quality.SetBounds(columns.Quality, 6, columns.Actions - columns.Quality - 12, cellHeight);
            for (var i = 0; i < row.Actions.Length; i++) row.Actions[i].SetBounds(columns.Actions + i * (action + 6), (height - action) / 2, action, action);
        }
        else
        {
            var imageWidth = row.Thumbnail is null ? 0 : 76;
            row.Thumbnail?.SetBounds(contentLeft, 8, 68, 44);
            row.Title.SetBounds(contentLeft + imageWidth, 7, columns.Right - contentLeft - imageWidth, titleHeight);
            row.Url.SetBounds(contentLeft + imageWidth, row.Title.Bottom + 2, columns.Right - contentLeft - imageWidth, line);
            var factsTop = row.Url.Bottom + 6; var factsWidth = (columns.Right - contentLeft - 12) / 2;
            row.Status.SetBounds(contentLeft, factsTop, factsWidth, line); row.Quality.SetBounds(contentLeft + factsWidth + 12, factsTop, factsWidth, Math.Max(line * 2, cellHeight));
            var actionTop = row.Quality.Bottom + 8;
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
