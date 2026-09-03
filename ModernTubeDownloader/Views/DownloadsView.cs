using System.Globalization;
using ModernFormsNext;
using ModernFormsNext.Animations;
using ModernTubeDownloader.Localization;
using ModernTubeDownloader.Models;
using ModernTubeDownloader.Services;
using ModernTubeDownloader.Theming;
using ModernTubeDownloader.Utilities;

namespace ModernTubeDownloader.Views;

internal sealed class DownloadsView : UserControl
{
    private enum PreviewState
    {
        Empty,
        Analyzing,
        Analyzed,
        Failed
    }

    private const int EmptyPreviewHeight = 126;
    private const int AnalyzingPreviewHeight = 140;
    private const int FailedPreviewHeight = 154;
    private const int AnalyzedPreviewHeight = 178;
    private const int CompactEmptyPreviewHeight = 116;
    private const int CompactAnalyzingPreviewHeight = 130;
    private const int CompactFailedPreviewHeight = 144;
    private const int CompactAnalyzedPreviewHeight = 170;

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
    private readonly Panel previewPlaceholder;
    private readonly AppVectorIcon previewIcon;
    private readonly Label previewPlaceholderText;
    private readonly PictureBox previewImage;
    private readonly Label previewTitle;
    private readonly Label previewFacts;
    private readonly Label qualityLabel;
    private readonly Label feedback;
    private readonly ComboBox quality;
    private readonly Button addButton;
    private readonly Button detailsButton;
    private readonly ProgressBar analysisProgress;
    private readonly Panel queueToolbar;
    private readonly Label queueTitle;
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
    private CancellationTokenSource? analyzeCancellation;
    private Guid? selectedItemId;
    private PreviewState previewState;
    private bool queueBuilt;

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
        urlInput = new TextBox
        {
            Height = 48,
            Padding = new Padding(14, 0, 14, 0),
            TextAlign = ContentAlignment.MiddleLeft
        };
        AppUi.Input(urlInput);
        urlInput.Font = new Font("Segoe UI", 12.5f);
        pasteButton = new Button { Height = 48, Width = 48 };
        analyzeButton = new Button { Height = 48 };
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
        urlPanel.Controls.AddRange([urlLabel, urlInput, pasteButton, analyzeButton]);

        previewPanel = new Panel();
        AppUi.Card(previewPanel);
        previewPlaceholder = new Panel();
        AppUi.Card(previewPlaceholder, secondary: true);
        previewIcon = new AppVectorIcon(AppIconKind.Download, 30, AppThemeTokens.TextSecondary);
        previewPlaceholderText = AppUi.Heading("URL", 11.5f);
        previewPlaceholderText.TextAlign = ContentAlignment.MiddleCenter;
        previewPlaceholder.Controls.AddRange([previewIcon, previewPlaceholderText]);
        previewImage = new PictureBox { SizeMode = PictureBoxSizeMode.Zoom, Visible = false };
        previewImage.Style.Border.Radius = 12;
        previewImage.Style.Border.Width = 0;
        AppUi.BindBackground(previewImage, AppThemeTokens.SurfaceSecondary);
        previewTitle = AppUi.Heading(string.Empty, 17f);
        previewTitle.Multiline = true;
        previewFacts = AppUi.Muted();
        previewFacts.Multiline = true;
        qualityLabel = AppUi.Muted();
        quality = new ComboBox { Enabled = false };
        AppUi.Input(quality);
        addButton = new Button { Enabled = false };
        detailsButton = new Button { Enabled = false };
        AppUi.Primary(addButton);
        AppUi.Secondary(detailsButton);
        feedback = AppUi.Muted();
        analysisProgress = new ProgressBar { Minimum = 0, Maximum = 100, Value = 45, Visible = false };
        addButton.Click += AddClicked;
        detailsButton.Click += DetailsClicked;
        previewPanel.Controls.AddRange([
            previewPlaceholder,
            previewImage,
            previewTitle,
            previewFacts,
            qualityLabel,
            quality,
            addButton,
            detailsButton,
            feedback,
            analysisProgress]);

        queueToolbar = new Panel();
        AppUi.BindBackground(queueToolbar);
        queueTitle = AppUi.Heading(string.Empty, 16.5f);
        pauseButton = new Button();
        moreButton = new Button { Text = "…", Width = 44 };
        AppUi.Secondary(pauseButton);
        AppUi.Secondary(moreButton);
        pauseButton.Click += (_, _) => services.Queue.SetPaused(!services.Queue.IsPaused);
        moreButton.Click += (_, _) => Application.RunOnUIThread(
            () => queueMenu.Show(
                moreButton,
                moreButton.PointToScreen(new System.Drawing.Point(0, moreButton.Height + 4))));
        queueToolbar.Controls.AddRange([queueTitle, pauseButton, moreButton]);

        queueArea = new Panel();
        AppUi.BindBackground(queueArea);
        queueHost = new FlowLayoutPanel
        {
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

        Controls.AddRange([header, urlPanel, previewPanel, queueToolbar, queueArea]);
        SizeChanged += ViewSizeChanged;
        text.LanguageChanged += LanguageChanged;
        services.Queue.Changed += QueueChanged;
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
        urlLabel.SetBounds(22, compactHeight ? 8 : 12, width - 44, 24);
        var analyzeWidth = width < 760 ? 112 : 136;
        var inputTop = compactHeight ? 38 : 48;
        const int inputHeight = 48;
        analyzeButton.SetBounds(width - 22 - analyzeWidth, inputTop, analyzeWidth, inputHeight);
        pasteButton.SetBounds(analyzeButton.Left - 10 - inputHeight, inputTop, inputHeight, inputHeight);
        urlInput.SetBounds(22, inputTop, Math.Max(220, pasteButton.Left - 32), inputHeight);

        var previewTop = urlPanel.Bottom + gap;
        var previewHeight = GetPreviewHeight(compactHeight);
        previewPanel.SetBounds(page, previewTop, width, previewHeight);
        var analyzed = previewState == PreviewState.Analyzed;
        var thumbnailWidth = analyzed
            ? Math.Clamp((int)Math.Round(width * 0.18d), 176, 216)
            : compactHeight ? 96 : 112;
        var thumbnailHeight = analyzed
            ? (int)Math.Round(thumbnailWidth * 9d / 16d)
            : compactHeight ? 64 : 72;
        var thumbnailTop = Math.Max(12, (previewHeight - thumbnailHeight) / 2);
        previewPlaceholder.SetBounds(22, thumbnailTop, thumbnailWidth, thumbnailHeight);
        previewImage.SetBounds(22, thumbnailTop, thumbnailWidth, thumbnailHeight);
        var iconGroupHeight = previewIcon.Height + 4 + 22;
        previewIcon.SetBounds(
            (thumbnailWidth - previewIcon.Width) / 2,
            Math.Max(6, (thumbnailHeight - iconGroupHeight) / 2),
            previewIcon.Width,
            previewIcon.Height);
        previewPlaceholderText.SetBounds(8, previewIcon.Bottom + 4, thumbnailWidth - 16, 22);
        var detailsLeft = previewPlaceholder.Right + 20;
        var detailsWidth = width - detailsLeft - 22;
        if (analyzed)
        {
            previewTitle.SetBounds(detailsLeft, 10, detailsWidth, 38);
            previewFacts.SetBounds(detailsLeft, 49, detailsWidth, 20);
            qualityLabel.SetBounds(detailsLeft, 71, 84, 17);
            feedback.SetBounds(detailsLeft + 92, 71, Math.Max(120, detailsWidth - 92), 17);
            var comboWidth = Math.Min(300, Math.Max(210, detailsWidth - 24));
            quality.SetBounds(detailsLeft, 89, comboWidth, 36);
            var actionTop = previewHeight - 46;
            var detailsActionWidth = Math.Min(132, Math.Max(112, detailsWidth / 4));
            var addWidth = Math.Min(184, Math.Max(154, detailsWidth / 3));
            detailsButton.SetBounds(detailsLeft, actionTop, detailsActionWidth, 36);
            addButton.SetBounds(detailsButton.Right + 10, actionTop, addWidth, 36);
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

        queueToolbar.SetBounds(page, previewPanel.Bottom + gap, width, compactHeight ? 44 : 50);
        queueTitle.SetBounds(0, compactHeight ? 4 : 7, Math.Max(180, width - 336), 34);
        moreButton.SetBounds(width - 44, compactHeight ? 3 : 5, 44, 40);
        pauseButton.SetBounds(moreButton.Left - 12 - 164, compactHeight ? 3 : 5, 164, 40);

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
            (PreviewState.Analyzed, false) => AnalyzedPreviewHeight,
            (PreviewState.Empty, true) => CompactEmptyPreviewHeight,
            (PreviewState.Analyzing, true) => CompactAnalyzingPreviewHeight,
            (PreviewState.Failed, true) => CompactFailedPreviewHeight,
            _ => CompactAnalyzedPreviewHeight
        };

    private async void PasteClicked(object? sender, EventArgs e)
    {
        try
        {
            var clipboardText = await Clipboard.GetTextAsync();
            if (!string.IsNullOrWhiteSpace(clipboardText))
                urlInput.Text = clipboardText.Trim();
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

    private async void AnalyzeClicked(object? sender, EventArgs e)
    {
        analyzeCancellation?.Cancel();
        analyzeCancellation?.Dispose();
        analyzeCancellation = new CancellationTokenSource();
        var cancellationToken = analyzeCancellation.Token;
        analyzeButton.Enabled = false;
        analyzedMetadata = null;
        previewImage.Image = null;
        SetPreviewState(PreviewState.Analyzing);

        try
        {
            await services.Tools.EnsureReadyAsync(cancellationToken);
            analyzedMetadata = await services.Metadata.AnalyzeAsync(urlInput.Text, cancellationToken);
            PopulateMetadata(analyzedMetadata);
            SetPreviewState(PreviewState.Analyzed);
            feedback.Text = text.Get("Downloads.FormatsFound", analyzedMetadata.Formats.Count);
        }
        catch (OperationCanceledException)
        {
            feedback.Text = text["Downloads.AnalysisCancelled"];
            SetPreviewState(PreviewState.Empty);
        }
        catch (Exception ex)
        {
            services.Logger.Error("Media analysis failed.", ex);
            analyzedMetadata = null;
            previewImage.Image = null;
            quality.Items.Clear();
            SetPreviewState(PreviewState.Failed);
        }
        finally
        {
            analyzeButton.Enabled = YtDlpMetadataService.TryValidateSourceUrl(urlInput.Text, out _, out _);
        }
    }

    private void SetPreviewState(PreviewState state)
    {
        var stateChanged = previewState != state;
        previewState = state;
        var analyzed = state == PreviewState.Analyzed;
        previewImage.Visible = analyzed && previewImage.Image is not null;
        previewPlaceholder.Visible = !previewImage.Visible;
        qualityLabel.Visible = analyzed;
        quality.Visible = analyzed;
        addButton.Visible = analyzed;
        detailsButton.Visible = analyzed;
        analysisProgress.Visible = state == PreviewState.Analyzing;
        feedback.Visible = analyzed || state == PreviewState.Failed;
        quality.Enabled = analyzed && quality.Items.Count > 0;
        addButton.Enabled = quality.Enabled;
        detailsButton.Enabled = analyzedMetadata is not null;
        previewPlaceholderText.Text = state switch
        {
            PreviewState.Analyzing => "…",
            PreviewState.Analyzed => "MTD",
            _ => "URL"
        };

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
        previewTitle.Text = metadata.Title;
        previewFacts.Text = $"{metadata.DisplayChannel}  •  {FormatDuration(metadata.DurationSeconds)}  •  {FormatViews(metadata.ViewCount)}  •  {FormatDate(metadata.UploadDate)}";
        var currentPresetId = (quality.SelectedItem as LocalizedOption<QualityPreset>)?.Value.Id
            ?? services.Settings.Current.DefaultQualityPresetId;
        quality.Items.Clear();
        foreach (var preset in FormatSelector.GetAvailablePresets(metadata))
            quality.Items.Add(new LocalizedOption<QualityPreset>(preset, text[$"Quality.{preset.Id}"]));
        SelectQuality(currentPresetId);
        _ = LoadPreviewThumbnailAsync(metadata.ThumbnailUrl);
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
        var image = await services.Thumbnails.GetAsync(url).ConfigureAwait(false);
        if (image is not null && ReferenceEquals(expectedMetadata, analyzedMetadata))
            Application.RunOnUIThread(() =>
            {
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
            var selection = FormatSelector.Select(analyzedMetadata, selected.Value);
            var item = DownloadQueueItem.FromMetadata(analyzedMetadata, selected.Value, selection);
            services.Queue.Add(item);
            selectedItemId = item.Id;
            feedback.Text = text.Get("Downloads.Added", item.Title);
        }
        catch (Exception ex)
        {
            services.Logger.Error("Adding a queue item failed.", ex);
            feedback.Text = text["Error.Generic"];
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

    private void QueueChanged(object? sender, QueueChangedEventArgs e)
    {
        Application.RunOnUIThread(() =>
        {
            pauseButton.Text = services.Queue.IsPaused ? text["Queue.Resume"] : text["Queue.Pause"];
            if (e.Kind is QueueChangeKind.Collection or QueueChangeKind.Order)
                BuildQueueCards(e.Kind == QueueChangeKind.Collection ? e.ItemId : null);
            else if (e.ItemId is { } id && cards.TryGetValue(id, out var card) && services.Queue.Find(id) is { } item)
                card.UpdateItem(item, selectedItemId == id);
            BuildQueueMenu();
        });
    }

    private void ToolStatusChanged(object? sender, EventArgs e)
        => Application.RunOnUIThread(UpdateEngineStatus);

    private void UpdateEngineStatus()
    {
        var engine = services.Tools.EngineStatus;
        toolStatus.Text = engine switch
        {
            DownloadEngineStatus.Ready => text["Header.EngineReady"],
            DownloadEngineStatus.Failed => text["Header.EngineFailed"],
            _ => text["Header.EnginePreparing"]
        };
        AppUi.BindBackground(toolStatusDot, engine switch
        {
            DownloadEngineStatus.Ready => AppThemeTokens.Success,
            DownloadEngineStatus.Failed => AppThemeTokens.Error,
            _ => AppThemeTokens.Info
        });
    }

    private void BuildQueueCards(Guid? scrollToItemId = null)
    {
        var previousIds = cards.Keys.ToHashSet();
        foreach (var card in cards.Values)
            card.Dispose();
        cards.Clear();
        queueHost.Controls.Clear();
        var items = services.Queue.Snapshot();
        if (selectedItemId is { } selected && items.All(item => item.Id != selected))
            selectedItemId = null;
        foreach (var item in items)
        {
            var card = new QueueItemCard(services.Thumbnails, text);
            card.ActionRequested += CardActionRequested;
            cards[item.Id] = card;
            queueHost.Controls.Add(card);
            card.UpdateItem(item, selectedItemId == item.Id);
            if (queueBuilt && !previousIds.Contains(item.Id))
            {
                card.Opacity = 0f;
                card.TranslationY = 8f;
                _ = Task.WhenAll(
                    card.FadeToAsync(1f, 190, Easings.CubicOut),
                    card.TranslateToAsync(0f, 0f, 190, Easings.CubicOut));
            }
        }
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
        queueMenu.Items.Clear();
        AddQueueMenu(text["Queue.ClearPending"], () => services.Queue.RemoveAllPending());
        queueMenu.Items.Add(new MenuSeparatorItem());
        var hasSelection = selectedItemId.HasValue;
        AddQueueMenu(text["Queue.MoveFirst"], () => MoveSelectedTo(0), hasSelection);
        AddQueueMenu(text["Queue.MoveUp"], () => MoveSelectedBy(-1), hasSelection);
        AddQueueMenu(text["Queue.MoveDown"], () => MoveSelectedBy(1), hasSelection);
        AddQueueMenu(text["Queue.MoveLast"], () => MoveSelectedTo(int.MaxValue), hasSelection);
        queueMenu.Items.Add(new MenuSeparatorItem());
        AddQueueMenu(text["Queue.RetrySelected"], RetrySelected, hasSelection);
        AddQueueMenu(text["Queue.RemoveSelected"], RemoveSelected, hasSelection);
        AddQueueMenu(text["Queue.CancelSelected"], CancelSelected, hasSelection);
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
        var detail = item.ErrorMessage ?? item.StatusMessage;
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
        urlInput.Placeholder = text["Downloads.UrlPlaceholder"];
        pasteButton.AccessibleName = text["Downloads.PasteTooltip"];
        pasteToolTip.SetToolTip(pasteButton, text["Downloads.PasteTooltip"]);
        pasteToolTip.SetToolTip(pasteIcon, text["Downloads.PasteTooltip"]);
        analyzeButton.Text = text["Common.Analyze"];
        qualityLabel.Text = text["Downloads.Quality"];
        addButton.Text = text["Downloads.AddToQueue"];
        detailsButton.Text = text["Common.Details"];
        queueTitle.Text = text["Downloads.QueueTitle"];
        pauseButton.Text = services.Queue.IsPaused ? text["Queue.Resume"] : text["Queue.Pause"];
        queueEmptyTitle.Text = text["Downloads.QueueEmptyTitle"];
        queueEmptyBody.Text = text["Downloads.QueueEmptyBody"];
        UpdateEngineStatus();
        SetPreviewState(previewState);
        if (analyzedMetadata is not null)
            PopulateMetadata(analyzedMetadata);
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
            services.Queue.Changed -= QueueChanged;
            services.Tools.Changed -= ToolStatusChanged;
            text.LanguageChanged -= LanguageChanged;
            SizeChanged -= ViewSizeChanged;
            queueHost.SizeChanged -= QueueHostSizeChanged;
            pasteToolTip.Dispose();
            analyzeCancellation?.Cancel();
            analyzeCancellation?.Dispose();
        }
        base.Dispose(disposing);
    }
}
