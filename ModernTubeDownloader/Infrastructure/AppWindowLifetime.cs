using System.Runtime.CompilerServices;
using ModernFormsNext;

namespace ModernTubeDownloader.Infrastructure;

// The pinned WindowBase is a Component, not a Control: Dispose releases its render
// surface/input bindings but does not dispose the public client control tree.
// Application windows explicitly own that tree, including thumbnail copies and timers.
internal static class AppWindowLifetime
{
    private static readonly ConditionalWeakTable<Form, Ownership> owners = new();
    internal static void Attach(Form form) => owners.GetValue(form, owner => new Ownership(owner));

    private sealed class Ownership
    {
        private bool released;
        internal Ownership(Form owner) => owner.Disposed += Release;
        private void Release(object? sender, EventArgs args)
        {
            if (released || sender is not Form owner) return;
            released = true;
            owner.Disposed -= Release;
            List<Exception>? failures = null;
            foreach (var control in owner.Controls.ToArray())
            {
                try { control.Dispose(); }
                catch (Exception failure) { (failures ??= []).Add(failure); }
            }
            if (failures is not null) throw new AggregateException("Application window control cleanup failed.", failures);
        }
    }
}
