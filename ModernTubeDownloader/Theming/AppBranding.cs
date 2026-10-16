using ModernFormsNext;
using SkiaSharp;

namespace ModernTubeDownloader.Theming;

internal static class AppBranding
{
    private const string IconResourceName = "ModernTubeDownloader.Assets.AppIcon.png";
    private const int TitleBarIconSize = 24;
    private static readonly Lazy<SKBitmap> NativeIcon = new(LoadIcon);
    private static readonly Lazy<SKBitmap> TitleBarIcon = new(LoadTitleBarIcon);

    public static string Version => typeof(AppBranding).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false).OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion ?? "";

    public static SKBitmap Icon => NativeIcon.Value;
    public static string SourceCommit => typeof(AppBranding).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyMetadataAttribute), false)
        .OfType<System.Reflection.AssemblyMetadataAttribute>().FirstOrDefault(attribute => attribute.Key == "SourceCommit")?.Value ?? "—";

    public static string ExportNotificationIcon(string dataDirectory)
    {
        var directory = System.IO.Path.Combine(dataDirectory, "branding");
        System.IO.Directory.CreateDirectory(directory);
        var path = System.IO.Path.Combine(directory, "AppIcon.png");
        if (!System.IO.File.Exists(path))
        {
            using var stream = typeof(AppBranding).Assembly.GetManifestResourceStream(IconResourceName)!;
            using var file = System.IO.File.Create(path);
            stream.CopyTo(file);
        }
        return path;
    }

    public static void Apply(Form form)
    {
        ArgumentNullException.ThrowIfNull(form);
        form.Image = NativeIcon.Value;
        form.TitleBar.Image = TitleBarIcon.Value;
    }

    private static SKBitmap LoadIcon()
    {
        using var stream = typeof(AppBranding).Assembly.GetManifestResourceStream(IconResourceName)
            ?? throw new InvalidOperationException($"Embedded application icon '{IconResourceName}' was not found.");
        return SKBitmap.Decode(stream)
            ?? throw new InvalidOperationException($"Embedded application icon '{IconResourceName}' could not be decoded.");
    }

    private static SKBitmap LoadTitleBarIcon() =>
        NativeIcon.Value.Resize(
            new SKSizeI(TitleBarIconSize, TitleBarIconSize),
            new SKSamplingOptions(SKCubicResampler.Mitchell))
        ?? throw new InvalidOperationException("The application icon could not be resized for the title bar.");
}
