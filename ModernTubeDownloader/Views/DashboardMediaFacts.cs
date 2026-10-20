using ModernFormsNext;
using ModernFormsNext.Accessibility;
using ModernTubeDownloader.Theming;

namespace ModernTubeDownloader.Views;

/// <summary>Compact, measured media facts. Values come from the canonical item, never inferred media properties.</summary>
internal sealed class DashboardMediaFacts : UserControl
{
    private readonly List<(AppVectorIcon Icon, Label Label)> items = [];
    private readonly ToolTip tips = new();
    public event EventHandler? Activated;

    public DashboardMediaFacts()
    {
        Style.BackgroundColor = SkiaSharp.SKColors.Transparent;
        Style.Border.Width = 0;
    }

    public void SetFacts(params (AppIconKind Icon, string Text)[] facts)
    {
        while (items.Count < facts.Length)
        {
            var icon = new AppVectorIcon(AppIconKind.Info, 14) { AccessibilityView = AccessibilityView.Hidden };
            var label = AppUi.Muted(); label.AutoEllipsis = true; AppTypography.Bind(label, 9.5f);
            icon.Click += Activate; label.Click += Activate;
            items.Add((icon, label)); Controls.AddRange([icon, label]);
        }
        for (var i = 0; i < items.Count; i++)
        {
            var (icon, label) = items[i];
            icon.Visible = label.Visible = i < facts.Length;
            if (i >= facts.Length) { icon.SetBounds(0, 0, 0, 0); label.SetBounds(0, 0, 0, 0); continue; }
            icon.SetKind(facts[i].Icon); label.Text = facts[i].Text; tips.SetToolTip(label, label.Text);
        }
    }

    public void Arrange(int width)
    {
        var x = 0; var y = 0; var line = AppTypography.TextHeight(22);
        foreach (var (icon, label) in items.Where(i => i.Label.Visible))
        {
            var desired = Math.Min(Math.Max(1, width), DashboardVisual.TextWidth(label, label.Text) + 20);
            if (x > 0 && x + desired > width) { x = 0; y += line; }
            icon.SetBounds(x, y + (line - 14) / 2, 14, 14);
            label.SetBounds(x + 20, y, Math.Max(1, desired - 20), line);
            x += desired + 14;
        }
        Height = items.Any(i => i.Label.Visible) ? y + line : 0;
    }

    private void Activate(object? sender, EventArgs e) => Activated?.Invoke(this, e);
    protected override void Dispose(bool disposing) { if (disposing) tips.Dispose(); base.Dispose(disposing); }
}
