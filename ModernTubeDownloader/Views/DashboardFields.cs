using ModernFormsNext;
using ModernTubeDownloader.Theming;

namespace ModernTubeDownloader.Views;

/// <summary>Reuses native labels for a selected item's labelled facts; the parent supplies scrolling.</summary>
internal sealed class DashboardFields : Panel
{
    private readonly List<(Label Key, Label Value)> rows = [];
    private readonly ToolTip tips = new();
    public DashboardFields() { Style.BackgroundColor = SkiaSharp.SKColors.Transparent; DashboardVisual.Flat(this); SizeChanged += (_, _) => LayoutFields(); }
    public void SetRows(params (string Key, string? Value)[] values)
    {
        var visible = values.Where(v => !string.IsNullOrWhiteSpace(v.Value)).ToArray();
        while (rows.Count < visible.Length)
        {
            var key = AppUi.Muted(); var value = AppUi.Muted(); AppUi.BindForeground(value);
            AppTypography.Bind(key, 9); AppTypography.Bind(value, 9.5f); key.Multiline = value.Multiline = true; value.AutoEllipsis = true;
            rows.Add((key, value)); Controls.AddRange([key, value]);
        }
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i]; row.Key.Visible = row.Value.Visible = i < visible.Length;
            if (i >= visible.Length) continue;
            row.Key.Text = visible[i].Key; row.Value.Text = visible[i].Value!; tips.SetToolTip(row.Value, row.Value.Text);
        }
        LayoutFields();
    }
    private void LayoutFields()
    {
        if (Width <= 0) { Height = 0; return; }
        var y = 0; var keyWidth = Math.Min(116, Width * 2 / 5); var line = AppTypography.TextHeight(22);
        foreach (var row in rows.Where(r => r.Key.Visible))
        {
            var valueWidth = Math.Max(1, Width - keyWidth);
            var height = Math.Max(line, Math.Max(DashboardVisual.LabelHeight(row.Key, keyWidth - 8, 3), DashboardVisual.LabelHeight(row.Value, valueWidth, 3)));
            row.Key.SetBounds(0, y, keyWidth - 8, height); row.Value.SetBounds(keyWidth, y, valueWidth, height);
            y += height + AppTypography.TextHeight(4);
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
        AppUi.BindBackground(this, AppThemeTokens.SurfaceSecondary); AppUi.BindForeground(value, token);
        caption.Multiline = true; AppTypography.Bind(caption, 9); Controls.AddRange([caption, value]);
        SizeChanged += (_, _) => { caption.SetBounds(12, 10, Math.Max(1, Width - 24), AppTypography.TextHeight(36)); value.SetBounds(12, caption.Bottom + 2, Math.Max(1, Width - 24), AppTypography.TextHeight(40)); };
    }
    public void Present(string name, int number) { caption.Text = name; value.Text = number.ToString(); }
}
