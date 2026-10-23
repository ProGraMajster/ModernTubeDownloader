using System.Text.Json;
using System.Text.Json.Serialization;
using ModernTubeDownloader.Infrastructure;

namespace ModernTubeDownloader.Theming;

public sealed class AppearanceProfile
{
    public int SchemaVersion { get; set; } = 2;
    public AppInterfaceMode InterfaceMode { get; set; }
    public ModernDashboardAppearance ModernDashboardShell { get; set; } = new();
    public GlobalAppearance Global { get; set; } = new();
    public TypographyPreferences Typography { get; set; } = new();
    public NavigationAppearance Navigation { get; set; } = new();
    public DownloadsAppearance Downloads { get; set; } = new();
    public LiveAppearance Live { get; set; } = new();
    public HistoryAppearance History { get; set; } = new();
    public OtherAppearance Other { get; set; } = new();
    public BatchAppearance BatchImport { get; set; } = new();
    public SettingsAppearance Settings { get; set; } = new();
    public DetailsAppearance Details { get; set; } = new();
    public static AppearanceProfile Preset(string name) => name switch
    {
        "Compact" => new() { Global = new() { Density = AppearanceDensity.Compact, SpacingPercent = 90 }, Downloads = new() { CardDensity = AppearanceDensity.Compact }, History = new() { CompactList = true }, Settings = new() { CompactCards = true, SectionSpacing = 8 } },
        "Large" => new() { Typography = new() { AppTextScale = 125 }, Global = new() { Density = AppearanceDensity.Comfortable }, Navigation = new() { Width = NavigationWidth.Wide } },
        "Minimal" => new() { Global = new() { Animations = false, CornerRadius = 4 }, Downloads = new() { ShowAuthor = false, ShowSource = false }, History = new() { ShowOutputPath = false }, Details = new() { CollapseTechnical = true } },
        "ModernDashboard" => new() { InterfaceMode = AppInterfaceMode.ModernDashboard, Global = new() { Theme = AppearanceTheme.Dark, CornerRadius = 10 } },
        "Default" => new(), _ => throw new ArgumentException("Appearance.Error.Profile")
    };
    public AppearanceProfile Copy() => AppearanceProfileStore.Validate(JsonSerializer.Serialize(this, AppearanceProfileStore.Options));
    public void ResetSection(string section)
    {
        var property = typeof(AppearanceProfile).GetProperty(section);
        if (property is null || section == nameof(SchemaVersion)) throw new ArgumentException("Appearance.Error.Profile");
        property.SetValue(this, property.GetValue(new AppearanceProfile()));
    }
}
public sealed class TypographyPreferences { public int AppTextScale { get; set; } = 100; }
public enum AppInterfaceMode { Classic, ModernDashboard }
public enum DashboardSidebarMode { Expanded, Compact, Auto }
public enum DashboardInspectorMode { Always, Auto, Hidden }
public enum DashboardWidgetsMode { All, Compact, Hidden }
public sealed class ModernDashboardAppearance
{
    public DashboardSidebarMode Sidebar { get; set; } = DashboardSidebarMode.Auto;
    public DashboardInspectorMode Inspector { get; set; } = DashboardInspectorMode.Auto;
    public DashboardWidgetsMode Widgets { get; set; }
    public AppearanceDensity QueueDensity { get; set; } = AppearanceDensity.Normal;
    public AppearanceSize Thumbnail { get; set; } = AppearanceSize.Normal;
    public bool ShowAuthor { get; set; } = true;
    public bool ShowSource { get; set; } = true;
    public bool ShowQuality { get; set; } = true;
    public bool ShowFormat { get; set; } = true;
    public bool ShowSize { get; set; } = true;
    public bool ShowSpeed { get; set; } = true;
    public bool ShowEta { get; set; } = true;
    public ActionPresentation Actions { get; set; }
    public bool Statistics { get; set; } = true;
    public bool SpeedChart { get; set; } = true;
    public bool ActiveProcesses { get; set; } = true;
}
public enum AppearanceTheme { System, Light, Dark, Custom }
public enum AppearanceDensity { Compact, Normal, Comfortable }
public enum AppearanceSize { None, Small, Normal, Large }
public enum AnimationIntensity { Reduced, Normal, Enhanced }
public enum NavigationWidth { Narrow, Normal, Wide }
public enum NavigationMode { IconAndText, Icons }
public enum ProgressPresentation { Full, Compact, Hidden }
public enum ActionPresentation { Always, Hover }
public enum DetailsTabStyle { Normal, Compact }
public sealed class GlobalAppearance
{
    public AppearanceTheme Theme { get; set; }
    public Dictionary<string, string> Colors { get; set; } = [];
    public int CornerRadius { get; set; } = 16;
    public AppearanceDensity Density { get; set; } = AppearanceDensity.Normal;
    public int SpacingPercent { get; set; } = 100;
    public AppearanceSize IconSize { get; set; } = AppearanceSize.Normal;
    public bool Animations { get; set; } = true;
    public AnimationIntensity AnimationIntensity { get; set; } = AnimationIntensity.Normal;
}
public sealed class NavigationAppearance { public NavigationWidth Width { get; set; } = NavigationWidth.Normal; public NavigationMode Mode { get; set; } }
public sealed class DownloadsAppearance
{
    public AppearanceSize ThumbnailSize { get; set; } = AppearanceSize.Normal;
    public AppearanceDensity CardDensity { get; set; } = AppearanceDensity.Normal;
    public bool ShowAuthor { get; set; } = true;
    public bool ShowSource { get; set; } = true;
    public bool ShowSpeed { get; set; } = true;
    public bool ShowEta { get; set; } = true;
    public bool ShowFileSize { get; set; } = true;
    public ProgressPresentation Progress { get; set; }
    public ActionPresentation Actions { get; set; }
    public AppearanceDensity AnalysisDensity { get; set; } = AppearanceDensity.Normal;
}
public sealed class LiveAppearance
{
    public AppearanceDensity CardDensity { get; set; } = AppearanceDensity.Normal;
    public AppearanceSize ThumbnailSize { get; set; } = AppearanceSize.None;
    public bool ShowElapsed { get; set; } = true;
    public bool ShowBytes { get; set; } = true;
    public bool ShowParts { get; set; } = true;
    public bool ShowReconnectCount { get; set; } = true;
    public bool ShowSource { get; set; } = true;
    public bool CompactWaiting { get; set; }
    public bool EmphasizeActive { get; set; } = true;
}
public sealed class HistoryAppearance
{
    public AppearanceSize ThumbnailSize { get; set; } = AppearanceSize.Normal;
    public bool CompactList { get; set; }
    public bool ShowDate { get; set; } = true;
    public bool ShowSource { get; set; } = true;
    public bool ShowOutputPath { get; set; } = true;
    public bool ShowQuality { get; set; } = true;
    public bool ShowContainer { get; set; } = true;
}
public sealed class OtherAppearance { public AppearanceDensity CardDensity { get; set; } = AppearanceDensity.Normal; }
public sealed class BatchAppearance
{
    public AppearanceDensity RowDensity { get; set; } = AppearanceDensity.Normal;
    public bool ShowUrl { get; set; } = true;
    public bool ShowSource { get; set; } = true;
    public bool ShowTitle { get; set; } = true;
    public bool ShowQuality { get; set; } = true;
    public bool ShowContainer { get; set; } = true;
    public bool Thumbnail { get; set; }
    public int UrlColumnWidth { get; set; } = 640;
    public bool StatusDetails { get; set; } = true;
    public ProgressPresentation Progress { get; set; } = ProgressPresentation.Compact;
}
public sealed class SettingsAppearance { public bool CompactCards { get; set; } public int SectionSpacing { get; set; } = 14; }
public sealed class DetailsAppearance
{
    public DetailsTabStyle Tabs { get; set; }
    public AppearanceDensity MetadataDensity { get; set; } = AppearanceDensity.Normal;
    public bool CollapseTechnical { get; set; }
    public AppearanceSize ThumbnailSize { get; set; } = AppearanceSize.None;
}
public static class AppearancePresentation
{
    public static AppearanceProfile Current { get; internal set; } = new();
    public static int ImageWidth(AppearanceSize size, int normal) => size switch { AppearanceSize.None => 0, AppearanceSize.Small => normal * 3 / 4, AppearanceSize.Large => normal * 5 / 4, _ => normal };
    public static int Spacing(int normal) => Math.Max(4, (int)Math.Round(normal * Current.Global.SpacingPercent / 100d * (Current.Global.Density == AppearanceDensity.Compact ? .8 : Current.Global.Density == AppearanceDensity.Comfortable ? 1.2 : 1)));
    public static int DensityExtra(AppearanceDensity density) => density == AppearanceDensity.Compact ? -12 : density == AppearanceDensity.Comfortable ? 24 : 0;
    public static int AnimationMilliseconds(int normal) => !Current.Global.Animations ? 0 : Current.Global.AnimationIntensity == AnimationIntensity.Reduced ? normal / 2 : Current.Global.AnimationIntensity == AnimationIntensity.Enhanced ? normal * 3 / 2 : normal;
}

public sealed class AppearanceProfileStore(string path)
{
    internal static readonly JsonSerializerOptions Options = new(JsonDefaults.Options)
    { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, MaxDepth = 10 };
    public async Task<AppearanceProfile> LoadAsync(IAppLogger logger)
    {
        try { if (!File.Exists(path)) return new(); if (new FileInfo(path).Length > 65536) throw new InvalidDataException("Appearance.Error.Profile"); return Validate(await File.ReadAllTextAsync(path)); }
        catch (Exception ex) { logger.Error("Appearance profile invalid; original preserved, defaults used.", ex); return new(); }
    }
    public Task SaveAsync(AppearanceProfile profile) => AtomicJsonFile.WriteRawJsonAsync(path, JsonSerializer.Serialize(profile, Options));
    public static AppearanceProfile Validate(string json)
    {
        if (json.Length > 65536) throw new InvalidDataException("Appearance.Error.Profile");
        using (var document = JsonDocument.Parse(json, new() { MaxDepth = 10 })) RejectDuplicates(document.RootElement);
        var value = JsonSerializer.Deserialize<AppearanceProfile>(json, Options) ?? throw new InvalidDataException("Appearance.Error.Profile");
        if (value.SchemaVersion is not (1 or 2) || !Enum.IsDefined(value.InterfaceMode) || value.ModernDashboardShell is null || value.Typography is null || value.Typography.AppTextScale is < 80 or > 200) throw new InvalidDataException("Appearance.Error.Profile");
        value.SchemaVersion = 2;
        if (value.Global is null || value.Navigation is null || value.Downloads is null || value.Live is null || value.History is null || value.Other is null || value.BatchImport is null || value.Settings is null || value.Details is null ||
            value.Global.Colors is null || value.Global.CornerRadius is < 0 or > 32 || value.Global.SpacingPercent is < 75 or > 150 || value.Settings.SectionSpacing is < 4 or > 40 || value.BatchImport.UrlColumnWidth is < 200 or > 1200)
            throw new InvalidDataException("Appearance.Error.Profile");
        foreach (var section in new object[] { value.Global, value.Navigation, value.Downloads, value.Live, value.History, value.Other, value.BatchImport, value.Details, value.ModernDashboardShell })
            foreach (var property in section.GetType().GetProperties()) if (property.PropertyType.IsEnum && !Enum.IsDefined(property.PropertyType, property.GetValue(section)!)) throw new InvalidDataException("Appearance.Error.Profile");
        var colors = new[] { "Accent", "Background", "Surface", "SurfaceSecondary", "Border", "TextPrimary", "TextSecondary" };
        foreach (var (key, color) in value.Global.Colors)
            if (!colors.Contains(key) || color is null || color.Length != 7 || color[0] != '#' || !color[1..].All(Uri.IsHexDigit)) throw new InvalidDataException("Appearance.Error.Profile");
        return value;
    }
    private static void RejectDuplicates(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object) return;
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in value.EnumerateObject()) { if (!names.Add(property.Name)) throw new InvalidDataException("Appearance.Error.Profile"); RejectDuplicates(property.Value); }
    }
}
