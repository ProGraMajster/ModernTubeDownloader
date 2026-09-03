using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace ModernTubeDownloader.Localization;

public sealed class LocalizationService
{
    private const string FallbackLanguage = "en";
    private readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> languages;
    private string language;

    public LocalizationService(string requestedLanguage)
    {
        languages = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["en"] = Load("en"),
            ["pl"] = Load("pl")
        };
        language = Normalize(requestedLanguage);
    }

    public event EventHandler? LanguageChanged;

    public string Language => language;

    public CultureInfo Culture => language == "pl"
        ? CultureInfo.GetCultureInfo("pl-PL")
        : CultureInfo.GetCultureInfo("en-US");

    public IReadOnlyCollection<string> AvailableLanguages => languages.Keys.ToArray();

    public string this[string key] => Get(key);

    public string Get(string key, params object?[] arguments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        var value = TryGet(language, key) ?? TryGet(FallbackLanguage, key) ?? $"[{key}]";
        return arguments.Length == 0 ? value : string.Format(Culture, value, arguments);
    }

    public bool SetLanguage(string requestedLanguage)
    {
        var normalized = Normalize(requestedLanguage);
        if (string.Equals(language, normalized, StringComparison.OrdinalIgnoreCase))
            return false;

        language = normalized;
        CultureInfo.CurrentUICulture = Culture;
        LanguageChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public IReadOnlyCollection<string> GetKeys(string languageCode)
        => languages[Normalize(languageCode)].Keys.ToArray();

    private string? TryGet(string languageCode, string key)
        => languages.TryGetValue(languageCode, out var values) && values.TryGetValue(key, out var value)
            ? value
            : null;

    private string Normalize(string? requestedLanguage)
        => requestedLanguage is not null && languages.ContainsKey(requestedLanguage)
            ? requestedLanguage.ToLowerInvariant()
            : FallbackLanguage;

    private static IReadOnlyDictionary<string, string> Load(string languageCode)
    {
        var assembly = typeof(LocalizationService).Assembly;
        var suffix = $".Resources.Languages.{languageCode}.json";
        var resourceName = assembly.GetManifestResourceNames()
            .SingleOrDefault(name => name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Missing localization resource '{languageCode}'.");

        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Could not open localization resource '{resourceName}'.");
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream)
            ?? throw new InvalidOperationException($"Localization resource '{resourceName}' is empty.");
    }
}
