using System.Drawing;
using ModernFormsNext;
using ModernFormsNext.Drawing;
using ModernFormsNext.WindowKit.Backend;
using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Services;
using ModernTubeDownloader.Settings;

namespace ModernTubeDownloader.Theming;

public static class AppThemeTokens
{
    public const string Background = "App.Background";
    public const string Surface = "App.Surface";
    public const string SurfaceSecondary = "App.SurfaceSecondary";
    public const string SurfaceHover = "App.SurfaceHover";
    public const string Border = "App.Border";
    public const string TextPrimary = "App.TextPrimary";
    public const string TextSecondary = "App.TextSecondary";
    public const string TextDisabled = "App.TextDisabled";
    public const string Accent = "App.Accent";
    public const string AccentHover = "App.AccentHover";
    public const string AccentPressed = "App.AccentPressed";
    public const string AccentText = "App.AccentText";
    public const string Success = "App.Success";
    public const string Warning = "App.Warning";
    public const string Error = "App.Error";
    public const string Info = "App.Info";
    public const string Navigation = "App.Navigation";
    public const string NavigationHover = "App.NavigationHover";
    public const string NavigationSelected = "App.NavigationSelected";
    public const string NavigationSelectedText = "App.NavigationSelectedText";

    public static string BrushResource(string token)
        => ThemeResourceKeys.Create(ThemeTokenCategory.Brush, token);
}

public sealed class AppAppearanceService : IDisposable
{
    private const string LightThemeId = "moderntubedownloader.light";
    private const string DarkThemeId = "moderntubedownloader.dark";
    private readonly SettingsService settings;
    private readonly IAppLogger logger;
    private readonly ModernFormsNext.Timer systemThemeTimer;
    private ThemeVariant effectiveVariant;
    private bool initialized;

    public AppAppearanceService(SettingsService settings, IAppLogger logger)
    {
        this.settings = settings;
        this.logger = logger;
        systemThemeTimer = new ModernFormsNext.Timer { Interval = 2000 };
        systemThemeTimer.Tick += SystemThemeTimerTick;
    }

    public event EventHandler? ModeChanged;

    public AppThemeMode Mode => settings.Current.ThemeMode;
    public ThemeVariant EffectiveVariant => effectiveVariant;

    public void Initialize()
    {
        if (initialized)
            return;

        initialized = true;
        var manager = ThemeManager.Current;
        manager.Register(CreateLightTheme(), replace: true);
        manager.Register(CreateDarkTheme(), replace: true);
        ApplyCurrent(animated: false);
        UpdateSystemMonitoring();
    }

    public async Task SetModeAsync(AppThemeMode mode)
    {
        settings.Current.ThemeMode = mode;
        ApplyCurrent(animated: true);
        UpdateSystemMonitoring();
        await settings.SaveAsync().ConfigureAwait(false);
        ModeChanged?.Invoke(this, EventArgs.Empty);
    }

    public void RefreshSystemTheme()
    {
        if (Mode != AppThemeMode.System)
            return;

        var resolved = ResolveSystemVariant();
        if (resolved != effectiveVariant)
            Apply(resolved, animated: true);
    }

    private void ApplyCurrent(bool animated)
    {
        var variant = Mode switch
        {
            AppThemeMode.Light => ThemeVariant.Light,
            AppThemeMode.Dark => ThemeVariant.Dark,
            _ => ResolveSystemVariant()
        };
        Apply(variant, animated);
    }

    private void Apply(ThemeVariant variant, bool animated)
    {
        var definition = variant == ThemeVariant.Dark ? CreateDarkTheme() : CreateLightTheme();
        var result = ThemeManager.Current.Apply(definition, new ThemeApplyOptions
        {
            Transition = new ThemeTransitionOptions
            {
                Enabled = animated,
                Duration = TimeSpan.FromMilliseconds(220),
                Easing = ThemeEasing.EaseInOut,
                RespectReducedMotion = true
            }
        });

        if (result.Status == ThemeApplyStatus.Applied)
            effectiveVariant = variant;
        else
            logger.Warning($"Theme '{definition.Id}' could not be applied: {result.Status}.");
    }

    private static ThemeVariant ResolveSystemVariant()
        => PlatformServiceRegistry.GetService<IPlatformThemeSettings>()?.GetPreferredVariant() switch
        {
            PlatformColorScheme.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Light
        };

    private void UpdateSystemMonitoring()
    {
        if (Mode == AppThemeMode.System)
            systemThemeTimer.Start();
        else
            systemThemeTimer.Stop();
    }

    private void SystemThemeTimerTick(object? sender, EventArgs e) => RefreshSystemTheme();

    private static ThemeDefinition CreateLightTheme()
        => CreateTheme(
            LightThemeId,
            "ModernTubeDownloader Light",
            BuiltInThemes.LightThemeId,
            ThemeVariant.Light,
            background: "#F5F7FB",
            surface: "#FFFFFF",
            surfaceSecondary: "#EEF2F8",
            surfaceHover: "#E5ECF6",
            border: "#DDE3EC",
            textPrimary: "#172033",
            textSecondary: "#637083",
            textDisabled: "#626F80",
            accent: "#3867E8",
            accentHover: "#2E59CF",
            accentPressed: "#2448AC",
            success: "#16845B",
            warning: "#B66B00",
            error: "#C93C4A",
            info: "#2877C7",
            navigation: "#EBF0F8",
            navigationHover: "#DFE7F3",
            navigationSelected: "#D7E2FF");

    private static ThemeDefinition CreateDarkTheme()
        => CreateTheme(
            DarkThemeId,
            "ModernTubeDownloader Dark",
            BuiltInThemes.DarkThemeId,
            ThemeVariant.Dark,
            background: "#101521",
            surface: "#18202E",
            surfaceSecondary: "#222C3C",
            surfaceHover: "#2A3649",
            border: "#303B4D",
            textPrimary: "#F2F5FA",
            textSecondary: "#A9B4C5",
            textDisabled: "#8D9BAE",
            accent: "#7396FF",
            accentHover: "#8BA8FF",
            accentPressed: "#5E80E8",
            success: "#4DCB91",
            warning: "#E6A84A",
            error: "#FF7782",
            info: "#6DB5F5",
            navigation: "#131B28",
            navigationHover: "#202B3C",
            navigationSelected: "#293A63");

    private static ThemeDefinition CreateTheme(
        string id,
        string name,
        string baseTheme,
        ThemeVariant variant,
        string background,
        string surface,
        string surfaceSecondary,
        string surfaceHover,
        string border,
        string textPrimary,
        string textSecondary,
        string textDisabled,
        string accent,
        string accentHover,
        string accentPressed,
        string success,
        string warning,
        string error,
        string info,
        string navigation,
        string navigationHover,
        string navigationSelected)
    {
        var theme = new ThemeDefinition(id, name)
        {
            BaseTheme = baseTheme,
            Variant = variant,
            Author = "ModernTubeDownloader"
        };
        var accentText = variant == ThemeVariant.Dark ? background : "#FFFFFF";

        Set(theme, ThemeTokens.Colors.Background.Name, background);
        Set(theme, ThemeTokens.Colors.Surface.Name, surface);
        Set(theme, ThemeTokens.Colors.SurfaceVariant.Name, surfaceSecondary);
        Set(theme, ThemeTokens.Colors.TextPrimary.Name, textPrimary);
        Set(theme, ThemeTokens.Colors.TextSecondary.Name, textSecondary);
        Set(theme, ThemeTokens.Colors.TextDisabled.Name, textDisabled);
        Set(theme, ThemeTokens.Colors.Border.Name, border);
        Set(theme, ThemeTokens.Colors.Divider.Name, border);
        Set(theme, ThemeTokens.Colors.Primary.Name, accent);
        Set(theme, ThemeTokens.Colors.PrimaryHover.Name, accentHover);
        Set(theme, ThemeTokens.Colors.PrimaryPressed.Name, accentPressed);
        Set(theme, ThemeTokens.Colors.PrimaryText.Name, accentText);
        Set(theme, ThemeTokens.Colors.Secondary.Name, surfaceSecondary);
        Set(theme, ThemeTokens.Colors.Accent.Name, accent);
        Set(theme, ThemeTokens.Colors.Success.Name, success);
        Set(theme, ThemeTokens.Colors.Warning.Name, warning);
        Set(theme, ThemeTokens.Colors.Error.Name, error);
        Set(theme, ThemeTokens.Colors.Info.Name, info);

        // MFN's ComboBox popup contains a legacy ListBox. Its inherited Legacy.*
        // palette takes precedence over the semantic colors above during projection.
        Set(theme, "Legacy.ControlLowColor", surface);
        Set(theme, "Legacy.ControlMidColor", surfaceSecondary);
        Set(theme, "Legacy.ControlHighlightLowColor", surfaceHover);
        Set(theme, "Legacy.ForegroundColor", textPrimary);
        // TextBox placeholders and disabled control renderers consume this legacy value
        // before the semantic TextDisabled token when a base theme defines both.
        Set(theme, "Legacy.ForegroundDisabledColor", textDisabled);

        Set(theme, AppThemeTokens.Background, background);
        Set(theme, AppThemeTokens.Surface, surface);
        Set(theme, AppThemeTokens.SurfaceSecondary, surfaceSecondary);
        Set(theme, AppThemeTokens.SurfaceHover, surfaceHover);
        Set(theme, AppThemeTokens.Border, border);
        Set(theme, AppThemeTokens.TextPrimary, textPrimary);
        Set(theme, AppThemeTokens.TextSecondary, textSecondary);
        Set(theme, AppThemeTokens.TextDisabled, textDisabled);
        Set(theme, AppThemeTokens.Accent, accent);
        Set(theme, AppThemeTokens.AccentHover, accentHover);
        Set(theme, AppThemeTokens.AccentPressed, accentPressed);
        Set(theme, AppThemeTokens.AccentText, accentText);
        Set(theme, AppThemeTokens.Success, success);
        Set(theme, AppThemeTokens.Warning, warning);
        Set(theme, AppThemeTokens.Error, error);
        Set(theme, AppThemeTokens.Info, info);
        Set(theme, AppThemeTokens.Navigation, navigation);
        Set(theme, AppThemeTokens.NavigationHover, navigationHover);
        Set(theme, AppThemeTokens.NavigationSelected, navigationSelected);
        Set(theme, AppThemeTokens.NavigationSelectedText, variant == ThemeVariant.Dark ? "#FFFFFF" : textPrimary);

        theme.Corners["Card"] = 16;
        theme.Corners["Button"] = 10;
        theme.Corners["Input"] = 10;
        theme.Spacing["Page"] = 24;
        theme.Spacing["Section"] = 16;
        return theme;
    }

    private static void Set(ThemeDefinition theme, string key, string hex)
    {
        var color = ColorTranslator.FromHtml(hex);
        theme.Colors[key] = color;
        theme.Brushes[key] = new SolidColorBrush(color);
    }

    public void Dispose()
    {
        systemThemeTimer.Tick -= SystemThemeTimerTick;
        systemThemeTimer.Dispose();
    }
}
