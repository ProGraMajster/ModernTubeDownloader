using System.Globalization;
using ModernFormsNext;
using ModernFormsNext.Animations;
using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Localization;
using ModernTubeDownloader.Models;
using ModernTubeDownloader.Services;
using ModernTubeDownloader.Theming;
using ModernTubeDownloader.Utilities;
using SkiaSharp;

namespace ModernTubeDownloader.Views;

internal sealed class DownloadsView : UserControl
{
#if DEBUG
    internal Func<Form, IDisposable?>? RegisterDetailsForAutomation { get; set; }
    internal Func<Form, IDisposable?>? RegisterAdvancedForAutomation { get; set; }
#endif
    public event EventHandler<LiveNavigationRequest>? LiveRequested;
    public event EventHandler? SupportedSourcesRequested;
    private readonly Button supportedSourcesButton;
    private readonly Label sourceSummary;

    private enum PreviewState
    {
        Empty,
        Analyzing,
        Analyzed,
        PlaylistAnalyzed,
        Failed
    }

    private const int EmptyPreviewHeight = 126;
    private const int AnalyzingPreviewHeight = 140;
    private const int FailedPreviewHeight = 154;
    private const int AnalyzedPreviewHeight = 218;
    private const int CompactEmptyPreviewHeight = 116;
    private const int CompactAnalyzingPreviewHeight = 130;
    private const int CompactFailedPreviewHeight = 144;
    private const int CompactAnalyzedPreviewHeight = 210;

    private readonly AppServices services;
    private readonly LocalizationService text;
    private readonly Panel header;
    private readonly Label pageTitle;
    private readonly Label pageSubtitle;
    private readonly Panel enginePill;
    private readonly Panel toolStatusDot;
    private readonly Label toolStatus;
    private readonly Panel urlPanel;
    private readonly Label urlLabel;
    private readonly TextBox urlInput;
    private readonly Button pasteButton;
    private readonly AppVectorIcon pasteIcon;
    private readonly ToolTip pasteToolTip = new() { InitialDelay = 350, AutoPopDelay = 5000 };
    private readonly Button analyzeButton;
    private readonly Panel previewPanel;
    private readonly PlaylistPreviewControl playlistPreview;
    private readonly Panel previewPlaceholder;
    private readonly AppVectorIcon previewIcon;
    private readonly PictureBox previewImage;
    private readonly Label previewTitle;
    private readonly Label previewFacts;
    private readonly Label qualityLabel;
    private readonly Label containerLabel;
    private readonly Label feedback;
    private readonly ComboBox quality;
    private readonly ComboBox container;
    private readonly MediaRangeEditor rangeEditor;
    private readonly Label rangeHint;
    private readonly Button addButton;
    private readonly Button detailsButton;
    private readonly Button advancedButton;
    private readonly Button clearPreviewButton;
    private readonly ProgressBar analysisProgress;
    private readonly Panel queueToolbar;
    private readonly Label queueTitle;
    private readonly Label admissionCountdown;
    private readonly ModernFormsNext.Timer admissionCountdownTimer;
    private readonly Button pauseButton;
    private readonly Button moreButton;
    private readonly ContextMenu queueMenu = new();
    private readonly Panel queueArea;
    private readonly FlowLayoutPanel queueHost;
    private readonly Panel queueEmpty;
    private readonly AppVectorIcon queueEmptyIcon;
    private readonly Label queueEmptyTitle;
    private readonly Label queueEmptyBody;
    private readonly Dictionary<Guid, QueueItemCard> cards = [];
    private VideoMetadata? analyzedMetadata;
    private SubtitleOptions currentSubtitles = new();
    private SponsorBlockOptions currentSponsorBlock = new();
    private CancellationTokenSource? analyzeCancellation;
    private Guid? selectedItemId;
    private PreviewState previewState;
    private bool queueBuilt;
    private readonly CancellationTokenSource lifetimeCancellation = new();
    private bool disposed;

    public DownloadsView(AppServices services)
    {
        this.services = services;
        text = services.Localization;
        Dock = DockStyle.Fill;
        AppUi.BindBackground(this);

        header = new Panel();
        AppUi.BindBackground(header);
        pageTitle = AppUi.Heading(string.Empty, 24f);
        pageSubtitle = AppUi.Muted();
        enginePill = new Panel { Height = 34 };
        AppUi.Card(enginePill, secondary: true);
        enginePill.Style.Border.Radius = 17;
        toolStatusDot = new Panel { Left = 13, Top = 12, Width = 10, Height = 10 };
        toolStatusDot.Style.Border.Radius = 5;
        toolStatusDot.Style.Border.Width = 0;
        toolStatus = AppUi.Muted();
        toolStatus.Left = 32;
        toolStatus.Top = 5;
        toolStatus.Height = 24;
        toolStatus.AutoEllipsis = false;
        enginePill.Controls.AddRange([toolStatusDot, toolStatus]);
        header.Controls.AddRange([pageTitle, pageSubtitle, enginePill]);

        urlPanel = new Panel();
        AppUi.Card(urlPanel);
        urlLabel = AppUi.Muted();
        supportedSourcesButton = new Button { AccessibleAutomationId = "SupportedSourcesButton" };
        AppUi.Secondary(supportedSourcesButton);
        supportedSourcesButton.Click += (_, _) => SupportedSourcesRequested?.Invoke(this, EventArgs.Empty);
        sourceSummary = AppUi.Muted();
        sourceSummary.AccessibleAutomationId = "AnalyzedSourceSummary";
        urlInput = new TextBox
        {
            AccessibleAutomationId = "DownloadUrlInput",
            Height = 48,
            Padding = new Padding(14, 0, 14, 0),
            TextAlign = ContentAlignment.MiddleLeft
        };
        AppUi.Input(urlInput);
        urlInput.Font = new Font("Segoe UI", 12.5f);
        pasteButton = new Button { Height = 48, Width = 48, AccessibleAutomationId = "PasteButton" };
        analyzeButton = new Button { Height = 48, Enabled = false, AccessibleAutomationId = "AnalyzeButton" };
        AppUi.Secondary(pasteButton);
        AppUi.Primary(analyzeButton);
        pasteIcon = new AppVectorIcon(AppIconKind.Clipboard, 24, AppThemeTokens.TextPrimary)
        {
            Left = 12,
            Top = 12
        };
        pasteIcon.Click += (_, _) => pasteButton.PerformClick();
        pasteButton.Controls.Add(pasteIcon);
        pasteButton.Click += PasteClicked;
        analyzeButton.Click += AnalyzeClicked;
        urlInput.TextChanged += UrlChanged;
        urlPanel.Controls.AddRange([urlLabel, urlInput, pasteButton, analyzeButton, supportedSourcesButton]);

        previewPanel = new Panel { AccessibleAutomationId = "PreviewCard" };
        previewPanel.Controls.Add(sourceSummary);
        AppUi.Card(previewPanel);
        previewPlaceholder = new Panel();
        AppUi.Card(previewPlaceholder, secondary: true);
        previewIcon = new AppVectorIcon(AppIconKind.Download, 30, AppThemeTokens.TextSecondary);
        previewPlaceholder.Controls.Add(previewIcon);
        previewImage = new PictureBox { SizeMode = PictureBoxSizeMode.Zoom, Visible = false };
        previewImage.Style.Border.Radius = 12;
        previewImage.Style.Border.Width = 0;
        AppUi.BindBackground(previewImage, AppThemeTokens.SurfaceSecondary);
        previewTitle = AppUi.Heading(string.Empty, 17f);
        previewTitle.Multiline = true;
        previewFacts = AppUi.Muted();
        previewFacts.Multiline = true;
        qualityLabel = AppUi.Muted();
        quality = new ComboBox { Enabled = false, AccessibleAutomationId = "QualitySelector" };
        AppUi.Input(quality);
        containerLabel = AppUi.Muted();
        container = new ComboBox { Enabled = false, AccessibleAutomationId = "ContainerSelector" };
        AppUi.Input(container);
        container.SelectedIndexChanged += ContainerChanged;
        rangeEditor = new MediaRangeEditor(text, "Video");
        rangeEditor.ModeChanged += (_, _) => UpdateLayout();
        rangeHint = AppUi.Muted();
        rangeHint.AccessibleAutomationId = "VideoRangeKeyframeHint";
        addButton = new Button { Enabled = false, AccessibleAutomationId = "AddToQueueButton" };
        detailsButton = new Button { Enabled = false, AccessibleAutomationId = "DetailsButton" };
        advancedButton = new Button { Enabled = false, AccessibleAutomationId = "AdvancedOptionsButton" };
        AppUi.Primary(addButton);
        AppUi.Secondary(detailsButton);
        AppUi.Secondary(advancedButton);
        clearPreviewButton = new Button { Text = "×", Visible = false, AccessibleAutomationId = "ClearAnalyzedPreviewButton" };
        feedback = AppUi.Muted();
        analysisProgress = new ProgressBar { Minimum = 0, Maximum = 100, Value = 45, Visible = false };
        addButton.Click += AddClicked;
        detailsButton.Click += DetailsClicked;
        advancedButton.Click += AdvancedClicked;
        clearPreviewButton.Click += (_, _) => ClearAnalyzedPreview();
        previewPanel.Controls.AddRange([
            previewPlaceholder,
            previewImage,
            previewTitle,
            previewFacts,
            qualityLabel,
            quality,
            containerLabel,
            container,
            rangeEditor,
            rangeHint,
            addButton,
            detailsButton,
            advancedButton,
            clearPreviewButton,
            feedback,
            analysisProgress]);

        playlistPreview = new PlaylistPreviewControl(text) { Visible = false };
        playlistPreview.AddRequested += PlaylistAddRequested;
        playlistPreview.AdvancedOptionsRequested += AdvancedClicked;

        queueToolbar = new Panel();
        AppUi.BindBackground(queueToolbar);
        queueTitle = AppUi.Heading(string.Empty, 16.5f);
        admissionCountdown = AppUi.Muted();
        admissionCountdown.AccessibleAutomationId = "QueueAdmissionCountdown";
        admissionCountdown.Visible = false;
        pauseButton = new Button { AccessibleAutomationId = "QueuePauseButton" };
        moreButton = new Button { Text = "…", Width = 44, AccessibleAutomationId = "QueueMenuButton" };
        AppUi.Secondary(pauseButton);
        AppUi.Secondary(moreButton);
        pauseButton.Click += (_, _) => services.Queue.SetPaused(!services.Queue.IsPaused);
        moreButton.Click += (_, _) => Application.RunOnUIThread(
            () => queueMenu.Show(
                moreButton,
                moreButton.PointToScreen(new System.Drawing.Point(0, moreButton.Height + 4))));
        queueToolbar.Controls.AddRange([queueTitle, admissionCountdown, pauseButton, moreButton]);
        admissionCountdownTimer = new ModernFormsNext.Timer { Interval = 1000 };
        admissionCountdownTimer.Tick += (_, _) =>
        {
            UpdateAdmissionCountdown();
        };
        admissionCountdownTimer.Start();

        queueArea = new Panel();
        AppUi.BindBackground(queueArea);
        queueHost = new AppScrollFlowLayoutPanel
        {
            AccessibleAutomationId = "QueueList",
            Dock = DockStyle.Fill,
            AutoScroll = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = new Padding(0, 0, 8, 0)
        };
        AppUi.BindBackground(queueHost);
        queueHost.SizeChanged += QueueHostSizeChanged;
        queueEmpty = new Panel { Dock = DockStyle.Fill };
        AppUi.Card(queueEmpty, secondary: true);
        queueEmptyIcon = new AppVectorIcon(AppIconKind.Queue, 42, AppThemeTokens.TextSecondary);
        queueEmptyTitle = AppUi.Heading(string.Empty, 16f);
        queueEmptyTitle.TextAlign = ContentAlignment.BottomCenter;
        queueEmptyBody = AppUi.Muted();
        queueEmptyBody.TextAlign = ContentAlignment.TopCenter;
        queueEmptyBody.Multiline = true;
        queueEmpty.Controls.AddRange([queueEmptyIcon, queueEmptyTitle, queueEmptyBody]);
        queueArea.Controls.Add(queueHost);
        queueArea.Controls.Add(queueEmpty);

        Controls.AddRange([header, urlPanel, previewPanel, playlistPreview, queueToolbar, queueArea]);
        SizeChanged += ViewSizeChanged;
        text.LanguageChanged += LanguageChanged;
        services.Queue.Changed += QueueChanged;
        services.Settings.Changed += SettingsChanged;
        services.Tools.Changed += ToolStatusChanged;
        ApplyLocalization();
        SetPreviewState(PreviewState.Empty);
        BuildQueueCards();
        UpdateLayout();
    }

    private void UpdateLayout()
    {
        const int page = 28;
        const int gap = 14;
        var compactHeight = ClientSize.Height < 660;
        var width = Math.Max(560, ClientSize.Width - (page * 2));
        var statusInline = width >= 620;
        var statusWidth = statusInline ? 292 : Math.Min(292, width);
        var headerHeight = statusInline
            ? compactHeight ? 52 : 62
            : compactHeight ? 96 : 102;
        header.SetBounds(page, compactHeight ? 8 : 14, width, headerHeight);
        pageTitle.SetBounds(0, 0, statusInline ? width - statusWidth - 16 : width, 36);
        enginePill.SetBounds(width - statusWidth, statusInline ? 1 : 36, statusWidth, 34);
        toolStatus.Width = statusWidth - 44;
        pageSubtitle.SetBounds(0, statusInline ? compactHeight ? 32 : 38 : 74, width, 24);

        var sectionGap = compactHeight ? 4 : 8;
        urlPanel.SetBounds(page, header.Bottom + sectionGap, width, compactHeight ? 96 : 108);
        urlLabel.SetBounds(22, compactHeight ? 8 : 12, width - 310, 24);
        supportedSourcesButton.SetBounds(width - 276, compactHeight ? 7 : 11, 254, 28);
        var analyzeWidth = width < 760 ? 112 : 136;
        var inputTop = compactHeight ? 38 : 48;
        const int inputHeight = 48;
        analyzeButton.SetBounds(width - 22 - analyzeWidth, inputTop, analyzeWidth, inputHeight);
        pasteButton.SetBounds(analyzeButton.Left - 10 - inputHeight, inputTop, inputHeight, inputHeight);
        urlInput.SetBounds(22, inputTop, Math.Max(220, pasteButton.Left - 32), inputHeight);

        var previewTop = urlPanel.Bottom + gap;
        var previewHeight = GetPreviewHeight(compactHeight);
        previewPanel.SetBounds(page, previewTop, width, previewHeight);
        clearPreviewButton.SetBounds(width - 42, 10, 30, 30);
        playlistPreview.SetBounds(page, previewTop, width, previewHeight);
        var analyzed = previewState == PreviewState.Analyzed;
        var mediaPreview = analyzed;
        rangeHint.Visible = analyzed && rangeEditor.IsCustom;
        var thumbnailWidth = mediaPreview
            ? Math.Clamp((int)Math.Round(width * 0.18d), 176, 216)
            : compactHeight ? 96 : 112;
        var thumbnailHeight = mediaPreview
            ? (int)Math.Round(thumbnailWidth * 9d / 16d)
            : compactHeight ? 64 : 72;
        var thumbnailTop = Math.Max(12, (previewHeight - thumbnailHeight) / 2);
        previewPlaceholder.SetBounds(22, thumbnailTop, thumbnailWidth, thumbnailHeight);
        previewImage.SetBounds(22, thumbnailTop, thumbnailWidth, thumbnailHeight);
        previewIcon.SetBounds(
            (thumbnailWidth - previewIcon.Width) / 2,
            Math.Max(6, (thumbnailHeight - previewIcon.Height) / 2),
            previewIcon.Width,
            previewIcon.Height);
        var detailsLeft = previewPlaceholder.Right + 20;
        var detailsWidth = width - detailsLeft - 22;
        if (analyzed)
        {
            sourceSummary.SetBounds(22, Math.Max(6, thumbnailTop - 36), thumbnailWidth, 34);
            previewTitle.SetBounds(detailsLeft, 9, detailsWidth - 36, 34);
            previewFacts.SetBounds(detailsLeft, 44, detailsWidth, 20);
            var fieldGap = 10;
            var comboWidth = Math.Max(130, (detailsWidth - fieldGap) / 2);
            qualityLabel.SetBounds(detailsLeft, 64, comboWidth, 15);
            containerLabel.SetBounds(detailsLeft + comboWidth + fieldGap, 64, comboWidth, 15);
            quality.SetBounds(detailsLeft, 80, comboWidth, 32);
            container.SetBounds(quality.Right + fieldGap, 80, comboWidth, 32);
            rangeEditor.SetBounds(detailsLeft, 118, detailsWidth, 34);
            var rangeOffset = rangeEditor.IsCustom ? 20 : 0;
            rangeHint.SetBounds(detailsLeft, 152, detailsWidth, 18);
            feedback.SetBounds(detailsLeft, (compactHeight ? 148 : 154) + rangeOffset, detailsWidth, 17);
            var actionTop = (compactHeight ? 168 : 176) + rangeOffset;
            var detailsActionWidth = Math.Min(132, Math.Max(112, detailsWidth / 4));
            var addWidth = Math.Min(184, Math.Max(154, detailsWidth / 3));
            detailsButton.SetBounds(detailsLeft, actionTop, detailsActionWidth, 34);
            addButton.SetBounds(detailsButton.Right + 10, actionTop, addWidth, 34);
            advancedButton.SetBounds(22, actionTop, thumbnailWidth, 34);
        }
        else
        {
            var copyTop = previewState switch
            {
                PreviewState.Empty => Math.Max(16, (previewHeight - 68) / 2),
                _ => 18
            };
            previewTitle.SetBounds(detailsLeft, copyTop, detailsWidth, 30);
            previewFacts.SetBounds(detailsLeft, copyTop + 32, detailsWidth, 40);
            feedback.SetBounds(detailsLeft, previewFacts.Bottom + 4, detailsWidth, 22);
            analysisProgress.SetBounds(detailsLeft, previewFacts.Bottom + 8, Math.Max(180, detailsWidth - 12), 10);
        }

        queueToolbar.SetBounds(page, previewTop + previewHeight + gap, width, compactHeight ? 44 : 50);
        moreButton.SetBounds(width - 44, compactHeight ? 3 : 5, 44, 40);
        pauseButton.SetBounds(moreButton.Left - 12 - 164, compactHeight ? 3 : 5, 164, 40);
        var countdownLeft = Math.Max(180, pauseButton.Left - 190);
        queueTitle.SetBounds(0, compactHeight ? 4 : 7, countdownLeft - 8, 34);
        admissionCountdown.SetBounds(countdownLeft, compactHeight ? 8 : 11,
            Math.Max(0, pauseButton.Left - countdownLeft - 8), 28);

        var queueTop = queueToolbar.Bottom + 4;
        queueArea.SetBounds(page, queueTop, width, Math.Max(100, ClientSize.Height - queueTop - 16));
        var emptyCenter = Math.Max(70, queueEmpty.Height / 2);
        queueEmptyIcon.SetBounds(Math.Max(12, (queueEmpty.Width - queueEmptyIcon.Width) / 2), Math.Max(10, emptyCenter - 74), queueEmptyIcon.Width, queueEmptyIcon.Height);
        queueEmptyTitle.SetBounds(24, queueEmptyIcon.Bottom + 8, Math.Max(200, queueEmpty.Width - 48), 34);
        queueEmptyBody.SetBounds(48, queueEmptyTitle.Bottom + 4, Math.Max(160, queueEmpty.Width - 96), 48);
        ResizeCards();
    }

    private int GetPreviewHeight(bool compact)
        => (previewState, compact) switch
        {
            (PreviewState.Empty, false) => EmptyPreviewHeight,
            (PreviewState.Analyzing, false) => AnalyzingPreviewHeight,
            (PreviewState.Failed, false) => FailedPreviewHeight,
            (PreviewState.Analyzed, false) => AnalyzedPreviewHeight + (rangeEditor.IsCustom ? 20 : 0),
            (PreviewState.PlaylistAnalyzed, false) => ClientSize.Width >= 1016 ? 380 : 420,
            (PreviewState.Empty, true) => CompactEmptyPreviewHeight,
            (PreviewState.Analyzing, true) => CompactAnalyzingPreviewHeight,
            (PreviewState.Failed, true) => CompactFailedPreviewHeight,
            (PreviewState.PlaylistAnalyzed, true) => ClientSize.Width >= 1016 ? 310 : 350,
            _ => CompactAnalyzedPreviewHeight + (rangeEditor.IsCustom ? 20 : 0)
        };

    private async void PasteClicked(object? sender, EventArgs e)
    {
        try
        {
            var clipboardText = await Clipboard.GetTextAsync();
            if (!string.IsNullOrWhiteSpace(clipboardText))
            {
                urlInput.Text = clipboardText.Trim();
                if (!YtDlpMetadataService.TryValidateSourceUrl(urlInput.Text, out _, out _))
                    feedback.Text = text["Downloads.InvalidUrl"];
            }
        }
        catch (Exception ex)
        {
            services.Logger.Error("Clipboard read failed.", ex);
            feedback.Text = text["Downloads.ClipboardError"];
        }
    }

    private void UrlChanged(object? sender, EventArgs e)
    {
        analyzeButton.Enabled = YtDlpMetadataService.TryValidateSourceUrl(urlInput.Text, out _, out _);
        if (string.IsNullOrWhiteSpace(urlInput.Text) && analyzedMetadata is null)
            SetPreviewState(PreviewState.Empty);
    }

    public Task AcceptExternalUrlAsync(string sourceUrl, bool autoAnalyze)
    {
        if (!Infrastructure.ExternalMediaRequest.TryValidateSource(sourceUrl, out var normalized))
            throw new ArgumentException("Unsupported external media URL.", nameof(sourceUrl));
        urlInput.Text = normalized;
        return autoAnalyze ? AnalyzeAsync() : Task.CompletedTask;
    }

    public Task AcceptCheckedSourceAsync(SourceCheckResult request)
        => AcceptDesktopUrlAsync(request.SourceUrl, request.Playlist is not null);

    internal Task AcceptDesktopUrlAsync(string sourceUrl, bool detectedPlaylist = false)
    {
        if (!YtDlpMetadataService.TryValidateSourceUrl(sourceUrl, out var normalized, out _))
        {
            feedback.Text = text["Downloads.InvalidUrl"];
            return Task.CompletedTask;
        }
        urlInput.Text = normalized;
        return AnalyzeAsync(detectedPlaylist);
    }

    public async Task AcceptExternalRequestAsync(Infrastructure.ExternalMediaRequest request)
    {
        if (!Infrastructure.ExternalMediaRequest.TryValidateSource(request.SourceUrl, out var normalized))
            throw new ArgumentException("Unsupported external media URL.", nameof(request));
        if (!request.AutoQueue)
        {
            await AcceptExternalUrlAsync(normalized, request.AutoAnalyze);
            if (analyzedMetadata is not null)
            {
                SelectContainer(request.RequestedContainer);
                PopulateAvailableQualities(analyzedMetadata, request.RequestedQualityPresetId);
            }
            else if (YtDlpMetadataService.IsPlaylistUrl(normalized))
                playlistPreview.SetOptions(request.RequestedQualityPresetId, request.RequestedContainer);
            return;
        }

        // Quick downloads use a separate analysis path so another browser request cannot
        // cancel an in-progress request by replacing the manual preview's token.
        urlInput.Text = normalized;
        await services.Tools.EnsureAnalysisReadyAsync(lifetimeCancellation.Token);
        var preset = QualityPreset.Find(request.RequestedQualityPresetId ?? services.Settings.Current.DefaultQualityPresetId);
        if (YtDlpMetadataService.IsPlaylistUrl(normalized))
        {
            if (!request.AutoQueuePlaylist)
            {
                await AcceptExternalUrlAsync(normalized, true);
                return;
            }
            var playlist = await services.Metadata.AnalyzePlaylistAsync(normalized, lifetimeCancellation.Token);
            var selection = new PlaylistSelection(playlist);
            services.Queue.AddRangeSkippingDuplicates(selection.CreateQueueItems(preset, request.RequestedContainer,
                subtitles: services.Settings.Current.SubtitleDefaults,
                sponsorBlock: services.Settings.Current.SponsorBlockDefaults));
        }
        else
        {
            var metadata = await services.Metadata.AnalyzeAsync(normalized, lifetimeCancellation.Token);
            var availability = MediaAvailabilityPolicy.Classify(metadata);
            if (availability is MediaAvailabilityKind.ActiveLive or MediaAvailabilityKind.Upcoming)
            {
                LiveRequested?.Invoke(this, new LiveNavigationRequest(normalized, metadata, availability));
                return;
            }
            var formats = FormatSelector.Select(metadata, preset, request.RequestedContainer);
            services.Queue.AddRangeSkippingDuplicates([
                DownloadQueueItem.FromMetadata(metadata, preset, formats, request.RequestedContainer,
                    subtitles: services.Settings.Current.SubtitleDefaults,
                    sponsorBlock: services.Settings.Current.SponsorBlockDefaults)]);
        }
    }

    public void ShowExternalRequestError(Exception? error = null)
    {
        SetPreviewState(PreviewState.Failed);
        feedback.Text = error is null ? text["Error.Generic"]
            : text[DownloadFailureClassifier.Classify(error).UserMessageKey];
    }

    private async void AnalyzeClicked(object? sender, EventArgs e) => await AnalyzeAsync();

    private async Task AnalyzeAsync(bool detectedPlaylist = false)
    {
        analyzeCancellation?.Cancel();
        var currentAnalysis = new CancellationTokenSource();
        analyzeCancellation = currentAnalysis;
        var cancellationToken = currentAnalysis.Token;
        var sourceUrl = urlInput.Text;
        analyzeButton.Enabled = false;
        analyzedMetadata = null;
        previewImage.Image = null;
        currentSubtitles = services.Settings.Current.SubtitleDefaults.Copy();
        currentSponsorBlock = services.Settings.Current.SponsorBlockDefaults.Copy();
        SetPreviewState(PreviewState.Analyzing);

        try
        {
            await services.Tools.EnsureAnalysisReadyAsync(cancellationToken);
            if (disposed || !ReferenceEquals(analyzeCancellation, currentAnalysis) || cancellationToken.IsCancellationRequested)
                return;
            if (detectedPlaylist || YtDlpMetadataService.IsPlaylistUrl(sourceUrl))
            {
                var playlist = await services.Metadata.AnalyzePlaylistAsync(sourceUrl, cancellationToken, detectedPlaylist);
                if (disposed || !ReferenceEquals(analyzeCancellation, currentAnalysis) || cancellationToken.IsCancellationRequested)
                    return;
                playlistPreview.ShowPlaylist(
                    playlist,
                    services.Settings.Current.DefaultQualityPresetId,
                    services.Settings.Current.PreferredVideoContainer);
                SetPreviewState(PreviewState.PlaylistAnalyzed);
            }
            else
            {
                analyzedMetadata = await services.Metadata.AnalyzeAsync(sourceUrl, cancellationToken);
                if (disposed || !ReferenceEquals(analyzeCancellation, currentAnalysis) || cancellationToken.IsCancellationRequested)
                    return;
                var availability = MediaAvailabilityPolicy.Classify(analyzedMetadata);
                if (availability is MediaAvailabilityKind.ActiveLive or MediaAvailabilityKind.Upcoming)
                {
                    LiveRequested?.Invoke(this, new LiveNavigationRequest(sourceUrl, analyzedMetadata, availability));
                    ClearAnalyzedPreview();
                }
                else
                {
                    SetPreviewState(PreviewState.Analyzed);
                    PopulateMetadata(analyzedMetadata);
                    feedback.Text = availability == MediaAvailabilityKind.Ready
                        ? text.Get("Downloads.FormatsFound", analyzedMetadata.Formats.Count)
                        : text[DownloadFailureClassifier.Classify(new MediaAvailabilityException(availability)).UserMessageKey];
                }
            }
        }
        catch (OperationCanceledException)
        {
            if (!disposed && ReferenceEquals(analyzeCancellation, currentAnalysis))
            {
                feedback.Text = text["Downloads.AnalysisCancelled"];
                SetPreviewState(PreviewState.Empty);
            }
        }
        catch (Exception ex)
        {
            services.Logger.Error("Media analysis failed.", ex);
            if (disposed || !ReferenceEquals(analyzeCancellation, currentAnalysis)) return;
            if (DownloadFailureClassifier.Classify(ex).Category == DownloadFailureCategory.Upcoming)
            {
                LiveRequested?.Invoke(this, new LiveNavigationRequest(sourceUrl, null, MediaAvailabilityKind.Upcoming));
                ClearAnalyzedPreview();
                return;
            }
            analyzedMetadata = null;
            previewImage.Image = null;
            quality.Items.Clear();
            SetPreviewState(PreviewState.Failed);
            feedback.Text = text[DownloadFailureClassifier.Classify(ex).UserMessageKey];
        }
        finally
        {
            if (!disposed && ReferenceEquals(analyzeCancellation, currentAnalysis))
            {
                analyzeCancellation = null;
                analyzeButton.Enabled = YtDlpMetadataService.TryValidateSourceUrl(urlInput.Text, out _, out _);
            }
            currentAnalysis.Dispose();
        }
    }

    private void SetPreviewState(PreviewState state)
    {
        var stateChanged = previewState != state;
        if (state == PreviewState.Analyzing && stateChanged)
            rangeEditor.Reset();
        previewState = state;
        var analyzed = state == PreviewState.Analyzed;
        var hasOptions = analyzed;
        previewPanel.Visible = state != PreviewState.PlaylistAnalyzed;
        playlistPreview.Visible = state == PreviewState.PlaylistAnalyzed;
        previewImage.Visible = analyzed && previewImage.Image is not null;
        sourceSummary.Visible = analyzed;
        previewPlaceholder.Visible = !previewImage.Visible;
        qualityLabel.Visible = hasOptions;
        quality.Visible = hasOptions;
        containerLabel.Visible = hasOptions;
        container.Visible = hasOptions;
        rangeEditor.Visible = analyzed;
        rangeHint.Visible = analyzed && rangeEditor.IsCustom;
        addButton.Visible = hasOptions;
        detailsButton.Visible = analyzed;
        advancedButton.Visible = analyzed;
        clearPreviewButton.Visible = hasOptions;
        analysisProgress.Visible = state == PreviewState.Analyzing;
        feedback.Visible = hasOptions || state == PreviewState.Failed;
        quality.Enabled = hasOptions && quality.Items.Count > 0;
        container.Enabled = hasOptions && container.Items.Count > 0;
        addButton.Enabled = quality.Enabled;
        detailsButton.Enabled = analyzedMetadata is not null;
        advancedButton.Enabled = analyzedMetadata is not null;
        addButton.Text = text["Downloads.AddToQueue"];
        if (state == PreviewState.Empty)
        {
            previewTitle.Text = text["Downloads.EmptyTitle"];
            previewFacts.Text = text["Downloads.EmptyBody"];
            feedback.Text = string.Empty;
        }
        else if (state == PreviewState.Analyzing)
        {
            previewTitle.Text = text["Downloads.AnalyzingTitle"];
            previewFacts.Text = text["Downloads.AnalyzingBody"];
            feedback.Text = string.Empty;
        }
        else if (state == PreviewState.Failed)
        {
            previewTitle.Text = text["Downloads.AnalysisFailedTitle"];
            previewFacts.Text = text["Downloads.AnalysisFailedBody"];
            feedback.Text = text["Error.Generic"];
        }


        UpdateLayout();
        if (stateChanged)
            AnimatePreviewState();
    }

    private void ClearAnalyzedPreview()
    {
        UiCrashDiagnostics.VerifyUiThread("downloads.clear-preview");
        analyzedMetadata = null;
        previewImage.Image = null;
        currentSubtitles = services.Settings.Current.SubtitleDefaults.Copy();
        currentSponsorBlock = services.Settings.Current.SponsorBlockDefaults.Copy();
        quality.SelectedIndex = -1;
        quality.Items.Clear();
        container.SelectedIndex = -1;
        container.Items.Clear();
        rangeEditor.Reset();
        SetPreviewState(PreviewState.Empty);
        UiCrashDiagnostics.Record("preview.clear");
    }

    private void AnimatePreviewState()
    {
        previewPanel.CancelAnimations();
        previewPanel.Opacity = 0.86f;
        previewPanel.TranslationY = 6f;
        _ = Task.WhenAll(
            previewPanel.FadeToAsync(1f, 190, Easings.CubicOut),
            previewPanel.TranslateToAsync(0f, 0f, 190, Easings.CubicOut));
    }

    private void PopulateMetadata(VideoMetadata metadata)
    {
        sourceSummary.Multiline = true;
        var sourceFacts = ExtractorIdentityService.Facts(metadata);
        var sourceName = string.IsNullOrWhiteSpace(sourceFacts.Identity.DisplayName)
            ? text["Common.NotAvailable"] : sourceFacts.Identity.DisplayName;
        sourceSummary.Text = $"{text["Sources.Source"]}: {sourceName}" + Environment.NewLine +
            text[$"Sources.Type.{sourceFacts.Kind}"];
        previewTitle.Text = metadata.Title;
        previewFacts.Text = $"{metadata.DisplayChannel}  •  {FormatDuration(metadata.DurationSeconds)}  •  {FormatViews(metadata.ViewCount)}  •  {FormatDate(metadata.UploadDate)}";
        PopulateContainers(SelectedContainer());
        PopulateAvailableQualities(metadata);
        _ = LoadPreviewThumbnailAsync(metadata.ThumbnailUrl);
    }

    private void PopulateAvailableQualities(VideoMetadata metadata, string? selectedPresetId = null)
    {
        var currentPresetId = selectedPresetId ?? (quality.SelectedItem as LocalizedOption<QualityPreset>)?.Value.Id
            ?? services.Settings.Current.DefaultQualityPresetId;
        quality.Items.Clear();
        foreach (var preset in FormatSelector.GetAvailablePresets(metadata, SelectedContainer()))
            quality.Items.Add(new LocalizedOption<QualityPreset>(preset, text[$"Quality.{preset.Id}"]));
        SelectQuality(currentPresetId);
        quality.Enabled = quality.Items.Count > 0;
        addButton.Enabled = quality.Enabled;
    }

    private void PopulateContainers(PreferredVideoContainer selected)
    {
        container.SelectedIndex = -1;
        container.Items.Clear();
        foreach (var value in Enum.GetValues<PreferredVideoContainer>())
            container.Items.Add(new LocalizedOption<PreferredVideoContainer>(value, text[$"Container.{value}"]));
        SelectContainer(selected);
    }

    private PreferredVideoContainer SelectedContainer() =>
        (container.SelectedItem as LocalizedOption<PreferredVideoContainer>)?.Value
        ?? services.Settings.Current.PreferredVideoContainer;

    private void SelectContainer(PreferredVideoContainer selected)
    {
        for (var index = 0; index < container.Items.Count; index++)
            if (container.Items[index] is LocalizedOption<PreferredVideoContainer> option && option.Value == selected)
            {
                container.SelectedIndex = index;
                return;
            }
        if (container.Items.Count > 0)
            container.SelectedIndex = 0;
    }

    private void ContainerChanged(object? sender, EventArgs e)
    {
        if (analyzedMetadata is not null)
            PopulateAvailableQualities(analyzedMetadata);
    }

    private void SelectQuality(string presetId)
    {
        for (var index = 0; index < quality.Items.Count; index++)
        {
            if (quality.Items[index] is LocalizedOption<QualityPreset> option && option.Value.Id == presetId)
            {
                quality.SelectedIndex = index;
                return;
            }
        }
        if (quality.Items.Count > 0)
            quality.SelectedIndex = 0;
    }

    private async Task LoadPreviewThumbnailAsync(string? url)
    {
        if (!services.Settings.Current.DownloadThumbnail)
            return;
        var expectedMetadata = analyzedMetadata;
        SKBitmap? image;
        try { image = await services.Thumbnails.GetAsync(url, lifetimeCancellation.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) { return; }
        if (image is not null && ReferenceEquals(expectedMetadata, analyzedMetadata))
            Application.RunOnUIThread(() =>
            {
                if (disposed || lifetimeCancellation.IsCancellationRequested || !ReferenceEquals(expectedMetadata, analyzedMetadata))
                    return;
                previewImage.Image = image;
                previewImage.Visible = true;
                previewPlaceholder.Visible = false;
            });
    }

    private void AddClicked(object? sender, EventArgs e)
    {
        if (analyzedMetadata is null || quality.SelectedItem is not LocalizedOption<QualityPreset> selected)
            return;
        try
        {
            if (!rangeEditor.TryGetRange(analyzedMetadata.DurationSeconds,
                    analyzedMetadata.IsLive == true || analyzedMetadata.LiveStatus == "is_live",
                    out var requestedRange, out var rangeError))
            {
                feedback.Text = text[rangeError!];
                return;
            }
            if (services.Queue.ContainsPendingOrActive(analyzedMetadata.Id, selected.Value.Id, SelectedContainer(),
                    requestedRange, currentSubtitles, currentSponsorBlock))
            {
                feedback.Text = text["Downloads.AlreadyQueued"];
                return;
            }
            var selectedContainer = SelectedContainer();
            var selection = FormatSelector.Select(analyzedMetadata, selected.Value, selectedContainer);
            if (!CanQueueWithEnhancements(requestedRange, out var enhancementError))
            {
                feedback.Text = enhancementError;
                return;
            }
            var item = DownloadQueueItem.FromMetadata(analyzedMetadata, selected.Value, selection, selectedContainer,
                requestedRange, currentSubtitles, currentSponsorBlock);
            services.Queue.Add(item);
            selectedItemId = item.Id;
            feedback.Text = text.Get("Downloads.Added", item.Title);
        }
        catch (Exception ex)
        {
            services.Logger.Error("Adding a queue item failed.", ex);
            feedback.Text = text[DownloadFailureClassifier.Classify(ex).UserMessageKey];
        }
    }

    private void PlaylistAddRequested(object? sender, EventArgs e)
    {
        if (playlistPreview.Selection is not { } selection) return;
        try
        {
            if (!playlistPreview.TryGetRange(out var requestedRange, out var rangeError))
            {
                playlistPreview.SetFeedback(text[rangeError!]);
                return;
            }
            if (!CanQueueWithEnhancements(requestedRange, out var enhancementError))
            {
                playlistPreview.SetFeedback(enhancementError);
                return;
            }
            var candidates = selection.CreateQueueItems(playlistPreview.SelectedPreset, playlistPreview.SelectedContainer,
                requestedRange, currentSubtitles, currentSponsorBlock);
            var result = services.Queue.AddRangeSkippingDuplicates(candidates);
            var unavailable = selection.Playlist.Entries.Count(entry => !entry.IsAvailable);
            playlistPreview.SetFeedback(text.Get("Playlist.AddSummary", result.Added, result.DuplicatesSkipped, unavailable));
            if (result.Added > 0)
                selectedItemId = services.Queue.Snapshot().Last().Id;
        }
        catch (Exception ex)
        {
            services.Logger.Error("Adding selected playlist entries failed.", ex);
            playlistPreview.SetFeedback(ex is ArgumentException ? text["Range.Error.Duration"] : text["Error.Generic"]);
        }
    }

    private async void DetailsClicked(object? sender, EventArgs e)
    {
        if (analyzedMetadata is not { } metadata || FindForm() is not { } owner)
            return;

        var videoId = string.IsNullOrWhiteSpace(metadata.Id) ? "<unknown>" : metadata.Id;
        services.Logger.Info($"Opening video details for id={videoId}.");
        try
        {
            using var form = new VideoDetailsForm(metadata, text);
#if DEBUG
            using var automationRegistration = RegisterDetailsForAutomation?.Invoke(form);
#endif
            await form.ShowDialog(owner);
            services.Logger.Info($"Closed video details for id={videoId}.");
        }
        catch (Exception ex)
        {
            services.Logger.Error($"Opening video details failed for id={videoId}.", ex);
            feedback.Text = text["Details.OpenFailed"];
            feedback.Visible = true;
        }
    }

    private async void AdvancedClicked(object? sender, EventArgs e)
    {
        if (FindForm() is not { } owner) return;
        try
        {
            using var form = new AdvancedDownloadOptionsForm(text, services.Settings.Current,
                previewState == PreviewState.Analyzed ? analyzedMetadata : null,
                currentSubtitles, currentSponsorBlock,
                previewState == PreviewState.PlaylistAnalyzed ? playlistPreview.SelectedContainer : SelectedContainer());
#if DEBUG
            using var automationRegistration = RegisterAdvancedForAutomation?.Invoke(form);
#endif
            if (await form.ShowDialog(owner) != DialogResult.OK) return;
            currentSubtitles = form.Subtitles;
            currentSponsorBlock = form.SponsorBlock;
            if (currentSponsorBlock.Mode == SponsorBlockMode.Remove)
                feedback.Text = text["SponsorBlock.RemoveExperimentalWarning"];
        }
        catch (Exception ex)
        {
            services.Logger.Error("Opening advanced download options failed.", ex);
            if (previewState == PreviewState.PlaylistAnalyzed)
                playlistPreview.SetFeedback(text["Error.Generic"]);
            else feedback.Text = text["Error.Generic"];
        }
    }

    private bool CanQueueWithEnhancements(MediaTimeRange range, out string error)
    {
        error = string.Empty;
        var selectedContainer = previewState == PreviewState.PlaylistAnalyzed
            ? playlistPreview.SelectedContainer : SelectedContainer();
        var issue = DownloadOptionCompatibilityValidator.Check(range, currentSubtitles,
            currentSponsorBlock, selectedContainer);
        if (issue != DownloadOptionIssue.None)
            error = text[DownloadOptionCompatibilityValidator.MessageKey(issue)];
        return error.Length == 0;
    }

    private void QueueChanged(object? sender, QueueChangedEventArgs e)
    {
        Application.RunOnUIThread(() =>
        {
            pauseButton.Text = services.Queue.IsPaused ? text["Queue.Resume"] : text["Queue.Pause"];
            UpdateAdmissionCountdown();
            if (e.Kind is QueueChangeKind.Collection or QueueChangeKind.Order)
                BuildQueueCards(e.Kind == QueueChangeKind.Collection ? e.ItemId : null);
            else if (e.ItemId is { } id && cards.TryGetValue(id, out var card) && services.Queue.Find(id) is { } item)
                card.UpdateItem(item, selectedItemId == id);
            BuildQueueMenu();
        });
    }

    private void ToolStatusChanged(object? sender, EventArgs e)
        => Application.RunOnUIThread(UpdateEngineStatus);

    private void SettingsChanged(object? sender, EventArgs e)
        => Application.RunOnUIThread(() =>
        {
            UpdateQueuePositionBadges(services.Queue.Snapshot());
            UpdateAdmissionCountdown();
        });

    private void UpdateAdmissionCountdown()
    {
        var remaining = services.Processor.RemainingAdmissionDelay;
        var seconds = remaining is { } delay ? (int)Math.Ceiling(delay.TotalSeconds) : 0;
        admissionCountdown.Visible = seconds > 0;
        if (seconds > 0) admissionCountdown.Text = text.Get("Queue.NextDownloadIn", seconds);
    }

    private void UpdateEngineStatus()
    {
        var engine = services.Tools.EngineStatus;
        toolStatus.Text = engine switch
        {
            DownloadEngineStatus.Ready => text["Header.EngineReady"],
            DownloadEngineStatus.Degraded => text["Header.EngineDegraded"],
            DownloadEngineStatus.Failed => text["Header.EngineFailed"],
            _ => text["Header.EnginePreparing"]
        };
        AppUi.BindBackground(toolStatusDot, engine switch
        {
            DownloadEngineStatus.Ready => AppThemeTokens.Success,
            DownloadEngineStatus.Degraded => AppThemeTokens.Warning,
            DownloadEngineStatus.Failed => AppThemeTokens.Error,
            _ => AppThemeTokens.Info
        });
    }

    private void BuildQueueCards(Guid? scrollToItemId = null)
    {
        UiCrashDiagnostics.VerifyUiThread("downloads.queue-rebuild");
        UiCrashDiagnostics.Record("queue.rebuild.begin", "queueHost", queueHost.Controls.Count);
        var previousIds = cards.Keys.ToHashSet();
        var items = services.Queue.Snapshot();
        if (selectedItemId is { } selected && items.All(item => item.Id != selected))
            selectedItemId = null;
        queueHost.SuspendLayout();
        try
        {
            foreach (var card in cards.Values)
                card.Dispose();
            cards.Clear();
            queueHost.Controls.Clear();
            foreach (var item in items)
            {
                var card = new QueueItemCard(services.Thumbnails, text);
                card.ActionRequested += CardActionRequested;
                cards[item.Id] = card;
                queueHost.Controls.Add(card);
                card.UpdateItem(item, selectedItemId == item.Id);
                if (queueBuilt && items.Count <= 40 && !previousIds.Contains(item.Id))
                {
                    card.Opacity = 0f;
                    card.TranslationY = 8f;
                    _ = Task.WhenAll(
                        card.FadeToAsync(1f, 190, Easings.CubicOut),
                        card.TranslateToAsync(0f, 0f, 190, Easings.CubicOut));
                }
            }
        }
        finally { queueHost.ResumeLayout(false); }
        UpdateQueuePositionBadges(items);
        queueBuilt = true;
        queueEmpty.Visible = items.Count == 0;
        queueHost.Visible = items.Count != 0;
        if (queueEmpty.Visible)
            queueEmpty.BringToFront();
        else
            queueHost.BringToFront();
        ResizeCards();
        queueHost.PerformLayout();
        if (scrollToItemId is { } id && !previousIds.Contains(id) && cards.ContainsKey(id))
            ScrollQueueCardIntoView(id);
        BuildQueueMenu();
        UiCrashDiagnostics.Record("queue.rebuild.end", "queueHost", queueHost.Controls.Count);
    }

    private void UpdateQueuePositionBadges(IReadOnlyList<DownloadQueueItem> items)
    {
        var positions = QueuePositionPresenter.Build(items, services.Settings.Current.ShowQueuePositionNumbers);
        foreach (var (id, card) in cards)
            card.SetQueuePosition(positions.GetValueOrDefault(id));
    }

    private void ScrollQueueCardIntoView(Guid itemId)
    {
        if (!cards.TryGetValue(itemId, out var card))
            return;

        var scroll = queueHost.VerticalScrollProperties;
        var targetValue = QueueScrollPlanner.CalculateVerticalValue(
            queueHost.DisplayRectangle,
            card.Bounds,
            scroll.Value,
            scroll.Minimum,
            scroll.Maximum);
        if (targetValue != scroll.Value)
            scroll.Value = targetValue;
    }

    private void ResizeCards()
    {
        // DisplayRectangle reflects the space left after FlowLayoutPanel padding and any
        // visible vertical scrollbar. Size cards to that viewport so vertical overflow does
        // not create a secondary horizontal scrollbar.
        var width = Math.Max(520, queueHost.DisplayRectangle.Width - 4);
        foreach (var card in cards.Values)
            card.Width = width;
    }

    private void BuildQueueMenu()
    {
        var items = services.Queue.Snapshot();
        var selectedIndex = selectedItemId is { } id ? Array.FindIndex(items.ToArray(), item => item.Id == id) : -1;
        var selected = selectedIndex >= 0 ? items[selectedIndex] : null;
        var movable = selected?.Status is DownloadStatus.Queued or DownloadStatus.Waiting;
        var removable = selected is not null && selected.Status is not (
            DownloadStatus.Waiting or DownloadStatus.DownloadingVideo or DownloadStatus.DownloadingAudio or
            DownloadStatus.Merging or DownloadStatus.Finalizing);
        var cancellable = selected?.Status is DownloadStatus.Waiting or DownloadStatus.DownloadingVideo or
            DownloadStatus.DownloadingAudio or DownloadStatus.Merging or DownloadStatus.Finalizing;
        var retryable = selected?.Status is DownloadStatus.Failed or DownloadStatus.Cancelled or
            DownloadStatus.Partial or DownloadStatus.Interrupted;
        queueMenu.Items.Clear();
        AddQueueMenu(text["Queue.ClearPending"], () => services.Queue.RemoveAllPending(),
            items.Any(item => item.Status is DownloadStatus.Queued or DownloadStatus.Waiting));
        queueMenu.Items.Add(new MenuSeparatorItem());
        AddQueueMenu(text["Queue.MoveFirst"], () => MoveSelectedTo(0), movable && selectedIndex > 0);
        AddQueueMenu(text["Queue.MoveUp"], () => MoveSelectedBy(-1), movable && selectedIndex > 0);
        AddQueueMenu(text["Queue.MoveDown"], () => MoveSelectedBy(1), movable && selectedIndex < items.Count - 1);
        AddQueueMenu(text["Queue.MoveLast"], () => MoveSelectedTo(int.MaxValue), movable && selectedIndex < items.Count - 1);
        queueMenu.Items.Add(new MenuSeparatorItem());
        AddQueueMenu(text["Queue.RetrySelected"], RetrySelected, retryable);
        AddQueueMenu(text["Queue.RemoveSelected"], RemoveSelected, removable);
        AddQueueMenu(text["Queue.CancelSelected"], CancelSelected, cancellable);
    }

    private void AddQueueMenu(string label, Action action, bool enabled = true)
    {
        var item = queueMenu.Items.Add(label, null, (_, _) => action());
        item.Enabled = enabled;
    }

    private async void CardActionRequested(object? sender, QueueItemActionEventArgs e)
    {
        selectedItemId = e.ItemId;
        switch (e.Action)
        {
            case QueueItemAction.Select: RefreshSelection(); break;
            case QueueItemAction.Remove: services.Queue.Remove(e.ItemId); break;
            case QueueItemAction.MoveUp: MoveSelectedBy(-1); break;
            case QueueItemAction.MoveDown: MoveSelectedBy(1); break;
            case QueueItemAction.MoveFirst: MoveSelectedTo(0); break;
            case QueueItemAction.MoveLast: MoveSelectedTo(int.MaxValue); break;
            case QueueItemAction.Retry: services.Queue.Retry(e.ItemId); break;
            case QueueItemAction.Details: await ShowQueueDetailsAsync(e.ItemId); break;
            case QueueItemAction.Cancel: services.Processor.CancelActive(e.ItemId); break;

            case QueueItemAction.OpenFile: ShellService.OpenFile(services.Queue.Find(e.ItemId)?.FinalFile); break;
            case QueueItemAction.OpenFolder: ShellService.OpenFolderForFile(services.Queue.Find(e.ItemId)?.FinalFile); break;
        }
        BuildQueueMenu();
    }

    private async Task ShowQueueDetailsAsync(Guid itemId)
    {
        var item = services.Queue.Find(itemId);
        var owner = FindForm();
        if (item is null || owner is null)
            return;

        var status = text[$"Status.{item.Status}"];
        var localizedDetail = item.StatusMessageKey is { Length: > 0 } key ? text[key] : item.StatusMessage;
        var failure = item.FailureMessageKey is { Length: > 0 } failureKey
            ? text[failureKey] : item.ErrorMessage;
        var detail = failure is { Length: > 0 }
            ? $"{localizedDetail}\n\n{failure}\n\n{text["Error.Suggestion"]}" : localizedDetail;
        if (item.FailureTechnicalSummary is { Length: > 0 } technical)
            detail += $"\n\n{text["Error.Details"]}\n{technical}";
        using var dialog = new MessageBoxForm(
            text.Get("Queue.DetailsTitle", item.Title),
            text.Get("Queue.DetailsBody", status, detail));
        await dialog.ShowDialog(owner);
    }

    private void RefreshSelection()
    {
        foreach (var pair in cards)
        {
            if (services.Queue.Find(pair.Key) is { } item)
                pair.Value.UpdateItem(item, selectedItemId == pair.Key);
        }
    }

    private void MoveSelectedBy(int offset)
    {
        if (selectedItemId is { } id)
            services.Queue.MoveBy(id, offset);
    }

    private void MoveSelectedTo(int target)
    {
        if (selectedItemId is not { } id)
            return;
        var items = services.Queue.Snapshot();
        services.Queue.Move(id, target == int.MaxValue ? items.Count - 1 : target);
    }

    private void RetrySelected()
    {
        if (selectedItemId is { } id)
            services.Queue.Retry(id);
    }

    private void RemoveSelected()
    {
        if (selectedItemId is { } id && services.Queue.Remove(id))
            selectedItemId = null;
    }

    private void CancelSelected()
    {
        if (selectedItemId is { } id)
            services.Processor.CancelActive(id);
    }

    private void ApplyLocalization()
    {
        pageTitle.Text = text["Downloads.Title"];
        pageSubtitle.Text = text["Downloads.Subtitle"];
        urlLabel.Text = text["Downloads.UrlLabel"];
        supportedSourcesButton.Text = text["Sources.Link"];
        urlInput.Placeholder = text["Downloads.UrlPlaceholder"];
        pasteButton.AccessibleName = text["Downloads.PasteTooltip"];
        pasteToolTip.SetToolTip(pasteButton, text["Downloads.PasteTooltip"]);
        pasteToolTip.SetToolTip(pasteIcon, text["Downloads.PasteTooltip"]);
        pasteToolTip.SetToolTip(clearPreviewButton, text["Downloads.ClearPreview"]);
        analyzeButton.Text = text["Common.Analyze"];
        qualityLabel.Text = text["Downloads.Quality"];
        containerLabel.Text = text["Downloads.Container"];
        rangeHint.Text = text["Range.KeyframeWarning"];
        addButton.Text = text["Downloads.AddToQueue"];
        detailsButton.Text = text["Common.Details"];
        advancedButton.Text = text["Advanced.MoreOptions"];
        queueTitle.Text = text["Downloads.QueueTitle"];
        pauseButton.Text = services.Queue.IsPaused ? text["Queue.Resume"] : text["Queue.Pause"];
        queueEmptyTitle.Text = text["Downloads.QueueEmptyTitle"];
        queueEmptyBody.Text = text["Downloads.QueueEmptyBody"];
        UpdateEngineStatus();
        SetPreviewState(previewState);
        if (analyzedMetadata is not null) PopulateMetadata(analyzedMetadata);
        BuildQueueMenu();
    }

    private string FormatViews(long? views)
        => views is null ? text["Common.NotAvailable"] : text.Get("Downloads.Views", views.Value.ToString("N0", text.Culture));

    private static string FormatDuration(double? seconds)
        => seconds is null ? "—" : TimeSpan.FromSeconds(seconds.Value).ToString(@"hh\:mm\:ss");

    private string FormatDate(string? value)
        => value is { Length: 8 } && DateTime.TryParseExact(value, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date.ToString("d", text.Culture)
            : value ?? text["Common.NotAvailable"];

    private void ViewSizeChanged(object? sender, EventArgs e) => UpdateLayout();
    private void QueueHostSizeChanged(object? sender, EventArgs e) => ResizeCards();
    private void LanguageChanged(object? sender, EventArgs e) => Application.RunOnUIThread(ApplyLocalization);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            disposed = true;
            lifetimeCancellation.Cancel();
            services.Queue.Changed -= QueueChanged;
            services.Settings.Changed -= SettingsChanged;
            services.Tools.Changed -= ToolStatusChanged;
            text.LanguageChanged -= LanguageChanged;
            playlistPreview.AddRequested -= PlaylistAddRequested;
            playlistPreview.AdvancedOptionsRequested -= AdvancedClicked;
            SizeChanged -= ViewSizeChanged;
            queueHost.SizeChanged -= QueueHostSizeChanged;
            pasteToolTip.Dispose();
            admissionCountdownTimer.Dispose();
            analyzeCancellation?.Cancel();
            lifetimeCancellation.Dispose();
        }
        base.Dispose(disposing);
    }
}
