using System.Runtime.CompilerServices;
using ModernFormsNext;
using ModernFormsNext.Animations;
using SkiaSharp;

namespace ModernTubeDownloader.Theming;

internal static class AppUi
{
    private enum ButtonVariant
    {
        Primary,
        Secondary
    }

    private static readonly ConditionalWeakTable<Control, ThemeBindingState> ThemeBindings = new();

    public static void BindBackground(Control control, string token = AppThemeTokens.Background)
    {
        var state = ThemeBindings.GetValue(control, static target => new ThemeBindingState(target));
        state.BackgroundToken = token;
        state.Refresh();
    }

    public static void BindForeground(Control control, string token = AppThemeTokens.TextPrimary)
    {
        var state = ThemeBindings.GetValue(control, static target => new ThemeBindingState(target));
        state.ForegroundToken = token;
        state.Refresh();
    }

    public static void Surface(Control control, bool secondary = false)
    {
        BindBackground(control, secondary ? AppThemeTokens.SurfaceSecondary : AppThemeTokens.Surface);
        BindForeground(control);
    }

    public static void Card(Control panel, bool secondary = false)
    {
        Surface(panel, secondary);
        panel.Style.Border.Radius = 16;
        panel.Style.Border.Width = 0;
    }

    public static void Primary(Button button)
    {
        button.Style.Border.Radius = 10;
        button.Style.Border.Width = 0;
        button.Font = new Font("Segoe UI", 10.5f, FontStyle.Bold);
        button.TextAlign = ContentAlignment.MiddleCenter;
        BindButtonStates(button, ButtonVariant.Primary);
        button.Ripple = new RippleEffect { Enabled = true };
        button.PressEffect = new PressScaleEffect { Enabled = true, PressedScale = 0.97f };
    }

    public static void Secondary(Button button)
    {
        button.Style.Border.Radius = 10;
        button.Style.Border.Width = 0;
        button.Font = new Font("Segoe UI", 10.5f);
        button.TextAlign = ContentAlignment.MiddleCenter;
        BindButtonStates(button, ButtonVariant.Secondary);
        button.Ripple = new RippleEffect { Enabled = true };
        button.PressEffect = new PressScaleEffect { Enabled = true, PressedScale = 0.97f };
    }

    public static void NavigationButton(Button button, bool selected)
    {
        button.ClearResourceReference(nameof(Control.BackgroundBrush));
        button.ClearResourceReference(nameof(Control.TextBrush));
        button.BackgroundBrush = null;
        button.TextBrush = null;
        button.Style.BackgroundColor = GetColor(selected ? AppThemeTokens.NavigationSelected : AppThemeTokens.Navigation);
        button.Style.ForegroundColor = GetColor(selected ? AppThemeTokens.AccentText : AppThemeTokens.TextPrimary);
        button.StyleHover.BackgroundColor = GetColor(selected ? AppThemeTokens.NavigationSelected : AppThemeTokens.NavigationHover);
        button.StyleHover.ForegroundColor = GetColor(selected ? AppThemeTokens.AccentText : AppThemeTokens.TextPrimary);
        button.StyleFocused.BackgroundColor = GetColor(selected ? AppThemeTokens.NavigationSelected : AppThemeTokens.NavigationHover);
        button.StyleFocused.ForegroundColor = GetColor(selected ? AppThemeTokens.AccentText : AppThemeTokens.TextPrimary);
        button.StylePressed.BackgroundColor = GetColor(AppThemeTokens.NavigationSelected);
        button.StylePressed.ForegroundColor = GetColor(AppThemeTokens.AccentText);
        button.Style.Border.Radius = 10;
        button.Style.Border.Width = 0;
        button.Ripple ??= new RippleEffect { Enabled = true };
        button.PressEffect ??= new PressScaleEffect { Enabled = true, PressedScale = 0.98f };
        button.Invalidate();
    }

    public static void Input(Control control)
    {
        Surface(control);
        control.Style.Border.Radius = 10;
        control.Font = new Font("Segoe UI", 10.5f);
    }

    public static Label Heading(string text, float size = 22f)
    {
        var label = new Label
        {
            Text = text,
            Font = new Font("Segoe UI", size, FontStyle.Bold),
            AutoEllipsis = false
        };
        label.Style.BackgroundColor = SKColors.Transparent;
        BindForeground(label);
        return label;
    }

    public static Label Muted(string text = "")
    {
        var label = new Label
        {
            Text = text,
            AutoEllipsis = false,
            Font = new Font("Segoe UI", 10.5f)
        };
        label.Style.BackgroundColor = SKColors.Transparent;
        BindForeground(label, AppThemeTokens.TextSecondary);
        return label;
    }

    internal static SKColor GetColor(string token)
    {
        if (ThemeManager.Current.ActiveSnapshot?.Colors.TryGetValue(token, out var color) == true)
            return new SKColor(color.R, color.G, color.B, color.A);
        return SKColors.Transparent;
    }

    private static void BindButtonStates(Button button, ButtonVariant variant)
    {
        var state = ThemeBindings.GetValue(button, static target => new ThemeBindingState(target));
        state.BackgroundToken = null;
        state.ForegroundToken = null;
        state.ButtonVariant = variant;
        button.ClearResourceReference(nameof(Control.BackgroundBrush));
        button.ClearResourceReference(nameof(Control.TextBrush));
        button.BackgroundBrush = null;
        button.TextBrush = null;
        state.Refresh();
    }

    private sealed class ThemeBindingState
    {
        private readonly WeakReference<Control> target;
        private readonly EventHandler<ThemeChangedEventArgs> handler;

        public ThemeBindingState(Control control)
        {
            target = new WeakReference<Control>(control);
            handler = ThemeChanged;
            ThemeManager.Current.ThemeChanged += handler;
        }

        public string? BackgroundToken { get; set; }
        public string? ForegroundToken { get; set; }
        public ButtonVariant? ButtonVariant { get; set; }

        public void Refresh()
        {
            if (!target.TryGetTarget(out var control))
                return;
            if (BackgroundToken is not null)
                control.SetResourceReference(nameof(Control.BackgroundBrush), AppThemeTokens.BrushResource(BackgroundToken));
            if (ForegroundToken is not null)
                control.SetResourceReference(nameof(Control.TextBrush), AppThemeTokens.BrushResource(ForegroundToken));
            if (control is Button button && ButtonVariant is { } variant)
                RefreshButtonStates(button, variant);
            control.Invalidate();
        }

        private static void RefreshButtonStates(Button button, ButtonVariant variant)
        {
            if (variant == AppUi.ButtonVariant.Primary)
            {
                ApplyButtonState(button, button.Style, AppThemeTokens.Accent, AppThemeTokens.AccentText);
                ApplyButtonState(button, button.StyleHover, AppThemeTokens.AccentHover, AppThemeTokens.AccentText);
                ApplyButtonState(button, button.StyleFocused, AppThemeTokens.AccentHover, AppThemeTokens.AccentText);
                ApplyButtonState(button, button.StylePressed, AppThemeTokens.AccentPressed, AppThemeTokens.AccentText);
                return;
            }

            ApplyButtonState(button, button.Style, AppThemeTokens.SurfaceSecondary, AppThemeTokens.TextPrimary);
            ApplyButtonState(button, button.StyleHover, AppThemeTokens.NavigationHover, AppThemeTokens.TextPrimary);
            ApplyButtonState(button, button.StyleFocused, AppThemeTokens.NavigationHover, AppThemeTokens.TextPrimary);
            ApplyButtonState(button, button.StylePressed, AppThemeTokens.NavigationSelected, AppThemeTokens.AccentText);
        }

        private static void ApplyButtonState(Button button, ControlStyle style, string backgroundToken, string foregroundToken)
        {
            style.BackgroundBrush = null;
            style.ForegroundBrush = null;
            style.BackgroundColor = GetColor(backgroundToken);
            style.ForegroundColor = GetColor(foregroundToken);
            style.Border.Radius = 10;
            style.Border.Width = 0;
            style.TextFont = button.Font;
        }

        private void ThemeChanged(object? sender, ThemeChangedEventArgs e)
        {
            if (target.TryGetTarget(out _))
            {
                Refresh();
                return;
            }
            ThemeManager.Current.ThemeChanged -= handler;
        }
    }
}
