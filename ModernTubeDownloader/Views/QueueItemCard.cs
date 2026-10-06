using ModernFormsNext;
using ModernFormsNext.Animations;
using ModernTubeDownloader.Localization;
using ModernTubeDownloader.Models;
using ModernTubeDownloader.Services;
using ModernTubeDownloader.Theming;
using ModernTubeDownloader.Utilities;
using SkiaSharp;

namespace ModernTubeDownloader.Views;

internal enum QueueItemAction
{
    Select,
    Remove,
    MoveUp,
    MoveDown,
    MoveFirst,
    MoveLast,
    Retry,
    Details,
    Cancel,
    OpenFile,
    OpenFolder
}

internal sealed class QueueItemActionEventArgs(Guid itemId, QueueItemAction action) : EventArgs
{
    public Guid ItemId { get; } = itemId;
    public QueueItemAction Action { get; } = action;
}

internal sealed class QueueItemCard : UserControl
{
    private readonly ThumbnailCacheService thumbnails;
    private readonly LocalizationService text;
    private readonly PictureBox thumbnail;
    private readonly PictureBox thumbnailPlaceholder;
    private readonly Label positionBadge;
    private readonly Label title;
    private readonly Label subtitle;
    private readonly Label status;
    private readonly Label transfer;
    private readonly ProgressBar progress;
    private readonly Button primaryAction;
    private readonly Button secondaryAction;
    private readonly Button moreAction;
    private readonly ContextMenu actionsMenu = new();
    private DownloadQueueItem? currentItem;
    private string? loadedThumbnail;
    private Guid itemId;
    private bool selected;
    private readonly CancellationTokenSource lifetimeCancellation = new();
    private bool disposed;

    public QueueItemCard(ThumbnailCacheService thumbnails, LocalizationService text)
    {
        this.thumbnails = thumbnails;
        this.text = text;
        Height = 150;
        Margin = new Padding(0, 0, 0, 14);
        Padding = new Padding(18);
        AppUi.Card(this);

        thumbnail = new PictureBox { SizeMode = PictureBoxSizeMode.Zoom };
        thumbnail.Style.Border.Radius = 10;
        thumbnail.Style.Border.Width = 0;
        AppUi.BindBackground(thumbnail, AppThemeTokens.SurfaceSecondary);
        thumbnailPlaceholder = new PictureBox { Image = AppBranding.Icon, SizeMode = PictureBoxSizeMode.Zoom, Padding = new Padding(20) };
        thumbnailPlaceholder.Dock = DockStyle.Fill;
        thumbnail.Controls.Add(thumbnailPlaceholder);
        positionBadge = new Label
        {
            Visible = false,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI", 10.5f, FontStyle.Bold)
        };
        AppUi.BindBackground(positionBadge, AppThemeTokens.NavigationSelected);
        AppUi.BindForeground(positionBadge, AppThemeTokens.NavigationSelectedText);
        positionBadge.Style.Border.Radius = 8;
        positionBadge.Style.Border.Width = 0;
        title = AppUi.Heading(string.Empty, 13.5f);
        subtitle = AppUi.Muted();
        status = AppUi.Muted();
        status.Font = new Font("Segoe UI", 10.5f, FontStyle.Bold);
        transfer = AppUi.Muted();
        transfer.Font = new Font("Segoe UI", 10.25f);
        transfer.TextAlign = ContentAlignment.TopRight;
        progress = new ProgressBar { Minimum = 0, Maximum = 100 };
        primaryAction = new Button();
        secondaryAction = new Button();
        moreAction = new Button { Text = "…" };
        AppUi.Primary(primaryAction);
        AppUi.Secondary(secondaryAction);
        AppUi.Secondary(moreAction);

        primaryAction.Click += (_, _) => ExecuteAction(primaryAction.Tag);
        secondaryAction.Click += (_, _) => ExecuteAction(secondaryAction.Tag);
        moreAction.Click += (_, _) => Application.RunOnUIThread(
            () => actionsMenu.Show(
                moreAction,
                moreAction.PointToScreen(new System.Drawing.Point(0, moreAction.Height + 4))));
        Click += SelectCard;
        title.Click += SelectCard;
        subtitle.Click += SelectCard;
        status.Click += SelectCard;
        thumbnail.Click += SelectCard;
        positionBadge.Click += SelectCard;
        SizeChanged += CardSizeChanged;
        text.LanguageChanged += LanguageChanged;
        Controls.AddRange([thumbnail, title, subtitle, status, transfer, progress, primaryAction, secondaryAction, moreAction, positionBadge]);
        positionBadge.BringToFront();
        LayoutControls();
    }

    public event EventHandler<QueueItemActionEventArgs>? ActionRequested;

    public void SetQueuePosition(string? position)
    {
        positionBadge.Text = position ?? string.Empty;
        positionBadge.Visible = !string.IsNullOrEmpty(position);
    }

    public void UpdateItem(DownloadQueueItem item, bool isSelected)
    {
        currentItem = item;
        itemId = item.Id;
        AccessibleAutomationId = $"QueueItem-{item.Id:N}";
        status.AccessibleAutomationId = $"QueueStatus-{item.Id:N}";
        positionBadge.AccessibleAutomationId = $"QueuePosition-{item.Id:N}";
        primaryAction.AccessibleAutomationId = $"QueuePrimaryAction-{item.Id:N}";
        secondaryAction.AccessibleAutomationId = $"QueueSecondaryAction-{item.Id:N}";
        selected = isSelected;
        title.Text = item.Title;
        subtitle.Text = $"{item.Channel}  •  {text[$"Quality.{QualityPreset.Find(item.QualityPresetId).Id}"]}" +
            (string.IsNullOrWhiteSpace(item.PlaylistTitle) ? string.Empty : $"  •  {text.Get("Queue.PlaylistPart", item.PlaylistTitle)}") +
            (item.RequestedRange is { Mode: MediaRangeMode.Custom } ? $"  •  {item.RequestedRange.DisplayText}" : string.Empty);
        var localizedStatus = item.Status == DownloadStatus.Waiting && item.RetryDelaySeconds > 0
            ? text.Get("Queue.Detail.RetryWaiting", item.AttemptCount + 1, item.MaximumAttempts, item.RetryDelaySeconds)
            : text[$"Status.{item.Status}"];
        if (item.Status == DownloadStatus.Completed && item.HasPostProcessingWarnings)
            localizedStatus = $"{localizedStatus}  •  {text["Queue.CompletedWithWarnings"]}";
        status.Text = isSelected
            ? $"{text["Queue.Selected"]}  •  {localizedStatus}"
            : localizedStatus;
        AnimateProgressTo((int)Math.Clamp(Math.Round(item.ProgressPercent), 0, 100));
        transfer.Text = BuildTransferText(item);
        AppUi.BindForeground(status, item.Status switch
        {
            DownloadStatus.Completed => AppThemeTokens.Success,
            DownloadStatus.Failed or DownloadStatus.Cancelled or DownloadStatus.Partial => AppThemeTokens.Error,
            DownloadStatus.DownloadingVideo or DownloadStatus.DownloadingAudio or DownloadStatus.Merging or
                DownloadStatus.Finalizing => AppThemeTokens.Info,
            _ => AppThemeTokens.TextSecondary
        });
        ConfigureActions(item);
        BuildContextMenu(item);
        AppUi.BindBackground(this, isSelected ? AppThemeTokens.NavigationSelected : AppThemeTokens.Surface);

        if (!string.Equals(loadedThumbnail, item.ThumbnailUrl, StringComparison.Ordinal))
        {
            loadedThumbnail = item.ThumbnailUrl;
            _ = LoadThumbnailAsync(item.ThumbnailUrl);
        }
    }

    private void LayoutControls()
    {
        var innerWidth = Math.Max(460, Width - 36);
        var compact = innerWidth < 760;
        Height = compact ? 204 : 150;
        thumbnail.SetBounds(18, 18, compact ? 144 : 168, compact ? 81 : 94);
        positionBadge.SetBounds(thumbnail.Left + 8, thumbnail.Top + 8, 34, 27);
        var infoLeft = thumbnail.Right + 16;
        var actionsWidth = compact ? 0 : 132;
        var actionsLeft = Width - 18 - actionsWidth;
        var infoRight = compact ? Width - 18 : actionsLeft - 18;
        var infoWidth = Math.Max(220, infoRight - infoLeft);
        title.SetBounds(infoLeft, 15, infoWidth, 30);
        subtitle.SetBounds(infoLeft, 47, infoWidth, 24);
        var statusWidth = Math.Max(150, infoWidth / 2);
        status.SetBounds(infoLeft, 76, statusWidth, 24);
        transfer.SetBounds(infoLeft + statusWidth, 76, Math.Max(80, infoWidth - statusWidth), 24);
        progress.SetBounds(compact ? 18 : infoLeft, compact ? 119 : 112, compact ? Width - 36 : infoWidth, 10);

        if (compact)
        {
            primaryAction.SetBounds(18, 148, 134, 40);
            secondaryAction.SetBounds(162, 148, 134, 40);
            moreAction.SetBounds(Width - 62, 148, 44, 40);
        }
        else
        {
            primaryAction.SetBounds(actionsLeft, 15, 132, 38);
            secondaryAction.SetBounds(actionsLeft, 59, 132, 38);
            moreAction.SetBounds(actionsLeft + 88, 103, 44, 34);
        }
    }

    private void AnimateProgressTo(int target)
    {
        var start = progress.Value;
        if (start == target)
            return;

        progress.Animate(
            "QueueProgress",
            TimeSpan.FromMilliseconds(180),
            eased => progress.Value = (int)Math.Round(start + ((target - start) * eased)),
            Easings.CubicOut);
    }

    private async Task LoadThumbnailAsync(string? url)
    {
        var expected = url;
        SKBitmap? image;
        try { image = await thumbnails.GetAsync(url, lifetimeCancellation.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) { return; }
        if (image is null || !string.Equals(expected, loadedThumbnail, StringComparison.Ordinal))
            return;
        Application.RunOnUIThread(() =>
        {
            if (disposed || lifetimeCancellation.IsCancellationRequested || !string.Equals(expected, loadedThumbnail, StringComparison.Ordinal))
                return;
            thumbnail.Image = image;
            thumbnailPlaceholder.Visible = false;
        });
    }

    private string BuildTransferText(DownloadQueueItem item)
    {
        var parts = new List<string> { $"{item.ProgressPercent:F0}%" };
        if (item.DownloadedBytes is { } downloaded)
            parts.Add(item.TotalBytes is { } total ? $"{FormatBytes(downloaded)} / {FormatBytes(total)}" : FormatBytes(downloaded));
        if (item.SpeedBytesPerSecond is { } speed)
            parts.Add($"{FormatBytes((long)speed)}/s");
        if (item.Eta is { } eta)
            parts.Add(text.Get("Queue.Eta", eta.ToString(@"mm\:ss")));
        return string.Join("  ", parts);
    }

    private void ConfigureActions(DownloadQueueItem item)
    {
        primaryAction.Visible = true;
        secondaryAction.Visible = true;
        switch (item.Status)
        {
            case DownloadStatus.Queued:
                SetAction(primaryAction, text["Common.Remove"], QueueItemAction.Remove);
                SetAction(secondaryAction, text["Queue.MoveUp"], QueueItemAction.MoveUp);
                break;
            case DownloadStatus.Waiting:
            case DownloadStatus.DownloadingVideo:
            case DownloadStatus.DownloadingAudio:
            case DownloadStatus.Merging:
            case DownloadStatus.Finalizing:
                SetAction(primaryAction, text["Common.Cancel"], QueueItemAction.Cancel);
                secondaryAction.Visible = false;
                break;
            case DownloadStatus.Failed:
            case DownloadStatus.Cancelled:
            case DownloadStatus.Interrupted:
                SetAction(primaryAction, text["Common.Retry"], QueueItemAction.Retry);
                SetAction(secondaryAction, text["Common.Details"], QueueItemAction.Details);
                break;
            case DownloadStatus.Partial:
                SetAction(primaryAction, item.FinalFile is null ? text["Common.Retry"] : text["Common.OpenFile"],
                    item.FinalFile is null ? QueueItemAction.Retry : QueueItemAction.OpenFile);
                SetAction(secondaryAction, text["Common.Retry"], QueueItemAction.Retry);
                break;
            case DownloadStatus.Completed:
                SetAction(primaryAction, text["Common.OpenFile"], QueueItemAction.OpenFile);
                SetAction(secondaryAction, text["Common.OpenFolder"], QueueItemAction.OpenFolder);
                break;
        }
    }

    private void BuildContextMenu(DownloadQueueItem item)
    {
        actionsMenu.Items.Clear();
        AddMenuItem(text["Common.Select"], QueueItemAction.Select);
        actionsMenu.Items.Add(new MenuSeparatorItem());
        if (item.Status == DownloadStatus.Queued)
        {
            AddMenuItem(text["Queue.MoveFirst"], QueueItemAction.MoveFirst);
            AddMenuItem(text["Queue.MoveUp"], QueueItemAction.MoveUp);
            AddMenuItem(text["Queue.MoveDown"], QueueItemAction.MoveDown);
            AddMenuItem(text["Queue.MoveLast"], QueueItemAction.MoveLast);
            actionsMenu.Items.Add(new MenuSeparatorItem());
            AddMenuItem(text["Common.Remove"], QueueItemAction.Remove);
        }
        else if (item.Status is DownloadStatus.Waiting or DownloadStatus.DownloadingVideo or
                 DownloadStatus.DownloadingAudio or DownloadStatus.Merging or DownloadStatus.Finalizing)
        {
            AddMenuItem(text["Common.Cancel"], QueueItemAction.Cancel);
        }
        else if (item.Status is DownloadStatus.Failed or DownloadStatus.Cancelled or DownloadStatus.Interrupted or DownloadStatus.Partial)
        {
            AddMenuItem(text["Common.Retry"], QueueItemAction.Retry);
            AddMenuItem(text["Common.Details"], QueueItemAction.Details);
            if (item.FinalFile is not null)
            {
                AddMenuItem(text["Common.OpenFile"], QueueItemAction.OpenFile);
                AddMenuItem(text["Common.OpenFolder"], QueueItemAction.OpenFolder);
            }
            AddMenuItem(text["Common.Remove"], QueueItemAction.Remove);
        }
        else
        {
            AddMenuItem(text["Common.OpenFile"], QueueItemAction.OpenFile);
            AddMenuItem(text["Common.OpenFolder"], QueueItemAction.OpenFolder);
        }
    }

    private void AddMenuItem(string label, QueueItemAction action)
        => actionsMenu.Items.Add(label, null, (_, _) => ActionRequested?.Invoke(this, new QueueItemActionEventArgs(itemId, action)));

    private static void SetAction(Button button, string label, QueueItemAction action)
    {
        button.Text = label;
        button.Tag = action;
    }

    private void ExecuteAction(object? tag)
    {
        if (tag is QueueItemAction action)
            ActionRequested?.Invoke(this, new QueueItemActionEventArgs(itemId, action));
    }

    private void SelectCard(object? sender, EventArgs e)
        => ActionRequested?.Invoke(this, new QueueItemActionEventArgs(itemId, QueueItemAction.Select));

    private void CardSizeChanged(object? sender, EventArgs e) => LayoutControls();

    private void LanguageChanged(object? sender, EventArgs e)
    {
        if (currentItem is not null)
            Application.RunOnUIThread(() => UpdateItem(currentItem, selected));
    }

    private static string FormatBytes(long value)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var size = (double)Math.Max(0, value);
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }
        return $"{size:F1} {units[unit]}";
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            disposed = true;
            lifetimeCancellation.Cancel();
            SizeChanged -= CardSizeChanged;
            text.LanguageChanged -= LanguageChanged;
            lifetimeCancellation.Dispose();
        }
        base.Dispose(disposing);
    }
}
