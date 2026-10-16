using System.Text.Json;
using ModernFormsNext;
using ModernFormsNext.Accessibility;
using ModernTubeDownloader.Models;
using ModernTubeDownloader.Services;
using ModernTubeDownloader.Theming;

namespace ModernTubeDownloader.Views;

internal sealed partial class HistoryView
{
    public event EventHandler<string>? RetryRequested;
    private bool dashboard;
    private int dashboardPage;
    private string dashboardFilter = "All";
    private Guid? selectedId;
    private string? selectedPath;
    private DashboardInspector? inspector;
    private FlowLayoutPanel? filters;
    private Button? export, previous, next;
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
    public void SetDashboard(bool value)
    {
        dashboard = value;
        if (!value) { AutoScroll = false; AppUi.Card(searchPanel); }
        if (value && inspector is null)
        {
            inspector = new(services); inspector.RetryRequested += (_, source) => RetryRequested?.Invoke(this, source); filters = new FlowLayoutPanel { WrapContents = true }; AppUi.BindBackground(filters);
            foreach (var id in new[] { "All", "Completed", "Errors", "Audio", "Video", "Playlists", "Live" })
            {
                var button = new Button { AccessibleAutomationId = "HistoryFilter" + id, Width = 154, Height = 36, Margin = new Padding(0, 0, 10, 6) };
                filterButtons[button] = id; button.Click += (_, _) => { dashboardFilter = id; dashboardPage = 0; RefreshItems(); }; filters.Controls.Add(button);
            }
            export = new Button { AccessibleAutomationId = "HistoryExport" }; AppUi.Primary(export); export.Click += async (_, _) => await ExportAsync();
            previous = new Button { Text = "‹", AccessibleAutomationId = "HistoryPrevious" }; next = new Button { Text = "›", AccessibleAutomationId = "HistoryNext" };
            AppUi.Secondary(previous); AppUi.Secondary(next); previous.Click += (_, _) => { dashboardPage--; RefreshItems(); }; next.Click += (_, _) => { dashboardPage++; RefreshItems(); };
            dateRange = new ComboBox { AccessibleAutomationId = "HistoryDateRange" }; AppUi.Input(dateRange);
            dateRange.Items.Add(services.Localization["Dashboard.AllDates"]); dateRange.Items.Add(services.Localization["Dashboard.Last7Days"]); dateRange.Items.Add(services.Localization["Dashboard.Last30Days"]); dateRange.SelectedIndex = 0;
            dateRange.SelectedIndexChanged += (_,_) => { days = dateRange.SelectedIndex switch { 1 => 7, 2 => 30, _ => 0 }; dashboardPage = 0; RefreshItems(); }; Controls.Add(dateRange);
            dateSort = new Button { AccessibleAutomationId = "HistoryDateSort" }; dateSort.Click += (_,_) => { oldestFirst = !oldestFirst; dashboardPage = 0; RefreshItems(); };
            tableHeader = new Panel(); tableHeader.Controls.Add(dateSort); AppUi.BindBackground(tableHeader, AppThemeTokens.Surface);
            foreach (var key in new[] { "Dashboard.Title", "Dashboard.Source", "Dashboard.Date", "Downloads.Quality", "Dashboard.Status" })
            { var label = AppUi.Muted(); label.Tag = key; AppTypography.Bind(label, 9.5f); tableHeadings.Add(label); tableHeader.Controls.Add(label); }
            Controls.Add(tableHeader);
            paging = AppUi.Muted(); Controls.AddRange([inspector, filters, export, previous, next, paging]);
        }
        foreach (var control in new Control?[] { inspector, filters, export, previous, next, paging, tableHeader, dateRange }) if (control is not null) control.Visible = value;
        RefreshItems(); UpdateLayout();
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
                await File.WriteAllTextAsync(path, JsonSerializer.Serialize(services.History.Snapshot().Where(e => FilterHistory(e) && Matches(e, search.Text.Trim())), Infrastructure.JsonDefaults.Options));
        }
        catch (Exception error) { services.Logger.Error("History export failed.", error); }
    }
    private void LayoutDashboard()
    {
        if (inspector is null) return;
        AutoScroll = true; var origin = DisplayRectangle.Location; var width = Math.Max(560, ClientSize.Width - 36);
        var line = AppTypography.TextHeight(42); var inline = width >= AppTypography.TextHeight(1050);
        var right = inline ? Math.Clamp(width / 4, 290, AppTypography.TextHeight(320)) : 0;
        var center = inline ? width - right - 12 : width;
        header.SetBounds(18 + origin.X, 18 + origin.Y, width, AppTypography.TextHeight(60));
        pageTitle.SetBounds(0, 0, width, AppTypography.TextHeight(48)); pageSubtitle.Visible = false;
        searchPanel.SetBounds(18 + origin.X, header.Bottom + 8, width, line); AppUi.BindBackground(searchPanel); searchPanel.Style.Border.Width = 0;
        var exportWidth = AppTypography.TextHeight(180); search.SetBounds(0, 0, Math.Max(160, width - exportWidth - AppTypography.TextHeight(190) - 24), line);
        export!.SetBounds(18 + origin.X + width - exportWidth, searchPanel.Top, exportWidth, line); export.Text = text["Dashboard.ExportHistory"];
        dateRange!.SetBounds(searchPanel.Left + search.Right + 12, searchPanel.Top, AppTypography.TextHeight(190), line);
        filters!.SetBounds(18 + origin.X, searchPanel.Bottom + 12, width, (int)Math.Ceiling(filterButtons.Count / (double)Math.Max(1, width / (AppTypography.TextHeight(144) + 10))) * AppTypography.TextHeight(42));
        var entries = services.History.Snapshot();
        foreach (var (button, filter) in filterButtons)
        {
            var count = entries.Count(e => InDateRange(e) && MatchesFilter(e, filter));
            button.Text = text[filter == "Live" ? "Nav.Live" : "Dashboard." + filter] + $" ({count})";
            button.Width = AppTypography.TextHeight(144); button.Height = AppTypography.TextHeight(36); DashboardVisual.Button(button); AppUi.NavigationButton(button, dashboardFilter == filter); DashboardVisual.Flat(button);
        }
        var tableTop = filters.Bottom + 12;
        tableHeader!.SetBounds(18 + origin.X, tableTop, center, AppTypography.TextHeight(36));
        var cols = HistoryTable.Columns(center);
        var compact = center < 850 || AppTypography.Scale > 1.25;
        for (var i = 0; i < tableHeadings.Count; i++) { var c = cols[i]; tableHeadings[i].SetBounds(c.Left, 0, c.Width, tableHeader.Height); tableHeadings[i].Text = text[(string)tableHeadings[i].Tag!]; tableHeadings[i].Visible = !compact || i == 0; }
        tableHeadings[2].Visible = false; dateSort!.Text = text["Dashboard.Date"] + (oldestFirst ? " ↑" : " ↓"); dateSort.SetBounds(cols[2].Left, 0, cols[2].Width, tableHeader.Height); DashboardVisual.Button(dateSort);
        if (compact) dateSort.SetBounds(center - AppTypography.TextHeight(140), 0, AppTypography.TextHeight(140), tableHeader.Height);
        var top = tableHeader.Bottom; var height = Math.Max(AppTypography.TextHeight(250), ClientSize.Height - (top - origin.Y) - line - 30);
        list.SetBounds(18 + origin.X, top, center, height); empty.SetBounds(list.Left, top, center, height);
        inspector.Visible = AppearancePresentation.Current.ModernDashboardShell.Inspector != DashboardInspectorMode.Hidden;
        inspector.SetBounds(inline ? list.Right + 12 : list.Left, inline ? top : list.Bottom + line + 20, inline ? right : center, inline ? height + line + 12 : AppTypography.TextHeight(380));
        previous!.SetBounds(list.Left + 12, list.Bottom + 10, 38, line); next!.SetBounds(previous.Right + 140, previous.Top, 38, line);
        paging!.SetBounds(previous.Right + 12, previous.Top + 4, 120, line);
        var countFiltered = entries.Count(e => FilterHistory(e) && Matches(e, search.Text.Trim())); var pages = Math.Max(1, (countFiltered + 39) / 40);
        paging.Text = $"{dashboardPage + 1} / {pages}"; previous.Enabled = dashboardPage > 0; next.Enabled = dashboardPage < pages - 1;
        previous.AccessibleName = text["Batch.Previous"]; next.AccessibleName = text["Batch.Next"]; dashboardTips.SetToolTip(previous, previous.AccessibleName); dashboardTips.SetToolTip(next, next.AccessibleName);
        emptyIcon.SetBounds((center - 44) / 2, Math.Max(20, height / 2 - 80), 44, 44);
        emptyTitle.SetBounds(20, emptyIcon.Bottom + 8, center - 40, AppTypography.TextHeight(40)); emptyBody.SetBounds(20, emptyTitle.Bottom + 8, center - 40, AppTypography.TextHeight(60));
        DashboardVisual.Button(export!, true); DashboardVisual.Button(previous); DashboardVisual.Button(next!);
        ResizeCards();
        var entry = entries.FirstOrDefault(e => e.QueueItemId == selectedId && e.FinalPath == selectedPath);
        if (entry is not null) inspector.ShowHistory(entry); else inspector.Clear();
    }
}

internal sealed partial class HistoryEntryCard
{
    private bool dashboard;
    private bool rowSelected;
    private readonly List<Label> cells = [];
    private AppVectorIcon? folderIcon;
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
    public void SetDashboard(bool value, bool selected)
    {
        dashboard = value; rowSelected = selected; TabStop = value; SetControlBehavior(ControlBehaviors.Selectable, value);
        AccessibleAutomationId = $"HistoryItem-{entry.QueueItemId:N}-{entry.LivePartIndex?.ToString() ?? "vod"}"; AccessibleName = entry.Title;
        AppUi.BindBackground(this, selected ? AppThemeTokens.RowSelected : AppThemeTokens.Surface);
        if (value) { AppTypography.Bind(title, 10.5f); AppTypography.Bind(facts, 9.5f); }
        AppTypography.SetDashboardPresentation(this, value);
        if (folderIcon is not null) folderIcon.Visible = value;
        if (!value) openFolder.Text = text["Common.OpenFolder"];
        LayoutControls();
    }
    private void LayoutDashboardCard()
    {
        while (cells.Count < 4) { var label = AppUi.Muted(); label.Multiline = true; AppTypography.Bind(label, 9.25f); cells.Add(label); Controls.Add(label); }
        var columns = HistoryTable.Columns(Width); var compact = Width < 850 || AppTypography.Scale > 1.25;
        var line = AppTypography.TextHeight(23); Height = compact ? line * 5 + 24 : AppTypography.TextHeight(78); Margin = new Padding(0,0,0,1);
        var imageWidth = 84; placeholder.SetBounds(12, 10, imageWidth, 54); thumbnail.Bounds = placeholder.Bounds;
        title.SetBounds(108, 8, columns[0].Width - 108, line * 2); title.AutoEllipsis = true; title.Multiline = true;
        facts.Text = entry.Channel; facts.SetBounds(title.Left, title.Bottom, title.Width, line); path.Visible = false;
        cells[0].Text = SourcePresentation.Host(entry.SourceUrl);
        cells[1].Text = entry.CompletedAt.ToLocalTime().ToString("yyyy-MM-dd\nHH:mm", text.Culture);
        cells[2].Text = System.IO.Path.GetExtension(entry.FinalPath).TrimStart('.').ToUpperInvariant() + "\n" + entry.Quality;
        cells[3].Text = text["Status." + (entry.WasPartial ? "Partial" : entry.Status)];
        for (var i = 0; i < 4; i++) { var c = columns[i + 1]; cells[i].SetBounds(c.Left, 12, c.Width - 8, line * 2); cells[i].Visible = !compact; cells[i].AutoEllipsis = true; }
        AppUi.BindForeground(cells[3], entry.WasPartial ? AppThemeTokens.Warning : entry.Status == "Completed" ? AppThemeTokens.Success : AppThemeTokens.Error);
        if (compact)
        {
            title.Width = Width - title.Left - 56;
            facts.Text = entry.Channel + " · " + SourcePresentation.Host(entry.SourceUrl) + "\n" + entry.CompletedAt.ToLocalTime().ToString("g", text.Culture) + " · " + System.IO.Path.GetExtension(entry.FinalPath).TrimStart('.').ToUpperInvariant() + " · " + entry.Quality;
            facts.Multiline = true; facts.SetBounds(title.Left, title.Bottom + 2, title.Width, line * 2);
            cells[3].Visible = true; cells[3].SetBounds(title.Left, facts.Bottom + 4, title.Width, line);
        }
        else facts.Multiline = false;
        if (folderIcon is null) { folderIcon = new AppVectorIcon(AppIconKind.Folder, 20); folderIcon.AccessibilityView = ModernFormsNext.Accessibility.AccessibilityView.Hidden; folderIcon.Click += (_,_) => openFolder.PerformClick(); openFolder.Controls.Add(folderIcon); }
        folderIcon.SetBounds(6,7,20,20);
        openFile.Visible = false; openFolder.SetBounds(Width - 42, 20, 32, 34); openFolder.Text = ""; openFolder.AccessibleName = text["Common.OpenFolder"];
        DashboardVisual.Button(openFolder); DashboardVisual.Flat(this, !rowSelected);
    }
}

internal static class HistoryTable
{
    internal static (int Left, int Width)[] Columns(int width)
    {
        var content = Math.Max(500, width - 52); var title = Math.Max(260, content * 42 / 100);
        var source = content * 14 / 100; var date = content * 15 / 100; var format = content * 16 / 100;
        return [(12, title), (title + 16, source), (title + source + 16, date), (title + source + date + 16, format), (title + source + date + format + 16, Math.Max(60, content - title - source - date - format))];
    }
}
