using System.Runtime.CompilerServices;
using ModernFormsNext;

namespace ModernTubeDownloader.Theming;

public enum AppTextToken { BaseText, SmallText, Caption, Body, BodyStrong, Subtitle, Title, Hero }

/// <summary>Absolute logical point sizes. This class never changes DPI, RenderScaling, transforms or icon size.</summary>
public static class AppTypography
{
    private sealed record Binding(float Size, FontStyle Style);
    private static readonly ConditionalWeakTable<Control, Binding> bindings = new();
    private static readonly List<WeakReference<Control>> targets = [];
    private static readonly Dictionary<int, Action> layouts = [];
    private sealed record RowFrame(int Top, int Height, int AppliedTop, int AppliedHeight);
    private static readonly ConditionalWeakTable<Control, RowFrame> frames = new();
    private static int nextLayout;
    public static int TextScale { get; private set; } = 100;
    public static float Scale => TextScale / 100f;
    public static float LogicalSize(AppTextToken token) => token switch
    { AppTextToken.SmallText => 9.5f, AppTextToken.Caption => 10, AppTextToken.Subtitle => 15, AppTextToken.Title => 22, AppTextToken.Hero => 28, _ => 10.5f };
    public static Font Font(float baseSize, FontStyle style = FontStyle.Regular) => new("Segoe UI", baseSize * Scale, style);
    public static Font Font(AppTextToken token) => Font(LogicalSize(token), token is AppTextToken.BodyStrong or AppTextToken.Subtitle or AppTextToken.Title or AppTextToken.Hero ? FontStyle.Bold : FontStyle.Regular);
    public static void Bind(Control control, float size, FontStyle style = FontStyle.Regular)
    {
        if (!bindings.TryGetValue(control, out _)) { targets.Add(new(control)); control.Disposed += Remove; }
        bindings.Remove(control); bindings.Add(control, new(size, style)); Apply(control, new(size, style));
    }
    public static void Bind(Control control, AppTextToken token) => Bind(control, LogicalSize(token), token is AppTextToken.BodyStrong or AppTextToken.Subtitle or AppTextToken.Title or AppTextToken.Hero ? FontStyle.Bold : FontStyle.Regular);
    public static void BindDefault(Control control)
    { if (!bindings.TryGetValue(control, out _)) Bind(control, 10.5f); }
    private static void Remove(object? sender, EventArgs e) { if (sender is Control control) bindings.Remove(control); }
    private static void Apply(Control control, Binding binding)
    {
        control.Font = Font(binding.Size, binding.Style);
        foreach (var style in new[] { control.Style, control.StyleHover, control.StyleFocused, control.StylePressed, control.StyleDisabled }) style.TextFont = control.Font;
        control.Invalidate();
    }
    public static void SetScale(int percent)
    {
        if (percent is < 80 or > 200) throw new ArgumentOutOfRangeException(nameof(percent));
        TextScale = percent;
        for (var index = targets.Count - 1; index >= 0; index--)
        {
            if (!targets[index].TryGetTarget(out var control) || !bindings.TryGetValue(control, out var binding)) targets.RemoveAt(index);
            else Apply(control, binding);
        }
        foreach (var action in layouts.Values.ToArray()) action();
    }
    public static IDisposable RegisterLayout(Action action)
    { var id = ++nextLayout; layouts.Add(id, action); return new LayoutLease(id); }
    public static void RegisterLayout(Control owner, Action action)
    { var lease = RegisterLayout(action); owner.Disposed += (_, _) => lease.Dispose(); }
    /// <summary>Run after the page computes its default logical bounds. Expands vertical text rows, not DPI or icons.</summary>
    public static void ReflowRows(Control panel, bool recursive = false)
    {
        if (Scale <= 1) return;
        foreach (var child in panel.Controls.ToArray())
        {
            if (child.Dock != DockStyle.None) continue;
            var top = TextHeight(child.Top);
            var height = child is PictureBox or ProgressBar ? child.Height : TextHeight(child.Height);
            frames.Remove(child); frames.Add(child, new(child.Top, child.Height, top, height));
            child.SetBounds(child.Left, top, child.Width, height);
            if (recursive && child is Panel && child is not FlowLayoutPanel) ReflowRows(child, true);
        }
    }
    public static void UnflowRows(Control panel, bool recursive = false)
    {
        foreach (var child in panel.Controls.ToArray())
        {
            if (frames.TryGetValue(child, out var frame))
            { if (child.Top == frame.AppliedTop && child.Height == frame.AppliedHeight) child.SetBounds(child.Left, frame.Top, child.Width, frame.Height); frames.Remove(child); }
            if (recursive && child is Panel && child is not FlowLayoutPanel) UnflowRows(child, true);
        }
    }
    private sealed class LayoutLease(int id) : IDisposable { public void Dispose() => layouts.Remove(id); }
    public static int TextHeight(int logical) => (int)Math.Ceiling(logical * Math.Max(1, Scale));
}
