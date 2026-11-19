using System.Globalization;

namespace ModernTubeDownloader.Localization;

public sealed record AppLocale(string Code, string NativeName, string DisplayNameKey, string CultureName, string Region, string Fallback = "en")
{
    public CultureInfo Culture => CultureInfo.GetCultureInfo(CultureName);
    public string DisplayName(LocalizationService text)
    {
        var translated = text[DisplayNameKey];
        return string.Equals(translated, NativeName, StringComparison.Ordinal) ? NativeName : NativeName + " · " + translated;
    }
}

/// <summary>Canonical persisted locale IDs. Legacy en/pl remain stable; regional aliases normalize here.</summary>
public static class LocaleRegistry
{
    public static IReadOnlyList<AppLocale> Supported { get; } = Array.AsReadOnly(new AppLocale[]
    {
        new("en", "English (United States)", "Language.English", "en-US", "neutral"),
        new("pl", "Polski", "Language.Polish", "pl-PL", "PL"),
        new("de-DE", "Deutsch (Deutschland)", "Language.German", "de-DE", "DE"),
        new("fr-FR", "Français (France)", "Language.French", "fr-FR", "FR"),
        new("es-ES", "Español (España)", "Language.Spanish", "es-ES", "ES"),
        new("it-IT", "Italiano (Italia)", "Language.Italian", "it-IT", "IT"),
        new("pt-BR", "Português (Brasil)", "Language.PortugueseBrazil", "pt-BR", "BR"),
        new("nl-NL", "Nederlands (Nederland)", "Language.Dutch", "nl-NL", "NL"),
        new("cs-CZ", "Čeština (Česko)", "Language.Czech", "cs-CZ", "CZ"),
        new("sk-SK", "Slovenčina (Slovensko)", "Language.Slovak", "sk-SK", "SK"),
        new("ru-RU", "Русский (Россия)", "Language.Russian", "ru-RU", "RU"),
        new("hu-HU", "Magyar (Magyarország)", "Language.Hungarian", "hu-HU", "HU"),
        new("tr-TR", "Türkçe (Türkiye)", "Language.Turkish", "tr-TR", "TR"),
        new("ja-JP", "日本語 (日本)", "Language.Japanese", "ja-JP", "JP"),
        new("ko-KR", "한국어 (대한민국)", "Language.Korean", "ko-KR", "KR"),
        new("zh-CN", "简体中文 (中国)", "Language.ChineseSimplified", "zh-CN", "CN"),
    });

    public static string Normalize(string? code)
    {
        if (string.Equals(code, "en-US", StringComparison.OrdinalIgnoreCase)) return "en";
        if (string.Equals(code, "pl-PL", StringComparison.OrdinalIgnoreCase)) return "pl";
        return Supported.FirstOrDefault(locale => string.Equals(locale.Code, code, StringComparison.OrdinalIgnoreCase))?.Code ?? "en";
    }

    public static AppLocale Get(string? code) => Supported.Single(locale => locale.Code == Normalize(code));

    public static IReadOnlyList<AppLocale> Search(string? query, LocalizationService text)
    {
        var search = (query ?? "").Trim();
        return Supported.Where(locale => search.Length == 0 || new[] { locale.Code, locale.NativeName, text[locale.DisplayNameKey], locale.CultureName }
                .Any(value => text.Culture.CompareInfo.IndexOf(value, search, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0))
            .OrderBy(locale => locale.NativeName, StringComparer.Create(text.Culture, true)).ToArray();
    }
}
