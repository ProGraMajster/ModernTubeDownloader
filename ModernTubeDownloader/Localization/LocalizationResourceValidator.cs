using System.Text;
using System.Text.Json;
using System.Collections.ObjectModel;

namespace ModernTubeDownloader.Localization;

public sealed record LocalizationIssue(string Code, string Key);

/// <summary>Validates source JSON before dictionary materialization can hide duplicate keys.</summary>
public static class LocalizationResourceValidator
{
    private static readonly HashSet<string> singleLineActions = new(StringComparer.Ordinal)
    {
        "Experience.Back", "Experience.Next", "Experience.Skip", "Experience.Finish", "Experience.Apply",
        "Organizer.Move", "Organizer.Pause", "Organizer.Resume", "Organizer.Recover", "Organizer.Undo"
    };
    public static IReadOnlyDictionary<string, string> Read(Stream stream, string resourceName)
    {
        using var document = JsonDocument.Parse(stream);
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (document.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidDataException($"Invalid localization root: {resourceName}.");
        foreach (var item in document.RootElement.EnumerateObject())
        {
            if (item.Value.ValueKind != JsonValueKind.String || !result.TryAdd(item.Name, item.Value.GetString()!))
                throw new InvalidDataException($"Duplicate or non-string localization key: {resourceName}/{item.Name}.");
        }
        var issues = Validate(result);
        if (issues.Count > 0) throw new InvalidDataException($"Invalid localization value: {resourceName}/{issues[0].Key} ({issues[0].Code}).");
        return new ReadOnlyDictionary<string, string>(result);
    }

    public static IReadOnlyList<LocalizationIssue> Validate(IReadOnlyDictionary<string, string> values, IReadOnlyDictionary<string, string>? baseline = null)
    {
        var issues = new List<LocalizationIssue>();
        foreach (var (key, value) in values)
        {
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value)) issues.Add(new("empty", key));
            if (value.Length > 16384 || value.Any(character => char.IsControl(character) && character is not '\n' and not '\r' and not '\t')) issues.Add(new("control-text", key));
            if ((key.StartsWith("Nav.", StringComparison.Ordinal) || key.StartsWith("Common.", StringComparison.Ordinal) || singleLineActions.Contains(key))
                && (value.Length > 256 || value.IndexOfAny(['\n', '\r', '\t']) >= 0)) issues.Add(new("control-text", key));
            if (value.EnumerateRunes().Any(rune => rune.Value == 0xFFFD)) issues.Add(new("unicode", key));
            if (value.Contains('\u2581') || value.Contains("&amp;", StringComparison.Ordinal) || value.Contains("&gt;", StringComparison.Ordinal) || value.Contains("&lt;", StringComparison.Ordinal)) issues.Add(new("encoding-artifact", key));
            try { CompositeFormat.Parse(value); }
            catch (FormatException) { issues.Add(new("format", key)); }
            if (baseline is not null)
            {
                if (!baseline.TryGetValue(key, out var original)) issues.Add(new("unknown-key", key));
                else if (!Indices(original).SequenceEqual(Indices(value))) issues.Add(new("placeholders", key));
            }
        }
        if (baseline is not null)
            foreach (var key in baseline.Keys.Except(values.Keys, StringComparer.Ordinal)) issues.Add(new("missing-key", key));
        return issues;
    }

    private static IEnumerable<int> Indices(string value)
    {
        // Escaped braces are literals. Unescaped argument starts retain their index;
        // CompositeFormat.Parse above performs the full grammar validation.
        var indices = new HashSet<int>();
        for (var index = 0; index < value.Length; index++)
        {
            if (value[index] != '{') continue;
            if (index + 1 < value.Length && value[index + 1] == '{') { index++; continue; }
            var start = ++index;
            while (index < value.Length && char.IsAsciiDigit(value[index])) index++;
            if (int.TryParse(value.AsSpan(start, index - start), out var number)) indices.Add(number);
        }
        return indices.Order();
    }
}
