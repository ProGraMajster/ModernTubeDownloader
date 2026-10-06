using ModernTubeDownloader.Models;

namespace ModernTubeDownloader.Services;

public enum MediaAvailabilityKind { Ready, ActiveLive, Upcoming, Processing, Private, Unavailable }

public sealed class MediaAvailabilityException(MediaAvailabilityKind kind) : InvalidOperationException($"Media is not downloadable: {kind}.")
{
    public MediaAvailabilityKind Kind { get; } = kind;
}

public static class MediaAvailabilityPolicy
{
    public static MediaAvailabilityKind Classify(VideoMetadata metadata)
    {
        var availability = metadata.Availability?.Trim();
        if (string.Equals(availability, "private", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(availability, "needs_auth", StringComparison.OrdinalIgnoreCase))
            return MediaAvailabilityKind.Private;
        if (string.Equals(availability, "unavailable", StringComparison.OrdinalIgnoreCase))
            return MediaAvailabilityKind.Unavailable;

        // yt-dlp's explicit status wins over legacy flags: was_live is a replay, not a live stream.
        return metadata.LiveStatus?.ToLowerInvariant() switch
        {
            "is_live" => MediaAvailabilityKind.ActiveLive,
            "is_upcoming" => MediaAvailabilityKind.Upcoming,
            "post_live" => MediaAvailabilityKind.Processing,
            "was_live" or "not_live" => MediaAvailabilityKind.Ready,
            _ => metadata.IsLive == true ? MediaAvailabilityKind.ActiveLive : MediaAvailabilityKind.Ready
        };
    }

    public static void EnsureDownloadable(VideoMetadata metadata)
    {
        var kind = Classify(metadata);
        if (kind != MediaAvailabilityKind.Ready)
            throw new MediaAvailabilityException(kind);
    }
}
