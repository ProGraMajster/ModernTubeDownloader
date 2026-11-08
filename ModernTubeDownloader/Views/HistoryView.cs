using ModernFormsNext;
using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Localization;
using ModernTubeDownloader.Models;
using ModernTubeDownloader.Services;
using ModernTubeDownloader.Theming;

namespace ModernTubeDownloader.Views;

internal sealed partial class HistoryView : UserControl
{
    private readonly AppServices services;
    private readonly LocalizationService text;
    private readonly Panel header;
    private readonly Label pageTitle;
    private readonly Label pageSubtitle;
    private readonly Panel searchPanel;
    private readonly TextBox search;
    private readonly FlowLayoutPanel list;
    private readonly Panel empty;
    private readonly AppVectorIcon emptyIcon;
    private readonly Label emptyTitle;
    private readonly Label emptyBody;
    private readonly List<HistoryEntryCard> cards = [];

    private readonly Panel pager = new();
    private readonly Button classicPrevious = new() { AccessibleAutomationId = "HistoryPreviousPage" };
    private readonly Button classicNext = new() { AccessibleAutomationId = "HistoryNextPage" };
    private readonly Label pageSummary = new() { AccessibleAutomationId = "HistoryPageSummary" };
    private readonly ModernFormsNext.Timer debounce = new() { Interval = 200 };
    private CancellationTokenSource? filtering;
    private DownloadHistoryEntry[] filtered = [];
    private int page;
    private int total;
    private long generation;
    private bool dirty = true;
    private bool disposed;
    private bool initialized;
    private bool resizingCards;
    private int changePosted;

    public HistoryView(AppServices services, bool useDashboard = false)
    {
        using var constructionTiming = PerformanceDiagnostics.Measure("history.constructor");
        this.services = services;
        text = services.Localization;
        Dock = DockStyle.Fill;
        AppUi.BindBackground(this);

        header = new Panel();
        AppUi.BindBackground(header);
        pageTitle = AppUi.Heading(string.Empty, 24f);
        pageSubtitle = AppUi.Muted();
        header.Controls.AddRange([pageTitle, pageSubtitle]);

        searchPanel = new Panel();
        AppUi.Card(searchPanel);
        search = new TextBox { Height = 44, AccessibleAutomationId = "HistorySearch" };
        AppUi.Input(search);
        search.TextChanged += SearchChanged;
        searchPanel.Controls.Add(search);

        list = new AppScrollFlowLayoutPanel
        {
            AccessibleAutomationId = "HistoryList",
            AutoScroll = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = new Padding(0, 0, 8, 0)
        };
        AppUi.BindBackground(list);
        list.SizeChanged += ListSizeChanged;

        empty = new Panel();
        AppUi.Card(empty, secondary: true);
        emptyIcon = new AppVectorIcon(AppIconKind.History, 44, AppThemeTokens.TextSecondary);
        emptyTitle = AppUi.Heading(string.Empty, 16f);
        emptyTitle.TextAlign = ContentAlignment.BottomCenter;
        emptyBody = AppUi.Muted();
        emptyBody.Multiline = true;
        emptyBody.TextAlign = ContentAlignment.TopCenter;
        empty.Controls.AddRange([emptyIcon, emptyTitle, emptyBody]);
        AppUi.BindBackground(pager);
        AppUi.Secondary(classicPrevious); AppUi.Secondary(classicNext);
        AppUi.BindBackground(pageSummary); AppTypography.Bind(pageSummary, AppTextToken.SmallText);
        pager.Controls.AddRange([classicPrevious, pageSummary, classicNext]);
        Controls.AddRange([header, searchPanel, list, empty, pager]);
        classicPrevious.Click += (_, _) => ChangePage(-1);
        classicNext.Click += (_, _) => ChangePage(1);
        debounce.Tick += (_, _) => { debounce.Stop(); RefreshItems(); };

        SizeChanged += ViewSizeChanged;
        services.History.Changed += HistoryChanged;
        text.LanguageChanged += LanguageChanged;
        if (useDashboard) SetDashboard(true);
        ApplyLocalization();
        initialized = true;
        UpdateLayout();
        AppTypography.RegisterLayout(this, UpdateLayout);
    }

    private bool arrangingHistory;
    private void UpdateLayout()
    {
        if (arrangingHistory) return;
        arrangingHistory = true;
        SuspendLayout();
        try { UpdateLayoutCore(); }
        finally { ResumeLayout(true); arrangingHistory = false; }
    }

    private void UpdateLayoutCore()
    {
        PerformanceDiagnostics.Increment("history.layouts");
        pager.Visible = !dashboard;
        if (dashboard) { LayoutDashboard(); return; }
        pageSubtitle.Visible = true;
        const int page = 28;
        var width = Math.Max(540, ClientSize.Width - (page * 2));
        header.SetBounds(page, 14, width, AppTypography.TextHeight(62));
        pageTitle.SetBounds(0, 0, width, 36);
        pageSubtitle.SetBounds(0, 38, width, 24);
        AppTypography.ReflowRows(header);
        searchPanel.SetBounds(page, header.Bottom + 8, width, AppTypography.TextHeight(76));
        search.SetBounds(18, 16, width - 36, 44);
        AppTypography.ReflowRows(searchPanel);
        var contentTop = searchPanel.Bottom + 14;
        var pagerHeight = AppTypography.TextHeight(38);
        var contentHeight = Math.Max(120, ClientSize.Height - contentTop - pagerHeight - 30);
        pager.SetBounds(page, contentTop + contentHeight + 8, width, pagerHeight);
        var buttonWidth = Math.Min(width / 3, AppTypography.TextHeight(130));
        classicPrevious.SetBounds(0, 0, buttonWidth, pagerHeight);
        classicNext.SetBounds(width - buttonWidth, 0, buttonWidth, pagerHeight);
        pageSummary.SetBounds(buttonWidth + 8, 0, Math.Max(1, width - 2 * buttonWidth - 16), pagerHeight);
        pageSummary.TextAlign = ContentAlignment.MiddleCenter;
        list.SetBounds(page, contentTop, width, contentHeight);
        empty.SetBounds(page, contentTop, width, contentHeight);
        var emptyCenter = Math.Max(90, contentHeight / 2);
        emptyIcon.SetBounds(Math.Max(12, (width - emptyIcon.Width) / 2), Math.Max(18, emptyCenter - 86), emptyIcon.Width, emptyIcon.Height);
        emptyTitle.SetBounds(24, emptyIcon.Bottom + 10, width - 48, 36);
        emptyBody.SetBounds(48, emptyTitle.Bottom + 6, width - 96, 52);
        ResizeCards();
    }

    private void HistoryChanged(object? sender, EventArgs e)
    {
        PerformanceDiagnostics.Increment("history.changed");
        if (Interlocked.Exchange(ref changePosted, 1) != 0) return;
        Application.RunOnUIThread(() =>
        {
            Interlocked.Exchange(ref changePosted, 0);
            if (disposed) return;
            generation++; filtering?.Cancel();
            dirty = true;
            if (Visible) { debounce.Stop(); debounce.Start(); }
        });
    }

    private void SearchChanged(object? sender, EventArgs e)
    {
        page = 0; dirty = true;
        generation++; filtering?.Cancel();
        debounce.Stop();
        if (Visible) debounce.Start();
    }

    private async void RefreshItems()
    {
        if (disposed || !initialized || !Visible || !dirty) return;
        using var refreshTiming = PerformanceDiagnostics.Measure("history.refresh");
        PerformanceDiagnostics.Increment("history.rebuilds");
        filtering?.Cancel(); filtering?.Dispose();
        var request = filtering = new CancellationTokenSource();
        var token = request.Token;
        var version = ++generation;
        var query = search.Text.Trim();
        var culture = text.Culture;
        var all = services.History.Snapshot();
        var useDashboard = dashboard; var filter = dashboardFilter; var dateDays = days; var ascending = oldestFirst;
        var dateCutoff = dateDays == 0 ? DateTimeOffset.MinValue : DateTimeOffset.UtcNow.AddDays(-dateDays);
        try
        {
            // No controls, IO or callbacks on the worker; stale projections cannot publish.
            var result = await Task.Run(() =>
            {
                using var timing = PerformanceDiagnostics.Measure("history.filter");
                var matches = HistoryPage.Filter(all, query, culture, token);
                if (useDashboard) matches = matches.Where(e => e.CompletedAt >= dateCutoff && MatchesFilter(e, filter)).ToArray();
                if (useDashboard && ascending) Array.Reverse(matches);
                var counts = new Dictionary<string,int>();
                foreach (var id in new[] { "All", "Completed", "Errors", "Audio", "Video", "Playlists", "Live" })
                { token.ThrowIfCancellationRequested(); counts[id] = all.Count(e => e.CompletedAt >= dateCutoff && MatchesFilter(e,id)); }
                return (matches, counts);
            }, token).ConfigureAwait(false);
            Application.RunOnUIThread(() =>
            {
                if (disposed || token.IsCancellationRequested || version != generation || !Visible) return;
                filtered = result.matches; filterCounts = result.counts; historySnapshot = all; total = all.Count; dirty = false;
                page = HistoryPage.Clamp(page, filtered.Length);
                RenderPage();
            });
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { services.Logger.Error("History projection failed.", error); }
    }

    private void ChangePage(int delta)
    {
        var target = HistoryPage.Clamp(page + delta, filtered.Length);
        if (target == page) return;
        page = target;
        RenderPage();
        list.VerticalScrollProperties.Value = 0;
    }

    private void RenderPage()
    {
        using var timing = PerformanceDiagnostics.Measure("history.page.render");
        UiCrashDiagnostics.VerifyUiThread("history.page");
        var entries = filtered.Skip(page * HistoryPage.Size).Take(HistoryPage.Size).ToArray();
        list.SuspendLayout();
        try
        {
            for (var index = 0; index < entries.Length; index++)
            {
                if (index == cards.Count)
                {
                    var card = new HistoryEntryCard(entries[index], text, services.Thumbnails);
                    card.SetDashboard(dashboard, selectedId == entries[index].QueueItemId && selectedPath == entries[index].FinalPath);
                    card.SelectionRequested += SelectHistory;
                    card.Width = Math.Max(dashboard ? 320 : 520, list.DisplayRectangle.Width - 4);
                    cards.Add(card); list.Controls.Add(card);
                }
                else cards[index].UpdateEntry(entries[index]);
                cards[index].SetDashboard(dashboard, selectedId == entries[index].QueueItemId && selectedPath == entries[index].FinalPath);
                cards[index].Visible = true;
            }
            for (var index = entries.Length; index < cards.Count; index++)
            {
                cards[index].SuspendThumbnail();
                cards[index].Visible = false;
            }
        }
        finally { list.ResumeLayout(true); }
        empty.Visible = filtered.Length == 0;
        list.Visible = filtered.Length != 0;
        emptyTitle.Text = total == 0 ? text["History.EmptyTitle"] : text["History.NoResultsTitle"];
        emptyBody.Text = total == 0 ? text["History.EmptyBody"] : text["History.NoResultsBody"];
        classicPrevious.Enabled = page > 0;
        classicNext.Enabled = page + 1 < HistoryPage.Count(filtered.Length);
        pageSummary.Text = text.Get("History.PageSummary", page + 1, HistoryPage.Count(filtered.Length), filtered.Length, total);
        ResizeCards(); list.PerformLayout();
        if (dashboard) UpdateLayout();
        PerformanceDiagnostics.Set("history.horizontalRange", list.HorizontalScrollProperties.Maximum - list.HorizontalScrollProperties.LargeChange + 1);
        PerformanceDiagnostics.Set("history.cards", cards.Count);
        PerformanceDiagnostics.Set("history.visible", entries.Length);
        PerformanceDiagnostics.Set("history.matches", filtered.Length);
        PerformanceDiagnostics.Emit("history.page.ready");
    }

    private void ResizeCards()
    {
        if (resizingCards) return;
        resizingCards = true;
        var width = Math.Max(dashboard ? 320 : 520, list.DisplayRectangle.Width - 4);
        list.SuspendLayout();
        try { foreach (var card in cards) card.Width = width; }
        finally { list.ResumeLayout(true); resizingCards = false; }
        if (dashboard) LayoutDashboardTableHeading(width);
    }

    private void ApplyLocalization()
    {
        pageTitle.Text = text["History.Title"];
        pageSubtitle.Text = text["History.Subtitle"];
        search.Placeholder = text["History.SearchPlaceholder"];
        classicPrevious.Text = text["Playlist.PreviousPage"];
        classicNext.Text = text["Playlist.NextPage"];
        if (dateRange is not null)
        {
            dateRange.Items[0] = text["Dashboard.AllDates"];
            dateRange.Items[1] = text["Dashboard.Last7Days"];
            dateRange.Items[2] = text["Dashboard.Last30Days"];
        }
        dirty = true;
        if (initialized && Visible) RefreshItems();
    }

    private void ViewSizeChanged(object? sender, EventArgs e) => UpdateLayout();
    private void ListSizeChanged(object? sender, EventArgs e) => ResizeCards();
    private void LanguageChanged(object? sender, EventArgs e) => Application.RunOnUIThread(ApplyLocalization);

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (!initialized || disposed) return;
        if (!Visible)
        {
            debounce.Stop(); filtering?.Cancel();
            return;
        }
        RefreshItems();
        foreach (var card in cards.Where(card => card.Visible)) card.RefreshFileAvailability();
        UpdateLayout();
        empty.Invalidate();
        emptyIcon.Invalidate();
        emptyTitle.Invalidate();
        emptyBody.Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !disposed)
        {
            disposed = true; dashboardTips.Dispose();
            debounce.Dispose(); filtering?.Cancel(); filtering?.Dispose();
            search.TextChanged -= SearchChanged;
            services.History.Changed -= HistoryChanged;
            text.LanguageChanged -= LanguageChanged;
            SizeChanged -= ViewSizeChanged;
            list.SizeChanged -= ListSizeChanged;
        }
        base.Dispose(disposing);
    }
}
