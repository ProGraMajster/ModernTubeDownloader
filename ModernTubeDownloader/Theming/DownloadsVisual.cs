using ModernFormsNext;
using ModernFormsNext.Drawing;
using ModernTubeDownloader.Services;
using SkiaSharp;

namespace ModernTubeDownloader.Theming;

/// <summary>Downloads-only finishes sampled from the owner reference. Classic recipes stay separate.</summary>
internal static class DownloadsVisual
{
    public const string Background = "Downloads.Background", BackgroundEnd = "Downloads.BackgroundEnd", Panel = "Downloads.Panel", PanelEnd = "Downloads.PanelEnd",
        Selected = "Downloads.Selected", SelectedEnd = "Downloads.SelectedEnd", Border = "Downloads.Border", ButtonTop = "Downloads.ButtonTop", ButtonEnd = "Downloads.ButtonEnd";
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Button, SourceVectorIcon> sources = new();
    internal static void Card(Control control)
    {
        AppUi.BindGradient(control, Panel, PanelEnd); AppUi.BindBorder(control, Border);
        control.Style.Border.Radius = 8; control.Style.Border.Width = 1;
    }
    internal static void Source(Button button, SourceBrand brand, SourceContentKind kind = SourceContentKind.Video)
    {
        if (!sources.TryGetValue(button, out var icon))
        {
            icon = new SourceVectorIcon(brand) { AccessibilityView = ModernFormsNext.Accessibility.AccessibilityView.Hidden };
            icon.Click += (_, _) => button.PerformClick(); sources.Add(button, icon); button.Controls.Add(icon);
        }
        icon.Brand = brand; icon.Kind = kind; icon.SetBounds(12, (button.Height - 18) / 2, 18, 18);
        DashboardVisual.SetPadding(button, new Padding(36, 0, 10, 0));
    }
    internal static void Button(Button button, bool primary = false)
    {
        DashboardVisual.Button(button, primary); AppUi.SetDownloadsButtonSurface(button);
    }
}

/// <summary>Clips the existing PictureBox painter; fitting and loading stay native.</summary>
internal sealed class DownloadsPictureBox : PictureBox
{
    public bool RoundedPreview { get; set; }
    protected override void OnPaint(PaintEventArgs e)
    {
        if (!RoundedPreview || !AppTypography.IsDashboard(this)) { base.OnPaint(e); return; }
        e.Canvas.Save();
        try
        {
            using var clip = new SKRoundRect(new SKRect(0, 0, LogicalToDeviceUnits(Width), LogicalToDeviceUnits(Height)), LogicalToDeviceUnits(6));
            e.Canvas.ClipRoundRect(clip, antialias: true); base.OnPaint(e);
        }
        finally { e.Canvas.Restore(); }
    }
}
