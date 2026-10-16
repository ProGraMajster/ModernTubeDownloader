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
    private readonly Button retry = new() { AccessibleAutomationId = "HistoryRetry" };
    private readonly Button details = new(), options = new(), copy = new(), openFile = new(), openFolder = new(), apply = new(), advanced = new();
    private readonly PictureBox image = new() { SizeMode = PictureBoxSizeMode.Zoom };
    private readonly Label title = AppUi.Heading("", 12), author = AppUi.Muted(), link = AppUi.Muted(), facts = AppUi.Muted(), hint = AppUi.Muted();
    private readonly Panel editor = new();
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
        foreach (var button in new[] { details, options, copy, openFile, openFolder, advanced, retry }) AppUi.Secondary(button);
        AppUi.Primary(apply); AppUi.Input(quality); AppUi.Input(container); AppUi.Surface(editor);
        range = new(services.Localization, "Inspector") { CompactLayout = true };
        editor.Controls.AddRange([qualityLabel, quality, containerLabel, container, range, advanced, apply, hint]);
        Controls.AddRange([details, options, image, title, author, link, copy, facts, editor, openFile, openFolder, retry]);
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
            item.DurationSeconds is { } duration ? TimeSpan.FromSeconds(duration).ToString(@"hh\:mm\:ss") : null,
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
        RefreshText(); LayoutInspector();
    }
    private void Present(string name, string channel, string url, string? thumbnail, string? path)
    {
        hasSelection = true; title.Text = name; author.Text = channel; source = url; finalPath = path; link.Text = url;
        tips.SetToolTip(title, name); tips.SetToolTip(link, url);
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
        var t = services.Localization; details.Text = t["Common.Details"]; options.Text = t["Dashboard.Options"];
        copy.Text = t["Dashboard.CopyUrl"]; openFile.Text = t["Common.OpenFile"]; openFolder.Text = t["Common.OpenFolder"];
        qualityLabel.Text = t["Downloads.Quality"]; containerLabel.Text = t["Downloads.Container"];
        retry.Text = t["Common.Retry"]; apply.Text = t["Dashboard.ApplyOptions"]; advanced.Text = t["Advanced.MoreOptions"]; facts.Text = factText;
        quality.Enabled = container.Enabled = range.Enabled = advanced.Enabled = apply.Enabled = queued && metadata is not null;
    }
    private void LanguageChanged(object? sender, EventArgs e) => Application.RunOnUIThread(RefreshText);
    private void LayoutInspector()
    {
        foreach (var control in new Control[] { image, author, link, copy, facts, openFile, openFolder }) control.Visible = hasSelection;
        var o = DisplayRectangle.Location; var width = Math.Max(220, ClientSize.Width - 28); var line = AppTypography.TextHeight(34);
        retry.Visible = historyMode && hasSelection;
        details.SetBounds(12 + o.X, 10 + o.Y, width / 2, line); options.SetBounds(details.Right + 4, details.Top, width / 2 - 4, line);
        if (!hasSelection) { title.SetBounds(12 + o.X, details.Bottom + 40, width, AppTypography.TextHeight(100)); editor.Visible = false; return; }
        image.Visible = !string.IsNullOrWhiteSpace(thumbnailUrl) && !thumbnailFailed;
        image.SetBounds(12 + o.X, details.Bottom + 14, width, image.Visible ? width * 9 / 16 : 0);
        title.SetBounds(12 + o.X, image.Bottom + 12, width, AppTypography.TextHeight(55));
        author.SetBounds(12 + o.X, title.Bottom + 2, width, AppTypography.TextHeight(24));
        link.SetBounds(12 + o.X, author.Bottom + 6, width, AppTypography.TextHeight(28));
        copy.SetBounds(12 + o.X, link.Bottom + 4, width, line);
        editor.Visible = showOptions && options.Visible; facts.Visible = !editor.Visible;
        facts.SetBounds(12 + o.X, copy.Bottom + 14, width, AppTypography.TextHeight(290));
        editor.SetBounds(12 + o.X, copy.Bottom + 14, width, AppTypography.TextHeight(390));
        qualityLabel.SetBounds(0, 0, width, AppTypography.TextHeight(24)); quality.SetBounds(0, qualityLabel.Bottom, width, line);
        containerLabel.SetBounds(0, quality.Bottom + 10, width, AppTypography.TextHeight(24)); container.SetBounds(0, containerLabel.Bottom, width, line);
        range.SetBounds(0, container.Bottom + 12, width, AppTypography.TextHeight(200));
        advanced.SetBounds(0, range.Bottom + 8, width, line); apply.SetBounds(0, advanced.Bottom + 8, width, line);
        hint.SetBounds(0, apply.Bottom + 8, width, AppTypography.TextHeight(70)); editor.Height = hint.Bottom + 4;
        openFile.SetBounds(12 + o.X, (editor.Visible ? editor.Bottom : facts.Bottom) + 12, width, line);
        openFolder.SetBounds(12 + o.X, openFile.Bottom + 8, width, line);
        retry.SetBounds(openFile.Left, openFolder.Bottom + 8, width, line);
        AppUi.NavigationButton(details, !editor.Visible); AppUi.NavigationButton(options, editor.Visible);
    }
    protected override void Dispose(bool disposing) { if (disposing && !disposed) { disposed = true; lifetime.Cancel(); lifetime.Dispose(); tips.Dispose(); services.Localization.LanguageChanged -= LanguageChanged; } base.Dispose(disposing); }
}
