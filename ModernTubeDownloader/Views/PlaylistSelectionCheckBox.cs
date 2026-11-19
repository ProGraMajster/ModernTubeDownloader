using ModernFormsNext;
using ModernTubeDownloader.Theming;
using SkiaSharp;

namespace ModernTubeDownloader.Views;

// Only the paint differs. CheckBox owns keyboard, TogglePattern, focus and Checked.
internal sealed class PlaylistSelectionCheckBox : CheckBox
{
    public bool Dashboard { get; set; }
    protected override void OnPaint(PaintEventArgs e)
    {
        if (!Dashboard) { base.OnPaint(e); return; }
        var scale = (float)Scaling;
        var side = 18 * scale;
        var x = (LogicalToDeviceUnits(Width) - side) / 2f;
        var y = (LogicalToDeviceUnits(Height) - side) / 2f;
        using var paint = new SKPaint { IsAntialias = true, Color = AppUi.GetColor(Checked && Enabled ? AppThemeTokens.Accent : DownloadsVisual.Panel) };
        var rect = new SKRect(x, y, x + side, y + side);
        e.Canvas.DrawRoundRect(rect, 3 * scale, 3 * scale, paint);
        paint.Style = SKPaintStyle.Stroke; paint.StrokeWidth = scale;
        paint.Color = AppUi.GetColor(Enabled ? AppThemeTokens.TextSecondary : DownloadsVisual.Border);
        e.Canvas.DrawRoundRect(rect, 3 * scale, 3 * scale, paint);
        if (Checked)
        {
            paint.Color = Enabled ? SKColors.White : AppUi.GetColor(AppThemeTokens.TextSecondary);
            paint.StrokeWidth = 2 * scale; paint.StrokeCap = SKStrokeCap.Round; paint.StrokeJoin = SKStrokeJoin.Round;
            using var path = new SKPath();
            path.MoveTo(x + 4 * scale, y + 9 * scale); path.LineTo(x + 8 * scale, y + 13 * scale); path.LineTo(x + 14 * scale, y + 5 * scale);
            e.Canvas.DrawPath(path, paint);
        }
        if (Focused)
        {
            paint.Color = AppUi.GetColor(AppThemeTokens.Accent); paint.StrokeWidth = scale;
            rect.Inflate(3 * scale, 3 * scale); e.Canvas.DrawRoundRect(rect, 4 * scale, 4 * scale, paint);
        }
    }
}
