using ModernFormsNext;
using ModernFormsNext.Accessibility;
using ModernTubeDownloader.Models;
using ModernTubeDownloader.Theming;

namespace ModernTubeDownloader.Views;

internal sealed partial class QueueItemCard
{
    protected override AccessibleObject CreateAccessibilityInstance() => new QueueRowAccessibleObject(this);
    private sealed class QueueRowAccessibleObject(QueueItemCard row) : ControlAccessibleObject(row)
    {
        public override AccessibleControlType ControlType => row.dashboard ? AccessibleControlType.ListItem : base.ControlType;
        public override AccessibleStates State => base.State | (row.dashboard ? AccessibleStates.Selectable | (row.selected ? AccessibleStates.Selected : AccessibleStates.None) : AccessibleStates.None);
        public override AccessibleActions SupportedActions => base.SupportedActions | (row.dashboard && (base.SupportedActions & AccessibleActions.Focus) != 0 ? AccessibleActions.Select : AccessibleActions.None);
        public override bool PerformAction(AccessibleActions action, object? parameter = null)
        {
            if (action == AccessibleActions.Select && parameter is null && (SupportedActions & action) != 0) { row.SelectCard(row, EventArgs.Empty); return true; }
            return base.PerformAction(action, parameter);
        }
    }
    private bool dashboard;
    private Label? duration;
    private readonly ToolTip dashboardTips = new();
    public void SetDashboard(bool value)
    {
        dashboard = value; TabStop = value; SetControlBehavior(ControlBehaviors.Selectable, value);
        if (value && duration is null)
        {
            duration = AppUi.Muted(); AppTypography.Bind(duration, 9); duration.TextAlign = ContentAlignment.MiddleCenter;
            AppUi.BindBackground(duration, AppThemeTokens.Background); thumbnail.Controls.Add(duration);
        }
        if (duration is not null) duration.Visible = value;
        AppTypography.Bind(title, value ? 10.5f : 13.5f, FontStyle.Bold);
        AppTypography.Bind(subtitle, value ? 9.5f : 10.5f); AppTypography.Bind(status, value ? 9.5f : 10.5f);
        AppTypography.Bind(transfer, value ? 9 : 10.25f);
        AppTypography.SetDashboardPresentation(this, value);
        LayoutControls();
    }
    private void LayoutDashboardRow()
    {
        var p = AppearancePresentation.Current.ModernDashboardShell;
        var scale = AppTypography.Scale; var narrow = Width < 850 || scale > 1.25;
        var imageWidth = AppearancePresentation.ImageWidth(p.Thumbnail, 124);
        var h = (p.QueueDensity switch { AppearanceDensity.Compact => 84, AppearanceDensity.Comfortable => 112, _ => 96 });
        Height = AppTypography.TextHeight(narrow ? Math.Max(h, 120) : h); Margin = new Padding(0, 0, 0, 1);
        Padding = new Padding(8); thumbnail.Visible = imageWidth > 0;
        thumbnail.SetBounds(8, 8, imageWidth, imageWidth * 9 / 16); thumbnailPlaceholder.Padding = new Padding(16);
        if (duration is not null) { var badgeWidth = Math.Min(imageWidth, AppTypography.TextHeight(64)); var badgeHeight = AppTypography.TextHeight(22); duration.SetBounds(Math.Max(0, imageWidth - badgeWidth), Math.Max(0, thumbnail.Height - badgeHeight), badgeWidth, badgeHeight); duration.Visible = imageWidth > 0; }
        positionBadge.SetBounds(12, AppTypography.Scale > 1.25 && imageWidth > 0 ? thumbnail.Bottom + 8 : 12, AppTypography.TextHeight(28), AppTypography.TextHeight(24));
        var left = imageWidth == 0 ? positionBadge.Visible ? positionBadge.Right + 14 : 12 : imageWidth + 26; var right = Width - 98;
        var progressWidth = narrow ? right - left : Math.Clamp(Width / 3, 180, 330);
        var progressLeft = narrow ? left : right - progressWidth - 10;
        var textWidth = Math.Max(100, (narrow ? right : progressLeft - 12) - left);
        var line = AppTypography.TextHeight(24);
        title.SetBounds(left, 8, textWidth, line); title.AutoEllipsis = true;
        subtitle.SetBounds(left, title.Bottom + 1, textWidth, line); subtitle.AutoEllipsis = true;
        progress.SetBounds(progressLeft, narrow ? subtitle.Bottom + 7 : 32, Math.Max(50, progressWidth), 6);
        status.SetBounds(progressLeft, progress.Bottom + 5, Math.Max(50, progressWidth), line);
        transfer.SetBounds(progressLeft, status.Bottom, Math.Max(50, progressWidth), line); transfer.TextAlign = ContentAlignment.TopLeft;
        primaryAction.SetBounds(Width - 84, (Height - 36) / 2, 36, 36); secondaryAction.SetBounds(Width - 84, Height - 34, 36, 28);
        moreAction.SetBounds(Width - 42, (Height - 36) / 2, 34, 36);
        secondaryAction.Visible = false;
        progress.Visible = true; progress.Padding = new Padding(0); progress.Style.Border.Width = 0; AppUi.BindBackground(progress, AppThemeTokens.SurfaceSecondary);
        Style.Border.Width = 1; Style.Border.Color = AppUi.GetColor(selected ? AppThemeTokens.Accent : AppThemeTokens.Border);
        Style.Border.Radius = Math.Min(8, AppearancePresentation.Current.Global.CornerRadius);
    }
    private void UpdateDashboardRow(DownloadQueueItem item)
    {
        var p = AppearancePresentation.Current.ModernDashboardShell;
        var parts = new List<string>();
        if (p.ShowAuthor && item.Channel.Length > 0) parts.Add(item.Channel);
        if (p.ShowSource) parts.Add(Services.SourcePresentation.Host(item.SourceUrl));
        if (p.ShowQuality) parts.Add(text["Quality." + item.QualityPresetId]);
        if (p.ShowFormat) parts.Add(item.PreferredVideoContainer.ToString());
        if (p.ShowSize && item.TotalBytes is { } total) parts.Add(DashboardStyle.Bytes(total));
        subtitle.Text = string.Join(" · ", parts);
        if (duration is not null) duration.Text = DashboardStyle.Duration(item.DurationSeconds);
        dashboardTips.SetToolTip(title, item.Title); dashboardTips.SetToolTip(subtitle, subtitle.Text);
        dashboardTips.SetToolTip(primaryAction, primaryAction.Text); primaryAction.AccessibleName = primaryAction.Text;
        primaryAction.Enabled = primaryAction.Tag is not QueueItemAction.OpenFile || File.Exists(item.FinalFile);
        primaryAction.Text = primaryAction.Tag is QueueItemAction action ? action switch { QueueItemAction.OpenFile => "▷", QueueItemAction.Retry => "↻", QueueItemAction.Cancel or QueueItemAction.Remove => "×", _ => "…" } : "…";
        moreAction.AccessibleName = text["Dashboard.MoreActions"]; dashboardTips.SetToolTip(moreAction, text["Dashboard.MoreActions"]);
        AppUi.BindForeground(status, item.StatusMessageKey == "Queue.Detail.StoragePaused" || item.RetryDelaySeconds > 0 ? AppThemeTokens.Warning : item.Status switch
        {
            DownloadStatus.Completed => AppThemeTokens.Success,
            DownloadStatus.Failed or DownloadStatus.Partial => AppThemeTokens.Error,
            DownloadStatus.Cancelled or DownloadStatus.Interrupted => AppThemeTokens.TextSecondary,
            DownloadStatus.DownloadingVideo or DownloadStatus.DownloadingAudio or DownloadStatus.Merging or DownloadStatus.Finalizing => AppThemeTokens.Info,
            _ => AppThemeTokens.TextSecondary
        });
        if (item.StatusMessageKey == "Queue.Detail.StoragePaused") status.Text = text[item.StatusMessageKey];
        var transferParts = new List<string>();
        if (p.ShowSize && item.DownloadedBytes is { } bytes) transferParts.Add(DashboardStyle.Bytes(bytes));
        if (p.ShowSpeed && item.SpeedBytesPerSecond is { } speed) transferParts.Add(DashboardStyle.Bytes((long)speed) + "/s");
        if (p.ShowEta && item.Eta is { } eta) transferParts.Add(text.Get("Queue.Eta", eta.ToString(@"hh\:mm\:ss")));
        transfer.Text = string.Join(" · ", transferParts); transfer.AutoEllipsis = true; dashboardTips.SetToolTip(transfer, transfer.Text);
        LayoutDashboardRow();
    }
}
