using System.Text.Json;
using ModernFormsNext;
using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Models;
using ModernTubeDownloader.Services;
using ModernTubeDownloader.Theming;

namespace ModernTubeDownloader.Views;

/// <summary>Selected-item presentation. All writes go through the existing queue under its lock.</summary>
internal sealed class DashboardInspector : AppScrollPanel
{
    private readonly AppServices services;
    public event EventHandler<string>? RetryRequested;
    private bool historyMode;
    private readonly Button retry = new DashboardButton() { AccessibleAutomationId = "HistoryRetry" };
    private readonly Button details = new DashboardButton(), options = new DashboardButton(), copy = new DashboardButton(), openFile = new DashboardButton(), openFolder = new DashboardButton(), apply = new DashboardButton(), advanced = new DashboardButton();
    private readonly AppVectorIcon copyGlyph = new(AppIconKind.Clipboard, 18);
    private readonly PictureBox image = new() { SizeMode = PictureBoxSizeMode.Zoom };
    private readonly Label title = AppUi.Heading("", 12), author = AppUi.Muted(), link = AppUi.Muted(), facts = AppUi.Muted(), hint = AppUi.Muted();
    private readonly Panel editor = new();
    private readonly DashboardFields fields = new();
    private readonly Label qualityLabel = AppUi.Muted(), containerLabel = AppUi.Muted();
    private readonly ComboBox quality = new() { AccessibleAutomationId = "InspectorQuality" }, container = new() { AccessibleAutomationId = "InspectorContainer" };
    private readonly MediaRangeEditor range;
    private readonly ToolTip tips = new();
    private readonly CancellationTokenSource lifetime = new();
    private string? source, thumbnailUrl, finalPath;
    private Guid? queueId;
    private SubtitleOptions subtitles = new();
    private SponsorBlockOptions sponsors = new();
    private VideoMetadata? metadata;
    private bool disposed, showOptions, queued, hasSelection, thumbnailFailed;
    private string factText = "";
    public DashboardInspector(AppServices services)
    {
        this.services = services; details.AccessibleAutomationId = "InspectorDetails"; options.AccessibleAutomationId = "InspectorOptions"; apply.AccessibleAutomationId = "InspectorApplyOptions";
        AutoScroll = true; AccessibleAutomationId = "DashboardInspector";
        DashboardStyle.Card(this); AppUi.BindBackground(image, AppThemeTokens.SurfaceSecondary); title.Multiline = facts.Multiline = hint.Multiline = true;
        title.AutoEllipsis = author.AutoEllipsis = link.AutoEllipsis = true;
        AppUi.BindForeground(link, AppThemeTokens.Info);
        foreach (var button in new[] { details, options, copy, openFile, openFolder, advanced, retry }) AppUi.Secondary(button);
        copyGlyph.AccessibilityView = ModernFormsNext.Accessibility.AccessibilityView.Hidden; copyGlyph.Click += (_, _) => copy.PerformClick(); copy.Controls.Add(copyGlyph); AppTypography.Bind(options, 9.5f);
        AppUi.Primary(apply); AppUi.Input(quality); AppUi.Input(container); AppUi.Surface(editor);
        range = new(services.Localization, "Inspector") { CompactLayout = true };
        editor.Controls.AddRange([qualityLabel, quality, containerLabel, container, range, advanced, apply, hint]);
        Controls.AddRange([details, options, image, title, author, link, copy, facts, fields, editor, openFile, openFolder, retry]);
        details.Click += (_, _) => { showOptions = false; LayoutInspector(); }; options.Click += (_, _) => { showOptions = true; LayoutInspector(); };
        copy.Click += async (_, _) =>
        {
            try { if (source is not null) await Clipboard.SetTextAsync(source); }
            catch (Exception error) { services.Logger.Error("Inspector URL could not be copied.", error); }
        };
        retry.Click += (_, _) => { if (source is { Length: > 0 }) RetryRequested?.Invoke(this, source); };
        openFile.Click += (_, _) => Utilities.ShellService.OpenFile(finalPath);
        openFolder.Click += (_, _) => Utilities.ShellService.OpenFolderForFile(finalPath);
        apply.Click += (_, _) => ApplyOptions(); advanced.Click += async (_, _) => await AdvancedAsync();
        SizeChanged += (_, _) => LayoutInspector(); AppTypography.RegisterLayout(this, LayoutInspector);
        services.Localization.LanguageChanged += LanguageChanged;
        Clear();
    }
    public void Clear()
    {
        historyMode = false; hasSelection = false; queueId = null; source = finalPath = null; thumbnailUrl = null; image.Image = null;
        title.Text = services.Localization["Dashboard.SelectItem"]; author.Text = link.Text = factText = ""; copy.Enabled = openFile.Enabled = openFolder.Enabled = false;
        queued = false; options.Visible = false; showOptions = false; RefreshText(); LayoutInspector();
    }
    public void ShowQueue(DownloadQueueItem item)
    {
        historyMode = false;
        var changed = queueId != item.Id || queued != (item.Status == DownloadStatus.Queued);
        queueId = item.Id; queued = item.Status == DownloadStatus.Queued; options.Visible = true;
        Present(item.Title, item.Channel, item.SourceUrl, item.ThumbnailUrl, item.FinalFile);
        var t = services.Localization;
        factText = string.Join("\n\n", new[] {
            t[$"Status.{item.Status}"],
            t["Dashboard.AddedAt"] + ": " + item.AddedAt.ToLocalTime().ToString("g", t.Culture),
            SourcePresentation.Host(item.SourceUrl) + " · " + t["Quality." + item.QualityPresetId] + " · " + item.PreferredVideoContainer,
            item.DurationSeconds is not null ? DashboardStyle.Duration(item.DurationSeconds) : null,
            item.DownloadedBytes is { } bytes ? DashboardStyle.Bytes(bytes) + (item.TotalBytes is { } total ? " / " + DashboardStyle.Bytes(total) : "") : null,
            item.FinalFile ?? services.Settings.Current.FinalOutputDirectory,
            item.FailureMessageKey is { } error ? t[error] : item.StatusMessageKey is { } status ? t[status] : null,
            item.AttemptCount > 0 ? t.Get("Dashboard.Attempts", item.AttemptCount, item.MaximumAttempts) : null,
            item.FailureTechnicalSummary });
        if (changed)
        {
            metadata = null;
            try { if (!string.IsNullOrWhiteSpace(item.RawMetadataJson)) metadata = JsonSerializer.Deserialize<VideoMetadata>(item.RawMetadataJson, JsonDefaults.Options); }
            catch (JsonException) { /* Missing metadata leaves safe read-only options. */ }
            quality.Items.Clear(); foreach (var preset in QualityPreset.All) quality.Items.Add(new Localization.LocalizedOption<QualityPreset>(preset, t["Quality." + preset.Id]));
            quality.SelectedIndex = Array.FindIndex(QualityPreset.All.ToArray(), p => p.Id == item.QualityPresetId);
            container.Items.Clear(); foreach (var value in Enum.GetValues<PreferredVideoContainer>()) container.Items.Add(new Localization.LocalizedOption<PreferredVideoContainer>(value, t["Container." + value]));
            container.SelectedIndex = (int)item.PreferredVideoContainer;
            range.SetRange(item.RequestedRange); subtitles = item.SubtitleOptions.Copy(); sponsors = item.SponsorBlockOptions.Copy();
            hint.Text = t[queued ? "Dashboard.QueuedOptions" : "Dashboard.ReadOnlyOptions"];
        }
        fields.SetRows((t["Downloads.Container"], item.PreferredVideoContainer.ToString()), (t["Downloads.Quality"], t["Quality." + item.QualityPresetId]),
            (t["Dashboard.AddedAt"], item.AddedAt.ToLocalTime().ToString("g", t.Culture)), (t["Dashboard.Duration"], DashboardStyle.Duration(item.DurationSeconds)),
            (t["Dashboard.Status"], t["Status." + item.Status]), (t["Dashboard.Size"], item.TotalBytes is { } totalSize ? DashboardStyle.Bytes(totalSize) : null),
            (t["Dashboard.Output"], item.FinalFile ?? services.Settings.Current.FinalOutputDirectory),
            (t["Dashboard.Message"], item.FailureMessageKey is { } key ? t[key] : null));
        RefreshText(); LayoutInspector();
    }
    public void ShowHistory(DownloadHistoryEntry entry)
    {
        historyMode = true;
        queueId = null; options.Visible = false; showOptions = false;
        Present(entry.Title, entry.Channel, entry.SourceUrl, entry.ThumbnailUrl, entry.FinalPath);
        var t = services.Localization;
        factText = entry.Status + "\n\n" + entry.CompletedAt.ToLocalTime().ToString("g", t.Culture) + "\n\n" +
            SourcePresentation.Host(entry.SourceUrl) + " · " + entry.Quality + "\n\n" + entry.FinalPath +
            (entry.RecordedDuration is { } duration ? "\n\n" + duration : "");
        fields.SetRows((t["Dashboard.Source"], SourcePresentation.Host(entry.SourceUrl)), (t["Dashboard.AddedAt"], entry.CompletedAt.ToLocalTime().ToString("g", t.Culture)),
            (t["Downloads.Quality"], entry.Quality), (t["Dashboard.Status"], t["Status." + (entry.WasPartial ? "Partial" : entry.Status)]), (t["Dashboard.Output"], entry.FinalPath));
        RefreshText(); LayoutInspector();
    }
    public void ShowLive(LiveSession session)
    {
        historyMode = false;
        queueId = null; options.Visible = false; showOptions = false;
        Present(session.Title, session.Channel, session.SourceUrl, session.ThumbnailUrl, session.Parts.LastOrDefault());
        var t = services.Localization;
        factText = t["Live.State." + session.State] + "\n\n" + SourcePresentation.Host(session.SourceUrl) + "\n\n" +
            t["Quality." + session.QualityPresetId] + " · MKV\n\n" + session.RecordedDuration.ToString(@"hh\:mm\:ss") + " · " + DashboardStyle.Bytes(session.BytesWritten) +
            "\n\n" + t.Get("Live.PartCount", session.Parts.Count) + "\n\n" + t["Live.ReconnectCount"] + ": " + session.ResumeCount +
            (session.FailureMessageKey is { } error ? "\n\n" + t[error] : "");
        fields.SetRows((t["Dashboard.Source"], SourcePresentation.Host(session.SourceUrl)), (t["Downloads.Quality"], t["Quality." + session.QualityPresetId]),
            (t["Dashboard.Status"], t["Live.State." + session.State]), (t["Dashboard.Duration"], session.RecordedDuration.ToString(@"hh\:mm\:ss")),
            (t["Dashboard.Size"], DashboardStyle.Bytes(session.BytesWritten)), (t["Live.ReconnectCount"], session.ResumeCount.ToString()), (t["Dashboard.Output"], session.Parts.LastOrDefault()));
        RefreshText(); LayoutInspector();
    }
    private void Present(string name, string channel, string url, string? thumbnail, string? path)
    {
        hasSelection = true; title.Text = name; author.Text = channel; source = url; finalPath = path; link.Text = url;
        tips.SetToolTip(title, name); tips.SetToolTip(author, channel); tips.SetToolTip(link, url);
        openFile.Enabled = File.Exists(path); openFolder.Enabled = path is not null && Directory.Exists(System.IO.Path.GetDirectoryName(path));
        copy.Enabled = source.Length > 0;
        if (thumbnailUrl != thumbnail) { thumbnailUrl = thumbnail; thumbnailFailed = false; image.Image = null; _ = LoadAsync(thumbnail); }
    }
    private async Task LoadAsync(string? expected)
    {
        try { var bitmap = await services.Thumbnails.GetAsync(expected, lifetime.Token); Application.RunOnUIThread(() => { if (!disposed && thumbnailUrl == expected) { image.Image = bitmap; thumbnailFailed = bitmap is null; LayoutInspector(); } }); }
        catch (OperationCanceledException) { }
        catch (Exception error) { services.Logger.Error("Inspector thumbnail could not be loaded.", error); }
    }
    private async Task AdvancedAsync()
    {
        if (!queued || FindForm() is not { } owner || container.SelectedItem is not Localization.LocalizedOption<PreferredVideoContainer> selected) return;
        using var form = new AdvancedDownloadOptionsForm(services.Localization, services.Settings.Current, metadata, subtitles, sponsors, selected.Value);
        if (await form.ShowDialog(owner) == DialogResult.OK) { subtitles = form.Subtitles; sponsors = form.SponsorBlock; }
    }
    private void ApplyOptions()
    {
        if (!queued || metadata is null || queueId is not { } id || quality.SelectedItem is not Localization.LocalizedOption<QualityPreset> q || container.SelectedItem is not Localization.LocalizedOption<PreferredVideoContainer> c) return;
        try
        {
            if (!range.TryGetRange(metadata.DurationSeconds, false, out var requested, out var error)) { hint.Text = services.Localization[error!]; return; }
            var issue = DownloadOptionCompatibilityValidator.Check(requested, subtitles, sponsors, c.Value);
            if (issue != DownloadOptionIssue.None) { hint.Text = services.Localization[DownloadOptionCompatibilityValidator.MessageKey(issue)]; return; }
            var formats = FormatSelector.Select(metadata, q.Value, c.Value);
            services.Queue.Update(id, item =>
            {
                if (item.Status != DownloadStatus.Queued) throw new InvalidOperationException("Queue item is active.");
                item.QualityPresetId = q.Value.Id; item.PreferredVideoContainer = c.Value; item.SelectedFormats = formats;
                item.RequestedRange = requested; item.SubtitleOptions = subtitles.Copy(); item.SponsorBlockOptions = sponsors.Copy();
            }, persist: true);
            hint.Text = services.Localization["Dashboard.OptionsSaved"];
        }
        catch (Exception error) { services.Logger.Error("Queued inspector options could not be applied.", error); hint.Text = services.Localization["Dashboard.ReadOnlyOptions"]; }
    }
    private void RefreshText()
    {
        var t = services.Localization; details.Text = t["Common.Details"]; options.Text = t["Dashboard.OptionsShort"];
        copy.Text = ""; copy.AccessibleName = t["Dashboard.CopyUrl"]; tips.SetToolTip(copy, copy.AccessibleName); openFile.Text = t["Common.OpenFile"]; openFolder.Text = t["Common.OpenFolder"];
        qualityLabel.Text = t["Downloads.Quality"]; containerLabel.Text = t["Downloads.Container"];
        retry.Text = t["Common.Retry"]; apply.Text = t["Dashboard.ApplyOptions"]; advanced.Text = t["Advanced.MoreOptions"]; facts.Text = factText;
        quality.Enabled = container.Enabled = range.Enabled = advanced.Enabled = apply.Enabled = queued && metadata is not null;
    }
    private void LanguageChanged(object? sender, EventArgs e) => Application.RunOnUIThread(RefreshText);
    private void LayoutInspector()
    {
        foreach (var control in new Control[] { image, author, link, copy, facts, openFile, openFolder }) control.Visible = hasSelection;
        // DisplayRectangle is the actual viewport after the native scroll rail.
        var viewport = DisplayRectangle; var o = viewport.Location; var width = Math.Max(180, viewport.Width - 24); var line = AppTypography.TextHeight(34);
        retry.Visible = historyMode && hasSelection;
        details.SetBounds(12 + o.X, 10 + o.Y, width / 2, line); options.SetBounds(details.Right + 4, details.Top, width / 2 - 4, line);
        DashboardVisual.Tab(details, !showOptions); DashboardVisual.Tab(options, showOptions);
        if (!hasSelection)
        {
            title.SetBounds(12 + o.X, details.Bottom + 40, width, AppTypography.TextHeight(100)); editor.Visible = fields.Visible = false;
            foreach (var hidden in new Control[] { image, author, link, copy, facts, fields, editor, openFile, openFolder, retry }) hidden.SetBounds(0, 0, 0, 0);
            return;
        }
        image.Visible = !string.IsNullOrWhiteSpace(thumbnailUrl) && !thumbnailFailed;
        image.SetBounds(12 + o.X, details.Bottom + 14, width, image.Visible ? width * 9 / 16 : 0);
        title.SetBounds(12 + o.X, image.Bottom + 12, width, DashboardVisual.LabelHeight(title, width, 3));
        author.SetBounds(12 + o.X, title.Bottom + 2, width, AppTypography.TextHeight(24));
        link.SetBounds(12 + o.X, author.Bottom + 6, width - AppTypography.TextHeight(32) - 6, AppTypography.TextHeight(30));
        copy.SetBounds(link.Right + 6, link.Top, AppTypography.TextHeight(32), link.Height);
        copyGlyph.SetBounds((copy.Width - 18) / 2, (copy.Height - 18) / 2, 18, 18);
        var editing = showOptions && queueId is not null;
        editor.Visible = editing; facts.Visible = false; fields.Visible = !editing;
        fields.SetBounds(12 + o.X, copy.Bottom + 10, width, fields.Height);
        editor.SetBounds(12 + o.X, copy.Bottom + 14, width, AppTypography.TextHeight(390));
        qualityLabel.SetBounds(0, 0, width, AppTypography.TextHeight(24)); quality.SetBounds(0, qualityLabel.Bottom, width, line);
        containerLabel.SetBounds(0, quality.Bottom + 10, width, AppTypography.TextHeight(24)); container.SetBounds(0, containerLabel.Bottom, width, line);
        range.SetBounds(0, container.Bottom + 12, width, AppTypography.TextHeight(200));
        advanced.SetBounds(0, range.Bottom + 8, width, line); apply.SetBounds(0, advanced.Bottom + 8, width, line);
        hint.SetBounds(0, apply.Bottom + 8, width, AppTypography.TextHeight(70)); editor.Height = hint.Bottom + 4;
        var paired = width >= AppTypography.TextHeight(240);
        openFile.SetBounds(12 + o.X, (editing ? editor.Bottom : fields.Bottom) + 12, paired ? (width - 8) / 2 : width, line);
        openFolder.SetBounds(paired ? openFile.Right + 8 : openFile.Left, paired ? openFile.Top : openFile.Bottom + 8, openFile.Width, line);
        retry.SetBounds(openFile.Left, openFolder.Bottom + 8, width, line);
        DashboardVisual.Tab(details, !editing); DashboardVisual.Tab(options, editing);
        foreach (var button in new[] { copy, openFile, openFolder, retry, advanced, apply }) DashboardVisual.Button(button, button == apply);
        if (paired) { AppTypography.Bind(openFile, 9); AppTypography.Bind(openFolder, 9); }
        DashboardVisual.Icon(openFile, AppIconKind.Play); DashboardVisual.Icon(openFolder, AppIconKind.Folder);
        if (paired) { DashboardVisual.SetPadding(openFile, new Padding(32, 0, 6, 0)); DashboardVisual.SetPadding(openFolder, new Padding(32, 0, 6, 0)); }
        DashboardVisual.Icon(retry, AppIconKind.Retry);
        // Pinned MFN includes hidden default-layout children in its scroll extent.
        // Keep only the active section's bounds; the next layout rebuilds the other section.
        facts.SetBounds(0, 0, 0, 0);
        if (!editing) editor.SetBounds(0, 0, 0, 0); else fields.SetBounds(0, 0, 0, 0);
        if (!historyMode) retry.SetBounds(0, 0, 0, 0);
    }
    protected override void Dispose(bool disposing) { if (disposing && !disposed) { disposed = true; lifetime.Cancel(); lifetime.Dispose(); tips.Dispose(); services.Localization.LanguageChanged -= LanguageChanged; } base.Dispose(disposing); }
}
