using ModernFormsNext;

namespace ModernTubeDownloader.Shell;

/// <summary>Decorative titlebar text delegates drag/hit testing to the canonical FormTitleBar.</summary>
internal sealed class CaptionLabel : Label
{
    public CaptionLabel() { SetControlBehavior(ControlBehaviors.ReceivesMouseEvents, false); Style.BackgroundColor = SkiaSharp.SKColors.Transparent; TextAlign = ContentAlignment.MiddleLeft; }
}
