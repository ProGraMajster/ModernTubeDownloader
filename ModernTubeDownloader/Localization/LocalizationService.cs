using System.Globalization;
using System.Collections.Concurrent;

namespace ModernTubeDownloader.Localization;

public sealed class LocalizationService
{
    private const string FallbackLanguage = "en";
    private static readonly ConcurrentDictionary<string, Lazy<IReadOnlyDictionary<string, string>>> languages = new(StringComparer.OrdinalIgnoreCase);
    private string language;

    public LocalizationService(string requestedLanguage)
    {
        language = LocaleRegistry.Normalize(requestedLanguage);
        _ = Values(language); _ = Values(FallbackLanguage);
    }

    public event EventHandler? LanguageChanged;

    public string Language => language;

    public CultureInfo Culture => LocaleRegistry.Get(language).Culture;

    public IReadOnlyCollection<string> AvailableLanguages => LocaleRegistry.Supported.Select(locale => locale.Code).ToArray();

    public string this[string key] => Get(key);

    public string Get(string key, params object?[] arguments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        var value = TryGet(language, key) ?? TryGet(FallbackLanguage, key) ?? $"[{key}]";
        return arguments.Length == 0 ? value : string.Format(Culture, value, arguments);
    }

    public bool SetLanguage(string requestedLanguage)
    {
        var normalized = LocaleRegistry.Normalize(requestedLanguage);
        _ = Values(normalized);
        if (string.Equals(language, normalized, StringComparison.OrdinalIgnoreCase))
            return false;

        language = normalized;
        CultureInfo.CurrentUICulture = Culture;
        LanguageChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public IReadOnlyCollection<string> GetKeys(string languageCode)
        => Values(LocaleRegistry.Normalize(languageCode)).Keys.ToArray();

    public IReadOnlyDictionary<string, string> GetResources(string languageCode) => Values(LocaleRegistry.Normalize(languageCode));

    private string? TryGet(string languageCode, string key)
        => Values(languageCode).TryGetValue(key, out var value)
            ? value
            : null;

    private static IReadOnlyDictionary<string, string> Values(string code)
        => languages.GetOrAdd(code, static locale => new(() => Load(locale), LazyThreadSafetyMode.ExecutionAndPublication)).Value;

    private static IReadOnlyDictionary<string, string> Load(string languageCode)
    {
        var assembly = typeof(LocalizationService).Assembly;
        var suffix = $".Resources.Languages.{languageCode}.json";
        var resourceName = assembly.GetManifestResourceNames()
            .SingleOrDefault(name => name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Missing localization resource '{languageCode}'.");

        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Could not open localization resource '{resourceName}'.");
        return LocalizationResourceValidator.Read(stream, resourceName);
    }
}
