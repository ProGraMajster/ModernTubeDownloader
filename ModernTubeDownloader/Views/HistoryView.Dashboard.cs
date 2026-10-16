using System.Text.Json;
using ModernFormsNext;
using ModernFormsNext.Accessibility;
using ModernTubeDownloader.Models;
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
    private readonly Dictionary<Button, string> filterButtons = [];
    private readonly ToolTip dashboardTips = new();
    public void SetDashboard(bool value)
    {
        dashboard = value;
        if (!value) { AutoScroll = false; AppUi.Card(searchPanel); }
        if (value && inspector is null)
        {
            inspector = new(services); inspector.RetryRequested += (_, source) => RetryRequested?.Invoke(this, source); filters = new FlowLayoutPanel { WrapContents = true }; AppUi.BindBackground(filters);
            foreach (var id in new[] { "All", "Completed", "Errors", "Live" })
            {
                var button = new Button { AccessibleAutomationId = "HistoryFilter" + id, Width = 154, Height = 36, Margin = new Padding(0, 0, 10, 6) };
                filterButtons[button] = id; button.Click += (_, _) => { dashboardFilter = id; dashboardPage = 0; RefreshItems(); }; filters.Controls.Add(button);
            }
            export = new Button { AccessibleAutomationId = "HistoryExport" }; AppUi.Primary(export); export.Click += async (_, _) => await ExportAsync();
            previous = new Button { Text = "â€ą", AccessibleAutomationId = "HistoryPrevious" }; next = new Button { Text = "â€ş", AccessibleAutomationId = "HistoryNext" };
            AppUi.Secondary(previous); AppUi.Secondary(next); previous.Click += (_, _) => { dashboardPage--; RefreshItems(); }; next.Click += (_, _) => { dashboardPage++; RefreshItems(); };
            paging = AppUi.Muted(); Controls.AddRange([inspector, filters, export, previous, next, paging]);
        }
        foreach (var control in new Control?[] { inspector, filters, export, previous, next, paging }) if (control is not null) control.Visible = value;
        RefreshItems(); UpdateLayout();
    }
    private static bool Completed(DownloadHistoryEntry entry) => !entry.WasPartial && entry.Status.Equals("Completed", StringComparison.OrdinalIgnoreCase);
    private bool FilterHistory(DownloadHistoryEntry entry) => dashboardFilter switch { "Completed" => Completed(entry), "Errors" => !Completed(entry), "Live" => entry.WasLiveRecording, _ => true };
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
        var exportWidth = AppTypography.TextHeight(180); search.SetBounds(0, 0, width - exportWidth - 12, line);
        export!.SetBounds(18 + origin.X + width - exportWidth, searchPanel.Top, exportWidth, line); export.Text = text["Dashboard.ExportHistory"];
        filters!.SetBounds(18 + origin.X, searchPanel.Bottom + 12, width, AppTypography.TextHeight(width < 780 ? 88 : 42));
        var entries = services.History.Snapshot();
        foreach (var (button, filter) in filterButtons)
        {
            var count = entries.Count(e => filter switch { "Completed" => Completed(e), "Errors" => !Completed(e), "Live" => e.WasLiveRecording, _ => true });
            button.Text = text[filter == "Live" ? "Nav.Live" : "Dashboard." + filter] + $" ({count})";
            button.Width = AppTypography.TextHeight(160); button.Height = AppTypography.TextHeight(36); AppUi.NavigationButton(button, dashboardFilter == filter);
        }
        var top = filters.Bottom + 12; var height = Math.Max(AppTypography.TextHeight(250), ClientSize.Height - (top - origin.Y) - line - 30);
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
        ResizeCards();
        var entry = entries.FirstOrDefault(e => e.QueueItemId == selectedId && e.FinalPath == selectedPath);
        if (entry is not null) inspector.ShowHistory(entry); else inspector.Clear();
    }
}

internal sealed partial class HistoryEntryCard
{
    private bool dashboard;
    private bool rowSelected;
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
        AppUi.BindBackground(this, selected ? AppThemeTokens.NavigationSelected : AppThemeTokens.Surface);
        if (value) { AppTypography.Bind(title, 10.5f, FontStyle.Bold); AppTypography.Bind(facts, 9.5f); }
        AppTypography.SetDashboardPresentation(this, value);
        LayoutControls();
    }
    private void LayoutDashboardCard()
    {
        var imageWidth = AppearancePresentation.ImageWidth(AppearancePresentation.Current.History.ThumbnailSize, 92);
        var line = AppTypography.TextHeight(24); var width = Math.Max(120, Width - imageWidth - 180);
        Height = Math.Max(imageWidth * 9 / 16 + 16, line * 3 + 12); Margin = new Padding(0, 0, 0, 1);
        placeholder.SetBounds(10, 8, imageWidth, imageWidth * 9 / 16); thumbnail.Bounds = placeholder.Bounds;
        title.SetBounds(imageWidth + 24, 6, width, line); title.AutoEllipsis = true;
        facts.SetBounds(title.Left, title.Bottom + 2, width, line * 2); facts.Multiline = true;
        path.Visible = false; openFile.SetBounds(Width - 140, 8, 128, AppTypography.TextHeight(30)); openFolder.SetBounds(Width - 140, openFile.Bottom + 6, 128, openFile.Height);
        Style.Border.Width = 1; Style.Border.Color = AppUi.GetColor(AppThemeTokens.Border); Style.Border.Radius = 5;
    }
}
