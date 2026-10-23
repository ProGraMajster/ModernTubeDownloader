using System.Drawing;
using System.Diagnostics;
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
    public const string RowSelected = "App.RowSelected";
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
    private readonly AppearanceProfileStore? profiles;
    public AppearanceProfile Profile { get; private set; } = new();
    private bool classicRecovery;
    public AppInterfaceMode InterfaceMode => classicRecovery ? AppInterfaceMode.Classic : Profile.InterfaceMode;
    public void ForceClassicForSession() { classicRecovery = true; if (initialized) ApplyCurrent(false); }

    public AppAppearanceService(SettingsService settings, IAppLogger logger, string? profileFile = null)
    {
        this.settings = settings;
        this.logger = logger;
        if (profileFile is not null) { profiles = new(profileFile); Profile = profiles.LoadAsync(logger).GetAwaiter().GetResult(); if (!File.Exists(profileFile)) Profile.Global.Theme = (AppearanceTheme)(int)settings.Current.ThemeMode; }
        else Profile.Global.Theme = (AppearanceTheme)(int)settings.Current.ThemeMode;
        if (Profile.Global.Theme != AppearanceTheme.Custom) settings.Current.ThemeMode = (AppThemeMode)(int)Profile.Global.Theme;
        systemThemeTimer = new ModernFormsNext.Timer { Interval = 2000 };
        systemThemeTimer.Tick += SystemThemeTimerTick;
    }

    public event EventHandler? ModeChanged;
    public async Task ApplyProfileAsync(AppearanceProfile profile)
    {
        var elapsed = Stopwatch.StartNew();
        Profile = profile.Copy();
        AppearancePresentation.Current = Profile;
        if (Profile.Global.Theme != AppearanceTheme.Custom) settings.Current.ThemeMode = (AppThemeMode)(int)Profile.Global.Theme;
        ApplyCurrent(animated: false); AppTypography.SetScale(Profile.Typography.AppTextScale); UpdateSystemMonitoring();
        ModeChanged?.Invoke(this, EventArgs.Empty);
        logger.Info($"Appearance UI apply completed in {elapsed.Elapsed.TotalMilliseconds:F1} ms before persistence.");
        if (profiles is not null) await profiles.SaveAsync(Profile);
    }
    public async Task SetTextScaleAsync(int percent)
    {
        var elapsed = Stopwatch.StartNew();
        if (percent is < 80 or > 200) throw new ArgumentOutOfRangeException(nameof(percent));
        Profile.Typography.AppTextScale = percent; AppTypography.SetScale(percent);
        ModeChanged?.Invoke(this, EventArgs.Empty);
        logger.Info($"Text scale UI apply completed in {elapsed.Elapsed.TotalMilliseconds:F1} ms before persistence.");
        if (profiles is not null) await profiles.SaveAsync(Profile);
    }

    public AppThemeMode Mode => settings.Current.ThemeMode;
    public ThemeVariant EffectiveVariant => effectiveVariant;

    public void Initialize()
    {
        if (initialized)
            return;

        initialized = true;
        AppearancePresentation.Current = Profile;
        // Constructing services (including worker-thread test services) must not mutate an existing UI.
        AppTypography.SetScale(Profile.Typography.AppTextScale);
        var manager = ThemeManager.Current;
        manager.Register(CreateLightTheme(), replace: true);
        manager.Register(CreateDarkTheme(), replace: true);
        ApplyCurrent(animated: false);
        UpdateSystemMonitoring();
    }

    public async Task SetModeAsync(AppThemeMode mode)
    {
        settings.Current.ThemeMode = mode;
        Profile.Global.Theme = (AppearanceTheme)(int)mode;
        AppearancePresentation.Current = Profile;
        ApplyCurrent(animated: true);
        UpdateSystemMonitoring();
        // Subscribers own native controls; publish before the asynchronous file continuation.
        ModeChanged?.Invoke(this, EventArgs.Empty);
        await settings.SaveAsync().ConfigureAwait(false);
        if (profiles is not null) await profiles.SaveAsync(Profile);
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
        var definition = InterfaceMode == AppInterfaceMode.ModernDashboard ? CreateDashboardTheme(variant) : variant == ThemeVariant.Dark ? CreateDarkTheme() : CreateLightTheme();
        if (Profile.Global.Theme == AppearanceTheme.Custom)
        {
            var mapping = new Dictionary<string, string[]> {
                ["Accent"] = [AppThemeTokens.Accent, AppThemeTokens.AccentHover, AppThemeTokens.AccentPressed, ThemeTokens.Colors.Primary.Name, ThemeTokens.Colors.PrimaryHover.Name, ThemeTokens.Colors.PrimaryPressed.Name],
                ["Background"] = [AppThemeTokens.Background, ThemeTokens.Colors.Background.Name],
                ["Surface"] = [AppThemeTokens.Surface, ThemeTokens.Colors.Surface.Name, "Legacy.ControlLowColor"],
                ["SurfaceSecondary"] = [AppThemeTokens.SurfaceSecondary, AppThemeTokens.SurfaceHover, AppThemeTokens.Navigation, AppThemeTokens.NavigationHover, AppThemeTokens.NavigationSelected, ThemeTokens.Colors.SurfaceVariant.Name, "Legacy.ControlMidColor", "Legacy.ControlHighlightLowColor"],
                ["Border"] = [AppThemeTokens.Border, ThemeTokens.Colors.Border.Name],
                ["TextPrimary"] = [AppThemeTokens.TextPrimary, AppThemeTokens.NavigationSelectedText, ThemeTokens.Colors.TextPrimary.Name, "Legacy.ForegroundColor"],
                ["TextSecondary"] = [AppThemeTokens.TextSecondary, ThemeTokens.Colors.TextSecondary.Name] };
            foreach (var (key, color) in Profile.Global.Colors) foreach (var token in mapping[key]) Set(definition, token, color);
            if (Profile.Global.Colors.TryGetValue("Accent", out var accent))
            { var c = ColorTranslator.FromHtml(accent); var foreground = ColorContrast(c, Color.White) >= ColorContrast(c, Color.Black) ? "#FFFFFF" : "#000000"; Set(definition, AppThemeTokens.AccentText, foreground); Set(definition, ThemeTokens.Colors.PrimaryText.Name, foreground); }
        }
        var result = ThemeManager.Current.Apply(definition, new ThemeApplyOptions
        {
            Transition = new ThemeTransitionOptions
            {
                // MFN color-transition frames repaint without invalidating TextBox's cached
                // shaped text. Commit Modern themes atomically so existing inputs retain
                // readable target colors; preserve Classic's established transition policy.
                Enabled = animated && Profile.Global.Animations && InterfaceMode != AppInterfaceMode.ModernDashboard,
                Duration = TimeSpan.FromMilliseconds(AppearancePresentation.AnimationMilliseconds(220)),
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

    private static ThemeDefinition CreateDashboardTheme(ThemeVariant variant)
    {
        var dark = variant == ThemeVariant.Dark;
        var theme = CreateTheme(dark ? DarkThemeId : LightThemeId, "Modern Dashboard", dark ? BuiltInThemes.DarkThemeId : BuiltInThemes.LightThemeId,
            variant, dark ? "#0D161D" : "#F2F5F8", dark ? "#121C24" : "#FAFBFD", dark ? "#1C2935" : "#E9EFF5",
            dark ? "#243746" : "#DCE8F4", dark ? "#273847" : "#CBD7E3", dark ? "#F2F6FA" : "#172637",
            dark ? "#B6C4D3" : "#4F6378", dark ? "#91A1B5" : "#53677B", "#006FEA", "#0071E6", "#075DC4",
            dark ? "#38D996" : "#087E52", dark ? "#FFB548" : "#926000", dark ? "#FF5D6C" : "#BA2438", dark ? "#48A6FF" : "#0965C7",
            dark ? "#0F1925" : "#E7EEF6", dark ? "#203249" : "#D8E5F3", dark ? "#173E6C" : "#CFE3FC");
        Set(theme, AppThemeTokens.AccentText, "#FFFFFF"); Set(theme, ThemeTokens.Colors.PrimaryText.Name, "#FFFFFF");
        Set(theme, "Legacy.ForegroundColorOnAccent", dark ? "#F2F6FA" : "#172637");
        Set(theme, "Legacy.AccentColor2", "#076DE2");
        Set(theme, AppThemeTokens.RowSelected, dark ? "#1A2B3A" : "#DDEBFA");
        Set(theme, DownloadsVisual.Background, dark ? "#10171D" : "#F2F5F8");
        Set(theme, DownloadsVisual.BackgroundEnd, dark ? "#0F171E" : "#EDF2F7");
        Set(theme, DownloadsVisual.Panel, dark ? "#151E26" : "#FAFBFD");
        Set(theme, DownloadsVisual.PanelEnd, dark ? "#161F26" : "#F4F7FA");
        Set(theme, DownloadsVisual.Selected, dark ? "#182635" : "#E4EFFB");
        Set(theme, DownloadsVisual.SelectedEnd, dark ? "#192736" : "#DDEBFA");
        Set(theme, DownloadsVisual.Border, dark ? "#263440" : "#CBD7E3");
        Set(theme, DownloadsVisual.ButtonTop, dark ? "#202934" : "#F0F4F8");
        Set(theme, DownloadsVisual.ButtonEnd, dark ? "#212B36" : "#E8EEF5");
        return theme;
    }

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
        Set(theme, AppThemeTokens.RowSelected, variant == ThemeVariant.Dark ? "#172B42" : "#DDEBFA");
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
    public static double ColorContrast(Color first, Color second)
    {
        static double Light(Color color) { static double Channel(byte b) { var c = b / 255d; return c <= .04045 ? c / 12.92 : Math.Pow((c + .055) / 1.055, 2.4); } return .2126 * Channel(color.R) + .7152 * Channel(color.G) + .0722 * Channel(color.B); }
        var a = Light(first); var b = Light(second); return (Math.Max(a, b) + .05) / (Math.Min(a, b) + .05);
    }

    public void Dispose()
    {
        systemThemeTimer.Tick -= SystemThemeTimerTick;
        systemThemeTimer.Dispose();
    }
}
