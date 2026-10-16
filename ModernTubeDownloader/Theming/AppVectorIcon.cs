using System.Drawing;
using ModernFormsNext;
using ModernFormsNext.Drawing;
using SkiaSharp;

namespace ModernTubeDownloader.Theming;

internal enum AppIconKind
{
    Clipboard,
    Download,
    Queue,
    Live,
    History,
    Settings, Help, Info, Link, Search, Play, Close, More, Retry, Folder
}

internal sealed class AppVectorIcon : UserControl
{
    private readonly List<(Shape Shape, bool Fill)> shapes = [];
    private string colorToken;
    private AppIconKind kind;

    public AppVectorIcon(AppIconKind kind, int size = 24, string colorToken = AppThemeTokens.TextSecondary)
    {
        this.colorToken = colorToken;
        this.kind = kind;
        Width = size;
        Height = size;
        Style.BackgroundColor = SKColors.Transparent;
        Build(kind, size / 24f);
        foreach (var (shape, _) in shapes)
        {
            shape.Click += ChildClicked;
            Controls.Add(shape);
        }
        ThemeManager.Current.ThemeChanged += ThemeChanged;
        ApplyTheme();
    }

    public void SetColorToken(string token)
    {
        colorToken = token;
        ApplyTheme();
    }
    public void SetIconSize(int size)
    {
        if (Width == size && Height == size) return;
        foreach (var (shape, _) in shapes) { Controls.Remove(shape); shape.Dispose(); }
        shapes.Clear(); Width = Height = size; Build(kind, size / 24f);
        foreach (var (shape, _) in shapes) { shape.Click += ChildClicked; Controls.Add(shape); }
        ApplyTheme();
    }

    public void SetKind(AppIconKind value)
    {
        if (kind == value) return; kind = value;
        foreach (var (shape, _) in shapes) { Controls.Remove(shape); shape.Dispose(); }
        shapes.Clear(); Build(kind, Width / 24f);
        foreach (var (shape, _) in shapes) { shape.Click += ChildClicked; Controls.Add(shape); }
        ApplyTheme();
    }

    private void Build(AppIconKind kind, float scale)
    {
        switch (kind)
        {
            case AppIconKind.Folder:
                AddPath(scale, [ClosedFigure((2,7),(10,7),(12,10),(22,10),(20,21),(2,21)), OpenFigure((2,7),(2,4),(9,4),(12,7),(21,7),(21,10))]); break;
            case AppIconKind.Link:
                AddPath(scale, [OpenFigure((9,16),(6,19),(3,19),(1,16),(1,13),(7,7),(10,7)), OpenFigure((14,17),(17,17),(23,11),(23,8),(20,5),(17,5),(14,8))]);
                AddLine(8*scale,16*scale,16*scale,8*scale,scale); break;
            case AppIconKind.Search:
                AddCircle(3*scale,3*scale,13*scale,scale); AddLine(15*scale,15*scale,22*scale,22*scale,scale); break;
            case AppIconKind.Play:
                AddPath(scale, [ClosedFigure((8,4),(20,12),(8,20))]); break;
            case AppIconKind.Close:
                AddLine(6*scale,6*scale,18*scale,18*scale,scale); AddLine(18*scale,6*scale,6*scale,18*scale,scale); break;
            case AppIconKind.More:
                foreach (var y in new[] { 5, 11, 17 }) AddFilledCircle(10*scale,y*scale,3*scale); break;
            case AppIconKind.Retry:
                AddPath(scale, [OpenFigure((20,10),(18,5),(12,3),(6,5),(3,11),(5,18),(11,21),(18,18)), OpenFigure((20,4),(20,10),(14,10))]); break;

            case AppIconKind.Live:
                AddCircle(3 * scale, 3 * scale, 18 * scale, scale);
                AddFilledCircle(8 * scale, 8 * scale, 8 * scale);
                break;
            case AppIconKind.Clipboard:
                AddPath(scale, [
                    ClosedFigure((6, 5), (18, 5), (18, 21), (6, 21)),
                    ClosedFigure((9, 3), (15, 3), (15, 7), (9, 7))]);
                break;
            case AppIconKind.Download:
                AddPath(scale, [
                    OpenFigure((12, 3), (12, 15)),
                    OpenFigure((7.5f, 10.5f), (12, 15), (16.5f, 10.5f)),
                    OpenFigure((5, 18), (5, 21), (19, 21), (19, 18))]);
                break;
            case AppIconKind.Queue:
                for (var row = 0; row < 3; row++)
                {
                    var y = (6 + (row * 6)) * scale;
                    AddFilledCircle(3 * scale, y - (1.5f * scale), 3 * scale);
                    AddLine(9 * scale, y, 21 * scale, y, scale);
                }
                break;
            case AppIconKind.History:
                AddCircle(2 * scale, 2 * scale, 20 * scale, scale);
                AddLine(12 * scale, 12 * scale, 12 * scale, 7 * scale, scale);
                AddLine(12 * scale, 12 * scale, 16 * scale, 14 * scale, scale);
                break;
            case AppIconKind.Help:
            case AppIconKind.Info:
                AddCircle(2 * scale, 2 * scale, 20 * scale, scale);
                if (kind == AppIconKind.Help) AddPath(scale, [OpenFigure((8, 8), (9, 5), (15, 5), (16, 9), (12, 12), (12, 14))]);
                else AddLine(12 * scale, 10 * scale, 12 * scale, 17 * scale, scale);
                AddFilledCircle(11 * scale, (kind == AppIconKind.Help ? 17 : 6) * scale, 2 * scale);
                break;
            case AppIconKind.Settings:
                AddLine(4 * scale, 6 * scale, 20 * scale, 6 * scale, scale);
                AddLine(4 * scale, 12 * scale, 20 * scale, 12 * scale, scale);
                AddLine(4 * scale, 18 * scale, 20 * scale, 18 * scale, scale);
                AddFilledCircle(7 * scale, 4 * scale, 4 * scale);
                AddFilledCircle(14 * scale, 10 * scale, 4 * scale);
                AddFilledCircle(9 * scale, 16 * scale, 4 * scale);
                break;
        }
    }

    private void AddPath(float scale, IEnumerable<PathFigure> figures)
    {
        var geometry = new PathGeometry();
        foreach (var figure in figures)
        {
            if (scale != 1f)
            {
                figure.StartPoint = Scale(figure.StartPoint, scale);
                foreach (var segment in figure.Segments.OfType<LineSegment>())
                    segment.Point = Scale(segment.Point, scale);
            }
            geometry.Figures.Add(figure);
        }
        AddShape(new ModernFormsNext.Path
        {
            Dock = DockStyle.Fill,
            Data = geometry,
            StrokeThickness = Math.Max(1.6f, 1.8f * scale),
            StrokeLineCap = StrokeLineCap.Round,
            StrokeLineJoin = StrokeLineJoin.Round
        });
    }

    private void AddLine(float x1, float y1, float x2, float y2, float scale)
        => AddShape(new Line
        {
            Dock = DockStyle.Fill,
            StartPoint = new PointF(x1, y1),
            EndPoint = new PointF(x2, y2),
            StrokeThickness = Math.Max(1.6f, 1.8f * scale),
            StrokeLineCap = StrokeLineCap.Round
        });

    private void AddCircle(float left, float top, float size, float scale)
        => AddShape(new Circle
        {
            Left = (int)Math.Round(left),
            Top = (int)Math.Round(top),
            Width = (int)Math.Round(size),
            Height = (int)Math.Round(size),
            StrokeThickness = Math.Max(1.6f, 1.8f * scale)
        });

    private void AddFilledCircle(float left, float top, float size)
        => AddShape(new Circle
        {
            Left = (int)Math.Round(left),
            Top = (int)Math.Round(top),
            Width = Math.Max(2, (int)Math.Round(size)),
            Height = Math.Max(2, (int)Math.Round(size)),
            StrokeThickness = 0
        }, fill: true);

    private void AddShape(Shape shape, bool fill = false)
    {
        shape.Style.BackgroundColor = SKColors.Transparent;
        shapes.Add((shape, fill));
    }

    private void ApplyTheme()
    {
        var skia = AppUi.GetColor(colorToken);
        var color = Color.FromArgb(skia.Alpha, skia.Red, skia.Green, skia.Blue);
        foreach (var (shape, fill) in shapes)
        {
            if (fill)
                shape.Fill = new SolidColorBrush(color);
            else
                shape.Stroke = new SolidColorBrush(color);
            shape.Invalidate();
        }
    }

    private void ChildClicked(object? sender, ModernFormsNext.MouseEventArgs e) => OnClick(e);
    private void ThemeChanged(object? sender, ThemeChangedEventArgs e) => ApplyTheme();

    private static PointF Scale(PointF point, float scale) => new(point.X * scale, point.Y * scale);

    private static PathFigure OpenFigure(params (float X, float Y)[] points)
        => CreateFigure(isClosed: false, points);

    private static PathFigure ClosedFigure(params (float X, float Y)[] points)
        => CreateFigure(isClosed: true, points);

    private static PathFigure CreateFigure(bool isClosed, params (float X, float Y)[] points)
    {
        var figure = new PathFigure(new PointF(points[0].X, points[0].Y), isClosed);
        foreach (var point in points.Skip(1))
            figure.Segments.Add(new LineSegment(new PointF(point.X, point.Y)));
        return figure;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            ThemeManager.Current.ThemeChanged -= ThemeChanged;
            foreach (var (shape, _) in shapes)
                shape.Click -= ChildClicked;
        }
        base.Dispose(disposing);
    }
}
