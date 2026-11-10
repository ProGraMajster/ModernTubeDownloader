using ModernFormsNext;
using ModernFormsNext.Accessibility;
using ModernTubeDownloader.Services;
using ModernTubeDownloader.Theming;

namespace ModernTubeDownloader.Views;

internal sealed partial class LiveView
{
    private FlowLayoutPanel? liveSources;
    private TextBox? liveSearch;
    private ComboBox? liveSort;
    private Button? livePrevious, liveNext;
    private Label? livePaging;
    private readonly Dictionary<Button, SourceBrand?> liveSourceButtons = [];
    private SourceBrand? liveSource;
    private int livePage;
    private LiveDashboardPage liveProjection = new([], 0, 0, 0, 0);
    private bool refreshingSessions;

    private void CreateLiveNavigation()
    {
        liveSources = new FlowLayoutPanel { WrapContents = true, AccessibleAutomationId = "LiveSourceFilters" };
        AppUi.BindBackground(liveSources);
        foreach (var source in new SourceBrand?[] { null, SourceBrand.YouTube, SourceBrand.Twitch, SourceBrand.Kick, SourceBrand.Facebook })
        {
            var button = new DashboardButton { AccessibleAutomationId = "LiveSource" + (source?.ToString() ?? "All"), Margin = new Padding(0, 0, 8, 6) };
            liveSourceButtons[button] = source;
            button.Click += (_, _) => { liveSource = source; ResetLivePage(); };
            liveSources.Controls.Add(button);
        }
        liveSearch = new TextBox { AccessibleAutomationId = "LiveSearch" }; AppUi.Input(liveSearch);
        liveSearch.TextChanged += (_, _) => ResetLivePage();
        liveSort = new ComboBox { AccessibleAutomationId = "LiveSort" }; AppUi.Input(liveSort);
        foreach (var key in new[] { "Live.SortNewest", "Live.SortOldest", "Dashboard.Title" }) liveSort.Items.Add(text[key]);
        liveSort.SelectedIndex = 0;
        liveSort.SelectedIndexChanged += (_, _) => ResetLivePage();
        livePrevious = new DashboardButton { Text = "‹", AccessibleAutomationId = "LivePrevious" };
        liveNext = new DashboardButton { Text = "›", AccessibleAutomationId = "LiveNext" };
        livePrevious.Click += (_, _) => { livePage--; RefreshSessions(); ResetLiveScroll(); };
        liveNext.Click += (_, _) => { livePage++; RefreshSessions(); ResetLiveScroll(); };
        livePaging = AppUi.Muted(); livePaging.AccessibleAutomationId = "LivePageSummary";
        scroll.Controls.AddRange([liveSources, liveSearch, liveSort, livePrevious, liveNext, livePaging]);
    }

    private void ResetLivePage() { livePage = 0; RefreshSessions(); ResetLiveScroll(); }
    private void ResetLiveScroll() => scroll.VerticalScrollProperties.Value = scroll.VerticalScrollProperties.Minimum;

    private void SetLiveNavigationVisible(bool visible)
    {
        foreach (var control in new Control?[] { liveSources, liveSearch, liveSort, livePrevious, liveNext, livePaging })
            if (control is not null) { control.Visible = visible; if (!visible) control.SetBounds(0, 0, 0, 0); }
    }

    private int LayoutLiveSources(int left, int top, int width)
    {
        var line = AppTypography.TextHeight(36);
        foreach (var entry in liveSourceButtons)
        {
            var button = entry.Key;
            button.Text = entry.Value switch { null => text["Dashboard.All"], SourceBrand.YouTube => "YouTube Live", SourceBrand.Facebook => "Facebook Live", _ => entry.Value.ToString()! };
            DownloadsVisual.Button(button, entry.Value == liveSource);
            AppTypography.Bind(button, 9.5f);
            button.Width = Math.Min(width, DashboardVisual.ActionWidth(button, entry.Value is not null)); button.Height = line;
            if (entry.Value is { } brand) DownloadsVisual.Source(button, brand);
        }
        // The existing supported-site dialog remains the authority for provider capabilities.
        sourceLive!.Text = text["Sources.Link"]; DownloadsVisual.Button(sourceLive); DashboardVisual.Icon(sourceLive, AppIconKind.Link);
        sourceLive.Width = Math.Min(width, DashboardVisual.ActionWidth(sourceLive, true)); sourceLive.Height = line;
        var used = 0; var rows = 1;
        foreach (var control in liveSources!.Controls)
        {
            if (used > 0 && used + control.Width + control.Margin.Right > width) { rows++; used = 0; }
            used += control.Width + control.Margin.Right;
        }
        liveSources.SetBounds(left, top, width, rows * (line + 6));
        return liveSources.Bottom;
    }

    private int LayoutLiveTools(int left, int top, int width)
    {
        var line = AppTypography.TextHeight(34);
        var sortWidth = Math.Min(width, AppTypography.TextHeight(190));
        liveSearch!.Placeholder = text["Live.Search"];
        liveSort!.AccessibleName = text["Live.Sort"];
        for (var index = 0; index < 3; index++) liveSort.Items[index] = text[new[] { "Live.SortNewest", "Live.SortOldest", "Dashboard.Title" }[index]];
        var inline = width >= AppTypography.TextHeight(460);
        liveSearch.SetBounds(left, top, inline ? width - sortWidth - 10 : width, line);
        liveSort.SetBounds(inline ? liveSearch.Right + 10 : left, inline ? top : liveSearch.Bottom + 8, sortWidth, line);
        return liveSort.Bottom;
    }

    private int LayoutLivePaging(int left, int top, int width)
    {
        var line = AppTypography.TextHeight(32);
        livePrevious!.Enabled = liveProjection.Index > 0; liveNext!.Enabled = liveProjection.Index + 1 < liveProjection.PageCount;
        livePrevious.AccessibleName = text["Live.PreviousPage"]; liveNext.AccessibleName = text["Live.NextPage"];
        livePaging!.Text = text.Get("Live.PageSummary", liveProjection.Total == 0 ? 0 : liveProjection.Index * LiveDashboardPage.PageSize + 1,
            liveProjection.Index * LiveDashboardPage.PageSize + liveProjection.Items.Length, liveProjection.Total);
        livePaging.TextAlign = ContentAlignment.MiddleLeft;
        livePrevious.SetBounds(left + width - line * 2 - 8, top, line, line);
        liveNext.SetBounds(livePrevious.Right + 8, top, line, line);
        livePaging.SetBounds(left, top, Math.Max(0, width - line * 2 - 20), line);
        DownloadsVisual.Button(livePrevious); DownloadsVisual.Button(liveNext);
        return top + line;
    }
}

internal sealed partial class LiveSessionCard
{
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!dashboard || e.Handled || current is null || e.KeyCode is not (Keys.Enter or Keys.Space)) return;
        e.Handled = e.SuppressKeyPress = true;
        SelectionRequested?.Invoke(this, current);
    }
    protected override AccessibleObject CreateAccessibilityInstance() => new LiveRowAccessibleObject(this);
    private sealed class LiveRowAccessibleObject(LiveSessionCard row) : ControlAccessibleObject(row)
    {
        public override AccessibleControlType ControlType => row.dashboard ? AccessibleControlType.ListItem : base.ControlType;
        public override AccessibleStates State => base.State | (row.dashboard ? AccessibleStates.Selectable | (row.selected ? AccessibleStates.Selected : AccessibleStates.None) : AccessibleStates.None);
        public override AccessibleActions SupportedActions => base.SupportedActions | (row.dashboard && (base.SupportedActions & AccessibleActions.Focus) != 0 ? AccessibleActions.Select : AccessibleActions.None);
        public override bool PerformAction(AccessibleActions action, object? parameter = null)
        {
            if (action == AccessibleActions.Select && parameter is null && row.current is { } session && (SupportedActions & action) != 0)
            { row.Select(); row.SelectionRequested?.Invoke(row, session); return true; }
            return base.PerformAction(action, parameter);
        }
    }
}
