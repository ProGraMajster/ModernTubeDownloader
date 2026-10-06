using ModernFormsNext;
using ModernTubeDownloader.Localization;
using ModernTubeDownloader.Models;
using ModernTubeDownloader.Services;
using ModernTubeDownloader.Theming;

namespace ModernTubeDownloader.Views;

internal sealed class SupportedSourcesForm : Form
{
    private const int PageSize = 12;
    private readonly SupportedSourcesService sources;
    private readonly SourceCheckService checker;
    private readonly LocalizationService text;
    private readonly Panel body = new() { Dock = DockStyle.Fill };
    private readonly Label heading = AppUi.Heading(string.Empty, 22);
    private readonly Label hint = AppUi.Muted();
    private readonly Label version = AppUi.Muted();
    private readonly Label popularLabel = AppUi.Muted();
    private readonly Panel popular = new();
    private readonly TextBox search = new() { AccessibleAutomationId = "SupportedSourcesSearch" };
    private readonly CheckBox technical = new() { AccessibleAutomationId = "SupportedSourcesTechnicalToggle" };
    private readonly AppScrollFlowLayoutPanel list = new() { AutoScroll = true, FlowDirection = FlowDirection.TopDown,
        WrapContents = false, AccessibleAutomationId = "SupportedSourcesList" };
    private readonly Button previous = new() { AccessibleAutomationId = "SupportedSourcesPrevious" };
    private readonly Button next = new() { AccessibleAutomationId = "SupportedSourcesNext" };
    private readonly Label pageInfo = AppUi.Muted();
    private readonly Label generic = AppUi.Muted();
    private readonly TextBox url = new() { AccessibleAutomationId = "SupportedSourcesUrlInput" };
    private readonly Button check = new() { AccessibleAutomationId = "SupportedSourcesCheckButton" };
    private readonly TextBox result = new() { MultiLine = true, ReadOnly = true, TextAlign = ContentAlignment.TopLeft,
        ScrollBars = ScrollBars.Vertical, AccessibleAutomationId = "SupportedSourcesResult" };
    private readonly Button open = new() { Visible = false, AccessibleAutomationId = "SupportedSourcesOpenButton" };
    private readonly Button close = new() { AccessibleAutomationId = "SupportedSourcesCloseButton" };
    private readonly ToolTip tooltip = new();
    private readonly CancellationTokenSource lifetime = new();
    private CancellationTokenSource? catalogCancellation;
    private CancellationTokenSource? checkCancellation;
    private SupportedSourcesCatalog? catalog;
    private SourceCheckResult? checkedSource;
    private int page;
    private string? expandedRow;
    private bool disposed;
    public SourceCheckResult? NavigationRequest { get; private set; }

    public SupportedSourcesForm(SupportedSourcesService sources, SourceCheckService checker, LocalizationService text,
        bool loadOnShown = true)
    {
        this.sources = sources; this.checker = checker; this.text = text;
        AppBranding.Apply(this); AccessibilityObject.AutomationId = "SupportedSourcesWindow";
        version.AccessibleAutomationId = "SupportedSourcesVersion";
        Size = new System.Drawing.Size(980, 880); MinimumSize = new System.Drawing.Size(760, 720);
        AppUi.BindBackground(body); AppUi.BindBackground(popular); AppUi.BindBackground(list);
        hint.Multiline = generic.Multiline = true;
        AppUi.Input(search); AppUi.Input(url); AppUi.Input(result);
        result.Font = new Font("Segoe UI", 11);
        AppUi.BindForeground(technical); AppUi.Primary(check); AppUi.Primary(open);
        foreach (var button in new[] { previous, next, close }) AppUi.Secondary(button);
        body.Controls.AddRange([heading, hint, version, popularLabel, popular, search, technical, list,
            previous, pageInfo, next, generic, url, check, result, open, close]);
        Controls.Add(body);
        search.TextChanged += (_, _) => { page = 0; expandedRow = null; Render(); };
        technical.CheckedChanged += (_, _) => { page = 0; expandedRow = null; Render(); };
        previous.Click += (_, _) => { page--; Render(); };
        next.Click += (_, _) => { page++; Render(); };
        check.Click += async (_, _) => await CheckUrlAsync();
        open.Click += (_, _) => { NavigationRequest = checkedSource; DialogResult = DialogResult.OK; Close(); };
        close.Click += (_, _) => Close();
        sources.Changed += CatalogChanged; text.LanguageChanged += LanguageChanged;
        if (loadOnShown) Shown += async (_, _) => await LoadCatalogAsync();
        SizeChanged += (_, _) => LayoutPage();
        ApplyText(); LayoutPage();
    }
    private void CatalogChanged(object? sender, EventArgs e) => Application.RunOnUIThread(async () =>
    { if (!disposed) await LoadCatalogAsync(); });
    private void LanguageChanged(object? sender, EventArgs e) => Application.RunOnUIThread(() =>
    { if (!disposed) { ApplyText(); Render(); ShowCheckResult(); } });
    internal void PresentCatalog(SupportedSourcesCatalog value) { catalog = value; RenderPopular(); Render(); }
    private async Task LoadCatalogAsync()
    {
        catalogCancellation?.Cancel(); catalogCancellation?.Dispose();
        var request = catalogCancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        catalog = null; Render(); version.Text = text["Sources.Loading"];
        try
        {
            // Hashing/list extraction must not run on the UI thread.
            var value = await Task.Run(() => sources.GetAsync(request.Token), request.Token);
            if (!disposed && request == catalogCancellation) PresentCatalog(value);
        }
        catch (OperationCanceledException) { }
        catch (Exception) { if (!disposed && request == catalogCancellation) version.Text = text["Sources.CatalogFailed"]; }
    }
    private void ApplyText()
    {
        Text = heading.Text = text["Sources.Title"]; hint.Text = text["Sources.Intro"];
        popularLabel.Text = text["Sources.Popular"]; search.Placeholder = text["Sources.Search"];
        technical.Text = text["Sources.Technical"]; generic.Text = text["Sources.GenericHint"];
        url.Placeholder = "https://…"; check.Text = text["Sources.CheckUrl"];
        previous.Text = text["Playlist.PreviousPage"]; next.Text = text["Playlist.NextPage"];
        close.Text = text["Common.Close"];
        tooltip.SetToolTip(technical, text["Sources.TechnicalHint"]);
        if (checkedSource is null) result.Text = text["Sources.CheckHint"];
    }
    private void RenderPopular()
    {
        DisposeChildren(popular);
        if (catalog is null) return;
        var index = 0;
        foreach (var family in catalog.Sources.Where(item => item.IsPopular && !item.IsBroken)
            .Select(item => item.Family).Distinct(StringComparer.OrdinalIgnoreCase).Take(10))
        {
            var button = new Button { Text = family, Width = 145, Height = 30, AccessibleAutomationId = "SupportedSourcePopular" + index++ };
            AppUi.Secondary(button); button.Click += (_, _) => search.Text = family;
            popular.Controls.Add(button);
        }
        LayoutPage();
    }
    private void Render()
    {
        if (disposed) return;
        DisposeChildren(list);
        search.Enabled = technical.Enabled = catalog is not null;
        if (catalog is null) { DisposeChildren(popular); previous.Enabled = next.Enabled = false; pageInfo.Text = string.Empty; return; }
        version.Text = text.Get("Sources.Version", catalog.Version, catalog.Sources.Count,
            text[catalog.Managed ? "Sources.Managed" : "Sources.Custom"]);
        var filtered = SupportedSourcesService.Search(catalog.Sources, search.Text);
        // Data filtering/grouping precedes rendering. At most twelve row controls exist.
        var rows = technical.Checked ? filtered.Select(item => new[] { item }).ToArray()
            : filtered.GroupBy(item => item.Family, StringComparer.OrdinalIgnoreCase).Select(group => group.ToArray()).ToArray();
        page = Math.Clamp(page, 0, Math.Max(0, (rows.Length - 1) / PageSize));
        previous.Enabled = page > 0; next.Enabled = (page + 1) * PageSize < rows.Length;
        pageInfo.Text = text.Get("Sources.Page", page + 1, Math.Max(1, (rows.Length + PageSize - 1) / PageSize), rows.Length);
        var rowIndex = 0;
        foreach (var row in rows.Skip(page * PageSize).Take(PageSize))
        {
            var item = row[0];
            var rowKey = technical.Checked ? "extractor:" + item.ExtractorKey : "family:" + item.Family;
            var panel = new Panel { Height = expandedRow == rowKey ? 164 : 72, Margin = new Padding(0, 0, 0, 6) };
            AppUi.Card(panel);
            var label = technical.Checked ? item.ExtractorKey : $"{item.DisplayName} ({row.Length})";
            var button = new Button { Text = (panel.Height > 72 ? "▾ " : "▸ ") + label, Height = 30,
                AccessibleAutomationId = "SupportedSourceRow" + rowIndex++ };
            AppUi.Secondary(button);
            var status = AppUi.Muted(row.Length == 1 ? SourcePresentation.Status(item, text) :
                text.Get("Sources.GroupStatus", row.Length,
                    row.Count(source => source.VerificationStatus == SourceVerificationStatus.VerifiedByMtd),
                    row.Count(source => source.IsBroken)));
            tooltip.SetToolTip(button, label);
            tooltip.SetToolTip(status, row.Length == 1 && item.IsBroken ? text["Sources.Broken"] : text["Sources.TechnicalHint"]);
            var content = new TextBox { ReadOnly = true, MultiLine = true, TextAlign = ContentAlignment.TopLeft,
                ScrollBars = ScrollBars.Vertical, Visible = panel.Height > 72, Font = new Font("Segoe UI", 10) };
            AppUi.Input(content);
            content.Text = string.Join(Environment.NewLine, row.Select(source =>
                $"{source.ExtractorKey} — {SourcePresentation.Status(source, text)}{Environment.NewLine}{source.Description}" +
                (source.Verification.Count > 0 ? Environment.NewLine + SourcePresentation.VerificationText(source.ExtractorKey, text) : "")));
            button.Click += (_, _) => { expandedRow = expandedRow == rowKey ? null : rowKey; Render(); };
            panel.Controls.AddRange([button, status, content]);
            list.Controls.Add(panel);
        }
        LayoutRows();
    }
    private async Task CheckUrlAsync()
    {
        checkCancellation?.Cancel(); checkCancellation?.Dispose();
        var request = checkCancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        request.CancelAfter(TimeSpan.FromMinutes(2));
        check.Enabled = false; open.Visible = false; checkedSource = null; result.Text = text["Sources.Checking"];
        try
        {
            var value = await checker.CheckAsync(url.Text, request.Token);
            if (!disposed && request == checkCancellation) { checkedSource = value; ShowCheckResult(); }
        }
        catch (OperationCanceledException) { if (!disposed && request == checkCancellation) result.Text = text["Error.Download.Timeout"]; }
        finally { if (!disposed && request == checkCancellation) check.Enabled = true; }
    }
    private void ShowCheckResult()
    {
        if (checkedSource is not { } value) return;
        result.Text = value.Metadata is { } metadata
            ? metadata.Title + Environment.NewLine + SourcePresentation.Details(metadata, text)
            : text[value.MessageKey ?? "Sources.Unsupported"];
        if (value.AuthenticationMayBeRequired) result.Text += Environment.NewLine + text["Sources.Authentication"];
        open.Visible = !value.AuthenticationMayBeRequired && (value.Metadata is not null || value.OpenLive);
        open.Text = text[value.OpenLive ? "Sources.OpenLive" : "Sources.OpenDownloads"];
    }
    private void LayoutPage()
    {
        var width = Math.Max(680, body.ClientSize.Width - 40);
        heading.SetBounds(20, 12, width, 36); hint.SetBounds(20, 50, width, 42);
        version.SetBounds(20, 94, width, 24); popularLabel.SetBounds(20, 124, width, 22);
        popular.SetBounds(20, 148, width, 66);
        var columns = Math.Max(5, width / 151); var index = 0;
        var buttonWidth = (width - (columns - 1) * 6) / columns;
        foreach (var button in popular.Controls) { button.SetBounds(index % columns * (buttonWidth + 6), index / columns * 33, buttonWidth, 30); index++; }
        search.SetBounds(20, 222, width - 280, 36); technical.SetBounds(search.Right + 12, 222, 268, 36);
        var bottom = Math.Max(424, body.ClientSize.Height - 250);
        list.SetBounds(20, 266, width, Math.Max(120, bottom - 304));
        previous.SetBounds(20, list.Bottom + 6, 120, 30); next.SetBounds(160, list.Bottom + 6, 120, 30);
        pageInfo.SetBounds(292, list.Bottom + 6, width - 272, 30);
        generic.SetBounds(20, bottom + 2, width, 36);
        url.SetBounds(20, bottom + 42, width - 176, 36); check.SetBounds(url.Right + 12, bottom + 42, 164, 36);
        result.SetBounds(20, bottom + 84, width, 112);
        open.SetBounds(20, bottom + 204, 270, 34); close.SetBounds(width - 100, bottom + 204, 120, 34);
        LayoutRows();
    }
    private void LayoutRows()
    {
        foreach (var panel in list.Controls.OfType<Panel>())
        {
            panel.Width = Math.Max(620, list.DisplayRectangle.Width - 4);
            panel.Controls[0].SetBounds(8, 6, panel.Width - 16, 30);
            panel.Controls[1].SetBounds(12, 40, panel.Width - 24, 24);
            panel.Controls[2].SetBounds(8, 70, panel.Width - 16, 86);
        }
    }
    private static void DisposeChildren(Control owner)
    { foreach (var child in owner.Controls.ToArray()) { owner.Controls.Remove(child); child.Dispose(); } }
    protected override void Dispose(bool disposing)
    {
        if (disposing && !disposed)
        {
            disposed = true; lifetime.Cancel(); catalogCancellation?.Cancel(); checkCancellation?.Cancel();
            sources.Changed -= CatalogChanged; text.LanguageChanged -= LanguageChanged;
            catalogCancellation?.Dispose(); checkCancellation?.Dispose(); lifetime.Dispose(); tooltip.Dispose();
        }
        base.Dispose(disposing);
    }
}
