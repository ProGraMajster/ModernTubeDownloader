using System.Drawing;
using ModernFormsNext;
using ModernFormsNext.Accessibility;
using ModernTubeDownloader.Theming;
using SkiaSharp;

namespace ModernTubeDownloader.Views;

internal sealed partial class SettingsView
{
    private readonly Label categoryEmpty = AppUi.Muted();
    private readonly Panel searchResults = new() { Visible = false, AccessibleAutomationId = "SettingsSearchResults" };
    private readonly Label searchCount = AppUi.Heading(string.Empty, 11), searchPosition = AppUi.Muted();
    private readonly Button searchClear = new DashboardButton { Text = "×", AccessibleAutomationId = "SettingsSearchClear", Visible = false };
    private readonly Button searchPrevious = new DashboardButton { Text = "‹", AccessibleAutomationId = "SettingsSearchPrevious" };
    private readonly Button searchNext = new DashboardButton { Text = "›", AccessibleAutomationId = "SettingsSearchNext" };
    private readonly SettingsSearchResultButton[] searchRows = Enumerable.Range(0, 3).Select(index =>
        new SettingsSearchResultButton { AccessibleAutomationId = "SettingsSearchResult" + index }).ToArray();
    private readonly ModernFormsNext.Timer searchHighlightClock = new() { Interval = 3500 };
    private IReadOnlyList<SettingsSearchEntry> searchMatches = [];
    private int searchPage, searchPageSize = 3;
    private string lastSearchQuery = string.Empty;
    private SettingsSearchEntry? highlightedSearchEntry;
    private SettingsSearchEntry? pendingKeyboardSearchEntry;
    private Control? searchNavigationTarget;
    internal int SettingsSearchMatchCount => searchMatches.Count;
    internal Control? SettingsSearchHighlightedTarget => highlightedSearchEntry?.Target;

    private void InitializeSettingsSearch()
    {
        AppUi.Input(categorySearch); AppUi.Card(searchResults);
        foreach (var command in new[] { searchClear, searchPrevious, searchNext }) AppUi.Secondary(command);
        searchCount.AccessibleAutomationId = "SettingsSearchCount";
        searchPosition.AccessibleAutomationId = "SettingsSearchPosition";
        searchResults.Controls.AddRange([searchCount, searchPrevious, searchNext, searchPosition, .. searchRows]);
        Controls.AddRange([searchResults, searchClear]);
        categorySearch.TextChanged += (_, _) => { FilterSettingsCategories(); UpdateLayout(); };
        categorySearch.KeyDown += SettingsSearchKeyDown;
        categorySearch.KeyUp += SettingsSearchKeyUp;
        searchClear.Click += (_, _) => ClearSettingsSearch();
        searchPrevious.Click += (_, _) => ChangeSettingsSearchPage(-1);
        searchNext.Click += (_, _) => ChangeSettingsSearchPage(1);
        foreach (var row in searchRows)
        {
            AppUi.Secondary(row);
            row.Click += (_, _) => { if (row.Entry is { } entry) ActivateSettingsSearchResult(entry); };
            row.KeyDown += SettingsSearchKeyDown;
        }
        searchHighlightClock.Tick += (_, _) => ClearSettingsSearchHighlight();
        categoryEmpty.AccessibleAutomationId = "SettingsSearchEmpty";
    }

    private void SettingsSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (!dashboard) return;
        if (e.KeyCode == Keys.Escape)
        {
            e.Handled = e.SuppressKeyPress = true; ClearSettingsSearch();
        }
        else if (ReferenceEquals(sender, categorySearch) && searchMatches.Count > 0 && e.KeyCode is Keys.Enter or Keys.Down)
        {
            e.Handled = e.SuppressKeyPress = true;
            if (e.KeyCode == Keys.Down) searchRows[0].Select();
            else pendingKeyboardSearchEntry = searchMatches[searchPage * searchPageSize];
        }
    }

    private void SettingsSearchKeyUp(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode != Keys.Enter || pendingKeyboardSearchEntry is not { } entry) return;
        pendingKeyboardSearchEntry = null;
        e.Handled = e.SuppressKeyPress = true;
        // The original TextBox owns the complete key gesture. Moving focus on
        // KeyDown lets Button.OnKeyUp invoke the destination instead of revealing it.
        if (dashboard && categorySearch.Focused) ActivateSettingsSearchResult(entry);
    }

    private void ClearSettingsSearch()
    {
        ClearSettingsSearchNavigation(); categorySearch.Text = string.Empty; categorySearch.Select();
    }

    private void ChangeSettingsSearchPage(int delta)
    {
        ClearSettingsSearchNavigation();
        searchPage = Math.Clamp(searchPage + delta, 0, Math.Max(0, (searchMatches.Count - 1) / searchPageSize));
        PresentSettingsSearchResults(); searchRows.FirstOrDefault(row => row.Visible)?.Select();
    }

    private void FilterSettingsCategories()
    {
        var query = categorySearch.Text.Trim();
        if (!string.Equals(query, lastSearchQuery, StringComparison.Ordinal))
        {
            searchPage = 0; lastSearchQuery = query; ClearSettingsSearchNavigation();
        }
        // Safe caption bindings only; never editable values or dynamic status labels.
        // Reuse canonical controls: filtering does not reconstruct the settings tree.
        searchMatches = dashboard && query.Length > 0
            ? SettingsSearchIndex.Find(BuildSettingsSearchEntries(), query, text.Culture) : [];
        foreach (var (category, button) in categoryButtons)
            button.Visible = !dashboard || query.Length == 0 || searchMatches.Any(entry => entry.Category == category);
        if (!dashboard) ClearSettingsSearchNavigation();
        PresentSettingsSearchResults();
    }

    private IEnumerable<SettingsSearchEntry> BuildSettingsSearchEntries()
    {
        foreach (var (card, category) in cardCategories)
        {
            var titleKey = card == notificationCard ? "Notification.SettingsTitle" : card == startupCard ? "Tray.SettingsTitle" :
                card == updatesCard ? "Updates.Title" : card == textScaleCard ? "Appearance.TextTitle" :
                card == personalizationCard ? "Appearance.Title" : card == interfaceCard ? "Dashboard.InterfaceStyle" :
                localizedText.GetValueOrDefault(card.Controls.FirstOrDefault()!, "Settings.Category." + category);
            var categoryTitle = text["Settings.Category." + category]; var cardTitle = text[titleKey];
            var description = string.Join(' ', localizedText.Where(pair => pair.Key is Label &&
                SettingsSearchIndex.IsWithin(pair.Key, card) && !rowLabelBounds.ContainsKey((Label)pair.Key)).Select(pair => text[pair.Value]));
            yield return new(titleKey, cardTitle, category, categoryTitle, cardTitle, description, card, card, card);
            foreach (var (control, key) in localizedText)
            {
                if (!SettingsSearchIndex.IsWithin(control, card) || control == card.Controls.FirstOrDefault() ||
                    control is not (Label or CheckBox or Button)) continue;
                var target = SearchOptionTarget(key) ?? (control is CheckBox or Button ? control : null);
                if (target is null) continue;
                yield return new(key, text[key], category, categoryTitle, cardTitle, string.Empty, card, control, target);
            }
            foreach (var (id, key) in SettingsSearchIndex.PanelOptions)
            {
                var target = SettingsSearchIndex.Descendants(card).FirstOrDefault(control => control.AccessibleAutomationId == id);
                if (target is null) continue;
                yield return new(key, text[key], category, categoryTitle, cardTitle, string.Empty, card, target, target);
            }
            if (card == notificationCard)
                foreach (var target in SettingsSearchIndex.Descendants(card).OfType<CheckBox>())
                {
                    var id = target.AccessibleAutomationId;
                    if (id is null || !id.StartsWith("Notification", StringComparison.Ordinal) || id == "NotificationDiagnostics") continue;
                    var key = "Notification.Preference." + id["Notification".Length..];
                    yield return new(key, text[key], category, categoryTitle, cardTitle, string.Empty, card, target, target);
                }
            if (card == personalizationCard)
                foreach (var option in PersonalizationSettingsPanel.SearchOptions)
                {
                    var location = text.Get("Settings.SearchLocation", cardTitle, text["Appearance.Section." + option.Section]);
                    yield return new(option.CaptionKey, text[option.CaptionKey], category, categoryTitle, location,
                        string.Empty, card, card, card, option);
                }
        }
    }

    private Control? SearchOptionTarget(string key) => key switch
    {
        "Settings.TemporaryDirectory" => temporaryDirectory, "Settings.OutputDirectory" => outputDirectory,
        "Settings.DefaultQuality" => defaultQuality, "Settings.DefaultContainer" => defaultContainer,
        "Settings.Concurrency" => concurrency, "Settings.FilenameTemplate" => filenameTemplate,
        "Settings.Conflict" => conflict, "Settings.AdditionalRetryAttempts" => additionalRetryAttempts,
        "Settings.MinimumInterDownloadDelay" => minimumInterDownloadDelay, "Settings.MaximumInterDownloadDelay" => maximumInterDownloadDelay,
        "Settings.LiveConcurrency" => liveConcurrency, "Settings.MinimumLiveCheck" => minimumLiveCheck,
        "Settings.MaximumLiveCheck" => maximumLiveCheck, "Settings.SubtitleLanguages" => subtitleLanguages,
        "Settings.SubtitleFormat" => subtitleFormat, "Settings.SponsorBlockMode" => sponsorMode,
        "Settings.SponsorBlockCategories" => sponsorCategories, "Settings.CookiesFile" => cookiesBrowse,
        "Settings.Theme" => theme, "Settings.Language" => language, "Settings.RemotePort" => remotePort,
        "Settings.RemoteAddress" => remoteAddress, "Settings.CustomArguments" => customArguments,
        "Settings.CustomYtDlp" => ytDlpPath, "Settings.CustomFfmpeg" => ffmpegPath, "Settings.CustomDeno" => denoPath,
        _ => null
    };

    private void PresentSettingsSearchResults()
    {
        searchResults.Visible = dashboard && categorySearch.Text.Trim().Length > 0;
        searchClear.Visible = dashboard && categorySearch.Text.Length > 0;
        searchClear.AccessibleName = text["Settings.SearchClear"];
        searchPrevious.AccessibleName = text["Playlist.PreviousPage"];
        searchNext.AccessibleName = text["Playlist.NextPage"];
        searchCount.Text = text.Get("Settings.SearchResults", searchMatches.Count);
        searchPage = Math.Clamp(searchPage, 0, Math.Max(0, (searchMatches.Count - 1) / searchPageSize));
        var first = searchPage * searchPageSize;
        for (var index = 0; index < searchRows.Length; index++)
        {
            var row = searchRows[index]; var match = first + index;
            row.Entry = index < searchPageSize && match < searchMatches.Count ? searchMatches[match] : null;
            row.Visible = row.Entry is not null; row.Text = row.Entry?.Caption ?? string.Empty;
            row.LocationCaption = row.Entry is { } entry ? text.Get("Settings.SearchLocation", entry.CategoryCaption, entry.CardCaption) : string.Empty;
            row.AccessibleName = row.Entry is null ? string.Empty : row.Text + " · " + row.LocationCaption;
            compactTips.SetToolTip(row, row.AccessibleName); row.Invalidate();
        }
        searchPosition.Text = text.Get("Settings.SearchResultPosition", searchMatches.Count == 0 ? 0 : first + 1,
            Math.Min(searchMatches.Count, first + searchPageSize), searchMatches.Count);
        searchPrevious.Enabled = searchPage > 0; searchNext.Enabled = first + searchPageSize < searchMatches.Count;
    }

    private void LayoutSettingsSearch()
    {
        if (!dashboard) { searchResults.Visible = searchClear.Visible = false; return; }
        if (searchClear.Visible)
        {
            var side = categorySearch.Height;
            searchClear.SetBounds(categorySearch.Right - side, categorySearch.Top, side, side);
            categorySearch.Width = Math.Max(1, categorySearch.Width - side - 6);
        }
        if (!searchResults.Visible) return;
        var line = AppTypography.TextHeight(30); var rowHeight = AppTypography.TextHeight(54);
        searchPageSize = Math.Clamp((Math.Max(rowHeight + line * 2, ClientSize.Height / 3) - line * 2) / rowHeight, 1, searchRows.Length);
        PresentSettingsSearchResults();
        var rows = Math.Max(1, Math.Min(searchPageSize, searchMatches.Count));
        var height = line * 2 + rows * rowHeight + 12;
        searchResults.SetBounds(24, categories.Bottom + 8, Math.Max(1, ClientSize.Width - 48), height);
        searchCount.SetBounds(12, 4, Math.Max(1, searchResults.Width - 24), line);
        for (var index = 0; index < searchRows.Length; index++)
            searchRows[index].SetBounds(8, line + 4 + index * rowHeight, Math.Max(1, searchResults.Width - 16), rowHeight - 4);
        var footer = line + 4 + rows * rowHeight;
        searchPrevious.SetBounds(8, footer, line, line); searchNext.SetBounds(searchResults.Width - line - 8, footer, line, line);
        searchPosition.SetBounds(line + 16, footer, Math.Max(1, searchResults.Width - line * 2 - 32), line);
        categoryEmpty.Bounds = searchMatches.Count == 0 ? new(36, searchResults.Top + line + 6, Math.Max(1, searchResults.Width - 24), rowHeight) : Rectangle.Empty;
        scroll.SetBounds(0, searchResults.Bottom + 8, ClientSize.Width, Math.Max(1, ClientSize.Height - searchResults.Bottom - 20));
    }

    private void ActivateSettingsSearchResult(SettingsSearchEntry entry)
    {
        if (!dashboard || !SettingsSearchIndex.IsWithin(entry.Card, this) || !SettingsSearchIndex.IsWithin(entry.Target, entry.Card)) return;
        ShowCategory(entry.Category);
        if (entry.Personalization is { } option)
        {
            if (personalizationCard.RevealSearchOption(option) is not { } controls) return;
            entry = entry with { CaptionControl = controls.Caption, Target = controls.Input };
            UpdateLayout();
        }
        var target = entry.Target;
        if (target == entry.Card) target = SettingsSearchIndex.Descendants(entry.Card).FirstOrDefault(control => control.CanSelect) ?? target;
        // Canonical reveal respects nested viewports and does not change values.
        if (!target.AccessibilityObject.PerformAction(AccessibleActions.ScrollIntoView))
        {
            // Disabled settings are still valid search results. Their own semantic
            // action is disabled, so reveal through the enabled canonical viewport.
            var bounds = SettingsSearchIndex.BoundsIn(target, scroll);
            var delta = bounds.Top < 8 ? bounds.Top - 8 : bounds.Bottom > scroll.Height - 8 ?
                Math.Min(bounds.Top - 8, bounds.Bottom - scroll.Height + 8) : 0;
            if (delta != 0) scroll.AccessibilityObject.PerformAction(AccessibleActions.Scroll,
                AccessibleScrollRequest.ByViewport(0, (double)delta / Math.Max(1, scroll.Height)));
        }
        if (target.CanSelect) target.Select(); else categorySearch.Select();
        highlightedSearchEntry = entry with { Target = target };
        searchNavigationTarget = target;
        target.KeyDown += SettingsSearchKeyDown;
        searchHighlightClock.Stop(); searchHighlightClock.Start(); Invalidate();
    }

    private void ClearSettingsSearchHighlight()
    {
        searchHighlightClock.Stop();
        highlightedSearchEntry = null; Invalidate();
    }

    private void ClearSettingsSearchNavigation()
    {
        ClearSettingsSearchHighlight(); pendingKeyboardSearchEntry = null;
        if (searchNavigationTarget is { } target) target.KeyDown -= SettingsSearchKeyDown;
        searchNavigationTarget = null;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (!dashboard || highlightedSearchEntry is not { } entry || !SettingsSearchIndex.IsWithin(entry.Target, this) || !entry.Target.Visible) return;
        var bounds = SettingsSearchIndex.BoundsIn(entry.Target, this);
        if (entry.CaptionControl.Visible && SettingsSearchIndex.IsWithin(entry.CaptionControl, entry.Card))
            bounds = Rectangle.Union(bounds, SettingsSearchIndex.BoundsIn(entry.CaptionControl, this));
        bounds.Inflate(4, 4);
        e.Canvas.Save();
        e.Canvas.ClipRect(new SKRect(LogicalToDeviceUnits(scroll.Left), LogicalToDeviceUnits(scroll.Top), LogicalToDeviceUnits(scroll.Right), LogicalToDeviceUnits(scroll.Bottom)));
        using var paint = new SKPaint { Color = SettingsSearchIndex.HighlightColor(entry.Card.CurrentStyle.GetBackgroundColor()), IsAntialias = true,
            Style = SKPaintStyle.Stroke, StrokeWidth = LogicalToDeviceUnits(2) };
        e.Canvas.DrawRoundRect(new SKRect(LogicalToDeviceUnits(bounds.Left), LogicalToDeviceUnits(bounds.Top),
            LogicalToDeviceUnits(bounds.Right), LogicalToDeviceUnits(bounds.Bottom)), LogicalToDeviceUnits(5), LogicalToDeviceUnits(5), paint);
        e.Canvas.Restore();
    }

    private void DisposeSettingsSearch() { ClearSettingsSearchNavigation(); searchHighlightClock.Dispose(); }
}
