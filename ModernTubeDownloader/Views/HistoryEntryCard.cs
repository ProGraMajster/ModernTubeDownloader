using ModernFormsNext;
using ModernTubeDownloader.Localization;
using ModernTubeDownloader.Models;
using ModernTubeDownloader.Services;
using ModernTubeDownloader.Theming;
using ModernTubeDownloader.Utilities;
using SkiaSharp;

namespace ModernTubeDownloader.Views;

internal sealed partial class HistoryEntryCard : UserControl
{
    private DownloadHistoryEntry entry;
    private bool thumbnailSuspended;
    private readonly LocalizationService text;
    private readonly ThumbnailCacheService thumbnails;
    private readonly Panel placeholder;
    private readonly PictureBox placeholderText;
    private readonly DownloadsPictureBox thumbnail;
    private readonly Label title;
    private readonly Label facts;
    private readonly Label path;
    private readonly Button openFile;
    private readonly Button openFolder;
    private readonly CancellationTokenSource lifetimeCancellation = new();
    private bool disposed;
    private readonly ToolTip rowTips = new();

    public HistoryEntryCard(DownloadHistoryEntry entry, LocalizationService text, ThumbnailCacheService thumbnails)
    {
        ModernTubeDownloader.Infrastructure.PerformanceDiagnostics.Increment("history.cards.created");
        this.entry = entry;
        this.text = text;
        this.thumbnails = thumbnails;
        Height = 132;
        Margin = new Padding(0, 0, 0, 14);
        AppUi.Card(this);

        placeholder = new Panel();
        AppUi.Card(placeholder, secondary: true);
        placeholderText = new PictureBox { Image = AppBranding.Icon, SizeMode = PictureBoxSizeMode.Zoom, Padding = new Padding(20) };
        placeholderText.Dock = DockStyle.Fill;
        placeholder.Controls.Add(placeholderText);
        thumbnail = new DownloadsPictureBox { SizeMode = PictureBoxSizeMode.Zoom, Visible = false };
        thumbnail.Style.Border.Radius = 10;
        thumbnail.Style.Border.Width = 0;
        AppUi.BindBackground(thumbnail, AppThemeTokens.SurfaceSecondary);
        title = AppUi.Heading(entry.Title, 13.5f);
        rowTips.SetToolTip(title, entry.Title);
        facts = AppUi.Muted();
        path = AppUi.Muted();
        openFile = new Button();
        openFolder = new Button();
        AppUi.Primary(openFile);
        AppUi.Secondary(openFolder);
        openFile.Enabled = File.Exists(entry.FinalPath);
        openFolder.Enabled = Directory.Exists(System.IO.Path.GetDirectoryName(entry.FinalPath));
        openFile.Click += (_, _) => ShellService.OpenFile(this.entry.FinalPath);
        openFolder.Click += (_, _) => ShellService.OpenFolderForFile(System.IO.Path.GetDirectoryName(this.entry.FinalPath));
        Controls.AddRange([placeholder, thumbnail, title, facts, path, openFile, openFolder]);
        Click += (_, _) => SelectionRequested?.Invoke(this, this.entry);
        title.Click += (_, _) => SelectionRequested?.Invoke(this, this.entry);
        thumbnail.Click += (_, _) => SelectionRequested?.Invoke(this, this.entry);
        SizeChanged += CardSizeChanged;
        text.LanguageChanged += LanguageChanged;
        ApplyLocalization();
        LayoutControls();
        AppTypography.RegisterLayout(this, () => { ApplyLocalization(); LayoutControls(); });
        UpdateEntry(entry, force: true);
    }

    private bool arrangingCard;
    private void LayoutControls()
    {
        if (arrangingCard) return;
        arrangingCard = true;
        SuspendLayout();
        try { LayoutControlsCore(); }
        finally
        {
            try { ResumeLayout(true); }
            finally { arrangingCard = false; }
        }
    }

    private void LayoutControlsCore()
    {
        if (dashboard) { LayoutDashboardCard(); return; }
        openFile.Visible = true; foreach (var cell in cells) cell.Visible = false;
        var appearance = AppearancePresentation.Current.History;
        // Compact list is a density choice, not the narrow-window stacked layout.
        var compact = Width < 760;
        Height = AppTypography.TextHeight(compact ? (appearance.CompactList ? 172 : 184) : (appearance.CompactList ? 120 : 132));
        var imageWidth = AppearancePresentation.ImageWidth(appearance.ThumbnailSize, compact ? 144 : 168);
        placeholder.SetBounds(18, 18, imageWidth, imageWidth * 9 / 16);
        thumbnail.SetBounds(18, 18, imageWidth, imageWidth * 9 / 16);
        placeholder.Visible = imageWidth > 0 && thumbnail.Image is null; thumbnail.Visible = imageWidth > 0 && thumbnail.Image is not null;
        path.Visible = appearance.ShowOutputPath;
        var textLeft = imageWidth == 0 ? 18 : placeholder.Right + AppearancePresentation.Spacing(16);
        var actionWidth = compact ? 0 : Math.Min(Width / 3, AppTypography.TextHeight(132));
        var textRight = compact ? Width - 18 : Width - 18 - actionWidth - 18;
        var textWidth = Math.Max(230, textRight - textLeft);
        title.SetBounds(textLeft, 16, textWidth, 30);
        facts.SetBounds(textLeft, 49, textWidth, 24);
        path.SetBounds(textLeft, 78, textWidth, 24);
        if (compact)
        {
            openFile.SetBounds(18, 125, 134, 40);
            openFolder.SetBounds(162, 125, 146, 40);
        }
        else
        {
            openFile.SetBounds(Width - 18 - actionWidth, 18, actionWidth, 40);
            openFolder.SetBounds(Width - 18 - actionWidth, 68, actionWidth, 40);
        }
        AppTypography.ReflowRows(this);
    }

    private void ApplyLocalization()
    {
        openFile.Text = text["Common.OpenFile"];
        openFolder.Text = text["Common.OpenFolder"];
        var qualityId = entry.QualityPresetId ?? QualityPreset.All
            .FirstOrDefault(item => string.Equals(item.DisplayName, entry.Quality, StringComparison.OrdinalIgnoreCase))?.Id;
        var quality = qualityId is not null ? text[$"Quality.{qualityId}"] : entry.Quality;
        var appearance = AppearancePresentation.Current.History;
        facts.Text = string.Join("  •  ", new[] { entry.Channel, appearance.ShowSource ? SourcePresentation.Host(entry.SourceUrl) : null, appearance.ShowQuality ? quality : null,
            appearance.ShowContainer ? System.IO.Path.GetExtension(entry.FinalPath).TrimStart('.').ToUpperInvariant() : null, appearance.ShowDate ? text.Get("History.CompletedAt", entry.CompletedAt.ToLocalTime().ToString("g", text.Culture)) : null }.Where(value => !string.IsNullOrEmpty(value))) +
            (entry.WasLiveRecording ? $"  •  LIVE{(entry.WasPartial ? " • " + text["Live.State.Partial"] : string.Empty)}" : string.Empty) +
            (string.IsNullOrWhiteSpace(entry.PlaylistTitle) ? string.Empty : $"  •  {text.Get("Queue.PlaylistPart", entry.PlaylistTitle)}");
        path.Text = text.Get("History.Path", entry.FinalPath);
    }

    internal void UpdateEntry(DownloadHistoryEntry value, bool force = false)
    {
        if (!force && !thumbnailSuspended && entry == value) return;
        var imageChanged = force || thumbnailSuspended || entry.ThumbnailUrl != value.ThumbnailUrl;
        entry = value;
        AccessibleAutomationId = dashboard ? $"HistoryItem-{value.QueueItemId:N}-{value.LivePartIndex?.ToString() ?? "vod"}" : "HistoryCard-" + value.QueueItemId.ToString("N") + "-" + value.LivePartIndex;
        AccessibleName = value.Title;
        title.Text = value.Title;
        openFile.AccessibleAutomationId = "HistoryOpenFile-" + value.QueueItemId.ToString("N");
        openFolder.AccessibleAutomationId = "HistoryOpenFolder-" + value.QueueItemId.ToString("N");
        openFile.Enabled = File.Exists(value.FinalPath);
        openFolder.Enabled = Directory.Exists(System.IO.Path.GetDirectoryName(value.FinalPath));
        rowTips.SetToolTip(title, value.Title);
        ApplyLocalization();
        if (dashboard) LayoutControls();
        if (imageChanged)
        {
            thumbnailSuspended = false;
            placeholder.Visible = AppearancePresentation.Current.History.ThumbnailSize != AppearanceSize.None;
            thumbnail.Visible = false;
            _ = ThumbnailImageBinding.For(thumbnail).LoadAsync(thumbnails, value.ThumbnailUrl, lifetimeCancellation.Token,
                () => { thumbnail.Visible = AppearancePresentation.Current.History.ThumbnailSize != AppearanceSize.None; placeholder.Visible = false; });
        }
    }

    internal void RefreshFileAvailability()
    {
        openFile.Enabled = File.Exists(entry.FinalPath);
        openFolder.Enabled = Directory.Exists(System.IO.Path.GetDirectoryName(entry.FinalPath));
    }

    internal void SuspendThumbnail()
    {
        if (thumbnailSuspended) return;
        thumbnailSuspended = true;
        ThumbnailImageBinding.For(thumbnail).Clear();
    }

    private void CardSizeChanged(object? sender, EventArgs e) => LayoutControls();
    private void LanguageChanged(object? sender, EventArgs e) => Application.RunOnUIThread(ApplyLocalization);

    protected override void Dispose(bool disposing)
    {
        if (disposing && !disposed)
        {
            ThumbnailImageBinding.For(thumbnail).Dispose();
            disposed = true;
            rowTips.Dispose();
            historyMenu.Dispose();
            lifetimeCancellation.Cancel();
            SizeChanged -= CardSizeChanged;
            text.LanguageChanged -= LanguageChanged;
            lifetimeCancellation.Dispose();
        }
        base.Dispose(disposing);
    }
}
