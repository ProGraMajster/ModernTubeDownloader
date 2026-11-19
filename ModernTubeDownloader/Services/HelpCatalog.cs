using System.Text.Json;

namespace ModernTubeDownloader.Services;

internal sealed record HelpTopic(string Id, string Title, string Body);
internal sealed record HelpTopics(string Language, bool IsLanguageFallback, IReadOnlyList<HelpTopic> Topics);

internal static class HelpCatalog
{
    private static readonly Lazy<IReadOnlyDictionary<string, IReadOnlyList<HelpTopic>>> Content = new(Load);
    internal static HelpTopics ForLanguage(string language)
    {
        var selected = Content.Value.ContainsKey(language) ? language : "en";
        return new(selected, !string.Equals(selected, language, StringComparison.OrdinalIgnoreCase), Content.Value[selected]);
    }
    private static IReadOnlyDictionary<string, IReadOnlyList<HelpTopic>> Load()
    {
        using var stream = typeof(HelpCatalog).Assembly.GetManifestResourceStream("ModernTubeDownloader.Resources.HelpTopics.json")
            ?? throw new InvalidDataException("Bundled help topics are missing.");
        if (stream.Length > 128 * 1024) throw new InvalidDataException("Bundled help exceeds its size limit.");
        using var document = JsonDocument.Parse(stream, new() { MaxDepth = 8 });
        if (document.RootElement.GetProperty("schemaVersion").GetInt32() != 1) throw new InvalidDataException("Unsupported help schema.");
        var result = new Dictionary<string, IReadOnlyList<HelpTopic>>(StringComparer.OrdinalIgnoreCase);
        foreach (var language in document.RootElement.GetProperty("languages").EnumerateObject())
        {
            var topics = new List<HelpTopic>(); var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in language.Value.EnumerateArray())
            {
                var id = item.GetProperty("id").GetString()!;
                var title = item.GetProperty("title").GetString()!; var body = item.GetProperty("body").GetString()!;
                if (id is not ("start" or "downloads" or "live" or "batch" or "settings" or "diagnostics") || !ids.Add(id) ||
                    string.IsNullOrWhiteSpace(title) || title.Length > 200 || string.IsNullOrWhiteSpace(body) || body.Length > 4096)
                    throw new InvalidDataException("Invalid help topic.");
                topics.Add(new(id, title, body));
            }
            if (topics.Count != 6) throw new InvalidDataException("All six help topics are required.");
            result.Add(language.Name, topics.AsReadOnly());
        }
        if (!result.ContainsKey("en")) throw new InvalidDataException("Help requires an English fallback.");
        return result;
    }
}
