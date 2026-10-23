using System.Runtime.CompilerServices;
using ModernFormsNext;

namespace ModernTubeDownloader.Theming;

public enum AppTextToken { BaseText, SmallText, Caption, Body, BodyStrong, Subtitle, Title, Hero }

/// <summary>Absolute logical point sizes. This class never changes DPI, RenderScaling, transforms or icon size.</summary>
public static class AppTypography
{
    private static readonly SkiaSharp.SKTypeface semibold = SkiaSharp.SKTypeface.FromFamilyName("Segoe UI", new SkiaSharp.SKFontStyle(600, 5, SkiaSharp.SKFontStyleSlant.Upright));
    private sealed record Binding(float Size, FontStyle Style, bool Semibold = false);
    private static readonly ConditionalWeakTable<Control, Binding> bindings = new();
    private static readonly ConditionalWeakTable<Control, int?[]> classicStateSizes = new();
    private sealed record Presentation(bool Dashboard);
    private static readonly ConditionalWeakTable<Control, Presentation> presentationRoots = new();
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
    public static void Bind(Control control, float size, FontStyle style = FontStyle.Regular) => BindRecipe(control, new(size, style));
    private static void BindRecipe(Control control, Binding binding)
    {
        if (!bindings.TryGetValue(control, out _)) { targets.Add(new(control)); control.Disposed += Remove; control.ParentChanged += Reapply; }
        bindings.Remove(control); bindings.Add(control, binding); Apply(control, binding);
    }
    public static void Bind(Control control, AppTextToken token) => Bind(control, LogicalSize(token), token is AppTextToken.BodyStrong or AppTextToken.Subtitle or AppTextToken.Title or AppTextToken.Hero ? FontStyle.Bold : FontStyle.Regular);
    internal static void BindSemibold(Control control, float size) => BindRecipe(control, new(size, FontStyle.Regular, Semibold: true));
    // MFN infers Bold from a weight-600 typeface, then TextMeasurer promotes it to
    // 700. These Downloads labels explicitly request 600, using the same public
    // Unicode painter/measurement path with no extra Bold flag.
    internal static FontStyle TextStyle(Control control) => IsDashboard(control) && bindings.TryGetValue(control, out var binding) && binding.Semibold
        ? FontStyle.Regular : control.CurrentStyle.GetFontStyle();
    internal static bool UsesSemibold(Control control) => IsDashboard(control) && bindings.TryGetValue(control, out var binding) && binding.Semibold;
    public static void BindDefault(Control control)
    { if (!bindings.TryGetValue(control, out _)) Bind(control, 10.5f); }
    private static void Reapply(object? sender, EventArgs e) { if (sender is Control c && bindings.TryGetValue(c, out var binding)) Apply(c, binding); }
    private static void Remove(object? sender, EventArgs e) { if (sender is Control control) { control.ParentChanged -= Reapply; bindings.Remove(control); } }
    private static void Apply(Control control, Binding binding)
    {
        var factor = 1f;
        for (Control? root = control; root is not null; root = root.Parent)
            if (presentationRoots.TryGetValue(root, out var presentation)) { factor = presentation.Dashboard ? 1.42f : 1; break; }
        var styles = new[] { control.Style, control.StyleHover, control.StyleFocused, control.StylePressed, control.StyleDisabled };
        var dashboard = IsDashboard(control);
        if (dashboard) classicStateSizes.GetValue(control, _ => styles.Select(s => s.FontSize).ToArray());
        // MFN exposes effective integer font sizes. Passing the fractional recipe again
        // would raise FontChanged on every refresh, although no rendered metric changed.
        var font = new Font("Segoe UI", Math.Max(1, (int)Math.Round(binding.Size * factor * Scale)), binding.Style);
        var changed = !control.Font.Equals(font);
        control.Font = font;
        classicStateSizes.TryGetValue(control, out var savedSizes);
        for (var index = 0; index < styles.Length; index++)
        {
            if (binding.Semibold)
            {
                if (!ReferenceEquals(styles[index].Font, semibold)) { styles[index].Font = semibold; changed = true; }
            }
            else if (!Equals(styles[index].TextFont, font)) { styles[index].TextFont = font; changed = true; }
            var size = dashboard ? (int?)font.SizeInPoints : index > 0 && savedSizes is not null ? savedSizes[index] : styles[index].FontSize;
            if (styles[index].FontSize != size) { styles[index].FontSize = size; changed = true; }
        }
        if (!dashboard) classicStateSizes.Remove(control);
        if (changed) control.Invalidate();
    }
    internal static bool IsDashboard(Control control)
    {
        for (Control? root = control; root is not null; root = root.Parent)
            if (presentationRoots.TryGetValue(root, out var p)) return p.Dashboard;
        return false;
    }
    internal static Action CaptureRecipes(Control root)
    {
        var recipes = new List<(Control Target, Binding Recipe)>();
        void Visit(Control c) { if (bindings.TryGetValue(c, out var recipe)) recipes.Add((c, recipe)); foreach (var child in c.Controls) Visit(child); }
        Visit(root);
        return () => { foreach (var (target, recipe) in recipes) if (bindings.TryGetValue(target, out _)) BindRecipe(target, recipe); };
    }
    internal static void SetDashboardPresentation(Control root, bool dashboard)
    {
        presentationRoots.Remove(root); presentationRoots.Add(root, new(dashboard));
        RefreshPresentation(root);
    }
    private static void RefreshPresentation(Control root)
    {
        if (bindings.TryGetValue(root, out var binding)) Apply(root, binding);
        foreach (var child in root.Controls) RefreshPresentation(child);
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
