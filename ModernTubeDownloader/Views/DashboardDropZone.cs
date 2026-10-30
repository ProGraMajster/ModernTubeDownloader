using ModernFormsNext;
using ModernTubeDownloader.Theming;
using SkiaSharp;

namespace ModernTubeDownloader.Views;

/// <summary>Visual boundary for the real shell file-drop route. Children retain native semantics.</summary>
internal sealed class DashboardDropZone : Panel
{
    public DashboardDropZone() { AppUi.BindBackground(this, AppThemeTokens.Surface); Style.Border.Width = 0; }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var dash = SKPathEffect.CreateDash([5, 4], 0);
        using var paint = new SKPaint { Color = AppUi.GetColor(AppThemeTokens.Info).WithAlpha(160), IsAntialias = true,
            Style = SKPaintStyle.Stroke, StrokeWidth = LogicalToDeviceUnits(1), PathEffect = dash };
        e.Canvas.DrawRoundRect(new SKRect(1, 1, LogicalToDeviceUnits(Width) - 1, LogicalToDeviceUnits(Height) - 1), 7, 7, paint);
    }
}
