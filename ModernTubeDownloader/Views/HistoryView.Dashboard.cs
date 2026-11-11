using System.Text.Json;
using ModernFormsNext;
using ModernFormsNext.Accessibility;
using ModernTubeDownloader.Models;
using ModernTubeDownloader.Services;
using ModernTubeDownloader.Theming;
using Size = System.Drawing.Size;

namespace ModernTubeDownloader.Views;

internal sealed partial class HistoryView
{
    public event EventHandler<string>? RetryRequested;
    private bool dashboard;
    private IReadOnlyList<DownloadHistoryEntry> historySnapshot = Array.Empty<DownloadHistoryEntry>();
    private Dictionary<string,int> filterCounts = new();
    private DownloadHistoryEntry? inspectedEntry;
    private void SelectHistory(object? sender, DownloadHistoryEntry selected)
    {
        selectedId = selected.QueueItemId; selectedPath = selected.FinalPath;
        inspector?.ShowHistory(selected); inspectedEntry = selected;
        foreach (var row in cards) row.SetDashboard(dashboard, row.ItemId == selectedId && row.ItemPath == selectedPath);
    }
    private void InvalidateProjection()
    {
        dirty = true; generation++; filtering?.Cancel(); RefreshItems();
    }
    private bool Matches(DownloadHistoryEntry entry, string query)
        => HistoryPage.Filter(new[] { entry }, query, text.Culture, default).Length != 0;
    private string dashboardFilter = "All";
    private Guid? selectedId;
    private string? selectedPath;
    private DashboardInspector? inspector;
    private FlowLayoutPanel? filters;
    private Button? export, previous, next;
    private Button? firstPage, lastPage;
    private NumericUpDown? jumpPage;
    private bool updatingPageNumber;
    private Label? paging;
    private Panel? tableHeader;
    private ComboBox? dateRange;
    private Button? dateSort;
    private bool oldestFirst;
    private int days;
    private bool InDateRange(DownloadHistoryEntry entry) => days == 0 || entry.CompletedAt >= DateTimeOffset.UtcNow.AddDays(-days);
    private readonly List<Label> tableHeadings = [];
    private readonly Dictionary<Button, string> filterButtons = [];
    private readonly ToolTip dashboardTips = new();
    private AppVectorIcon? searchIcon;
    public void SetDashboard(bool value)
    {
        dashboard = value;
        if (!value)
        {
            AutoScroll = false; AppUi.Card(searchPanel);
            DashboardVisual.RestoreTree(search);
            AppTypography.Bind(pageTitle, 24, FontStyle.Bold);
            pageSubtitle.TextAlign = ContentAlignment.TopLeft;
        }
        if (value && inspector is null)
        {
            inspector = new(services); inspector.RetryRequested += (_, source) => RetryRequested?.Invoke(this, source); filters = new FlowLayoutPanel { WrapContents = true }; AppUi.BindBackground(filters);
            foreach (var id in new[] { "All", "Completed", "Errors", "Audio", "Video", "Playlists", "Live" })
            {
                var button = new DashboardButton { AccessibleAutomationId = "HistoryFilter" + id, Width = 154, Height = 36, Margin = new Padding(0, 0, 10, 6) };
                filterButtons[button] = id; button.Click += (_, _) => { dashboardFilter = id; page = 0; InvalidateProjection(); }; filters.Controls.Add(button);
            }
            export = new DashboardButton { AccessibleAutomationId = "HistoryExport" }; AppUi.Primary(export); export.Click += async (_, _) => await ExportAsync();
            previous = new Button { Text = "‹", AccessibleAutomationId = "HistoryPrevious" }; next = new Button { Text = "›", AccessibleAutomationId = "HistoryNext" };
            AppUi.Secondary(previous); AppUi.Secondary(next); previous.Click += (_, _) => ChangePage(-1); next.Click += (_, _) => ChangePage(1);
            firstPage = new DashboardButton { Text = "«", AccessibleAutomationId = "HistoryFirst" };
            lastPage = new DashboardButton { Text = "»", AccessibleAutomationId = "HistoryLast" };
            firstPage.Click += (_, _) => GoToPage(0); lastPage.Click += (_, _) => GoToPage(HistoryPage.Count(filtered.Length) - 1);
            jumpPage = new NumericUpDown { Minimum = 1, Maximum = 1, Value = 1, AccessibleAutomationId = "HistoryJumpPage" };
            AppUi.Input(jumpPage);
            jumpPage.ValueChanged += (_, _) => { if (!updatingPageNumber) GoToPage((int)jumpPage.Value - 1); };
            Controls.AddRange([firstPage, lastPage, jumpPage]);
            dateRange = new ComboBox { AccessibleAutomationId = "HistoryDateRange" }; AppUi.Input(dateRange);
            dateRange.Items.Add(services.Localization["Dashboard.AllDates"]); dateRange.Items.Add(services.Localization["Dashboard.Last7Days"]); dateRange.Items.Add(services.Localization["Dashboard.Last30Days"]); dateRange.SelectedIndex = 0;
            dateRange.SelectedIndexChanged += (_,_) => { days = dateRange.SelectedIndex switch { 1 => 7, 2 => 30, _ => 0 }; page = 0; InvalidateProjection(); }; Controls.Add(dateRange);
            dateSort = new DashboardButton { AccessibleAutomationId = "HistoryDateSort" }; dateSort.Click += (_,_) => { oldestFirst = !oldestFirst; page = 0; InvalidateProjection(); };
            tableHeader = new Panel { AccessibleAutomationId = "HistoryTableHeader" }; tableHeader.Controls.Add(dateSort); AppUi.BindBackground(tableHeader, AppThemeTokens.Surface);
            foreach (var key in new[] { "Dashboard.Title", "Dashboard.Source", "Dashboard.Date", "Dashboard.Time", "Dashboard.Format", "Dashboard.Size", "Dashboard.Status" })
            { var label = AppUi.Muted(); label.Tag = key; label.TextAlign = ContentAlignment.MiddleLeft; label.AutoEllipsis = true; AppTypography.Bind(label, 9.5f); tableHeadings.Add(label); tableHeader.Controls.Add(label); }
            Controls.Add(tableHeader);
            paging = AppUi.Muted(); paging.AccessibleAutomationId = "HistoryPageSummary"; Controls.AddRange([inspector, filters, export, previous, next, paging]);
            searchIcon = new AppVectorIcon(AppIconKind.Search, 18) { AccessibilityView = AccessibilityView.Hidden };
            searchIcon.Click += (_, _) => search.Select(); searchPanel.Controls.Add(searchIcon);
        }
        if (searchIcon is not null) searchIcon.Visible = value;
        foreach (var control in new Control?[] { inspector, filters, export, previous, next, paging, tableHeader, dateRange, firstPage, lastPage, jumpPage })
            if (control is not null) { control.Visible = value; if (!value) control.SetBounds(0, 0, 0, 0); }
        AppTypography.SetDashboardPresentation(this, value);
        pageSummary.AccessibleAutomationId = value ? "HistoryClassicPageSummary" : "HistoryPageSummary";
        if (paging is not null) paging.AccessibleAutomationId = value ? "HistoryPageSummary" : "HistoryDashboardPageSummary";
        InvalidateProjection(); UpdateLayout();
    }
    private static bool Completed(DownloadHistoryEntry entry) => !entry.WasPartial && entry.Status.Equals("Completed", StringComparison.OrdinalIgnoreCase);
    private bool FilterHistory(DownloadHistoryEntry entry) => InDateRange(entry) && MatchesFilter(entry, dashboardFilter);
    private static bool MatchesFilter(DownloadHistoryEntry entry, string filter) => filter switch
    {
        "Completed" => Completed(entry), "Errors" => !Completed(entry), "Live" => entry.WasLiveRecording,
        "Audio" => new[] { ".mp3", ".m4a", ".aac", ".flac", ".ogg", ".wav", ".opus" }.Contains(System.IO.Path.GetExtension(entry.FinalPath).ToLowerInvariant()),
        "Video" => new[] { ".mp4", ".mkv", ".webm", ".mov", ".avi" }.Contains(System.IO.Path.GetExtension(entry.FinalPath).ToLowerInvariant()),
        "Playlists" => !string.IsNullOrEmpty(entry.PlaylistId) || !string.IsNullOrEmpty(entry.PlaylistTitle), _ => true
    };
    private async Task ExportAsync()
    {
        if (FindForm() is not { } form) return;
        try
        {
            var dialog = new SaveFileDialog { FileName = "history.json", DefaultExtension = "json" }; dialog.AddFilter("JSON", "*.json");
            if (await dialog.ShowDialog(form) == DialogResult.OK && dialog.FileName is { } path)
            {
                var snapshot = services.History.Snapshot(); var query = search.Text.Trim(); var culture = text.Culture;
                var filter = dashboardFilter; var cutoff = days == 0 ? DateTimeOffset.MinValue : DateTimeOffset.UtcNow.AddDays(-days);
                var ascending = oldestFirst;
                var json = await Task.Run(() =>
                {
                    var matches = HistoryPage.Filter(snapshot, query, culture, default)
                        .Where(e => e.CompletedAt >= cutoff && MatchesFilter(e, filter)).ToArray();
                    if (ascending) Array.Reverse(matches);
                    return JsonSerializer.Serialize(matches, Infrastructure.JsonDefaults.Options);
                });
                await File.WriteAllTextAsync(path, json);
            }
        }
        catch (Exception error) { services.Logger.Error("History export failed.", error); }
    }
    private void LayoutDashboard()
    {
        if (inspector is null) return;
        AutoScroll = true; var origin = DisplayRectangle.Location; var width = Math.Max(0, DisplayRectangle.Width - 36);
        var line = AppTypography.TextHeight(42);
        inspector.Visible = AppearancePresentation.Current.ModernDashboardShell.Inspector != DashboardInspectorMode.Hidden;
        var inline = inspector.Visible && width >= AppTypography.TextHeight(1050);
        var right = inline ? Math.Clamp(width / 4, 290, AppTypography.TextHeight(320)) : 0;
        var center = inline ? width - right - 12 : width;
        AppTypography.Bind(pageTitle, 20, FontStyle.Bold);
        header.SetBounds(18 + origin.X, 18 + origin.Y, width, line);
        var titleWidth = Math.Min(width, HistoryTable.MeasuredWidth(pageTitle) + 8);
        pageTitle.SetBounds(0, 0, titleWidth, line);
        pageSubtitle.Visible = width - titleWidth > AppTypography.TextHeight(300);
        pageSubtitle.SetBounds(titleWidth + 16, 6, Math.Max(0, width - titleWidth - 16), line - 6);
        pageSubtitle.TextAlign = ContentAlignment.MiddleLeft;
        export!.Text = text["Dashboard.ExportHistory"]; DashboardVisual.Button(export, true);
        var exportWidth = Math.Max(AppTypography.TextHeight(160), DashboardVisual.ActionWidth(export, true));
        var dateWidth = AppTypography.TextHeight(174);
        var inlineTools = width >= AppTypography.TextHeight(220) + exportWidth + dateWidth + 24;
        var searchWidth = inlineTools ? width - exportWidth - dateWidth - 24 : width;
        searchPanel.SetBounds(18 + origin.X, header.Bottom + 12, searchWidth, line); AppUi.BindBackground(searchPanel); searchPanel.Style.Border.Width = 0;
        search.SetBounds(0, 0, searchWidth, line); DashboardVisual.SetPadding(search, new Padding(42, 0, 12, 0));
        searchIcon!.SetBounds(14, (line - 18) / 2, 18, 18); searchIcon.BringToFront();
        var toolsTop = inlineTools ? searchPanel.Top : searchPanel.Bottom + 8;
        export.SetBounds(18 + origin.X + width - Math.Min(exportWidth, width / 2 - 6), toolsTop, Math.Min(exportWidth, width / 2 - 6), line);
        dateRange!.SetBounds(inlineTools ? searchPanel.Right + 12 : searchPanel.Left, toolsTop, inlineTools ? dateWidth : Math.Min(dateWidth, export.Left - searchPanel.Left - 12), line);
        DashboardVisual.Icon(export, AppIconKind.Download);
        var entries = historySnapshot;
        var filterRows = 1; var filterX = 0; var filterHeight = AppTypography.TextHeight(36);
        foreach (var (button, filter) in filterButtons)
        {
            var count = filterCounts.GetValueOrDefault(filter);
            button.Text = text[filter == "Live" ? "Nav.Live" : "Dashboard." + filter] + $" ({count})";
            DashboardVisual.Button(button, dashboardFilter == filter);
            button.Width = Math.Min(width, Math.Max(AppTypography.TextHeight(94), DashboardVisual.ActionWidth(button, true)));
            button.Height = filterHeight;
            if (filterX > 0 && filterX + button.Width + 10 > width) { filterRows++; filterX = 0; }
            filterX += button.Width + 10;
            DashboardVisual.Icon(button, filter switch { "Completed" => AppIconKind.Check, "Errors" => AppIconKind.Close, "Live" => AppIconKind.Live, "Playlists" => AppIconKind.Queue, "Audio" or "Video" => AppIconKind.Play, _ => AppIconKind.Info });
        }
        filters!.SetBounds(18 + origin.X, toolsTop + line + 12, width, filterRows * (filterHeight + 6));
        var tableTop = filters.Bottom + 12;
        tableHeader!.SetBounds(18 + origin.X, tableTop, center, AppTypography.TextHeight(38));
        var top = tableHeader.Bottom; var height = Math.Max(AppTypography.TextHeight(250), ClientSize.Height - (top - origin.Y) - line - 30);
        list.SetBounds(18 + origin.X, top, center, height); empty.SetBounds(list.Left, top, center, height);
        if (inspector.Visible)
            inspector.SetBounds(inline ? list.Right + 12 : list.Left, inline ? tableTop : list.Bottom + line + 20, inline ? right : center, inline ? tableHeader.Height + height + line + 12 : AppTypography.TextHeight(380));
        else inspector.SetBounds(0, 0, 0, 0);
        var pagerButtonWidth = AppTypography.TextHeight(32); var jumpWidth = AppTypography.TextHeight(76);
        firstPage!.SetBounds(list.Right - pagerButtonWidth * 4 - jumpWidth - 40, list.Bottom + 10, pagerButtonWidth, line);
        previous!.SetBounds(firstPage.Right + 8, firstPage.Top, pagerButtonWidth, line);
        jumpPage!.SetBounds(previous.Right + 8, previous.Top, jumpWidth, line);
        next!.SetBounds(jumpPage.Right + 8, previous.Top, pagerButtonWidth, line);
        lastPage!.SetBounds(next.Right + 8, previous.Top, pagerButtonWidth, line);
        paging!.SetBounds(list.Left + 12, previous.Top + 4, Math.Max(1, firstPage.Left - list.Left - 24), line);
        var countFiltered = filtered.Length; var pages = HistoryPage.Count(countFiltered);
        var first = countFiltered == 0 ? 0 : page * 40 + 1; var last = Math.Min(countFiltered, (page + 1) * 40);
        paging.Text = $"{first}–{last} / {countFiltered}     ·     {page + 1} / {pages}"; previous.Enabled = page > 0; next.Enabled = page < pages - 1;
        paging.AccessibleName = text.Get("History.PageSummary", page + 1, pages, countFiltered, total);
        firstPage.Enabled = previous.Enabled; lastPage.Enabled = next.Enabled;
        firstPage.AccessibleName = text["History.FirstPage"]; lastPage.AccessibleName = text["History.LastPage"];
        jumpPage.AccessibleName = text["History.GoToPage"]; jumpPage.Enabled = pages > 1;
        updatingPageNumber = true;
        try { jumpPage.Maximum = pages; jumpPage.Value = page + 1; }
        finally { updatingPageNumber = false; }
        previous.AccessibleName = text["Batch.Previous"]; next.AccessibleName = text["Batch.Next"]; dashboardTips.SetToolTip(previous, previous.AccessibleName); dashboardTips.SetToolTip(next, next.AccessibleName);
        emptyIcon.SetBounds((center - 44) / 2, Math.Max(20, height / 2 - 80), 44, 44);
        emptyTitle.SetBounds(20, emptyIcon.Bottom + 8, center - 40, AppTypography.TextHeight(40)); emptyBody.SetBounds(20, emptyTitle.Bottom + 8, center - 40, AppTypography.TextHeight(60));
        DashboardVisual.Button(previous); DashboardVisual.Button(next!);
        DashboardVisual.Button(firstPage); DashboardVisual.Button(lastPage);
        ResizeCards();
        var entry = entries.FirstOrDefault(e => e.QueueItemId == selectedId && e.FinalPath == selectedPath);
        if (entry != inspectedEntry)
        {
            inspectedEntry = entry;
            if (entry is not null) inspector.ShowHistory(entry); else inspector.Clear();
        }
    }

    private void LayoutDashboardTableHeading(int rowWidth)
    {
        if (tableHeader is null || dateSort is null) return;
        var cols = HistoryTable.Columns(rowWidth); var compact = HistoryTable.IsCompact(rowWidth);
        for (var i = 0; i < tableHeadings.Count; i++)
        {
            var column = cols[i]; var left = i == 0 ? HistoryTable.TitleLeft : column.Left;
            tableHeadings[i].SetBounds(left, 0, Math.Max(0, column.Left + column.Width - left), tableHeader.Height);
            tableHeadings[i].Text = text[(string)tableHeadings[i].Tag!]; tableHeadings[i].Visible = !compact || i == 0;
        }
        tableHeadings[2].Visible = false;
        dateSort.Text = text["Dashboard.Date"] + (oldestFirst ? " ↑" : " ↓"); DashboardVisual.Button(dateSort);
        AppUi.BindInteractiveSurface(dateSort, AppThemeTokens.Surface, AppThemeTokens.SurfaceHover);
        AppUi.BindForeground(dateSort, AppThemeTokens.TextSecondary); AppTypography.Bind(dateSort, 9.5f);
        dateSort.TextAlign = ContentAlignment.MiddleLeft; DashboardVisual.SetPadding(dateSort, new Padding(0));
        DashboardVisual.Flat(dateSort); dateSort.Style.Border.Radius = 0;
        dateSort.SetBounds(compact ? rowWidth - AppTypography.TextHeight(140) : cols[2].Left, 0, compact ? AppTypography.TextHeight(140) : cols[2].Width, tableHeader.Height);
    }
}

internal sealed partial class HistoryEntryCard
{
    private bool dashboard;
    private bool rowSelected;
    private readonly List<Label> cells = [];
    private AppVectorIcon? folderIcon;
    private AppVectorIcon? statusIcon;
    private AppVectorIcon? placeholderIcon;
    private Label? durationBadge;
    private SourceVectorIcon? historySource;
    internal Guid ItemId => entry.QueueItemId;
    internal string ItemPath => entry.FinalPath;
    protected override AccessibleObject CreateAccessibilityInstance() => new HistoryRowAccessibleObject(this);
    private sealed class HistoryRowAccessibleObject(HistoryEntryCard row) : ControlAccessibleObject(row)
    {
        public override AccessibleControlType ControlType => row.dashboard ? AccessibleControlType.ListItem : base.ControlType;
        public override AccessibleStates State => base.State | (row.dashboard ? AccessibleStates.Selectable | (row.rowSelected ? AccessibleStates.Selected : AccessibleStates.None) : AccessibleStates.None);
        public override AccessibleActions SupportedActions => base.SupportedActions | (row.dashboard && (base.SupportedActions & AccessibleActions.Focus) != 0 ? AccessibleActions.Select : AccessibleActions.None);
        public override bool PerformAction(AccessibleActions action, object? parameter = null)
        {
            if (action == AccessibleActions.Select && parameter is null && (SupportedActions & action) != 0) { row.Select(); row.SelectionRequested?.Invoke(row, row.entry); return true; }
            return base.PerformAction(action, parameter);
        }
    }
    public event EventHandler<DownloadHistoryEntry>? SelectionRequested;
    private bool presentationInitialized;
    public void SetDashboard(bool value, bool selected)
    {
        if (presentationInitialized && dashboard == value && rowSelected == selected) return;
        presentationInitialized = true;
        dashboard = value; rowSelected = selected; TabStop = value; SetControlBehavior(ControlBehaviors.Selectable, value);
        thumbnail.RoundedPreview = value;
        AccessibleAutomationId = $"HistoryItem-{entry.QueueItemId:N}-{entry.LivePartIndex?.ToString() ?? "vod"}"; AccessibleName = entry.Title;
        AppTypography.SetDashboardPresentation(this, value);
        if (value)
        {
            AppUi.BindBackground(this, selected ? AppThemeTokens.RowSelected : AppThemeTokens.Surface);
            AppTypography.Bind(title, DashboardVisual.ItemTitle); AppTypography.Bind(facts, DashboardVisual.Metadata);
        }
        else
        {
            DashboardVisual.RestoreTree(this); AppUi.Card(this);
            AppTypography.Bind(title, 13.5f, FontStyle.Bold); AppTypography.Bind(facts, 10.5f);
            title.Multiline = facts.Multiline = false; title.AutoEllipsis = facts.AutoEllipsis = false;
            title.TextAlign = facts.TextAlign = ContentAlignment.TopLeft;
            Margin = new Padding(0, 0, 0, 14); ApplyLocalization();
        }
        if (folderIcon is not null) folderIcon.Visible = value;
        if (statusIcon is not null) statusIcon.Visible = value;
        if (placeholderIcon is not null) placeholderIcon.Visible = value;
        if (durationBadge is not null) durationBadge.Visible = value && entry.DurationSeconds is > 0;
        if (more is not null) { more.Visible = value; if (!value) more.SetBounds(0, 0, 0, 0); }
        if (historySource is not null) historySource.Visible = value;
        placeholderText.Visible = !value;
        if (!value) openFolder.Text = text["Common.OpenFolder"];
        LayoutControls();
    }
    private void LayoutDashboardCard()
    {
        PrepareMoreAction();
        while (cells.Count < 6)
        {
            var label = AppUi.Muted(); label.Multiline = true; label.TextAlign = ContentAlignment.MiddleLeft;
            label.AccessibleAutomationId = "HistoryCell" + new[] { "Source", "Date", "Duration", "Format", "Size", "Status" }[cells.Count];
            label.Click += (_, _) => SelectionRequested?.Invoke(this, entry);
            AppTypography.Bind(label, 9.25f); cells.Add(label); Controls.Add(label);
        }
        var columns = HistoryTable.Columns(Width); var compact = HistoryTable.IsCompact(Width);
        var titleWidth = Math.Max(1, (compact ? Width - 88 : columns[0].Left + columns[0].Width - 12) - HistoryTable.TitleLeft);
        title.AutoEllipsis = true; title.Multiline = true; title.TextAlign = ContentAlignment.TopLeft;
        facts.AutoEllipsis = true; facts.Multiline = compact; facts.TextAlign = ContentAlignment.TopLeft;
        facts.Text = compact
            ? entry.Channel + " · " + SourcePresentation.Host(entry.SourceUrl) + "\n" + entry.CompletedAt.ToLocalTime().ToString("g", text.Culture) + " · " + System.IO.Path.GetExtension(entry.FinalPath).TrimStart('.').ToUpperInvariant() + " · " + entry.Quality
            : entry.Channel;
        var titleHeight = HistoryTable.MeasuredHeight(title, titleWidth, 2);
        var factsHeight = HistoryTable.MeasuredHeight(facts, titleWidth, compact ? 2 : 1);
        var statusHeight = HistoryTable.LineHeight(cells[5]);
        var textHeight = titleHeight + 3 + factsHeight + (compact ? statusHeight + 6 : 0);
        Height = Math.Max(AppTypography.TextHeight(70), textHeight + 12); Margin = new Padding(0, 0, 0, 1);
        placeholder.SetBounds(12, (Height - 48) / 2, 86, 48); thumbnail.Bounds = placeholder.Bounds;
        if (placeholderIcon is null)
        {
            placeholderIcon = new AppVectorIcon(AppIconKind.History, 24) { AccessibilityView = AccessibilityView.Hidden, AccessibleAutomationId = "HistoryThumbnailPlaceholder" };
            placeholderIcon.Click += (_, _) => SelectionRequested?.Invoke(this, entry); placeholder.Controls.Add(placeholderIcon);
        }
        placeholderIcon.SetBounds((placeholder.Width - 24) / 2, (placeholder.Height - 24) / 2, 24, 24);
        placeholderText.Visible = false;
        placeholder.Visible = thumbnail.Image is null; thumbnail.Visible = thumbnail.Image is not null;
        if (durationBadge is null)
        {
            durationBadge = AppUi.Muted(); AppUi.BindForeground(durationBadge); AppTypography.Bind(durationBadge, 8.5f);
            durationBadge.TextAlign = ContentAlignment.MiddleCenter;
            durationBadge.AccessibleAutomationId = "HistoryThumbnailDuration";
            AppUi.BindBackground(durationBadge, AppThemeTokens.Surface);
            durationBadge.Click += (_, _) => SelectionRequested?.Invoke(this, entry);
            Controls.Add(durationBadge);
        }
        durationBadge.Text = entry.DurationSeconds is { } duration ? DashboardStyle.Duration(duration) : "";
        durationBadge.Visible = entry.DurationSeconds is > 0;
        var badgeWidth = Math.Min(thumbnail.Width, HistoryTable.MeasuredWidth(durationBadge) + 8);
        var badgeHeight = HistoryTable.LineHeight(durationBadge) + 2;
        durationBadge.SetBounds(thumbnail.Right - badgeWidth, thumbnail.Bottom - badgeHeight, badgeWidth, badgeHeight);
        durationBadge.BringToFront();
        title.SetBounds(HistoryTable.TitleLeft, (Height - textHeight) / 2, titleWidth, titleHeight);
        facts.SetBounds(title.Left, title.Bottom + 3, title.Width, factsHeight); path.Visible = false;
        var identity = SourceBrandLookup.Resolve(entry.SourceUrl);
        cells[0].Text = identity.Name;
        rowTips.SetToolTip(cells[0], entry.SourceUrl);
        if (historySource is null)
        {
            historySource = new SourceVectorIcon { AccessibilityView = AccessibilityView.Hidden };
            historySource.Click += (_, _) => SelectionRequested?.Invoke(this, entry); cells[0].Controls.Add(historySource);
        }
        historySource.Brand = identity.Brand; historySource.Kind = identity.Kind;
        DashboardVisual.SetPadding(cells[0], new Padding(22, 0, 0, 0));
        cells[1].Text = entry.CompletedAt.ToLocalTime().ToString("yyyy-MM-dd\nHH:mm", text.Culture);
        cells[2].Text = entry.DurationSeconds is { } seconds ? DashboardStyle.Duration(seconds) : "—";
        cells[3].Text = System.IO.Path.GetExtension(entry.FinalPath).TrimStart('.').ToUpperInvariant() + "\n" + entry.Quality;
        cells[4].Text = entry.FileSizeBytes is { } bytes ? DashboardStyle.Bytes(bytes) : "—";
        cells[5].Text = text["Status." + (entry.WasPartial ? "Partial" : entry.Status)];
        for (var i = 0; i < cells.Count; i++)
        {
            var c = columns[i + 1]; var inset = i == 5 ? 22 : 0;
            cells[i].Multiline = i is not 0 and not 5;
            cells[i].SetBounds(c.Left + inset, 10, Math.Max(1, c.Width - 8 - inset), Height - 20);
            cells[i].Visible = !compact; cells[i].AutoEllipsis = true;
        }
        historySource.SetBounds(0, (cells[0].Height - 16) / 2, 16, 16); historySource.Visible = !compact;
        var completed = entry.Status.Equals("Completed", StringComparison.OrdinalIgnoreCase);
        var statusColor = entry.WasPartial ? AppThemeTokens.Warning : completed ? AppThemeTokens.Success : AppThemeTokens.Error;
        AppUi.BindForeground(cells[5], statusColor);
        if (compact)
        {
            cells[5].Visible = true; cells[5].SetBounds(title.Left + 22, facts.Bottom + 6, Math.Max(1, title.Width - 22), statusHeight);
        }
        if (statusIcon is null)
        {
            statusIcon = new AppVectorIcon(AppIconKind.Check, 16) { AccessibilityView = AccessibilityView.Hidden };
            statusIcon.Click += (_, _) => SelectionRequested?.Invoke(this, entry); Controls.Add(statusIcon);
        }
        statusIcon.SetKind(entry.WasPartial ? AppIconKind.Info : completed ? AppIconKind.Check : AppIconKind.Close);
        statusIcon.SetColorToken(statusColor); statusIcon.Visible = true;
        statusIcon.SetBounds(cells[5].Left - 22, cells[5].Top + (cells[5].Height - 16) / 2, 16, 16);
        if (folderIcon is null) { folderIcon = new AppVectorIcon(AppIconKind.Folder, 20); folderIcon.AccessibilityView = ModernFormsNext.Accessibility.AccessibilityView.Hidden; folderIcon.Click += (_,_) => openFolder.PerformClick(); openFolder.Controls.Add(folderIcon); }
        folderIcon.SetBounds(6,7,20,20);
        openFile.Visible = false; openFolder.SetBounds(Width - 78, (Height - 34) / 2, 32, 34); openFolder.Text = ""; openFolder.AccessibleName = text["Common.OpenFolder"];
        more!.Visible = true; more.SetBounds(Width - 40, openFolder.Top, 30, 34);
        more.AccessibleAutomationId = "HistoryMore-" + entry.QueueItemId.ToString("N"); more.AccessibleName = text["Dashboard.MoreActions"];
        DownloadsVisual.Button(more);
        DashboardVisual.Button(openFolder); DashboardVisual.Flat(this, !rowSelected);
        AppUi.BindGradient(this, rowSelected ? DownloadsVisual.Selected : DownloadsVisual.Panel, rowSelected ? DownloadsVisual.SelectedEnd : DownloadsVisual.PanelEnd);
        if (rowSelected)
            foreach (var style in new[] { Style, StyleHover, StylePressed, StyleFocused, StyleDisabled })
            { style.Border.Width = 1; style.Border.Color = AppUi.GetColor(AppThemeTokens.Accent); style.Border.Radius = 7; }
        else
            foreach (var style in new[] { Style, StyleHover, StylePressed, StyleFocused, StyleDisabled }) style.Border.Radius = 0;
    }
}

internal static class HistoryTable
{
    internal const int TitleLeft = 110;
    internal static bool IsCompact(int width) => width < AppTypography.TextHeight(820);
    internal static int MeasuredWidth(Label label)
    {
        var style = label.CurrentStyle;
        return (int)Math.Ceiling(TextMeasurer.MeasureText(label.Text, style.GetFont(), style.GetFontSize(),
            new Size(int.MaxValue, int.MaxValue), style.GetFontStyle()).Width);
    }
    internal static int LineHeight(Label label)
    {
        var style = label.CurrentStyle;
        return (int)Math.Ceiling(TextMeasurer.MeasureText("Ag", style.GetFont(), style.GetFontSize(),
            new Size(int.MaxValue, int.MaxValue), style.GetFontStyle()).Height) + 1;
    }
    internal static int MeasuredHeight(Label label, int width, int maxLines)
    {
        var style = label.CurrentStyle; var line = LineHeight(label);
        var measured = (int)Math.Ceiling(TextMeasurer.MeasureText(label.Text, style.GetFont(), style.GetFontSize(),
            new Size(Math.Max(1, width), int.MaxValue), style.GetFontStyle()).Height) + 1;
        return Math.Clamp(measured, line, line * maxLines);
    }
    internal static (int Left, int Width)[] Columns(int width)
    {
        var content = Math.Max(1, width - 96);
        int[] percentages = [36, 10, 12, 8, 10, 9, 15];
        var columns = new (int Left, int Width)[percentages.Length]; var left = 12;
        for (var i = 0; i < columns.Length; i++)
        {
            var size = i == columns.Length - 1 ? 12 + content - left : content * percentages[i] / 100;
            columns[i] = (left, size); left += size;
        }
        return columns;
    }
}
