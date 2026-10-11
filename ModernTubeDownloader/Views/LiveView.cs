using System.Diagnostics;
using Path = System.IO.Path;
using ModernFormsNext;
using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Localization;
using ModernTubeDownloader.Models;
using ModernTubeDownloader.Services;
using ModernTubeDownloader.Theming;

namespace ModernTubeDownloader.Views;

internal sealed record LiveNavigationRequest(string SourceUrl, VideoMetadata? Metadata, MediaAvailabilityKind Kind);

internal sealed class LiveView : UserControl
{
    private readonly AppServices services;
    private readonly LocalizationService text;
    private readonly AppScrollPanel scroll = new() { Dock = DockStyle.Fill, AutoScroll = true };
    private readonly Label heading = AppUi.Heading(string.Empty, 24);
    private readonly Label statistics = AppUi.Muted();
    private readonly Panel analysis = new();
    private readonly TextBox url = new() { AccessibleAutomationId = "LiveUrlInput" };
    private readonly Button analyze = new() { AccessibleAutomationId = "LiveAnalyzeButton" };
    private readonly Label title = AppUi.Heading(string.Empty, 14);
    private readonly Label message = AppUi.Muted();
    private readonly ComboBox quality = new() { AccessibleAutomationId = "LiveQualitySelector" };
    private readonly ComboBox container = new() { AccessibleAutomationId = "LiveContainerSelector" };
    private readonly RadioButton fromNow = new() { AccessibleAutomationId = "LiveFromNowOption" };
    private readonly RadioButton fromStart = new() { AccessibleAutomationId = "LiveFromStartOption" };
    private readonly ToolTip liveToolTip = new();
    private readonly Button start = new() { AccessibleAutomationId = "LiveStartButton" };
    private readonly Button vod = new() { AccessibleAutomationId = "LiveOpenDownloadsButton" };
    private readonly Section[] sections;
    private readonly Dictionary<Guid, LiveSessionCard> cards = [];
    private readonly CancellationTokenSource lifetime = new();
    private readonly System.Threading.Timer clock;
    private CancellationTokenSource? analysisCancellation;
    private VideoMetadata? metadata;
    private MediaAvailabilityKind availability;
    private Guid? editingId;
    private int refreshPending;
    private bool disposed;
#if DEBUG
    internal Func<Form, IDisposable?>? RegisterDetailsForAutomation { get; set; }
#endif

    public LiveView(AppServices services)
    {
        this.services = services; text = services.Localization;
        Dock = DockStyle.Fill; AccessibleAutomationId = "LiveView";
        AppUi.BindBackground(this); AppUi.BindBackground(scroll); AppUi.Card(analysis);
        AppUi.Input(url); AppUi.Input(quality); AppUi.Input(container);
        AppUi.Primary(analyze); AppUi.Primary(start); AppUi.Secondary(vod);
        AppUi.BindForeground(fromStart); AppUi.BindForeground(fromNow);
        statistics.AccessibleAutomationId = "LiveStatistics";
        message.AccessibleAutomationId = "LiveAnalysisSummary";
        message.Multiline = true;
        analysis.Controls.AddRange([url, analyze, title, message, quality, container, fromNow, fromStart, start, vod]);
        sections = [new("Live.Active", "LiveActiveList"), new("Live.Scheduled", "LiveScheduledList"),
            new("Live.Recent", "LivePartialList")];
        scroll.Controls.AddRange([heading, statistics, analysis, .. sections.Select(item => item.Panel)]);
        Controls.Add(scroll);
        analyze.Click += async (_, _) => await AnalyzeAsync(url.Text);
        start.Click += (_, _) => StartOrSchedule();
        vod.Click += (_, _) => OpenDownloadsRequested?.Invoke(this, url.Text);
        SizeChanged += (_, _) => LayoutPage();
        scroll.SizeChanged += (_, _) => LayoutPage();
        services.Live.Changed += StateChanged; text.LanguageChanged += LanguageChanged;
        clock = new System.Threading.Timer(_ => QueueRefresh(), null, 1000, 1000);
        ApplyText(); RefreshSessions(); LayoutPage();
        AppTypography.RegisterLayout(this, LayoutPage);
    }

    public event EventHandler<string>? OpenDownloadsRequested;
    public async Task AcceptUrlAsync(string sourceUrl)
    { url.Text = sourceUrl; await AnalyzeAsync(sourceUrl); }
    public void Present(LiveNavigationRequest request)
    {
        url.Text = request.SourceUrl; metadata = request.Metadata; availability = request.Kind;
        editingId = null; PopulateAnalysis();
    }

    private async Task AnalyzeAsync(string sourceUrl)
    {
        analysisCancellation?.Cancel(); analysisCancellation?.Dispose();
        analysisCancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        var current = analysisCancellation;
        editingId = null; metadata = null; availability = MediaAvailabilityKind.Ready;
        start.Visible = vod.Visible = quality.Visible = container.Visible = fromNow.Visible = fromStart.Visible = false;
        analyze.Enabled = false; title.Text = text["Downloads.AnalyzingTitle"]; message.Text = string.Empty;
        try
        {
            await services.Tools.EnsureAnalysisReadyAsync(current.Token);
            var result = await services.Metadata.AnalyzeAsync(sourceUrl, current.Token);
            if (disposed || current != analysisCancellation) return;
            metadata = result; availability = MediaAvailabilityPolicy.Classify(result);
            PopulateAnalysis();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (disposed || current != analysisCancellation) return;
            var failure = DownloadFailureClassifier.Classify(ex);
            services.Logger.Error("LIVE analysis failed.", ex);
            if (failure.Category == DownloadFailureCategory.Upcoming)
            { availability = MediaAvailabilityKind.Upcoming; PopulateAnalysis(); }
            else { title.Text = text["Downloads.AnalysisFailedTitle"]; message.Text = text[failure.UserMessageKey]; }
        }
        finally { if (!disposed && current == analysisCancellation) { analyze.Enabled = true; LayoutPage(); } }
    }

    private void PopulateAnalysis()
    {
        var live = availability is MediaAvailabilityKind.ActiveLive or MediaAvailabilityKind.Upcoming;
        title.Text = metadata?.Title ?? text["Live.UpcomingTitle"];
        message.Text = availability == MediaAvailabilityKind.ActiveLive ? text["Live.ActiveStatus"] + " • " + metadata?.DisplayChannel
            : availability == MediaAvailabilityKind.Upcoming ? text["Live.WaitHint"] : text["Live.UseDownloads"];
        if (metadata is not null)
        {
            var identity = ExtractorIdentityService.Identify(metadata.Extractor, metadata.ExtractorKey);
            message.Text += Environment.NewLine + SourcePresentation.Summary(metadata, text) + " • " +
                identity.RuntimeName + " • " + (metadata.LiveStatus ?? "—");
        }
        quality.Items.Clear();
        var presets = metadata is not null && availability == MediaAvailabilityKind.ActiveLive
            ? FormatSelector.GetAvailablePresets(metadata, PreferredVideoContainer.Mkv, allowActiveLive: true) : QualityPreset.All;
        foreach (var preset in presets) quality.Items.Add(new LocalizedOption<QualityPreset>(preset, text[$"Quality.{preset.Id}"]));
        if (quality.Items.Count > 0) quality.SelectedIndex = Math.Max(0, quality.Items.Cast<LocalizedOption<QualityPreset>>().ToList()
            .FindIndex(item => item.Value.Id == services.Settings.Current.DefaultQualityPresetId));
        container.Items.Clear();
        container.Items.Add(new LocalizedOption<PreferredVideoContainer>(PreferredVideoContainer.Mkv, "MKV"));
        container.SelectedIndex = 0;
        fromStart.Enabled = availability == MediaAvailabilityKind.Upcoming || metadata is not null && LiveCapturePolicy.SupportsFromStart(metadata);
        fromStart.Checked = false;
        fromNow.Checked = true;
        fromNow.Visible = fromStart.Visible = quality.Visible = container.Visible = live;
        start.Visible = live; start.Enabled = quality.Items.Count > 0;
        start.Text = text[availability == MediaAvailabilityKind.Upcoming ? "Live.WaitForStart" : "Live.StartRecording"];
        vod.Visible = !live;
        LayoutPage();
    }

    private void StartOrSchedule()
    {
        if (quality.SelectedItem is not LocalizedOption<QualityPreset> selected) return;
        try
        {
            var policy = fromStart.Checked && fromStart.Enabled ? LiveStartPolicy.FromStart : LiveStartPolicy.FromNow;
            if (editingId is { } id) services.Live.EditWaiting(id, selected.Value.Id, policy);
            else services.Live.Add(url.Text, metadata, selected.Value.Id, policy, availability == MediaAvailabilityKind.Upcoming);
            message.Text = text[availability == MediaAvailabilityKind.Upcoming ? "Live.WaitingQueued" : "Live.RecordingQueued"];
            editingId = null; start.Enabled = false;
        }
        catch (Exception ex)
        { services.Logger.Error("Could not add or edit a LIVE session.", ex); message.Text = text["Downloads.AlreadyQueued"]; }
    }

    private void Edit(LiveSession session)
    {
        editingId = session.Id; availability = MediaAvailabilityKind.Upcoming; metadata = null;
        url.Text = session.SourceUrl; PopulateAnalysis(); editingId = session.Id;
        title.Text = session.Title;
        for (var index = 0; index < quality.Items.Count; index++)
            if (quality.Items[index] is LocalizedOption<QualityPreset> option && option.Value.Id == session.QualityPresetId)
                quality.SelectedIndex = index;
        fromStart.Checked = session.StartPolicy == LiveStartPolicy.FromStart;
        start.Text = text["Live.SaveSchedule"];
        scroll.VerticalScrollProperties.Value = scroll.VerticalScrollProperties.Minimum;
    }

    private void StateChanged(object? sender, EventArgs e) => QueueRefresh();
    private void QueueRefresh()
    {
        if (disposed || Interlocked.Exchange(ref refreshPending, 1) != 0) return;
        Application.RunOnUIThread(() => { Interlocked.Exchange(ref refreshPending, 0); if (!disposed) RefreshSessions(); });
    }
    private void RefreshSessions()
    {
        UiCrashDiagnostics.VerifyUiThread("live.refresh");
        var sessions = services.Live.Snapshot().OrderByDescending(item => item.CreatedAt).ToArray();
        statistics.Text = text.Get("Live.Statistics",
            sessions.Count(item => item.State is LiveSessionState.Starting or LiveSessionState.Recording or LiveSessionState.Finalizing),
            sessions.Count(item => item.State is LiveSessionState.WaitingForLive or LiveSessionState.Pending),
            sessions.Count(item => item.State == LiveSessionState.Reconnecting));
        foreach (var missing in cards.Keys.Except(sessions.Select(item => item.Id)).ToArray())
        { cards[missing].Dispose(); cards.Remove(missing); }
        foreach (var session in sessions)
        {
            if (!cards.TryGetValue(session.Id, out var card))
            {
                card = new LiveSessionCard(services, Edit); cards.Add(session.Id, card);
#if DEBUG
                card.RegisterDetailsForAutomation = RegisterDetailsForAutomation;
#endif
            }
            var section = sections[session.IsActive || session.State == LiveSessionState.Pending ? 0
                : session.State == LiveSessionState.WaitingForLive ? 1 : 2];
            if (card.Parent != section.List) { card.Parent?.Controls.Remove(card); section.List.Controls.Add(card); }
            card.UpdateSession(session);
        }
        LayoutPage();
    }
    private void LayoutPage()
    {
        var width = Math.Max(480, scroll.ClientSize.Width - 56);
        var origin = scroll.DisplayRectangle.Location;
        heading.SetBounds(origin.X + 28, origin.Y + 16, width, AppTypography.TextHeight(38));
        statistics.SetBounds(origin.X + 28, heading.Bottom, width, AppTypography.TextHeight(24));
        analysis.SetBounds(origin.X + 28, statistics.Bottom + 16, width, AppTypography.TextHeight(start.Visible ? 318 : vod.Visible ? 208 : 160));
        url.SetBounds(18, 18, Math.Max(220, width - 190), 40);
        analyze.SetBounds(width - 158, 18, 140, 40);
        title.SetBounds(18, 72, width - 36, 28);
        message.SetBounds(18, 104, width - 36, 40);
        quality.SetBounds(18, 152, 230, 36); container.SetBounds(260, 152, 100, 36);
        fromNow.SetBounds(18, 198, width - 36, 30);
        fromStart.SetBounds(18, 232, width - 36, 30);
        start.SetBounds(18, 272, 260, 36); vod.SetBounds(18, 152, 280, 38);
        AppTypography.ReflowRows(analysis);
        var top = analysis.Bottom + 18;
        foreach (var section in sections)
        {
            var listHeight = section.List.Controls.Sum(c => c.Height + c.Margin.Bottom);
            section.Panel.SetBounds(origin.X + 28, top, width, AppTypography.TextHeight(52) + listHeight);
            section.Heading.SetBounds(0, 0, width, AppTypography.TextHeight(32));
            section.List.SetBounds(0, AppTypography.TextHeight(38), width, Math.Max(1, listHeight));
            foreach (var card in section.List.Controls.OfType<LiveSessionCard>()) card.Width = width;
            section.Empty.SetBounds(0, AppTypography.TextHeight(34), width, AppTypography.TextHeight(22)); section.Empty.Visible = section.List.Controls.Count == 0;
            top = section.Panel.Bottom + 14;
        }
    }
    private void ApplyText()
    {
        heading.Text = text["Nav.Live"]; analyze.Text = text["Common.Analyze"];
        title.Text = text["Live.AddTitle"]; message.Text = text["Live.PageHint"];
        fromStart.Text = text["Live.FromStart"];
        fromNow.Text = text["Live.FromNow"];
        liveToolTip.SetToolTip(fromStart, text["Live.FromStartTooltip"]);
        vod.Text = text["Live.OpenDownloads"];
        start.Visible = vod.Visible = quality.Visible = container.Visible = fromNow.Visible = fromStart.Visible = false;
        foreach (var section in sections) { section.Heading.Text = text[section.Key]; section.Empty.Text = text["Live.EmptySection"]; }
    }
    private void LanguageChanged(object? sender, EventArgs e)
    { Application.RunOnUIThread(() => { if (!disposed) { ApplyText(); if (metadata is not null || availability == MediaAvailabilityKind.Upcoming) PopulateAnalysis(); RefreshSessions(); } }); }
    protected override void Dispose(bool disposing)
    {
        if (disposing && !disposed)
        {
            disposed = true; clock.Dispose(); liveToolTip.Dispose(); lifetime.Cancel(); analysisCancellation?.Cancel();
            services.Live.Changed -= StateChanged; text.LanguageChanged -= LanguageChanged;
            analysisCancellation?.Dispose(); lifetime.Dispose();
        }
        base.Dispose(disposing);
    }
    private sealed class Section
    {
        public Section(string key, string id)
        {
            Key = key; AppUi.BindBackground(Panel); AppUi.BindBackground(List);
            List.AccessibleAutomationId = id; Panel.Controls.AddRange([Heading, List, Empty]);
        }
        public string Key { get; }
        public Panel Panel { get; } = new();
        public Label Heading { get; } = AppUi.Heading(string.Empty, 16);
        public Label Empty { get; } = AppUi.Muted();
        public FlowLayoutPanel List { get; } = new() { FlowDirection = FlowDirection.TopDown, WrapContents = false };
    }
}

internal sealed class LiveSessionCard : Panel
{
    internal const int CardHeight = 226;
    private readonly AppServices services;
    private readonly Action<LiveSession> edit;
    private readonly Label title = AppUi.Heading(string.Empty, 13);
    private readonly Label state = AppUi.Muted();
    private readonly Label facts = AppUi.Muted();
    private readonly Button primary = new();
    private readonly Button secondary = new();
    private readonly Button details = new();
    private readonly Button folder = new();
    private LiveSession? current;
    private readonly PictureBox thumbnail = new() { SizeMode = PictureBoxSizeMode.Zoom };
    private string? imageUrl;
    private readonly CancellationTokenSource imageLifetime = new();
    private bool imageDisposed;
#if DEBUG
    internal Func<Form, IDisposable?>? RegisterDetailsForAutomation { get; set; }
#endif
    public LiveSessionCard(AppServices services, Action<LiveSession> edit)
    {
        this.services = services; this.edit = edit;
        Height = CardHeight - 12; Margin = new Padding(0, 0, 0, 12); AppUi.Card(this);
        AppUi.Primary(primary); AppUi.Secondary(secondary); AppUi.Secondary(details);
        AppUi.Secondary(folder);
        facts.Multiline = true; Controls.AddRange([title, state, facts, primary, secondary, details, folder, thumbnail]);
        primary.Click += (_, _) => Act(); secondary.Click += (_, _) => Secondary(); details.Click += async (_, _) => await ShowDetailsAsync();
        folder.Click += (_, _) => { if (current?.Parts.LastOrDefault() is { } path && Directory.Exists(Path.GetDirectoryName(path)))
            Process.Start(new ProcessStartInfo("explorer.exe", Path.GetDirectoryName(path)!) { UseShellExecute = true }); };
        SizeChanged += (_, _) => LayoutCard(); LayoutCard();
        AppTypography.RegisterLayout(this, () => { LayoutCard(); if (current is not null) UpdateSession(current); });
    }
    public void UpdateSession(LiveSession session)
    {
        current = session; var text = services.Localization;
        AccessibleAutomationId = $"LiveSession-{session.Id:N}";
        title.Text = session.Title; state.Text = text[$"Live.State.{session.State}"];
        state.AccessibleAutomationId = $"LiveState-{session.Id:N}";
        var countdown = session.State == LiveSessionState.WaitingForLive ? session.NextCheckAt : session.RetryAt;
        var timingText = session.State == LiveSessionState.WaitingForLive && session.ScheduledStartAt is { } scheduled
            ? text.Get("Live.ScheduledStart", scheduled.ToLocalTime().ToString("g", text.Culture)) + " • "
            : session.RecordingStartedAt is { } started
                ? text["Live.StartedLabel"] + ": " + started.ToLocalTime().ToString("g", text.Culture) + " • " : string.Empty;
        var appearance = AppearancePresentation.Current.Live;
        var pieces = new List<string>();
        if (appearance.ShowElapsed) pieces.Add(session.RecordedDuration.ToString(@"hh\:mm\:ss"));
        if (appearance.ShowBytes) pieces.Add($"{session.BytesWritten / 1048576d:F1} MiB");
        if (appearance.ShowSource) pieces.Add(SourcePresentation.Host(session.SourceUrl));
        if (appearance.ShowParts) pieces.Add(text.Get("Live.PartCount", session.Parts.Count));
        if (appearance.ShowReconnectCount) pieces.Add(text["Live.ReconnectCount"] + ": " + session.ResumeCount);
        facts.Text = string.Join(" • ", pieces) + Environment.NewLine + timingText +
            (countdown is { } next ? text.Get("Live.NextCheck", Math.Max(0, (int)(next - DateTimeOffset.UtcNow).TotalSeconds)) : session.FailureMessageKey is { } failureKey ? text[failureKey] : text[$"Quality.{session.QualityPresetId}"]);
        AppUi.BindBackground(this, appearance.EmphasizeActive && session.IsActive ? AppThemeTokens.NavigationSelected : AppThemeTokens.Surface);
        if (appearance.ThumbnailSize != AppearanceSize.None && imageUrl != session.ThumbnailUrl) { imageUrl = session.ThumbnailUrl; _ = LoadImageAsync(imageUrl); }
        primary.Visible = session.IsActive || session.CanResume || session.State == LiveSessionState.WaitingForLive;
        primary.Text = text[session.IsActive ? "Live.StopAndSave" : session.State == LiveSessionState.WaitingForLive ? "Live.Edit" : "Live.Resume"];
        primary.AccessibleAutomationId = (session.IsActive ? "LiveStopSaveButton-" : session.State == LiveSessionState.WaitingForLive ? "LiveEditButton-" : "LiveResumeButton-") + session.Id.ToString("N");
        secondary.Visible = session.IsActive || session.State is LiveSessionState.Pending or LiveSessionState.WaitingForLive || session.Parts.Count > 0;
        secondary.Text = text[session.IsActive || session.State is LiveSessionState.Pending or LiveSessionState.WaitingForLive ? "Common.Cancel" : "Common.OpenFile"];
        secondary.AccessibleAutomationId = "LiveCancelButton-" + session.Id.ToString("N");
        details.Text = text["Common.Details"]; details.AccessibleAutomationId = "LiveDetailsButton-" + session.Id.ToString("N");
        folder.Visible = session.Parts.Count > 0;
        folder.Text = text["Common.OpenFolder"]; folder.AccessibleAutomationId = "LiveOpenFolderButton-" + session.Id.ToString("N");
        LayoutCard();
    }
    private async Task LoadImageAsync(string? expected)
    {
        try { var image = await services.Thumbnails.GetAsync(expected, imageLifetime.Token); if (!imageDisposed && imageUrl == expected) thumbnail.Image = image; }
        catch (OperationCanceledException) { }
        catch (Exception ex) { services.Logger.Error("LIVE thumbnail unavailable.", ex); }
    }
    private void Act()
    {
        if (current is null) return;
        if (current.IsActive) services.Live.StopAndSave(current.Id);
        else if (current.State == LiveSessionState.WaitingForLive) edit(current);
        else services.Live.Resume(current.Id);
    }
    private void Secondary()
    {
        if (current is null) return;
        if (current.IsActive || current.State is LiveSessionState.Pending or LiveSessionState.WaitingForLive)
            services.Live.Cancel(current.Id);
        else if (current.Parts.LastOrDefault() is { } path && File.Exists(path))
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }
    private async Task ShowDetailsAsync()
    {
        if (current is null || FindForm() is not { } owner) return;
        var session = services.Live.Find(current.Id) ?? current; var text = services.Localization;
        using var form = new Form { Text = text["Live.Details"], Size = new System.Drawing.Size(650, 530), MinimumSize = new System.Drawing.Size(500, 400) };
        form.AccessibilityObject.AutomationId = "LiveDetailsWindow";
        AppBranding.Apply(form);
        var body = new TextBox { Dock = DockStyle.Fill, MultiLine = true, ScrollBars = ScrollBars.Vertical, ReadOnly = true,
            TextAlign = ContentAlignment.TopLeft, Font = AppTypography.Font(11f), Padding = new Padding(16),
            AccessibleAutomationId = "LiveDetailsContent", Text =
            $"{session.Title}\n{text[$"Live.State.{session.State}"]}\n" +
            $"{text["Live.SourceState"]}: {session.SourceLiveStatus ?? "—"}\n" +
            $"{text["Live.ScheduledLabel"]}: {session.ScheduledStartAt?.ToLocalTime().ToString("g", text.Culture) ?? "—"}\n" +
            $"{text["Live.StartedLabel"]}: {session.RecordingStartedAt?.ToLocalTime().ToString("g", text.Culture) ?? "—"}\n" +
            $"{session.RecordedDuration} • {session.BytesWritten / 1048576d:F1} MiB\n" +
            $"{text[$"Quality.{session.QualityPresetId}"]} • MKV • {text[session.StartPolicy == LiveStartPolicy.FromStart ? "Live.FromStart" : "Live.FromNow"]}\n" +
            $"{text.Get("Live.PartCount", session.Parts.Count)} • {text["Live.CurrentPart"]}: {session.PartIndex}\n" +
            $"{text["Live.ReconnectCount"]}: {session.ResumeCount}\n" +
            (session.FailureMessageKey is { } error ? text[error] : "—") + "\n" +
            (session.RecoveryWarningKey is { } warning ? text[warning] : string.Empty) };
        AppUi.Input(body); form.Controls.Add(body);
        try
        {
#if DEBUG
            using var registration = RegisterDetailsForAutomation?.Invoke(form);
#endif
            await form.ShowDialog(owner);
        }
        catch (Exception ex) { services.Logger.Error($"LIVE details failed LiveSessionId={session.Id}", ex); state.Text = text["Details.OpenFailed"]; }
    }
    private void LayoutCard()
    {
        var appearance = AppearancePresentation.Current.Live;
        var compactWaiting = appearance.CompactWaiting && current?.State == LiveSessionState.WaitingForLive;
        var gap = appearance.CardDensity == AppearanceDensity.Compact ? 4 : appearance.CardDensity == AppearanceDensity.Comfortable ? 16 : 8;
        var imageWidth = AppearancePresentation.ImageWidth(appearance.ThumbnailSize, 120); thumbnail.Visible = imageWidth > 0;
        thumbnail.SetBounds(Width - imageWidth - 18, 14, imageWidth, imageWidth * 9 / 16);
        title.SetBounds(18, 14, Math.Max(200, Width - 36 - imageWidth), 28);
        var textWidth = Math.Max(200, Width - 36 - (imageWidth > 0 ? imageWidth + 12 : 0));
        state.SetBounds(18, 46, textWidth, 22);
        facts.SetBounds(18, 74, textWidth, 48);
        var actionWidth = Math.Max(150, (Width - 48) / 2);
        primary.SetBounds(18, 128, actionWidth, 34); secondary.SetBounds(24 + actionWidth, 128, actionWidth, 34);
        details.SetBounds(18, 168, actionWidth, 34); folder.SetBounds(24 + actionWidth, 168, actionWidth, 34);
        AppTypography.ReflowRows(this);
        primary.Top = compactWaiting ? facts.Top : facts.Bottom + gap; secondary.Top = primary.Top;
        details.Top = primary.Bottom + gap; folder.Top = details.Top;
        facts.Visible = !compactWaiting; Height = Math.Max(details.Bottom + 18, thumbnail.Bottom + 18);
    }
    protected override void Dispose(bool disposing) { if (disposing && !imageDisposed) { imageDisposed = true; imageLifetime.Cancel(); imageLifetime.Dispose(); } base.Dispose(disposing); }
}
