using ModernTubeDownloader.Models;

namespace ModernTubeDownloader.Services;

public static class LiveCapturePolicy
{
    // yt-dlp 2026.08.19 --help labels this feature experimental and names
    // these extractors. This is only eligibility, not a guarantee that the
    // source still exposes its earliest fragments.
    public static bool SupportsFromStart(VideoMetadata metadata) =>
        metadata.ExtractorKey is { } key &&
        (key.Equals("Youtube", StringComparison.OrdinalIgnoreCase) ||
         key.Equals("TwitchStream", StringComparison.OrdinalIgnoreCase) ||
         key.Equals("TVer", StringComparison.OrdinalIgnoreCase) ||
         key.Equals("MellowFan", StringComparison.OrdinalIgnoreCase));

    public static LiveStartPolicy EffectiveStartPolicy(VideoMetadata metadata, LiveStartPolicy requested) =>
        requested == LiveStartPolicy.FromStart && !SupportsFromStart(metadata)
            ? LiveStartPolicy.FromNow : requested;
}
