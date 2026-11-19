using ModernFormsNext;
using ModernTubeDownloader.Theming;

namespace ModernTubeDownloader.Views;

/// <summary>Reuses native labels for a selected item's labelled facts; the parent supplies scrolling.</summary>
internal sealed class DashboardFields : Panel
{
    private readonly List<(Label Key, Label Value)> rows = [];
    private readonly ToolTip tips = new();
    private readonly List<Panel> valueFrames = [];
    public bool SingleLineValues { get; set; }
    public bool InsetValues { get; set; }
    public DashboardFields() { Style.BackgroundColor = SkiaSharp.SKColors.Transparent; DashboardVisual.Flat(this); SizeChanged += (_, _) => LayoutFields(); }
    public void SetRows(params (string Key, string? Value)[] values)
    {
        var visible = values.Where(v => !string.IsNullOrWhiteSpace(v.Value)).ToArray();
        while (rows.Count < visible.Length)
        {
            var key = AppUi.Muted(); var value = AppUi.Muted(); AppUi.BindForeground(value);
            AppTypography.Bind(key, 9); AppTypography.Bind(value, 9.5f); key.Multiline = value.Multiline = true; value.AutoEllipsis = true;
            var frame = new Panel(); frame.Controls.Add(value);
            rows.Add((key, value)); valueFrames.Add(frame); Controls.AddRange([key, frame]);
        }
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i]; row.Key.Visible = row.Value.Visible = i < visible.Length;
            valueFrames[i].Visible = i < visible.Length;
            if (i >= visible.Length) { row.Key.SetBounds(0,0,0,0); valueFrames[i].SetBounds(0,0,0,0); continue; }
            row.Key.Text = visible[i].Key; row.Value.Text = visible[i].Value!; tips.SetToolTip(row.Value, row.Value.Text);
            // The value is nested in its frame; ParentChanged on the frame does not
            // reapply the child's presentation recipe after attachment to the page.
            AppTypography.Bind(row.Value, 9.5f);
            tips.SetToolTip(valueFrames[i], row.Value.Text);
        }
        LayoutFields();
    }
    private void LayoutFields()
    {
        if (Width <= 0) { Height = 0; return; }
        var y = 0; var keyWidth = InsetValues ? Math.Min(AppTypography.TextHeight(68), Width / 3) : Math.Min(116, Width * 2 / 5); var line = AppTypography.TextHeight(InsetValues ? 31 : 22);
        foreach (var row in rows.Where(r => r.Key.Visible))
        {
            var frame = valueFrames[rows.IndexOf(row)];
            var valueWidth = Math.Max(1, Width - keyWidth);
            row.Value.Multiline = !SingleLineValues;
            var height = Math.Max(line, Math.Max(DashboardVisual.LabelHeight(row.Key, keyWidth - 8, 3), DashboardVisual.LabelHeight(row.Value, valueWidth, SingleLineValues ? 1 : 3)));
            row.Key.TextAlign = InsetValues ? ContentAlignment.MiddleLeft : ContentAlignment.TopLeft;
            row.Key.SetBounds(0, y, keyWidth - 8, height); frame.SetBounds(keyWidth, y, valueWidth, height);
            row.Value.SetBounds(InsetValues ? 10 : 0, 0, Math.Max(1, valueWidth - (InsetValues ? 20 : 0)), height);
            row.Value.TextAlign = row.Key.TextAlign;
            if (InsetValues) { AppUi.BindGradient(frame, DownloadsVisual.ButtonTop, DownloadsVisual.ButtonEnd); AppUi.BindBorder(frame, DownloadsVisual.Border); frame.Style.Border.Radius = 6; }
            else { AppUi.ClearGradient(frame); frame.Style.BackgroundColor = SkiaSharp.SKColors.Transparent; DashboardVisual.Flat(frame); }
            y += height + AppTypography.TextHeight(InsetValues ? 7 : 4);
        }
        Height = y;
    }
    protected override void Dispose(bool disposing) { if (disposing) tips.Dispose(); base.Dispose(disposing); }
}

internal sealed class DashboardMetric : Panel
{
    private readonly Label caption = AppUi.Muted(), value = AppUi.Heading("", 18);
    public DashboardMetric(string token)
    {
        DashboardStyle.Card(this); AppUi.BindForeground(value);
        var icon = new AppVectorIcon(token == AppThemeTokens.Success ? AppIconKind.Check : token == AppThemeTokens.Error ? AppIconKind.Info :
            token == AppThemeTokens.Warning ? AppIconKind.Clipboard : AppIconKind.Queue, 20, token)
            { AccessibilityView = ModernFormsNext.Accessibility.AccessibilityView.Hidden };
        caption.Multiline = true; AppTypography.Bind(caption, 9); Controls.AddRange([icon, caption, value]);
        SizeChanged += (_, _) =>
        {
            icon.SetBounds(12, 14, 20, 20);
            var textWidth = Math.Max(1, Width - 54);
            caption.SetBounds(42, 10, textWidth, DashboardVisual.LabelHeight(caption, textWidth, 2));
            value.SetBounds(42, caption.Bottom + 2, textWidth, DashboardVisual.LabelHeight(value, textWidth, 1));
        };
    }
    public int DesiredHeight(int width) => 22 + DashboardVisual.LabelHeight(caption, Math.Max(1, width - 54), 2)
        + DashboardVisual.LabelHeight(value, Math.Max(1, width - 54), 1);
    public void Present(string name, int number) { caption.Text = name; value.Text = number.ToString(); }
}
