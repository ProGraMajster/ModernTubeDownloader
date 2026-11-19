using ModernFormsNext;
using ModernFormsNext.Accessibility;

namespace ModernTubeDownloader.Shell;

internal sealed class ShellNavigationButton : Button
{
    internal bool IsCurrent { get; set; }
    protected override AccessibleObject CreateAccessibilityInstance() => new NavigationAccessibleObject(this);
    private sealed class NavigationAccessibleObject(ShellNavigationButton button) : ControlAccessibleObject(button)
    {
        public override AccessibleStates State => base.State | AccessibleStates.Selectable | (button.IsCurrent ? AccessibleStates.Selected : AccessibleStates.None);
    }
}
