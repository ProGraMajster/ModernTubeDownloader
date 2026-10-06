namespace ModernTubeDownloader.Models;

public sealed record QualityPreset(string Id, string DisplayName, int? MaximumHeight = null, bool AudioOnly = false)
{
    public static readonly IReadOnlyList<QualityPreset> All =
    [
        new("best", "Best available"),
        new("2160", "2160p / 4K", 2160),
        new("1440", "1440p", 1440),
        new("1080", "1080p", 1080),
        new("720", "720p", 720),
        new("480", "480p", 480),
        new("360", "360p", 360),
        new("audio", "Audio only", AudioOnly: true)
    ];

    public static QualityPreset Find(string? id) =>
        All.FirstOrDefault(item => item.Id.Equals(id, StringComparison.OrdinalIgnoreCase)) ?? All[0];

    public override string ToString() => DisplayName;
}

public sealed record FormatSelection(
    string? VideoFormatId,
    string? AudioFormatId,
    string VideoExtension,
    string? AudioExtension,
    bool AudioOnly,
    bool RequiresMerge)
{
    // Missing extractor codec fields are not codec facts. Such a direct-media
    // selection must be inspected locally before it can become a final output.
    public bool RequiresStreamProbe { get; init; }

    public IEnumerable<string> FormatIds
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(VideoFormatId)) yield return VideoFormatId;
            if (!string.IsNullOrWhiteSpace(AudioFormatId)) yield return AudioFormatId;
        }
    }
}
