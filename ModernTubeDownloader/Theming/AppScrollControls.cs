using ModernFormsNext;

namespace ModernTubeDownloader.Theming;

internal static class AppScrollBehavior
{
    // ModernFormsNext uses logical layout pixels and one unit of wheel delta per notch.
    // Its default SmallChange of 5 is too slow for the application's long lists.
    internal const int MouseWheelScrollStep = 48;

    internal static void Apply(ScrollableControl control)
    {
        if (!control.AutoScroll)
            return;

        var vertical = control.VerticalScrollProperties;
        if (vertical.SmallChange != MouseWheelScrollStep)
            vertical.SmallChange = MouseWheelScrollStep;
    }
}

internal sealed class AppScrollPanel : Panel
{
    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        AppScrollBehavior.Apply(this);
    }
}

internal sealed class AppScrollFlowLayoutPanel : FlowLayoutPanel
{
    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        AppScrollBehavior.Apply(this);
    }
}
