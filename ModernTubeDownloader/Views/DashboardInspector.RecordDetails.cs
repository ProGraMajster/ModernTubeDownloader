using ModernFormsNext;
using ModernTubeDownloader.Theming;

namespace ModernTubeDownloader.Views;

internal sealed partial class DashboardInspector
{
    private readonly Panel recordStatus = new();
    private readonly AppVectorIcon recordStatusIcon = new(AppIconKind.Check, 16);
    private readonly Label recordStatusLabel = AppUi.Muted(), recordPathLabel = AppUi.Muted();
    private readonly TextBox recordPath = new() { ReadOnly = true, AccessibleAutomationId = "InspectorOutputPath" };
    private string? recordStatusKey;

    private void CreateRecordDetails()
    {
        DashboardVisual.Flat(recordStatus); recordStatus.Style.Border.Radius = 6;
        recordStatusIcon.AccessibilityView = ModernFormsNext.Accessibility.AccessibilityView.Hidden;
        AppTypography.Bind(recordStatusLabel, 9.5f); recordStatusLabel.AutoEllipsis = true;
        recordStatusLabel.AccessibleAutomationId = "InspectorStatus";
        recordStatus.Controls.AddRange([recordStatusIcon, recordStatusLabel]);
        AppTypography.Bind(recordPathLabel, 9); AppUi.Input(recordPath); AppTypography.Bind(recordPath, 9.5f);
        Controls.AddRange([recordStatus, recordPathLabel, recordPath]);
    }

    private void PresentRecordDetails(string statusKey, string colorToken, AppIconKind icon, string? path)
    {
        recordStatusKey = statusKey; recordStatusLabel.Text = services.Localization[statusKey];
        AppUi.BindForeground(recordStatusLabel, colorToken); recordStatusIcon.SetColorToken(colorToken); recordStatusIcon.SetKind(icon);
        recordStatus.Style.BackgroundColor = AppUi.GetColor(colorToken).WithAlpha(24);
        recordStatus.Style.Border.Width = 1; recordStatus.Style.Border.Color = AppUi.GetColor(colorToken).WithAlpha(55);
        recordPath.Text = path ?? ""; tips.SetToolTip(recordPath, path);
    }

    private int LayoutRecordStatus(int left, int top, int width)
    {
        recordStatus.Visible = true;
        recordStatusLabel.Text = recordStatusKey is null ? "" : services.Localization[recordStatusKey];
        var height = AppTypography.TextHeight(26);
        var pillWidth = Math.Min(width, DashboardVisual.TextWidth(recordStatusLabel, recordStatusLabel.Text) + 42);
        recordStatus.SetBounds(left, top, pillWidth, height);
        recordStatusIcon.SetBounds(8, (height - 16) / 2, 16, 16);
        recordStatusLabel.SetBounds(30, 0, Math.Max(1, pillWidth - 38), height);
        return recordStatus.Bottom;
    }

    private int LayoutRecordPath(int left, int top, int width)
    {
        recordPath.Visible = recordPathLabel.Visible = recordPath.Text.Length > 0;
        if (!recordPath.Visible) { recordPath.SetBounds(0, 0, 0, 0); recordPathLabel.SetBounds(0, 0, 0, 0); return top; }
        recordPathLabel.Text = services.Localization["Dashboard.Output"];
        recordPathLabel.SetBounds(left, top + 6, width, AppTypography.TextHeight(22));
        recordPath.SetBounds(left, recordPathLabel.Bottom + 3, width, AppTypography.TextHeight(32));
        AppUi.BindGradient(recordPath, DownloadsVisual.ButtonTop, DownloadsVisual.ButtonEnd);
        AppUi.BindBorder(recordPath, DownloadsVisual.Border); recordPath.Style.Border.Radius = 6;
        return recordPath.Bottom;
    }

    private void HideRecordDetails()
    {
        foreach (var control in new Control[] { recordStatus, recordPathLabel, recordPath })
        { control.Visible = false; control.SetBounds(0, 0, 0, 0); }
    }
}
