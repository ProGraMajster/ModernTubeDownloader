using ModernTubeDownloader.Localization;

namespace ModernTubeDownloader.WebRemote;

/// <summary>Public static UI text only; no profile, queue, file or authentication state.</summary>
internal static class RemoteLocalizationCatalog
{
    internal sealed record Locale(string Code, string NativeName, string Region);
    internal sealed record Catalog(IReadOnlyList<Locale> Locales, IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Resources);
    internal static readonly Lazy<Catalog> Current = new(Create);

    private static Catalog Create()
    {
        var text = new LocalizationService("en");
        var resources = LocaleRegistry.Supported.ToDictionary(locale => locale.Code, locale =>
            (IReadOnlyDictionary<string, string>)text.GetResources(locale.Code)
                .Where(pair => pair.Key.StartsWith("RemoteWeb.", StringComparison.Ordinal))
                .ToDictionary(pair => pair.Key[10..], pair => pair.Value, StringComparer.Ordinal), StringComparer.Ordinal);
        return new(LocaleRegistry.Supported.Select(locale => new Locale(locale.Code, locale.NativeName, locale.Region)).ToArray(), resources);
    }
}
