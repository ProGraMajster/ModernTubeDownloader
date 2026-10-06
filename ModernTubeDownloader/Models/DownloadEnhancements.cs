using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace ModernTubeDownloader.Models;

public enum SubtitleSource { Manual, Automatic, Both }
public enum SubtitleFormat { Auto, Srt, Vtt, Ass }

public sealed class SubtitleOptions
{
    public bool Enabled { get; set; }
    public SubtitleSource Source { get; set; } = SubtitleSource.Manual;
    public List<string> Languages { get; set; } = [];
    public SubtitleFormat Format { get; set; } = SubtitleFormat.Auto;
    public bool Embed { get; set; }
    public bool KeepFiles { get; set; } = true;

    public SubtitleOptions Copy() => new()
    {
        Enabled = Enabled, Source = Source, Languages = [.. Languages],
        Format = Format, Embed = Embed, KeepFiles = KeepFiles
    };

    public void Validate()
    {
        if (!Enabled) return;
        if (!Enum.IsDefined(Source) || !Enum.IsDefined(Format))
            throw new ArgumentException("Invalid subtitle source or format.");
        if (Languages.Count is 0 or > 20 || Languages.Any(language =>
                !Regex.IsMatch(language, @"^[a-zA-Z]{2,8}(?:[-_][a-zA-Z0-9]{1,8})*(?:\.\*)?$", RegexOptions.CultureInvariant)))
            throw new ArgumentException("Choose 1–20 valid subtitle language codes.");
        if (!Embed && !KeepFiles)
            throw new ArgumentException("Subtitle files must be kept when embedding is disabled.");
    }
}

public sealed record SubtitleTrack(string LanguageCode, string DisplayName, bool IsAutomatic,
    IReadOnlyList<string> AvailableFormats, string SourceType);

public static class SubtitleTrackReader
{
    public static IReadOnlyList<SubtitleTrack> Read(VideoMetadata metadata)
    {
        var tracks = new List<SubtitleTrack>();
        Add(tracks, metadata.Subtitles, false);
        Add(tracks, metadata.AutomaticCaptions, true);
        return tracks;
    }

    private static void Add(List<SubtitleTrack> tracks, Dictionary<string, JsonElement> source, bool automatic)
    {
        foreach (var (code, entries) in source)
        {
            if (string.IsNullOrWhiteSpace(code) || entries.ValueKind != JsonValueKind.Array || entries.GetArrayLength() == 0)
                continue;
            var formats = entries.EnumerateArray()
                .Where(entry => entry.ValueKind == JsonValueKind.Object && entry.TryGetProperty("ext", out _))
                .Select(entry => entry.GetProperty("ext").GetString())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value!).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            tracks.Add(new SubtitleTrack(code, DisplayName(code), automatic, formats,
                automatic ? "automatic_captions" : "subtitles"));
        }
    }

    private static string DisplayName(string code)
    {
        try { return CultureInfo.GetCultureInfo(code.Replace('_', '-')).NativeName; }
        catch (CultureNotFoundException) { return code; }
    }
}

public enum SponsorBlockMode { Off, Mark, Remove }

public sealed class SponsorBlockOptions
{
    private static readonly HashSet<string> SupportedCategories = new(StringComparer.OrdinalIgnoreCase)
        { "sponsor", "intro", "outro", "selfpromo", "interaction", "preview", "music_offtopic", "filler", "hook", "poi_highlight", "chapter" };

    public SponsorBlockMode Mode { get; set; }
    public List<string> Categories { get; set; } = ["sponsor"];

    public SponsorBlockOptions Copy() => new() { Mode = Mode, Categories = [.. Categories] };

    public string? ToYtDlpCategories()
    {
        if (Mode == SponsorBlockMode.Off) return null;
        if (!Enum.IsDefined(Mode) || Categories.Count is 0 or > 12 ||
            Categories.Any(category => !SupportedCategories.Contains(category)) ||
            Mode == SponsorBlockMode.Remove && Categories.Any(category =>
                category is "poi_highlight" or "chapter"))
            throw new ArgumentException("Invalid SponsorBlock categories for the selected mode.");
        return string.Join(',', Categories.Select(category => category.ToLowerInvariant())
            .Distinct(StringComparer.Ordinal));
    }
}
