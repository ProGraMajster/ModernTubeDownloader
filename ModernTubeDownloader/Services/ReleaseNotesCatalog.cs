using System.Text.Json;
using System.Text.RegularExpressions;

namespace ModernTubeDownloader.Services;

internal sealed record ReleaseNoteSection(string Category, string Title, IReadOnlyList<string> Items);
internal sealed record InstalledReleaseNotes(string Version, string Language, bool IsLanguageFallback,
    IReadOnlyList<ReleaseNoteSection> Sections, Uri OfficialUrl)
{
    public bool IsPrerelease => Version.Contains('-');
}

/// <summary>Reviewed, exact-version application resources. No network or mutable profile input.</summary>
internal static class ReleaseNotesCatalog
{
    private const string OfficialPrefix = "https://github.com/ProGraMajster/ModernTubeDownloader/releases/tag/v";
    private static readonly Lazy<IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyList<ReleaseNoteSection>>>> Entries = new(Load);

    internal static InstalledReleaseNotes? ForInstalled(string version, string language)
    {
        var normalized = NormalizeVersion(version);
        if (normalized is null || !Entries.Value.TryGetValue(normalized, out var translations)) return null;
        var selected = translations.ContainsKey(language) ? language : "en";
        return new(normalized, selected, !string.Equals(selected, language, StringComparison.OrdinalIgnoreCase),
            translations[selected], OfficialReleaseUrl(normalized)!);
    }

    internal static bool Supports(string version) => NormalizeVersion(version) is { } normalized && Entries.Value.ContainsKey(normalized);
    internal static IReadOnlyList<string> AvailableVersions => Entries.Value.Keys.ToArray();
    internal static Uri? OfficialReleaseUrl(string version) => NormalizeVersion(version) is { } normalized && !normalized.Contains("-dev.", StringComparison.Ordinal)
        ? new(OfficialPrefix + normalized) : null;

    internal static string? NormalizeVersion(string value)
    {
        if (value.Length > 100 || !Regex.IsMatch(value, @"\A[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z]+(?:[.-][0-9A-Za-z]+)*)?(?:\+[0-9A-Za-z]+(?:[.-][0-9A-Za-z]+)*)?\z", RegexOptions.CultureInvariant)) return null;
        return value.Split('+')[0];
    }

    private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyList<ReleaseNoteSection>>> Load()
    {
        using var stream = typeof(ReleaseNotesCatalog).Assembly.GetManifestResourceStream("ModernTubeDownloader.Resources.ReleaseNotes.json")
            ?? throw new InvalidDataException("Bundled release-note catalog is missing.");
        if (stream.Length > 256 * 1024) throw new InvalidDataException("Bundled release-note catalog exceeds its size limit.");
        using var document = JsonDocument.Parse(stream, new() { MaxDepth = 16 });
        var root = document.RootElement;
        if (root.GetProperty("schemaVersion").GetInt32() != 1) throw new InvalidDataException("Unsupported release-note catalog schema.");
        var releases = root.GetProperty("releases");
        if (releases.GetArrayLength() is < 1 or > 20) throw new InvalidDataException("Invalid release-note record count.");
        var result = new Dictionary<string, IReadOnlyDictionary<string, IReadOnlyList<ReleaseNoteSection>>>(StringComparer.Ordinal);
        foreach (var release in releases.EnumerateArray())
        {
            var version = release.GetProperty("version").GetString()!;
            if (NormalizeVersion(version) != version) throw new InvalidDataException("Release-note versions must be exact and omit build metadata.");
            var translations = new Dictionary<string, IReadOnlyList<ReleaseNoteSection>>(StringComparer.OrdinalIgnoreCase);
            foreach (var language in release.GetProperty("languages").EnumerateObject())
            {
                var sections = new List<ReleaseNoteSection>(); var categories = new HashSet<string>(StringComparer.Ordinal);
                foreach (var section in language.Value.EnumerateArray())
                {
                    var category = section.GetProperty("category").GetString()!;
                    if (category is not ("new" or "improved" or "fixed" or "limitations") || !categories.Add(category))
                        throw new InvalidDataException("Invalid or duplicate release-note category.");
                    var items = section.GetProperty("items").EnumerateArray().Select(item => ReadText(item)).ToArray();
                    if (items.Length is < 1 or > 12) throw new InvalidDataException("Invalid release-note item count.");
                    sections.Add(new(category, ReadText(section.GetProperty("title")), Array.AsReadOnly(items)));
                }
                if (sections.Count != 4) throw new InvalidDataException("Every release-note translation must include all four categories.");
                translations.Add(language.Name, sections.AsReadOnly());
            }
            if (!translations.ContainsKey("en")) throw new InvalidDataException("Release notes require an English fallback.");
            result.Add(version, translations);
        }
        return result;
    }

    private static string ReadText(JsonElement value)
    {
        var text = value.GetString();
        return !string.IsNullOrWhiteSpace(text) && text.Length <= 4096 ? text : throw new InvalidDataException("Invalid release-note text.");
    }

    internal static bool ShouldShow(string current, string? acknowledged, string? observed)
    {
        if (acknowledged is not null) return CompareVersions(current, acknowledged) > 0;
        return observed is null || CompareVersions(current, observed) >= 0;
    }
    private static int CompareVersions(string left, string right)
    {
        var a = left.Split('+')[0].Split('-', 2); var b = right.Split('+')[0].Split('-', 2);
        if (!System.Version.TryParse(a[0], out var av) || !System.Version.TryParse(b[0], out var bv)) return 0;
        var core = av.CompareTo(bv); if (core != 0) return core;
        if (a.Length == 1 || b.Length == 1) return (b.Length == 1 ? 0 : 1) - (a.Length == 1 ? 0 : 1);
        var ai = a[1].Split('.'); var bi = b[1].Split('.');
        for (var i = 0; i < Math.Min(ai.Length, bi.Length); i++)
        {
            var an = long.TryParse(ai[i], out var al); var bn = long.TryParse(bi[i], out var bl);
            var part = an && bn ? al.CompareTo(bl) : an != bn ? (an ? -1 : 1) : string.CompareOrdinal(ai[i], bi[i]);
            if (part != 0) return part;
        }
        return ai.Length.CompareTo(bi.Length);
    }
}
