using System.Globalization;
using ModernFormsNext;
using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Localization;
using ModernTubeDownloader.Models;
using ModernTubeDownloader.Theming;
using ModernTubeDownloader.Utilities;
using SkiaSharp;

namespace ModernTubeDownloader.Views;

internal sealed class PlaylistPreviewControl : UserControl
{
    private const int PageSize = 40;
    private readonly LocalizationService text;
    private readonly Label title;
    private readonly Label facts;
    private readonly Label selectedCount;
    private readonly Button selectAll;
    private readonly Button clearSelection;
    private readonly Label qualityLabel;
    private readonly ComboBox quality;
    private readonly Label containerLabel;
    private readonly ComboBox container;
    private readonly MediaRangeEditor rangeEditor;
    private readonly Label rangeHint;
    private readonly Label feedback;
    private readonly AppScrollFlowLayoutPanel entries;
    private readonly Button previousPage;
    private readonly Button nextPage;
    private readonly Label pageText;
    private readonly Button addSelected;
    private readonly Button advancedOptions;
    private readonly List<PlaylistEntryRow> rows = [];
    private PlaylistSelection? selection;
    private int page;
    private bool binding;

    public PlaylistPreviewControl(LocalizationService text)
    {
        this.text = text;
        AccessibleAutomationId = "PlaylistPreview";
        AppUi.Card(this);
        title = AppUi.Heading(string.Empty, 17f);
        title.AccessibleAutomationId = "PlaylistTitle";
        facts = AppUi.Muted();
        selectedCount = AppUi.Muted();
        selectedCount.AccessibleAutomationId = "PlaylistSelectedCount";
        selectAll = new Button { AccessibleAutomationId = "PlaylistSelectAllButton" };
        clearSelection = new Button { AccessibleAutomationId = "PlaylistClearSelectionButton" };
        AppUi.Secondary(selectAll);
        AppUi.Secondary(clearSelection);
        selectAll.Click += (_, _) => { selection?.SelectAll(); RefreshSelection(); };
        clearSelection.Click += (_, _) => { selection?.Clear(); RefreshSelection(); };

        qualityLabel = AppUi.Muted();
        quality = new ComboBox { AccessibleAutomationId = "PlaylistQualitySelector" };
        AppUi.Input(quality);
        foreach (var preset in QualityPreset.All)
            quality.Items.Add(new LocalizedOption<QualityPreset>(preset, text[$"Quality.{preset.Id}"]));
        quality.SelectedIndex = 0;
        containerLabel = AppUi.Muted();
        container = new ComboBox { AccessibleAutomationId = "PlaylistContainerSelector" };
        AppUi.Input(container);
        foreach (var value in Enum.GetValues<PreferredVideoContainer>())
            container.Items.Add(new LocalizedOption<PreferredVideoContainer>(value, text[$"Container.{value}"]));
        container.SelectedIndex = 0;
        rangeEditor = new MediaRangeEditor(text, "Playlist");
        rangeEditor.ModeChanged += (_, _) => LayoutControls();
        rangeHint = AppUi.Muted();
        rangeHint.AccessibleAutomationId = "PlaylistRangeKeyframeHint";
        feedback = AppUi.Muted();
        feedback.AccessibleAutomationId = "PlaylistAddResult";

        entries = new AppScrollFlowLayoutPanel
        {
            AccessibleAutomationId = "PlaylistEntriesList",
            AutoScroll = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = new Padding(2)
        };
        AppUi.BindBackground(entries, AppThemeTokens.SurfaceSecondary);
        entries.SizeChanged += (_, _) => ResizeRows();

        previousPage = new Button { AccessibleAutomationId = "PlaylistPreviousPageButton" };
        nextPage = new Button { AccessibleAutomationId = "PlaylistNextPageButton" };
        AppUi.Secondary(previousPage);
        AppUi.Secondary(nextPage);
        previousPage.Click += (_, _) => ChangePage(page - 1);
        nextPage.Click += (_, _) => ChangePage(page + 1);
        pageText = AppUi.Muted();
        pageText.AccessibleAutomationId = "PlaylistPageCount";

        addSelected = new Button { AccessibleAutomationId = "PlaylistAddSelectedButton" };
        AppUi.Primary(addSelected);
        addSelected.Click += (_, _) => AddRequested?.Invoke(this, EventArgs.Empty);
        advancedOptions = new Button { AccessibleAutomationId = "PlaylistAdvancedOptionsButton" };
        AppUi.Secondary(advancedOptions);
        advancedOptions.Click += (_, _) => AdvancedOptionsRequested?.Invoke(this, EventArgs.Empty);
        Controls.AddRange([title, facts, selectedCount, selectAll, clearSelection, qualityLabel, quality,
            containerLabel, container, rangeEditor, rangeHint, feedback, entries,
            previousPage, nextPage, pageText, advancedOptions, addSelected]);
        SizeChanged += (_, _) => LayoutControls();
        text.LanguageChanged += LanguageChanged;
        ApplyLocalization();
        LayoutControls();
    }

    public event EventHandler? AddRequested;
    public event EventHandler? AdvancedOptionsRequested;
    public PlaylistSelection? Selection => selection;
    public QualityPreset SelectedPreset => quality.SelectedItem is LocalizedOption<QualityPreset> option ? option.Value : QualityPreset.All[0];
    public PreferredVideoContainer SelectedContainer => container.SelectedItem is LocalizedOption<PreferredVideoContainer> option
        ? option.Value : PreferredVideoContainer.Auto;
    public void SetFeedback(string message) => feedback.Text = message;
    public bool TryGetRange(out MediaTimeRange range, out string? error)
        => rangeEditor.TryGetRange(null, false, out range, out error);

    public void SetOptions(string? qualityPresetId, PreferredVideoContainer preferredContainer)
    {
        for (var index = 0; index < quality.Items.Count; index++)
            if (quality.Items[index] is LocalizedOption<QualityPreset> qualityOption && qualityOption.Value.Id == qualityPresetId)
            {
                quality.SelectedIndex = index;
                break;
            }
        for (var index = 0; index < container.Items.Count; index++)
            if (container.Items[index] is LocalizedOption<PreferredVideoContainer> containerOption && containerOption.Value == preferredContainer)
            {
                container.SelectedIndex = index;
                break;
            }
    }

    public void ShowPlaylist(
        PlaylistMetadata playlist,
        string? defaultPresetId,
        PreferredVideoContainer defaultContainer = PreferredVideoContainer.Auto)
    {
        selection = new PlaylistSelection(playlist);
        rangeEditor.Reset();
        page = 0;
        feedback.Text = string.Empty;
        SetOptions(defaultPresetId, defaultContainer);
        BuildPage();
        ApplyLocalization();
    }

    private void ChangePage(int target)
    {
        if (selection is null || target < 0 || target >= PageCount()) return;
        page = target;
        BuildPage();
        RefreshPageControls();
    }

    private int PageCount() => selection is null ? 0 : Math.Max(1, (selection.Playlist.EntryCount + PageSize - 1) / PageSize);

    private void BuildPage()
    {
        UiCrashDiagnostics.VerifyUiThread("playlist.page-rebuild");
        UiCrashDiagnostics.Record("playlist.rebuild.begin", "playlistEntries", entries.Controls.Count);
        foreach (var row in rows) row.Dispose();
        rows.Clear();
        entries.Controls.Clear();
        if (selection is null) return;
        binding = true;
        try
        {
            var start = page * PageSize;
            var end = Math.Min(selection.Playlist.EntryCount, start + PageSize);
            for (var ordinal = start; ordinal < end; ordinal++)
            {
                var entry = selection.Playlist.Entries[ordinal];
                var row = new PlaylistEntryRow(ordinal, entry, selection.IsSelected(ordinal));
                row.Selector.CheckedChanged += RowCheckedChanged;
                rows.Add(row);
                entries.Controls.Add(row);
            }
        }
        finally { binding = false; }
        RefreshRowText();
        RefreshSelection();
        ResizeRows();
        entries.VerticalScrollProperties.Value = 0;
        UiCrashDiagnostics.Record("playlist.rebuild.end", "playlistEntries", entries.Controls.Count);
    }

    private void RowCheckedChanged(object? sender, EventArgs e)
    {
        if (binding || selection is null || sender is not CheckBox { Tag: int ordinal } check) return;
        selection.Set(ordinal, check.Checked);
        RefreshSelectionCount();
    }

    private void RefreshSelection()
    {
        if (selection is null) return;
        binding = true;
        try
        {
            foreach (var row in rows)
                row.Selector.Checked = selection.IsSelected(row.Ordinal);
        }
        finally { binding = false; }
        RefreshSelectionCount();
    }

    private void RefreshSelectionCount()
    {
        if (selection is null) return;
        selectedCount.Text = text.Get("Playlist.SelectedCount", selection.SelectedCount, selection.Playlist.EntryCount);
        addSelected.Enabled = selection.SelectedCount > 0;
    }

    private void RefreshRowText()
    {
        if (selection is null) return;
        foreach (var row in rows)
        {
            row.SetText(text["Playlist.Unavailable"]);
        }
    }

    private void ApplyLocalization()
    {
        selectAll.Text = text["Playlist.SelectAll"];
        clearSelection.Text = text["Playlist.ClearSelection"];
        previousPage.Text = text["Playlist.PreviousPage"];
        nextPage.Text = text["Playlist.NextPage"];
        addSelected.Text = text["Playlist.AddSelected"];
        advancedOptions.Text = text["Advanced.MoreOptions"];
        qualityLabel.Text = text["Downloads.Quality"];
        containerLabel.Text = text["Downloads.Container"];
        rangeHint.Text = text["Range.KeyframeWarning"];
        if (selection is not null)
        {
            title.Text = string.IsNullOrWhiteSpace(selection.Playlist.Title) ? text["Playlist.Untitled"] : selection.Playlist.Title;
            facts.Text = text.Get("Playlist.Facts", selection.Playlist.Channel ?? text["Common.NotAvailable"], selection.Playlist.EntryCount);
            RefreshPageControls();
            RefreshRowText();
            RefreshSelectionCount();
        }
        var selectedId = SelectedPreset.Id;
        quality.Items.Clear();
        foreach (var preset in QualityPreset.All)
            quality.Items.Add(new LocalizedOption<QualityPreset>(preset, text[$"Quality.{preset.Id}"]));
        for (var index = 0; index < quality.Items.Count; index++)
            if (quality.Items[index] is LocalizedOption<QualityPreset> option && option.Value.Id == selectedId)
                quality.SelectedIndex = index;
        var selectedContainer = SelectedContainer;
        container.Items.Clear();
        foreach (var value in Enum.GetValues<PreferredVideoContainer>())
            container.Items.Add(new LocalizedOption<PreferredVideoContainer>(value, text[$"Container.{value}"]));
        for (var index = 0; index < container.Items.Count; index++)
            if (container.Items[index] is LocalizedOption<PreferredVideoContainer> option && option.Value == selectedContainer)
                container.SelectedIndex = index;
        LayoutControls();
    }

    private void LayoutControls()
    {
        var width = Math.Max(540, ClientSize.Width);
        title.SetBounds(20, 12, width - 40, 32);
        facts.SetBounds(20, 44, width - 40, 24);
        selectedCount.SetBounds(20, 70, Math.Max(180, width - 460), 26);
        selectAll.SetBounds(width - 284, 68, 126, 30);
        clearSelection.SetBounds(width - 150, 68, 130, 30);
        var optionWidth = Math.Min(220, Math.Max(150, (width - 60) / 2));
        qualityLabel.SetBounds(20, 101, optionWidth, 19);
        containerLabel.SetBounds(qualityLabel.Right + 10, 101, optionWidth, 19);
        quality.SetBounds(20, 122, optionWidth, 35);
        container.SetBounds(quality.Right + 10, 122, optionWidth, 35);
        if (width >= 960)
        {
            rangeEditor.SetBounds(width - 480, 122, 460, 34);
            rangeHint.SetBounds(20, 161, width - 40, 18);
            feedback.SetBounds(360, Height - 49, Math.Max(120, width - 610), 28);
            entries.SetBounds(20, rangeEditor.IsCustom ? 185 : 165, width - 40,
                Math.Max(90, Height - (rangeEditor.IsCustom ? 245 : 225)));
        }
        else
        {
            feedback.SetBounds(container.Right + 12, 125, Math.Max(100, width - container.Right - 32), 28);
            rangeEditor.SetBounds(20, 164, width - 40, 34);
            rangeHint.SetBounds(20, 201, width - 40, 18);
            entries.SetBounds(20, rangeEditor.IsCustom ? 225 : 205, width - 40,
                Math.Max(90, Height - (rangeEditor.IsCustom ? 285 : 265)));
        }
        rangeHint.Visible = rangeEditor.IsCustom;
        if (width >= 760)
        {
            previousPage.Text = text["Playlist.PreviousPage"];
            nextPage.Text = text["Playlist.NextPage"];
            previousPage.SetBounds(20, Height - 52, 92, 34);
            pageText.SetBounds(120, Height - 49, 130, 28);
            nextPage.SetBounds(252, Height - 52, 92, 34);
            advancedOptions.SetBounds(width - 360, Height - 53, 116, 36);
            addSelected.SetBounds(width - 234, Height - 53, 214, 36);
        }
        else
        {
            previousPage.Text = "‹";
            nextPage.Text = "›";
            previousPage.AccessibleName = text["Playlist.PreviousPage"];
            nextPage.AccessibleName = text["Playlist.NextPage"];
            previousPage.SetBounds(20, Height - 52, 64, 34);
            pageText.SetBounds(90, Height - 49, 64, 28);
            nextPage.SetBounds(160, Height - 52, 64, 34);
            advancedOptions.SetBounds(width - 308, Height - 53, 116, 36);
            addSelected.SetBounds(width - 184, Height - 53, 164, 36);
        }
        ResizeRows();
    }

    private void RefreshPageControls()
    {
        pageText.Text = text.Get("Playlist.Page", page + 1, PageCount());
        previousPage.Enabled = page > 0;
        nextPage.Enabled = page + 1 < PageCount();
    }

    private void ResizeRows()
    {
        var width = Math.Max(300, entries.DisplayRectangle.Width - 4);
        foreach (var row in rows) row.Width = width;
    }

    private void LanguageChanged(object? sender, EventArgs e) => Application.RunOnUIThread(ApplyLocalization);

    protected override void Dispose(bool disposing)
    {
        if (disposing) text.LanguageChanged -= LanguageChanged;
        base.Dispose(disposing);
    }
}

internal sealed class PlaylistEntryRow : Panel
{
    private readonly PlaylistEntry entry;
    private readonly Label number;
    private readonly Label title;
    private readonly Label detail;

    public PlaylistEntryRow(int ordinal, PlaylistEntry entry, bool selected)
    {
        this.entry = entry;
        Ordinal = ordinal;
        AccessibleAutomationId = $"PlaylistEntryRow{ordinal + 1}";
        Height = 42;
        Margin = new Padding(0, 0, 0, 2);
        SetControlBehavior(ControlBehaviors.Hoverable);
        AppUi.BindInteractiveSurface(this, AppThemeTokens.SurfaceSecondary, AppThemeTokens.SurfaceHover);

        Selector = new CheckBox
        {
            AccessibleAutomationId = $"PlaylistEntryToggle{ordinal + 1}",
            Checked = selected,
            Enabled = entry.IsAvailable,
            Tag = ordinal,
            Text = string.Empty
        };
        foreach (var style in new[] { Selector.Style, Selector.StyleHover, Selector.StyleFocused, Selector.StylePressed, Selector.StyleDisabled })
        {
            style.BackgroundBrush = null;
            style.BackgroundColor = SKColors.Transparent;
        }
        number = AppUi.Muted($"{entry.PlaylistIndex}.");
        number.TextAlign = ContentAlignment.MiddleRight;
        title = AppUi.Muted();
        title.AccessibleAutomationId = $"PlaylistEntryTitle{ordinal + 1}";
        title.AutoEllipsis = true;
        title.Multiline = true;
        title.Font = new Font("Segoe UI", 10.5f);
        AppUi.BindForeground(title, entry.IsAvailable ? AppThemeTokens.TextPrimary : AppThemeTokens.TextSecondary);
        detail = AppUi.Muted();
        detail.AccessibleAutomationId = $"PlaylistEntryDetail{ordinal + 1}";
        detail.TextAlign = ContentAlignment.MiddleRight;
        Controls.AddRange([Selector, number, title, detail]);
        SizeChanged += (_, _) => LayoutContent();
        LayoutContent();
    }

    public int Ordinal { get; }
    public CheckBox Selector { get; }

    public void SetText(string unavailable)
    {
        title.Text = string.IsNullOrWhiteSpace(entry.Title) ? unavailable : entry.Title;
        detail.Text = !entry.IsAvailable ? unavailable :
            entry.DurationSeconds is { } seconds ? FormatDuration(seconds) : string.Empty;
    }

    private static string FormatDuration(double seconds)
    {
        if (seconds >= TimeSpan.MaxValue.TotalSeconds - 1) return "—";
        var duration = TimeSpan.FromSeconds(seconds);
        return string.Create(CultureInfo.InvariantCulture, $"{(long)duration.TotalHours:D2}:{duration.Minutes:D2}:{duration.Seconds:D2}");
    }

    private void LayoutContent()
    {
        Selector.SetBounds(9, 9, 25, 25);
        number.SetBounds(40, 7, 40, 28);
        var detailWidth = Math.Min(112, Math.Max(82, Width / 5));
        detail.SetBounds(Width - detailWidth - 12, 7, detailWidth, 28);
        title.SetBounds(88, 7, Math.Max(0, Width - detailWidth - 108), 28);
    }
}
