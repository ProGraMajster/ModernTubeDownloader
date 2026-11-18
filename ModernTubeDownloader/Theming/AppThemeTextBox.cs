using ModernFormsNext;

namespace ModernTubeDownloader.Theming;

/// <summary>Refreshes the native editor's own text cache after application theme bindings settle.</summary>
internal sealed class AppThemeTextBox : TextBox
{
    private bool disposed;
    public AppThemeTextBox()
    {
        ThemeManager.Current.ThemeChanged += ThemeCommitted;
        Disposed += (_, _) => { disposed = true; ThemeManager.Current.ThemeChanged -= ThemeCommitted; };
    }

    private void ThemeCommitted(object? sender, ThemeChangedEventArgs e)
    {
        if (!disposed && !Disposing)
            base.OnThemeChanged(EventArgs.Empty);
    }

    internal void RefreshBoundPresentation()
    {
        // State colors may be assigned after ThemeCommitted in the same theme
        // dispatch. Refresh the canonical editor cache only after those bindings.
        if (!disposed && !Disposing) base.OnThemeChanged(EventArgs.Empty);
    }
}
