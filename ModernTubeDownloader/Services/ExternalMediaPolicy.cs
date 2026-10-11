using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Models;

namespace ModernTubeDownloader.Services;

/// <summary>Metadata, not a browser's URL guess, determines whether a request may enter the VOD queue.</summary>
public static class ExternalMediaPolicy
{
    public static ExternalMediaRoute Route(ExternalMediaRequest request, SourceCheckResult result)
    {
        if (request.Intent == ExternalMediaIntent.Open) return ExternalMediaRoute.Open;
        if (result.OpenLive) return ExternalMediaRoute.LiveConfirmation;
        if (result.MessageKey is not null || result.AuthenticationMayBeRequired || result.Metadata is null) return ExternalMediaRoute.Error;
        if (result.Playlist is not null) return ExternalMediaRoute.PlaylistSelection;
        return request.AutoQueue && MediaAvailabilityPolicy.Classify(result.Metadata) == MediaAvailabilityKind.Ready
            ? ExternalMediaRoute.QuickQueue : ExternalMediaRoute.VideoPreview;
    }
}

public enum ExternalMediaRoute { Open, LiveConfirmation, PlaylistSelection, VideoPreview, QuickQueue, Error }
