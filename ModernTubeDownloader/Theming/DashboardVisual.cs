using System.Drawing;
using ContentAlignment = ModernFormsNext.ContentAlignment;
using ModernFormsNext;
using SkiaSharp;

namespace ModernTubeDownloader.Theming;

/// <summary>Modern presentation metrics, independent of monitor DPI and Classic recipes.</summary>
internal static class DashboardVisual
{
    private sealed record BorderRecipe(int? Width, int? Radius, SKColor? Color, int? BottomWidth, SKColor? BottomColor);
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Control, BorderRecipe[]> originalBorders = new();
    private sealed record PaddingRecipe(Padding ControlPadding, Padding?[] Styles);
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Control, PaddingRecipe> originalPadding = new();
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Button, AppVectorIcon> icons = new();
    public const int Margin = 20, Gap = 12, Radius = 10, Input = 46, Action = 36, Row = 98;
    public const float ItemTitle = 11.5f, Metadata = 9.5f, Caption = 9;
    public static int Text(int value) => AppTypography.TextHeight(value);
    public static int TextWidth(Control control, string text)
    {
        var style = control.CurrentStyle;
        return (int)Math.Ceiling(TextMeasurer.MeasureText(text, style.GetFont(), style.GetFontSize(),
            new Size(int.MaxValue, int.MaxValue), style.GetFontStyle()).Width);
    }
    public static int LabelHeight(Label label, int width, int maxLines)
    {
        var style = label.CurrentStyle;
        var line = (int)Math.Ceiling(TextMeasurer.MeasureText("Ag", style.GetFont(), style.GetFontSize(), new Size(int.MaxValue, int.MaxValue), style.GetFontStyle()).Height) + 2;
        var measured = (int)Math.Ceiling(TextMeasurer.MeasureText(label.Text, style.GetFont(), style.GetFontSize(), new Size(Math.Max(1, width), int.MaxValue), style.GetFontStyle()).Height) + 2;
        return Math.Clamp(measured, line, line * maxLines);
    }
    public static int ActionWidth(Button button, bool withIcon = false)
    {
        var style = button.CurrentStyle;
        var size = TextMeasurer.MeasureText(button.Text, style.GetFont(), style.GetFontSize(),
            new Size(int.MaxValue, int.MaxValue), style.GetFontStyle());
        return (int)Math.Ceiling(size.Width) + (withIcon ? 48 : 26);
    }
    public static void Flat(Control control, bool separator = false)
    {
        originalBorders.GetValue(control, c => Styles(c).Select(s => new BorderRecipe(s.Border.Width, s.Border.Radius, s.Border.Color, s.Border.Bottom.Width, s.Border.Bottom.Color)).ToArray());
        foreach (var style in new[] { control.Style, control.StyleHover, control.StylePressed, control.StyleFocused, control.StyleDisabled })
        {
            style.Border.Width = 0; style.Border.Color = SKColors.Transparent;
            if (separator) { style.Border.Radius = 0; style.Border.Bottom.Width = 1; style.Border.Bottom.Color = AppUi.GetColor(AppThemeTokens.Border); }
        }
    }
    private static ControlStyle[] Styles(Control c) => [c.Style, c.StyleHover, c.StylePressed, c.StyleFocused, c.StyleDisabled];
    public static void SetPadding(Control control, Padding padding)
    {
        originalPadding.GetValue(control, c => new PaddingRecipe(c.Padding, Styles(c).Select(s => s.Padding).ToArray()));
        control.Padding = padding; foreach (var style in Styles(control)) style.Padding = padding;
    }
    public static Action CaptureLabels(Control root)
    {
        var labels = new List<(Label Label, bool Multiline, bool Ellipsis, int Height)>();
        void Visit(Control c) { if (c is Label label) labels.Add((label,label.Multiline,label.AutoEllipsis,label.Height)); foreach(var child in c.Controls) Visit(child); }
        Visit(root);
        return () => { foreach(var row in labels) { row.Label.Multiline=row.Multiline; row.Label.AutoEllipsis=row.Ellipsis; row.Label.Height=row.Height; } };
    }
    public static void RestoreTree(Control control)
    {
        if (control is Button surfaceButton) AppUi.SetDashboardButtonSurface(surfaceButton, false);
        if (control is Button button && icons.TryGetValue(button, out var icon)) icon.Visible = false;
        if (originalPadding.TryGetValue(control, out var padding))
        {
            control.Padding = padding.ControlPadding; var styles = Styles(control);
            for (var i=0;i<styles.Length;i++) styles[i].Padding = padding.Styles[i]; originalPadding.Remove(control);
        }
        if (originalBorders.TryGetValue(control, out var saved))
        {
            var styles = Styles(control);
            for (var i = 0; i < styles.Length; i++)
            {
                styles[i].Border.Width = saved[i].Width; styles[i].Border.Color = saved[i].Color; styles[i].Border.Radius = saved[i].Radius;
                styles[i].Border.Bottom.Width = saved[i].BottomWidth; styles[i].Border.Bottom.Color = saved[i].BottomColor;
            }
            originalBorders.Remove(control);
        }
        AppUi.ClearGradient(control);
        foreach (var child in control.Controls) RestoreTree(child);
    }
    public static void Button(Button button, bool primary = false)
    {
        Flat(button); // Capture the Classic recipe before applying Modern presentation.
        if (primary) AppUi.Primary(button); else AppUi.Secondary(button);
        AppTypography.Bind(button, 10.5f, ModernFormsNext.FontStyle.Regular);
        AppUi.SetDashboardButtonSurface(button, true);
        foreach (var style in Styles(button))
        {
            style.Border.Width = 1;
            style.Border.Radius = 7;
            style.Border.Color = AppUi.GetColor(primary ? AppThemeTokens.Accent : AppThemeTokens.Border);
        }
        button.StyleFocused.Border.Width = 1; button.StyleFocused.Border.Color = AppUi.GetColor(AppThemeTokens.Accent);
        SetPadding(button, new Padding(12, 0, 12, 0));
    }
    public static void Icon(Button button, AppIconKind kind, bool iconOnly = false)
    {
        if (!icons.TryGetValue(button, out var icon))
        {
            icon = new AppVectorIcon(kind, 18, AppThemeTokens.TextPrimary)
            { AccessibilityView = ModernFormsNext.Accessibility.AccessibilityView.Hidden };
            icon.Click += (_, _) => button.PerformClick();
            // ControlAdded/layout callbacks may reenter Icon: publish before attaching.
            icons.Add(button, icon);
            button.Controls.Add(icon);
        }
        icon.SetKind(kind); icon.Visible = true;
        icon.SetColorToken(button.Style.ForegroundColor == AppUi.GetColor(AppThemeTokens.AccentText)
            ? AppThemeTokens.AccentText : AppThemeTokens.TextPrimary);
        icon.SetBounds(iconOnly ? (button.Width - 18) / 2 : 12, (button.Height - 18) / 2, 18, 18);
        SetPadding(button, new Padding(iconOnly ? 0 : 36, 0, 10, 0));
    }
    public static void Tab(Button button, bool selected)
    {
        AppUi.NavigationButton(button, selected); Flat(button);
        foreach (var style in new[] { button.Style, button.StyleHover, button.StylePressed, button.StyleFocused })
        { style.Border.Radius = 0; style.Border.Bottom.Width = selected ? 2 : 0; style.Border.Bottom.Color = AppUi.GetColor(AppThemeTokens.Accent); }
        button.Style.BackgroundColor = AppUi.GetColor(selected ? AppThemeTokens.SurfaceSecondary : AppThemeTokens.Surface);
    }
}

/// <summary>Uses MFN's public Unicode text painter against the available field. Keeps Label's semantic provider.</summary>
internal class DashboardLabel : Label
{
    protected override void OnPaint(PaintEventArgs e)
    {
        if (!AppTypography.IsDashboard(this)) { base.OnPaint(e); return; }
        var p = Padding;
        var bounds = new Rectangle(LogicalToDeviceUnits(p.Left), LogicalToDeviceUnits(p.Top),
            Math.Max(0, LogicalToDeviceUnits(Width - p.Horizontal)), Math.Max(0, LogicalToDeviceUnits(Height - p.Vertical)));
        e.Canvas.DrawText(Text, bounds, this, TextAlign, maxLines: Multiline ? null : 1, ellipsis: AutoEllipsis);
    }
}

/// <summary>Text-only dashboard action; native Button still owns input, focus, background and Invoke semantics.</summary>
internal class DashboardButton : Button
{
    static DashboardButton() => ModernFormsNext.Renderers.RenderManager.SetRenderer<DashboardButton>(new DashboardButtonRenderer());
    // 1 joins the right edge, 2 joins the left. Input, focus and Invoke stay native.
    public int JoinedEdge { get; set; }
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        if (JoinedEdge == 0 || !AppTypography.IsDashboard(this)) { base.OnPaintBackground(e); return; }
        var radius = LogicalToDeviceUnits(7);
        var rounded = new SKPoint(radius, radius); var square = SKPoint.Empty;
        using var outline = new SKRoundRect();
        outline.SetRectRadii(new SKRect(.5f, .5f, LogicalToDeviceUnits(Width) - .5f, LogicalToDeviceUnits(Height) - .5f),
            JoinedEdge == 1 ? [rounded, square, square, rounded] : [square, rounded, rounded, square]);
        // The retained MFN back buffer may contain the previous rectangular recipe.
        // Clear outside the new clip as well when switching presentation/styles.
        e.Canvas.Clear(SKColors.Transparent);
        e.Canvas.Save();
        try { e.Canvas.ClipRoundRect(outline, SKClipOperation.Intersect, true); base.OnPaintBackground(e); }
        finally { e.Canvas.Restore(); }
        using var border = new SKPaint { Color = CurrentStyle.Border.GetColor(), StrokeWidth = (float)Scaling, Style = SKPaintStyle.Stroke, IsAntialias = true };
        e.Canvas.DrawRoundRect(outline, border);
    }
}

internal sealed class DashboardButtonRenderer : ModernFormsNext.Renderers.ButtonRenderer
{
    public override Type Type => typeof(DashboardButton);
    protected override void Render(Button button, PaintEventArgs e)
    {
        if (!AppTypography.IsDashboard(button) || button.Image is not null) { base.Render(button, e); return; }
        var p = button.Padding;
        var bounds = new Rectangle(button.LogicalToDeviceUnits(p.Left), button.LogicalToDeviceUnits(p.Top),
            Math.Max(0, button.LogicalToDeviceUnits(button.Width - p.Horizontal)), Math.Max(0, button.LogicalToDeviceUnits(button.Height - p.Vertical)));
        e.Canvas.DrawText(button.Text, bounds, button, button.TextAlign, maxLines: 1, ellipsis: button.AutoEllipsis);
    }
}

/// <summary>App progress presentation with the existing native range semantics and value model.</summary>
internal sealed class DashboardProgress : ProgressBar
{
    public string FillToken { get; set; } = AppThemeTokens.Accent;
    protected override void OnPaint(PaintEventArgs e)
    {
        if (!AppTypography.IsDashboard(this)) { base.OnPaint(e); return; }
        var w = LogicalToDeviceUnits(Width); var h = LogicalToDeviceUnits(Height);
        using var paint = new SKPaint { IsAntialias = true, Color = AppUi.GetColor(AppThemeTokens.SurfaceSecondary) };
        e.Canvas.DrawRoundRect(new SKRect(0, 0, w, h), h / 2f, h / 2f, paint);
        var fraction = Math.Clamp((Value - Minimum) / (double)Math.Max(1, Maximum - Minimum), 0, 1);
        paint.Color = AppUi.GetColor(FillToken);
        e.Canvas.DrawRoundRect(new SKRect(0, 0, (float)(w * fraction), h), h / 2f, h / 2f, paint);
    }
}

/// <summary>Switch presentation of the canonical CheckBox, retaining its Toggle action and keyboard behavior.</summary>
internal sealed class DashboardToggle : CheckBox
{
    protected override void OnPaintBackground(PaintEventArgs e)
    { if (!AppTypography.IsDashboard(this)) base.OnPaintBackground(e); }
    protected override void OnPaint(PaintEventArgs e)
    {
        if (!AppTypography.IsDashboard(this)) { base.OnPaint(e); return; }
        var trackWidth = LogicalToDeviceUnits(36); var trackHeight = LogicalToDeviceUnits(20);
        var width = LogicalToDeviceUnits(Width); var height = LogicalToDeviceUnits(Height);
        var x = width - trackWidth - LogicalToDeviceUnits(2); var y = (height - trackHeight) / 2f;
        using var paint = new SKPaint { IsAntialias = true, Color = AppUi.GetColor(Enabled && Checked ? AppThemeTokens.Accent : AppThemeTokens.TextDisabled) };
        e.Canvas.DrawRoundRect(new SKRect(x, y, x + trackWidth, y + trackHeight), trackHeight / 2f, trackHeight / 2f, paint);
        paint.Color = SKColors.White; var radius = trackHeight / 2f - LogicalToDeviceUnits(3);
        e.Canvas.DrawCircle(Checked ? x + trackWidth - trackHeight / 2f : x + trackHeight / 2f, y + trackHeight / 2f, radius, paint);
        e.Canvas.DrawText(Text, new Rectangle(0, 0, Math.Max(0, (int)x - LogicalToDeviceUnits(8)), height), this, ContentAlignment.MiddleLeft, maxLines: null, ellipsis: true);
        if (Focused)
        {
            paint.Color = AppUi.GetColor(AppThemeTokens.Accent); paint.Style = SKPaintStyle.Stroke; paint.StrokeWidth = LogicalToDeviceUnits(1);
            e.Canvas.DrawRoundRect(new SKRect(1, 1, width - 2, height - 2), 4, 4, paint);
        }
    }
}
