using ModernFormsNext;
using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Localization;
using ModernTubeDownloader.Models;
using ModernTubeDownloader.Services;
using ModernTubeDownloader.Theming;

namespace ModernTubeDownloader.Views;

internal sealed class HistoryView : UserControl
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

    public HistoryView(AppServices services)
    {
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
        Controls.AddRange([header, searchPanel, list, empty]);

        SizeChanged += ViewSizeChanged;
        services.History.Changed += HistoryChanged;
        text.LanguageChanged += LanguageChanged;
        ApplyLocalization();
        RefreshItems();
        UpdateLayout();
    }

    private void UpdateLayout()
    {
        const int page = 28;
        var width = Math.Max(540, ClientSize.Width - (page * 2));
        header.SetBounds(page, 14, width, 62);
        pageTitle.SetBounds(0, 0, width, 36);
        pageSubtitle.SetBounds(0, 38, width, 24);
        searchPanel.SetBounds(page, 84, width, 76);
        search.SetBounds(18, 16, width - 36, 44);
        var contentTop = searchPanel.Bottom + 14;
        var contentHeight = Math.Max(120, ClientSize.Height - contentTop - 18);
        list.SetBounds(page, contentTop, width, contentHeight);
        empty.SetBounds(page, contentTop, width, contentHeight);
        var emptyCenter = Math.Max(90, contentHeight / 2);
        emptyIcon.SetBounds(Math.Max(12, (width - emptyIcon.Width) / 2), Math.Max(18, emptyCenter - 86), emptyIcon.Width, emptyIcon.Height);
        emptyTitle.SetBounds(24, emptyIcon.Bottom + 10, width - 48, 36);
        emptyBody.SetBounds(48, emptyTitle.Bottom + 6, width - 96, 52);
        ResizeCards();
    }

    private void HistoryChanged(object? sender, EventArgs e) => Application.RunOnUIThread(RefreshItems);

    private void SearchChanged(object? sender, EventArgs e) => RefreshItems();

    private void RefreshItems()
    {
        UiCrashDiagnostics.VerifyUiThread("history.rebuild");
        UiCrashDiagnostics.Record("history.rebuild.begin", "historyList", list.Controls.Count);
        foreach (var card in cards)
            card.Dispose();
        cards.Clear();
        list.Controls.Clear();
        var query = search.Text.Trim();
        var all = services.History.Snapshot();
        var filtered = all.Where(entry => Matches(entry, query)).ToArray();
        foreach (var entry in filtered)
        {
            var card = new HistoryEntryCard(entry, text, services.Thumbnails);
            cards.Add(card);
            list.Controls.Add(card);
        }

        empty.Visible = filtered.Length == 0;
        list.Visible = filtered.Length != 0;
        if (empty.Visible)
        {
            emptyTitle.Text = all.Count == 0 ? text["History.EmptyTitle"] : text["History.NoResultsTitle"];
            emptyBody.Text = all.Count == 0 ? text["History.EmptyBody"] : text["History.NoResultsBody"];
            empty.BringToFront();
        }
        else
        {
            list.BringToFront();
        }
        ResizeCards();
        UiCrashDiagnostics.Record("history.rebuild.end", "historyList", list.Controls.Count);
    }

    private static bool Matches(DownloadHistoryEntry entry, string query)
    {
        if (query.Length == 0)
            return true;
        return entry.Title.Contains(query, StringComparison.CurrentCultureIgnoreCase)
            || entry.Channel.Contains(query, StringComparison.CurrentCultureIgnoreCase)
            || entry.Quality.Contains(query, StringComparison.CurrentCultureIgnoreCase)
            || entry.FinalPath.Contains(query, StringComparison.CurrentCultureIgnoreCase);
    }

    private void ResizeCards()
    {
        var width = Math.Max(520, list.DisplayRectangle.Width - 4);
        foreach (var card in cards)
            card.Width = width;
    }

    private void ApplyLocalization()
    {
        pageTitle.Text = text["History.Title"];
        pageSubtitle.Text = text["History.Subtitle"];
        search.Placeholder = text["History.SearchPlaceholder"];
        RefreshItems();
    }

    private void ViewSizeChanged(object? sender, EventArgs e) => UpdateLayout();
    private void ListSizeChanged(object? sender, EventArgs e) => ResizeCards();
    private void LanguageChanged(object? sender, EventArgs e) => Application.RunOnUIThread(ApplyLocalization);

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (!Visible)
            return;
        RefreshItems();
        UpdateLayout();
        empty.Invalidate();
        emptyIcon.Invalidate();
        emptyTitle.Invalidate();
        emptyBody.Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            services.History.Changed -= HistoryChanged;
            text.LanguageChanged -= LanguageChanged;
            SizeChanged -= ViewSizeChanged;
            list.SizeChanged -= ListSizeChanged;
        }
        base.Dispose(disposing);
    }
}
