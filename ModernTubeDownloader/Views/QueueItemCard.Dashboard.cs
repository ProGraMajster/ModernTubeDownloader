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
    private Label? duration, dashboardAuthor;
    private AppVectorIcon? actionGlyph, menuGlyph, statusGlyph;
    private readonly ToolTip dashboardTips = new();
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (dashboard && Focused && !e.Handled && e.Modifiers == Keys.None && e.KeyCode is Keys.Space or Keys.Enter)
        {
            SelectCard(this, EventArgs.Empty);
            e.Handled = e.SuppressKeyPress = true;
        }
    }
    public void SetDashboard(bool value)
    {
        dashboard = value; TabStop = value; SetControlBehavior(ControlBehaviors.Selectable, value);
        if (value && duration is null)
        {
            duration = AppUi.Muted(); AppTypography.Bind(duration, 9); duration.TextAlign = ContentAlignment.MiddleCenter;
            AppUi.BindBackground(duration, AppThemeTokens.Background); Controls.Add(duration);
        }
        if (value && dashboardAuthor is null) { dashboardAuthor = AppUi.Muted(); Controls.Add(dashboardAuthor); AppTypography.Bind(dashboardAuthor, 9.5f); dashboardAuthor.AutoEllipsis = true; }
        if (dashboardAuthor is not null) dashboardAuthor.Visible = value;
        if (value && actionGlyph is null)
        {
            statusGlyph = new AppVectorIcon(AppIconKind.Info, 16) { AccessibilityView = AccessibilityView.Hidden }; Controls.Add(statusGlyph);
            actionGlyph = new AppVectorIcon(AppIconKind.Play, 20); menuGlyph = new AppVectorIcon(AppIconKind.More, 20);
            actionGlyph.AccessibilityView = menuGlyph.AccessibilityView = ModernFormsNext.Accessibility.AccessibilityView.Hidden;
            actionGlyph.Click += (_, _) => primaryAction.PerformClick(); menuGlyph.Click += (_, _) => moreAction.PerformClick();
            primaryAction.Controls.Add(actionGlyph); moreAction.Controls.Add(menuGlyph);
        }
        if (actionGlyph is not null) actionGlyph.Visible = menuGlyph!.Visible = value;
        if (statusGlyph is not null) statusGlyph.Visible = value;
        if (!value && currentItem is not null) ConfigureActions(currentItem);
        if (!value) transfer.Visible = true;
        if (duration is not null) duration.Visible = value;
        AppTypography.Bind(title, value ? DashboardVisual.ItemTitle : 13.5f, value ? FontStyle.Regular : FontStyle.Bold);
        title.Multiline = value;
        AppTypography.Bind(subtitle, value ? 9.5f : 10.5f); AppTypography.Bind(status, value ? 9.5f : 10.5f, value ? FontStyle.Regular : FontStyle.Bold);
        AppTypography.Bind(transfer, value ? 9 : 10.25f);
        AppTypography.SetDashboardPresentation(this, value);
        LayoutControls();
    }
    private void LayoutDashboardRow()
    {
        var p = AppearancePresentation.Current.ModernDashboardShell;
        var narrow = Width < AppTypography.TextHeight(860);
        var imageWidth = AppearancePresentation.ImageWidth(p.Thumbnail, 140);
        var h = (p.QueueDensity switch { AppearanceDensity.Compact => 88, AppearanceDensity.Comfortable => 116, _ => 100 });
        var line = AppTypography.TextHeight(22);
        Margin = new Padding(0, 0, 0, 1);
        Padding = new Padding(8); thumbnail.Visible = imageWidth > 0;
        thumbnail.SetBounds(8, 8, imageWidth, imageWidth * 9 / 16); thumbnailPlaceholder.Padding = new Padding(16);
        if (duration is not null) { var badgeWidth = Math.Min(imageWidth, AppTypography.TextHeight(64)); var badgeHeight = AppTypography.TextHeight(22); duration.SetBounds(thumbnail.Right - badgeWidth, thumbnail.Bottom - badgeHeight, badgeWidth, badgeHeight); duration.BringToFront(); duration.Visible = imageWidth > 0 && duration.Text.Length > 0; }
        positionBadge.SetBounds(12, AppTypography.Scale > 1.25 && imageWidth > 0 ? thumbnail.Bottom + 8 : 12, AppTypography.TextHeight(28), AppTypography.TextHeight(24));
        var left = imageWidth == 0 ? positionBadge.Visible ? positionBadge.Right + 14 : 12 : imageWidth + 26; var right = Width - 98;
        var progressWidth = narrow ? right - left : Math.Clamp(Width * 29 / 100, 210, 310);
        var progressLeft = narrow ? left : right - progressWidth - 10;
        var textWidth = Math.Max(100, (narrow ? right : progressLeft - 12) - left);
        title.SetBounds(left, 8, textWidth, DashboardVisual.LabelHeight(title, textWidth, 2)); title.AutoEllipsis = true;
        dashboardAuthor!.Visible = !string.IsNullOrWhiteSpace(dashboardAuthor.Text);
        dashboardAuthor.SetBounds(left, title.Bottom + (narrow ? 2 : 4), textWidth, dashboardAuthor.Visible ? line : 0);
        subtitle.SetBounds(left, (dashboardAuthor.Visible ? dashboardAuthor.Bottom : title.Bottom) + (narrow ? 2 : 3), textWidth, line); subtitle.AutoEllipsis = true;
        progress.SetBounds(progressLeft, narrow ? subtitle.Bottom + 6 : 28, Math.Max(50, progressWidth), 6);
        status.SetBounds(progressLeft + 22, progress.Bottom + (narrow ? 4 : 7), Math.Max(50, progressWidth - 22), line);
        statusGlyph?.SetBounds(progressLeft, status.Top + 2, 16, 16);
        transfer.SetBounds(progressLeft, status.Bottom, Math.Max(50, progressWidth), line); transfer.TextAlign = ContentAlignment.TopLeft;
        transfer.Visible = !string.IsNullOrWhiteSpace(transfer.Text);
        Height = Math.Max(AppTypography.TextHeight(h), Math.Max(thumbnail.Bottom, Math.Max(subtitle.Bottom, transfer.Visible ? transfer.Bottom : status.Bottom)) + 10);
        primaryAction.SetBounds(Width - 84, (Height - 36) / 2, 36, 36); secondaryAction.SetBounds(Width - 84, Height - 34, 36, 28);
        moreAction.SetBounds(Width - 42, (Height - 36) / 2, 34, 36);
        secondaryAction.Visible = false;
        actionGlyph?.SetBounds(8, 8, 20, 20); menuGlyph?.SetBounds(7, 8, 20, 20);
        progress.Visible = currentItem?.Status is not DownloadStatus.Completed and not DownloadStatus.Failed and not DownloadStatus.Cancelled and not DownloadStatus.Interrupted; progress.Padding = new Padding(0); progress.Style.Border.Width = 0; AppUi.BindBackground(progress, AppThemeTokens.SurfaceSecondary);
        DashboardVisual.Flat(this, !selected); Style.Border.Radius = selected ? 6 : 0;
        if (selected) { Style.Border.Width = 1; Style.Border.Color = AppUi.GetColor(AppThemeTokens.Accent).WithAlpha(130); }
        StyleFocused.Border.Width = 1; StyleFocused.Border.Color = AppUi.GetColor(AppThemeTokens.Accent);
        StyleHover.BackgroundColor = AppUi.GetColor(selected ? AppThemeTokens.RowSelected : AppThemeTokens.SurfaceHover);
        DashboardVisual.Button(primaryAction); DashboardVisual.Button(moreAction);
    }
    private void UpdateDashboardRow(DownloadQueueItem item)
    {
        var p = AppearancePresentation.Current.ModernDashboardShell;
        var parts = new List<string>();
        if (dashboardAuthor is not null) { dashboardAuthor.Text = p.ShowAuthor ? item.Channel : ""; dashboardTips.SetToolTip(dashboardAuthor, item.Channel); }
        if (p.ShowSource) parts.Add(Services.SourcePresentation.Host(item.SourceUrl));
        if (p.ShowQuality) parts.Add(text["Quality." + item.QualityPresetId]);
        if (p.ShowFormat) parts.Add(item.PreferredVideoContainer.ToString());
        if (p.ShowSize && item.TotalBytes is { } total) parts.Add(DashboardStyle.Bytes(total));
        subtitle.Text = string.Join(" · ", parts);
        if (duration is not null) duration.Text = DashboardStyle.Duration(item.DurationSeconds);
        dashboardTips.SetToolTip(title, item.Title); dashboardTips.SetToolTip(subtitle, subtitle.Text);
        dashboardTips.SetToolTip(primaryAction, primaryAction.Text); primaryAction.AccessibleName = primaryAction.Text;
        primaryAction.Enabled = primaryAction.Tag is not QueueItemAction.OpenFile || File.Exists(item.FinalFile);
        actionGlyph?.SetKind(primaryAction.Tag is QueueItemAction action ? action switch { QueueItemAction.OpenFile => AppIconKind.Play, QueueItemAction.Retry => AppIconKind.Retry, QueueItemAction.Cancel or QueueItemAction.Remove => AppIconKind.Close, _ => AppIconKind.More } : AppIconKind.More);
        primaryAction.Text = moreAction.Text = "";
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
        else if (item.Status != DownloadStatus.Waiting) status.Text = text["Status." + item.Status] +
            (item.Status == DownloadStatus.Completed && item.HasPostProcessingWarnings ? " · " + text["Queue.CompletedWithWarnings"] : "");
        var transferring = item.Status is DownloadStatus.DownloadingVideo or DownloadStatus.DownloadingAudio;
        if (transferring) status.Text += $" · {item.ProgressPercent:0}%";
        statusGlyph?.SetKind(item.StatusMessageKey == "Queue.Detail.StoragePaused" ? AppIconKind.Pause : item.Status switch { DownloadStatus.Completed => AppIconKind.Check, DownloadStatus.DownloadingVideo or DownloadStatus.DownloadingAudio => AppIconKind.Download, DownloadStatus.Merging or DownloadStatus.Finalizing => AppIconKind.Settings, DownloadStatus.Failed or DownloadStatus.Partial => AppIconKind.Info, DownloadStatus.Cancelled or DownloadStatus.Interrupted => AppIconKind.Close, _ => AppIconKind.History });
        statusGlyph?.SetColorToken(item.Status switch { DownloadStatus.Completed => AppThemeTokens.Success, DownloadStatus.Failed or DownloadStatus.Partial => AppThemeTokens.Error, _ => AppThemeTokens.TextSecondary });
        var transferParts = new List<string>();
        if (p.ShowSize && item.DownloadedBytes is { } bytes) transferParts.Add(DashboardStyle.Bytes(bytes) + (item.TotalBytes is { } totalBytes ? " / " + DashboardStyle.Bytes(totalBytes) : ""));
        if (p.ShowSpeed && item.SpeedBytesPerSecond is { } speed) transferParts.Add(DashboardStyle.Bytes((long)speed) + "/s");
        if (p.ShowEta && item.Eta is { } eta) transferParts.Add(text.Get("Queue.Eta", eta.ToString(@"hh\:mm\:ss")));
        transfer.Text = transferring ? string.Join(" · ", transferParts) : ""; transfer.AutoEllipsis = true; dashboardTips.SetToolTip(transfer, transfer.Text);
        progress.FillToken = item.RetryDelaySeconds > 0 || item.StatusMessageKey == "Queue.Detail.StoragePaused" ? AppThemeTokens.Warning : AppThemeTokens.Accent;
        LayoutDashboardRow();
    }
}
