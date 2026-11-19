using ModernFormsNext;

namespace ModernTubeDownloader.Infrastructure;

// One canonical root is retained throughout the change. Prepare its theme, fonts,
// chrome and final bounds off-window, then publish once. Never alter UIA providers.
internal sealed class PresentationUpdate : IDisposable
{
    private readonly Control root, parent;
    private readonly int index;
    private bool disposed;
    private PresentationUpdate(Control root, Control parent)
    {
        this.root = root; this.parent = parent; index = parent.Controls.GetChildIndex(root);
        parent.SuspendLayout();
        try { parent.Controls.Remove(root); }
        catch { parent.ResumeLayout(true); throw; }
    }
    internal static IDisposable? Begin(Control root) => root.Parent is { } parent ? new PresentationUpdate(root, parent) : null;
    public void Dispose()
    {
        if (disposed) return; disposed = true;
        try
        {
            // Resume the parent first so caption-height changes settle its viewport.
            parent.ResumeLayout(true);
            root.Bounds = parent.DisplayRectangle; root.PerformLayout();
        }
        finally
        {
            parent.Controls.Add(root);
            parent.Controls.SetChildIndex(root, Math.Min(index, parent.Controls.Count - 1));
        }
    }
}
