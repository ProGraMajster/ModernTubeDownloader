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
    private readonly SourceVectorIcon sourceGlyph = new() { AccessibilityView = ModernFormsNext.Accessibility.AccessibilityView.Hidden };
    private readonly AppVectorIcon emptyIcon = new(AppIconKind.Info, 32, AppThemeTokens.TextSecondary) { AccessibilityView = ModernFormsNext.Accessibility.AccessibilityView.Hidden };
    private readonly DownloadsPictureBox image = new() { SizeMode = PictureBoxSizeMode.Zoom };
    private readonly Label imageDuration = AppUi.Muted();
    private readonly FullValueLabel title = new(), link = new();
    private readonly FullValueLabel playlistContext = new() { AccessibleAutomationId = "InspectorPlaylist", Multiline = true, AutoEllipsis = true };
    private readonly Label author = AppUi.Muted(), facts = AppUi.Muted(), hint = AppUi.Muted();
    private readonly Panel editor = new();
    private readonly DashboardFields fields = new();
    private readonly Button subtitleEditor = new DashboardButton { AccessibleAutomationId = "InspectorSubtitles" };
    private readonly Button folderOpen = new DashboardButton { AccessibleAutomationId = "InspectorOpenFolder" };
    private readonly Label subtitleLabel = AppUi.Muted(), audioLabel = AppUi.Muted(), folderLabel = AppUi.Muted();
    private readonly TextBox audioValue = new() { ReadOnly = true, AccessibleAutomationId = "InspectorAudio" }, folderValue = new() { ReadOnly = true, AccessibleAutomationId = "InspectorFolder" };
    private readonly Label qualityLabel = AppUi.Muted(), containerLabel = AppUi.Muted();
    private readonly ComboBox quality = new() { AccessibleAutomationId = "InspectorQuality" }, container = new() { AccessibleAutomationId = "InspectorContainer" };
    private readonly MediaRangeEditor range;
    private readonly ToolTip tips = new();
    private readonly CancellationTokenSource lifetime = new();
    private string? source, thumbnailUrl, finalPath, folderPath;
    private Guid? queueId;
    private SubtitleOptions subtitles = new();
    private SponsorBlockOptions sponsors = new();
    private VideoMetadata? metadata;
    private bool disposed, showOptions, queued, hasSelection, thumbnailFailed;
    private bool arranging;
    private string factText = "";
    private bool CanEditOptions => queued && metadata?.Formats.Count > 0;
    internal int EmptyContentHeight => title.Bottom - DisplayRectangle.Y + 12;
    public DashboardInspector(AppServices services)
    {
        this.services = services; details.AccessibleAutomationId = "InspectorDetails"; options.AccessibleAutomationId = "InspectorOptions"; apply.AccessibleAutomationId = "InspectorApplyOptions";
        copy.AccessibleAutomationId = "InspectorCopyUrl";
        AutoScroll = true; AccessibleAutomationId = "DashboardInspector";
        DashboardStyle.Card(this); AppUi.BindBackground(image, AppThemeTokens.SurfaceSecondary); title.Multiline = facts.Multiline = hint.Multiline = true;
        AppUi.BindForeground(title); AppTypography.Bind(title, 12, FontStyle.Bold); AppUi.BindForeground(link);
        title.Style.BackgroundColor = link.Style.BackgroundColor = playlistContext.Style.BackgroundColor = SkiaSharp.SKColors.Transparent;
        title.AutoEllipsis = author.AutoEllipsis = link.AutoEllipsis = true;
        AppUi.BindForeground(link, AppThemeTokens.Info);
        foreach (var button in new[] { details, options, copy, openFile, openFolder, advanced, retry }) AppUi.Secondary(button);
        copyGlyph.AccessibilityView = ModernFormsNext.Accessibility.AccessibilityView.Hidden; copyGlyph.Click += (_, _) => copy.PerformClick(); copy.Controls.Add(copyGlyph); AppTypography.Bind(options, 9.5f);
        AppUi.Primary(apply); AppUi.Input(quality); AppUi.Input(container); AppUi.Surface(editor);
        range = new(services.Localization, "Inspector") { CompactLayout = true };
        range.ModeChanged += (_, _) => LayoutInspector();
        AppUi.Input(audioValue); AppUi.Input(folderValue); AppUi.Secondary(subtitleEditor);
        AppUi.Secondary(folderOpen); folderOpen.Click += (_, _) => Utilities.ShellService.OpenFolderForFile(folderPath);
        subtitleEditor.Click += async (_, _) => await AdvancedAsync();
        editor.Controls.AddRange([qualityLabel, quality, containerLabel, container, subtitleLabel, subtitleEditor, audioLabel, audioValue, folderLabel, folderValue, folderOpen, range, advanced, apply, hint]);
        container.TabIndex = 0; quality.TabIndex = 1; subtitleEditor.TabIndex = 2;
        audioValue.TabIndex = 3; folderValue.TabIndex = 4; folderOpen.TabIndex = 5;
        range.TabIndex = 6; advanced.TabIndex = 7; apply.TabIndex = 8;
        Controls.AddRange([details, options, image, title, author, playlistContext, link, sourceGlyph, copy, facts, fields, editor, openFile, openFolder, retry, emptyIcon]);
        title.AccessibleAutomationId = "InspectorTitle"; link.AccessibleAutomationId = "InspectorSource";
        AppTypography.Bind(playlistContext, 9.5f); AppUi.BindForeground(playlistContext, AppThemeTokens.TextSecondary);
        BindFullValue(playlistContext, () => playlistContext.Text);
        BindFullValue(title, () => title.Text); BindFullValue(link, () => source ?? ""); BindFullValue(folderValue, () => folderPath ?? "");
        AppTypography.Bind(imageDuration, 9.5f); imageDuration.TextAlign = ContentAlignment.MiddleCenter;
        AppUi.BindBackground(imageDuration, AppThemeTokens.Background); Controls.Add(imageDuration);
        image.Style.Border.Radius = 6; image.Style.Border.Width = 0;
        BindQueueTabNavigation(this);
        details.Click += (_, _) => { showOptions = false; LayoutInspector(); }; options.Click += (_, _) => { showOptions = true; LayoutInspector(); };
        copy.Click += async (_, _) =>
        {
            try { if (source is not null) await Clipboard.SetTextAsync(source); }
            catch (Exception error) { services.Logger.Error("Inspector URL could not be copied.", error); }
        };
        retry.Click += (_, _) => { if (source is { Length: > 0 }) RetryRequested?.Invoke(this, source); };
        openFile.Click += (_, _) => Utilities.ShellService.OpenFile(finalPath);
        openFolder.Click += (_, _) => Utilities.ShellService.OpenFolderForFile(folderPath);
        apply.Click += (_, _) => ApplyOptions(); advanced.Click += async (_, _) => await AdvancedAsync();
        SizeChanged += (_, _) => LayoutInspector(); AppTypography.RegisterLayout(this, LayoutInspector);
        services.Localization.LanguageChanged += LanguageChanged;
        Clear();
    }
    private void BindQueueTabNavigation(Control control)
    {
        // The pinned Windows backend drops control-character WM_CHAR messages, while its
        // adapter traverses Tab on KeyPress. Reuse MFN's traversal on KeyDown for this
        // queue inspector only; handled input also prevents a second move on surface hosts.
        control.KeyDown += (_, e) =>
        {
            if (queueId is null || !AppTypography.IsDashboard(this) || e.Handled ||
                e.KeyCode != Keys.Tab || e.Control || e.Alt) return;
            Control root = this;
            while (root.Parent is { } parent) root = parent;
            e.Handled = e.SuppressKeyPress = true;
            root.SelectNextControl(control, !e.Shift, true, true, true);
        };
        foreach (var child in control.Controls) BindQueueTabNavigation(child);
    }
    public void Clear()
    {
        historyMode = false; hasSelection = false; queueId = null; source = finalPath = folderPath = null; thumbnailUrl = null; image.Image = null;
        imageDuration.Text = "";
        title.Text = services.Localization["Dashboard.SelectItem"]; author.Text = link.Text = factText = ""; copy.Enabled = openFile.Enabled = openFolder.Enabled = false;
        queued = false; options.Visible = false; showOptions = false; RefreshText(); LayoutInspector();
    }
    public void ShowQueue(DownloadQueueItem item)
    {
        historyMode = false;
        fields.SingleLineValues = true; // Full values remain in each native tooltip.
        fields.InsetValues = true;
        var changed = queueId != item.Id || queued != (item.Status == DownloadStatus.Queued);
        queueId = item.Id; queued = item.Status == DownloadStatus.Queued; options.Visible = true;
        Present(item.Title, item.Channel, item.SourceUrl, item.ThumbnailUrl, item.FinalFile);
        playlistContext.Text = string.IsNullOrWhiteSpace(item.PlaylistTitle) ? "" : services.Localization.Get("Queue.PlaylistPart", item.PlaylistTitle) +
            (item.PlaylistIndex is { } ordinal ? $" · #{ordinal}" : "");
        folderPath = item.FinalFile is { Length: > 0 } finalFile ? System.IO.Path.GetDirectoryName(finalFile) : services.Settings.Current.FinalOutputDirectory;
        openFolder.Enabled = Directory.Exists(folderPath);
        imageDuration.Text = DashboardStyle.Duration(item.DurationSeconds);
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
            hint.Text = t[CanEditOptions ? "Dashboard.QueuedOptions" : queued ? item.PlaylistId is not null ? "Playlist.FormatsHint" : "Dashboard.MetadataRequired" : "Dashboard.InspectorReadOnly"];
        }
        var selectedAudio = metadata?.Formats.FirstOrDefault(f => f.Id == (item.SelectedFormats?.AudioFormatId ?? item.SelectedFormats?.VideoFormatId));
        var audio = selectedAudio?.HasAudio == true ? selectedAudio.AudioCodec : null;
        audioValue.Text = audio ?? t["Common.NotAvailable"];
        folderValue.Text = folderPath ?? "";
        fields.SetRows((t["Dashboard.Format"], DashboardStyle.OutputFormat(item)), (t["Downloads.Quality"], t["Quality." + item.QualityPresetId]),
            (t["Details.Subtitles"], item.SubtitleOptions.Enabled ? string.Join(", ", item.SubtitleOptions.Languages) + " · " + t["SubtitleFormat." + item.SubtitleOptions.Format] : t["Details.None"]),
            (t["Dashboard.Audio"], audio ?? t["Common.NotAvailable"]),
            (t["Dashboard.Folder"], folderPath),
            (t["Dashboard.Message"], item.FailureMessageKey is { } key ? t[key] : null));
        RefreshText(); LayoutInspector();
    }
    public void ShowHistory(DownloadHistoryEntry entry)
    {
        historyMode = true;
        fields.SingleLineValues = false;
        fields.InsetValues = false;
        queueId = null; options.Visible = false; showOptions = false;
        Present(entry.Title, entry.Channel, entry.SourceUrl, entry.ThumbnailUrl, entry.FinalPath);
        imageDuration.Text = entry.DurationSeconds is { } seconds ? DashboardStyle.Duration(seconds) : "";
        var t = services.Localization;
        factText = entry.Status + "\n\n" + entry.CompletedAt.ToLocalTime().ToString("g", t.Culture) + "\n\n" +
            SourcePresentation.Host(entry.SourceUrl) + " · " + entry.Quality + "\n\n" + entry.FinalPath +
            (entry.RecordedDuration is { } duration ? "\n\n" + duration : "");
        fields.SetRows((t["Dashboard.Source"], SourcePresentation.Host(entry.SourceUrl)), (t["Dashboard.AddedAt"], entry.CompletedAt.ToLocalTime().ToString("g", t.Culture)),
            (t["Dashboard.Duration"], entry.DurationSeconds is { } durationSeconds ? DashboardStyle.Duration(durationSeconds) : "—"),
            (t["Downloads.Container"], System.IO.Path.GetExtension(entry.FinalPath).TrimStart('.').ToUpperInvariant()),
            (t["Downloads.Quality"], entry.Quality), (t["Dashboard.Size"], entry.FileSizeBytes is { } size ? DashboardStyle.Bytes(size) : "—"),
            (t["Dashboard.Status"], t["Status." + (entry.WasPartial ? "Partial" : entry.Status)]), (t["Dashboard.Output"], entry.FinalPath));
        RefreshText(); LayoutInspector();
    }
    public void ShowLive(LiveSession session)
    {
        historyMode = false;
        fields.SingleLineValues = false;
        fields.InsetValues = false;
        queueId = null; options.Visible = false; showOptions = false;
        Present(session.Title, session.Channel, session.SourceUrl, session.ThumbnailUrl, session.Parts.LastOrDefault());
        imageDuration.Text = session.RecordedDuration > TimeSpan.Zero ? DashboardStyle.Duration(session.RecordedDuration.TotalSeconds) : "";
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
        playlistContext.Text = ""; hasSelection = true; title.Text = name; author.Text = channel; source = url; finalPath = folderPath = path; link.Text = url;
        tips.SetToolTip(title, name); tips.SetToolTip(author, channel); tips.SetToolTip(link, url);
        openFile.Enabled = File.Exists(path); openFolder.Enabled = path is not null && Directory.Exists(System.IO.Path.GetDirectoryName(path));
        copy.Enabled = source.Length > 0;
        var identity = SourceBrandLookup.Resolve(url);
        sourceGlyph.Brand = identity.Brand;
        link.AccessibleName = queueId is not null ? identity.Name + ": " + url : url;
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
        if (!CanEditOptions || FindForm() is not { } owner || container.SelectedItem is not Localization.LocalizedOption<PreferredVideoContainer> selected) return;
        using var form = new AdvancedDownloadOptionsForm(services.Localization, services.Settings.Current, metadata, subtitles, sponsors, selected.Value);
        if (await form.ShowDialog(owner) == DialogResult.OK) { subtitles = form.Subtitles; sponsors = form.SponsorBlock; RefreshText(); LayoutInspector(); }
    }
    private void ApplyOptions()
    {
        if (!CanEditOptions || metadata is null || queueId is not { } id || quality.SelectedItem is not Localization.LocalizedOption<QualityPreset> q || container.SelectedItem is not Localization.LocalizedOption<PreferredVideoContainer> c) return;
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
        catch (Exception error) { services.Logger.Error("Queued inspector options could not be applied.", error); hint.Text = services.Localization[services.Queue.Find(id)?.Status == DownloadStatus.Queued ? "Dashboard.OptionsUnavailable" : "Dashboard.ReadOnlyOptions"]; }
    }
    private void RefreshText()
    {
        var t = services.Localization; details.Text = t["Common.Details"]; options.Text = t["Dashboard.OptionsShort"];
        copy.Text = ""; copy.AccessibleName = t["Dashboard.CopyUrl"]; tips.SetToolTip(copy, copy.AccessibleName); openFile.Text = t["Common.OpenFile"]; openFolder.Text = t["Common.OpenFolder"];
        qualityLabel.Text = t["Downloads.Quality"]; containerLabel.Text = t["Dashboard.Format"];
        quality.AccessibleName = qualityLabel.Text; container.AccessibleName = containerLabel.Text;
        retry.Text = t["Common.Retry"]; apply.Text = t["Dashboard.ApplyOptions"]; advanced.Text = t["Advanced.MoreOptions"]; facts.Text = factText;
        subtitleLabel.Text = t["Details.Subtitles"]; audioLabel.Text = t["Dashboard.Audio"]; folderLabel.Text = t["Dashboard.Folder"];
        subtitleEditor.Text = (subtitles.Enabled ? string.Join(", ", subtitles.Languages) : t["Details.None"]) + " …";
        subtitleEditor.AccessibleName = t["Details.Subtitles"] + ": " + subtitleEditor.Text;
        audioValue.AccessibleName = audioLabel.Text; folderValue.AccessibleName = folderLabel.Text;
        folderOpen.AccessibleName = t["Common.OpenFolder"]; folderOpen.Enabled = Directory.Exists(folderPath);
        tips.SetToolTip(folderOpen, folderPath);
        tips.SetToolTip(audioValue, t["Dashboard.AudioAutomatic"]); tips.SetToolTip(folderValue, folderPath);
        tips.SetToolTip(folderLabel, t["Dashboard.FolderGlobal"]);
        tips.SetToolTip(subtitleEditor, t[queued ? "Advanced.MoreOptions" : "Dashboard.ReadOnlyOptions"]);
        subtitleEditor.Enabled = CanEditOptions;
        quality.Enabled = container.Enabled = range.Enabled = advanced.Enabled = apply.Enabled = CanEditOptions;
    }
    private void BindFullValue(Control control, Func<string> value)
    {
        control.KeyDown += async (_, e) =>
        {
            if (queueId is null) return;
            if (e.KeyCode == Keys.F1) { tips.Show(value(), control, new System.Drawing.Point(0, control.Height), 5000); e.Handled = true; }
            if (e.Control && e.KeyCode == Keys.C && control is not TextBox)
            {
                e.Handled = true;
                try { await Clipboard.SetTextAsync(value()); }
                catch (Exception error) { services.Logger.Error("Inspector value could not be copied.", error); }
            }
        };
    }
    private void LanguageChanged(object? sender, EventArgs e) => Application.RunOnUIThread(() => { RefreshText(); LayoutInspector(); });
    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        // The scroll rail can appear without SizeChanged. Reflow against its new viewport.
        if (range is not null) LayoutInspector();
    }
    private void LayoutInspector()
    {
        if (arranging) return;
        arranging = true;
        try
        {
            // Showing Options may introduce a rail during this arrangement. Settle both
            // width and extent now, rather than waiting for an unrelated outer resize.
            for (var pass = 0; pass < 3; pass++)
            {
                var viewportWidth = DisplayRectangle.Width;
                SuspendLayout();
                try { ArrangeInspector(); }
                finally { ResumeLayout(true); }
                if (DisplayRectangle.Width == viewportWidth) break;
            }
        }
        finally { arranging = false; }
    }
    private void ArrangeInspector()
    {
        foreach (var control in new Control[] { image, author, link, copy, facts, openFile, openFolder }) control.Visible = hasSelection;
        // DisplayRectangle is the actual viewport after the native scroll rail.
        var viewport = DisplayRectangle; var o = viewport.Location; var width = Math.Max(0, viewport.Width - 24); var line = AppTypography.TextHeight(34);
        retry.Visible = historyMode && hasSelection;
        image.RoundedPreview = queueId is not null;
        title.EnableKeyboardValues(queueId is not null); link.EnableKeyboardValues(queueId is not null);
        playlistContext.Visible = hasSelection && queueId is not null && !string.IsNullOrWhiteSpace(playlistContext.Text);
        playlistContext.EnableKeyboardValues(playlistContext.Visible);
        if (!playlistContext.Visible) playlistContext.SetBounds(0, 0, 0, 0);
        sourceGlyph.Visible = hasSelection && queueId is not null;
        if (!sourceGlyph.Visible) sourceGlyph.SetBounds(0, 0, 0, 0);
        details.SetBounds(12 + o.X, 10 + o.Y, width / 2, line); options.SetBounds(details.Right + 4, details.Top, width / 2 - 4, line);
        DashboardVisual.Tab(details, !showOptions); DashboardVisual.Tab(options, showOptions);
        emptyIcon.Visible = !hasSelection;
        if (!hasSelection)
        {
            emptyIcon.SetBounds(12 + o.X + (width - 32) / 2, details.Bottom + 44, 32, 32);
            title.TextAlign = ContentAlignment.MiddleCenter;
            title.SetBounds(24 + o.X, emptyIcon.Bottom + 16, Math.Max(0, width - 24), DashboardVisual.LabelHeight(title, Math.Max(1, width - 24), 3)); editor.Visible = fields.Visible = false;
            foreach (var hidden in new Control[] { options, image, imageDuration, author, link, copy, facts, fields, editor, openFile, openFolder, retry }) hidden.SetBounds(0, 0, 0, 0);
            return;
        }
        emptyIcon.SetBounds(0, 0, 0, 0); title.TextAlign = ContentAlignment.TopLeft;
        AppTypography.Bind(title, queueId is not null ? 10.5f : 12, FontStyle.Bold);
        if (queueId is not null)
        {
            DownloadsVisual.Card(this); AppTypography.BindSemibold(title, 11);
            AppTypography.Bind(details, 9.5f); AppTypography.Bind(options, 9.5f);
            details.Width = Math.Min(width / 2, DashboardVisual.TextWidth(details, details.Text) + 32);
            options.Left = details.Right + 4; options.Width = width - details.Width - 4;
        }
        else DashboardStyle.Card(this);
        image.Visible = !string.IsNullOrWhiteSpace(thumbnailUrl) && !thumbnailFailed;
        image.SetBounds(12 + o.X, details.Bottom + (queueId is not null ? 19 : 14), width, image.Visible ? width * 9 / 16 : 0);
        imageDuration.Visible = image.Visible && imageDuration.Text.Length > 0;
        if (imageDuration.Visible)
        {
            var badgeWidth = Math.Min(width, DashboardVisual.TextWidth(imageDuration, imageDuration.Text) + 12);
            var badgeHeight = AppTypography.TextHeight(25);
            imageDuration.SetBounds(image.Right - badgeWidth - 4, image.Bottom - badgeHeight - 4, badgeWidth, badgeHeight); imageDuration.BringToFront();
        }
        else imageDuration.SetBounds(0, 0, 0, 0);
        title.SetBounds(12 + o.X, image.Bottom + 12, width, DashboardVisual.LabelHeight(title, width, 3));
        author.SetBounds(12 + o.X, title.Bottom + 2, width, AppTypography.TextHeight(24));
        if (playlistContext.Visible) playlistContext.SetBounds(12 + o.X, author.Bottom + 4, width, DashboardVisual.LabelHeight(playlistContext, width, 2));
        link.SetBounds(12 + o.X, (playlistContext.Visible ? playlistContext.Bottom : author.Bottom) + 6, width - AppTypography.TextHeight(32) - 6, AppTypography.TextHeight(30));
        copy.SetBounds(link.Right + 6, link.Top, AppTypography.TextHeight(32), link.Height);
        copyGlyph.SetBounds((copy.Width - 18) / 2, (copy.Height - 18) / 2, 18, 18);
        if (queueId is not null)
        {
            sourceGlyph.SetBounds(link.Left, link.Top + (link.Height - 16) / 2, 16, 16);
            link.SetBounds(link.Left + 22, link.Top, Math.Max(1, link.Width - 22), link.Height);
            ArrangeQueueEditor(width, o, line); return;
        }
        var editing = showOptions && queueId is not null;
        editor.Visible = editing; facts.Visible = false; fields.Visible = !editing;
        fields.SetBounds(12 + o.X, copy.Bottom + 10, width, fields.Height);
        editor.SetBounds(12 + o.X, copy.Bottom + 14, width, editor.Height);
        qualityLabel.SetBounds(0, 0, width, AppTypography.TextHeight(24)); quality.SetBounds(0, qualityLabel.Bottom, width, line);
        containerLabel.SetBounds(0, quality.Bottom + 10, width, AppTypography.TextHeight(24)); container.SetBounds(0, containerLabel.Bottom, width, line);
        var optionLabelWidth = Math.Max(DashboardVisual.TextWidth(qualityLabel, qualityLabel.Text), DashboardVisual.TextWidth(containerLabel, containerLabel.Text)) + 12;
        if (queueId is not null && width - optionLabelWidth >= AppTypography.TextHeight(140))
        {
            containerLabel.SetBounds(0, 0, optionLabelWidth - 8, line); container.SetBounds(optionLabelWidth, 0, width - optionLabelWidth, line);
            qualityLabel.SetBounds(0, container.Bottom + 8, optionLabelWidth - 8, line); quality.SetBounds(optionLabelWidth, qualityLabel.Top, width - optionLabelWidth, line);
        }
        range.SetBounds(0, Math.Max(container.Bottom, quality.Bottom) + 12, width, range.Height);
        advanced.SetBounds(0, range.Bottom + 8, width, line); apply.SetBounds(0, advanced.Bottom + 8, width, line);
        hint.SetBounds(0, apply.Bottom + 8, width, DashboardVisual.LabelHeight(hint, width, 5)); editor.Height = hint.Bottom + 4;
        var paired = width >= AppTypography.TextHeight(240);
        openFile.SetBounds(12 + o.X, (editing ? editor.Bottom : fields.Bottom) + 12, paired ? (width - 8) / 2 : width, line);
        openFolder.SetBounds(paired ? openFile.Right + 8 : openFile.Left, paired ? openFile.Top : openFile.Bottom + 8, openFile.Width, line);
        retry.SetBounds(openFile.Left, openFolder.Bottom + 8, width, line);
        DashboardVisual.Tab(details, !editing); DashboardVisual.Tab(options, editing);
        if (queueId is not null) { AppTypography.Bind(details, 9.5f); AppTypography.Bind(options, 9.5f); }
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
        if (!options.Visible) options.SetBounds(0, 0, 0, 0);
    }
    private void ArrangeQueueEditor(int width, System.Drawing.Point origin, int actionHeight)
    {
        // Both tabs use these exact controls and the same uncommitted draft. Only
        // range / advanced disclosure changes; no rebind occurs on a tab switch.
        editor.Visible = true; fields.Visible = facts.Visible = retry.Visible = false;
        fields.SetBounds(0, 0, 0, 0); facts.SetBounds(0, 0, 0, 0); retry.SetBounds(0, 0, 0, 0);
        editor.SetBounds(12 + origin.X, copy.Bottom + 10, width, editor.Height);
        AppUi.BindGradient(editor, DownloadsVisual.Panel, DownloadsVisual.PanelEnd);
        var labels = new[] { containerLabel, qualityLabel, subtitleLabel, audioLabel, folderLabel };
        var values = new Control[] { container, quality, subtitleEditor, audioValue, folderValue };
        var rowHeight = AppTypography.TextHeight(31);
        AppTypography.Bind(hint, 9);
        var labelWidth = Math.Max(AppTypography.TextHeight(68), labels.Max(c => DashboardVisual.TextWidth(c, c.Text)) + 10);
        var inline = width - labelWidth >= AppTypography.TextHeight(130);
        var y = 0;
        for (var i = 0; i < labels.Length; i++)
        {
            var label = labels[i]; var value = values[i]; label.Visible = value.Visible = true;
            AppTypography.Bind(label, 9); AppTypography.Bind(value, 9.5f);
            if (value is TextBox or ComboBox)
            {
                AppUi.BindGradient(value, DownloadsVisual.ButtonTop, DownloadsVisual.ButtonEnd);
                AppUi.BindBorder(value, DownloadsVisual.Border); value.Style.Border.Radius = 6;
                if (value is TextBox) value.Padding = new Padding(9, 0, 6, 0);
            }
            label.TextAlign = ContentAlignment.MiddleLeft;
            label.SetBounds(0, y, inline ? labelWidth - 8 : width, inline ? rowHeight : AppTypography.TextHeight(23));
            value.SetBounds(inline ? labelWidth : 0, inline ? y : label.Bottom, inline ? width - labelWidth : width, rowHeight);
            y = value.Bottom + AppTypography.TextHeight(7);
        }
        DownloadsVisual.Button(subtitleEditor); AppTypography.Bind(subtitleEditor, 9.5f);
        var folderButtonWidth = AppTypography.TextHeight(31);
        folderValue.Width -= folderButtonWidth + 6;
        folderOpen.SetBounds(folderValue.Right + 6, folderValue.Top, folderButtonWidth, folderValue.Height);
        DownloadsVisual.Button(folderOpen); DashboardVisual.Icon(folderOpen, AppIconKind.Folder, true);
        range.Visible = advanced.Visible = showOptions;
        if (showOptions)
        {
            range.SetBounds(0, y + 4, width, range.Height);
            advanced.SetBounds(0, range.Bottom + 8, width, actionHeight); y = advanced.Bottom + 8;
        }
        else { range.SetBounds(0, 0, 0, 0); advanced.SetBounds(0, 0, 0, 0); }
        apply.Visible = queued;
        if (queued) { apply.SetBounds(0, y, width, actionHeight); y = apply.Bottom + 6; }
        else apply.SetBounds(0, 0, 0, 0);
        hint.SetBounds(0, y, width, DashboardVisual.LabelHeight(hint, width, 5));
        editor.Height = hint.Bottom;
        openFile.Visible = !queued && openFile.Enabled;
        if (openFile.Visible) openFile.SetBounds(editor.Left, editor.Bottom + 8, width, actionHeight);
        else openFile.SetBounds(0, 0, 0, 0);
        openFolder.Visible = false; openFolder.SetBounds(0, 0, 0, 0);
        foreach (var button in new[] { copy, openFile, openFolder, advanced, apply }) DownloadsVisual.Button(button, button == apply);
        DashboardVisual.Icon(openFile, AppIconKind.Play); DashboardVisual.Icon(openFolder, AppIconKind.Folder);
    }
    private sealed class FullValueLabel : DashboardLabel
    {
        public void EnableKeyboardValues(bool enabled)
        {
            SetControlBehavior(ControlBehaviors.Selectable, enabled); TabStop = enabled;
            StyleFocused.Border.Width = enabled ? 1 : 0;
            StyleFocused.Border.Color = AppUi.GetColor(AppThemeTokens.Accent);
        }
    }
    protected override void Dispose(bool disposing) { if (disposing && !disposed) { disposed = true; lifetime.Cancel(); lifetime.Dispose(); tips.Dispose(); services.Localization.LanguageChanged -= LanguageChanged; } base.Dispose(disposing); }
}
