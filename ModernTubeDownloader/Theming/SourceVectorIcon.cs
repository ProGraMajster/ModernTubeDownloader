using ModernFormsNext;
using ModernTubeDownloader.Services;
using SkiaSharp;

namespace ModernTubeDownloader.Theming;

/// <summary>Device-resolution vector marks; owns no raster cache or external resources.</summary>
internal sealed class SourceVectorIcon : Control
{
    private SourceBrand brand;
    private SourceContentKind kind;
    public SourceContentKind Kind { get => kind; set { if (kind != value) { kind = value; Invalidate(); } } }
    public SourceBrand Brand { get => brand; set { if (brand != value) { brand = value; Invalidate(); } } }
    public SourceVectorIcon(SourceBrand brand = SourceBrand.Other)
    {
        this.brand = brand; Style.BackgroundColor = SKColors.Transparent;
        TabStop = false; SetControlBehavior(ControlBehaviors.Selectable, false);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Canvas.Save();
        try
        {
            e.Canvas.Scale(LogicalToDeviceUnits(Width) / 24f, LogicalToDeviceUnits(Height) / 24f);
            var foreground = AppUi.GetColor(AppThemeTokens.TextPrimary);
            using var pen = new SKPaint { IsAntialias = true, Color = foreground, StrokeWidth = 1.5f, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round };
            void Path(bool fill, params SKPoint[] points)
            {
                using var path = new SKPath(); path.MoveTo(points[0]); foreach (var p in points.Skip(1)) path.LineTo(p);
                if (fill) path.Close(); pen.Style = fill ? SKPaintStyle.Fill : SKPaintStyle.Stroke; e.Canvas.DrawPath(path, pen);
            }
            void Video()
            {
                pen.Color = SKColor.Parse("#FF1431"); pen.Style = SKPaintStyle.Fill;
                e.Canvas.DrawRoundRect(new SKRect(1, 4, 23, 20), 4, 4, pen);
                pen.Color = SKColors.White; Path(true, new(10, 8), new(17, 12), new(10, 16));
            }
            // Content-context marks are used only on the corresponding action chips.
            // Rows and inspectors always keep the actual provider mark.
            switch (kind)
            {
                case SourceContentKind.Playlist:
                    Path(false, new(2,5), new(16,5)); Path(false,new(2,10),new(16,10)); Path(false,new(2,15),new(10,15)); Path(true,new(16,13),new(23,17),new(16,21)); return;
                case SourceContentKind.Channel:
                    e.Canvas.DrawRoundRect(new SKRect(2,7,22,21),3,3,pen); e.Canvas.DrawCircle(12,7,4,pen); Path(false,new(7,18),new(9,14),new(15,14),new(17,18)); return;
                case SourceContentKind.Shorts:
                    pen.Color = SKColor.Parse("#FF1431"); Path(true,new(14,1),new(21,5),new(21,10),new(16,13),new(20,15),new(20,20),new(10,24),new(3,20),new(3,15),new(8,12),new(4,10),new(4,5));
                    pen.Color = SKColors.White; Path(true,new(10,8),new(16,12),new(10,16)); return;
            }
            if (brand is SourceBrand.Kick or SourceBrand.TikTok)
            {
                // Preserve the bright brand ink without losing it on Light surfaces.
                pen.Color = SKColor.Parse("#0B1118"); pen.Style = SKPaintStyle.Fill;
                e.Canvas.DrawRoundRect(new SKRect(0,0,24,24),4,4,pen);
                e.Canvas.Translate(2,2); e.Canvas.Scale(20 / 24f); pen.Style = SKPaintStyle.Stroke;
            }
            switch (brand)
            {
                case SourceBrand.YouTube: Video(); break;
                case SourceBrand.Twitch:
                    pen.Color = SKColor.Parse("#A970FF"); Path(true,new(3,2),new(23,2),new(23,16),new(17,22),new(12,22),new(8,24),new(8,20),new(3,20));
                    pen.Color = SKColors.White; Path(false,new(7,5),new(20,5),new(20,14),new(16,18),new(7,18),new(7,5)); Path(false,new(12,8),new(12,13)); Path(false,new(17,8),new(17,13)); break;
                case SourceBrand.Facebook:
                    pen.Color = SKColor.Parse("#1877F2"); pen.Style = SKPaintStyle.Fill; e.Canvas.DrawCircle(12,12,11,pen);
                    pen.Color = SKColors.White; pen.StrokeWidth = 3; Path(false,new(13,22),new(13,7),new(16,5),new(19,5)); Path(false,new(9,12),new(18,12)); break;
                case SourceBrand.Kick:
                    pen.Color = SKColor.Parse("#53FC18"); Path(true,new(4,3),new(9,3),new(9,9),new(13,9),new(13,5),new(18,5),new(18,2),new(23,2),new(23,9),new(18,9),new(18,15),new(23,15),new(23,22),new(17,22),new(17,18),new(13,18),new(13,15),new(9,15),new(9,22),new(4,22)); break;
                case SourceBrand.Instagram:
                    pen.Color = SKColor.Parse("#EE5A9C"); e.Canvas.DrawRoundRect(new SKRect(2,2,22,22),6,6,pen); e.Canvas.DrawCircle(12,12,5,pen); pen.Style=SKPaintStyle.Fill; e.Canvas.DrawCircle(18,6,1.5f,pen); break;
                case SourceBrand.TikTok:
                    pen.Color = SKColor.Parse("#25F4EE"); pen.StrokeWidth=3; Path(false,new(14,3),new(14,17),new(11,21),new(6,21),new(4,17),new(6,14),new(9,14)); pen.Color=SKColors.White; Path(false,new(15,3),new(18,7),new(22,8)); break;
                case SourceBrand.Twitter:
                    Path(false,new(3,2),new(21,22)); Path(false,new(21,2),new(3,22)); Path(false,new(3,2),new(8,2),new(21,22),new(16,22),new(3,2)); break;
                case SourceBrand.Vimeo:
                    pen.Color=SKColor.Parse("#1AB7EA"); pen.StrokeWidth=4; Path(false,new(2,8),new(6,5),new(10,20),new(14,18),new(21,7),new(19,4),new(17,6)); break;
                case SourceBrand.Vk:
                    pen.Color=SKColor.Parse("#0077FF"); pen.StrokeWidth=3; Path(false,new(2,5),new(5,15),new(11,20),new(11,4)); Path(false,new(11,14),new(16,12),new(22,4)); Path(false,new(16,12),new(22,20)); break;
                default:
                    e.Canvas.DrawCircle(12,12,10,pen); e.Canvas.DrawOval(new SKRect(7,2,17,22),pen); Path(false,new(2,12),new(22,12)); break;
            }
        }
        finally { e.Canvas.Restore(); }
    }
}
