using ModernTubeDownloader.Models;

namespace ModernTubeDownloader.Services;

public static class ExtractorIdentityService
{
    // Friendly identities only, not a static supported-site or capability table.
    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Youtube"] = "youtube", ["YoutubeTab"] = "youtube:tab", ["YoutubePlaylist"] = "youtube:playlist",
        ["TwitchStream"] = "twitch:stream", ["TwitchVod"] = "twitch:vod", ["TwitchClips"] = "twitch:clips",
        ["InstagramStory"] = "instagram:story", ["Facebook"] = "facebook", ["Twitter"] = "twitter",
        ["KickLive"] = "kick:live", ["KickVOD"] = "kick:vod", ["KickClip"] = "kick:clips",
        ["Vimeo"] = "vimeo", ["Odysee"] = "odysee", ["LBRY"] = "lbry"
    };
    private static readonly Dictionary<string, string> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        ["youtube"] = "YouTube", ["twitch"] = "Twitch", ["tiktok"] = "TikTok", ["instagram"] = "Instagram",
        ["facebook"] = "Facebook", ["twitter"] = "X / Twitter", ["vimeo"] = "Vimeo", ["kick"] = "Kick",
        ["odysee"] = "Odysee", ["lbry"] = "Odysee / LBRY", ["vk"] = "VK"
    };
    public static string RuntimeName(string? value)
    {
        var key = value?.Trim() ?? string.Empty;
        return Aliases.GetValueOrDefault(key) ?? key;
    }
    public static SourceIdentity Identify(string? runtimeName, string? extractorKey = null)
    {
        var name = RuntimeName(string.IsNullOrWhiteSpace(runtimeName) ? extractorKey : runtimeName);
        var family = name.Split(':', 2)[0];
        // An unknown family keeps its technical name; no host-name guessing.
        return new(Names.GetValueOrDefault(family) ?? family, name, extractorKey ?? name,
            family.Equals("generic", StringComparison.OrdinalIgnoreCase));
    }
    public static bool IsPopular(string runtimeName) => Names.ContainsKey(RuntimeName(runtimeName).Split(':', 2)[0]);
    public static SourceFacts Facts(VideoMetadata metadata)
    {
        var identity = Identify(metadata.Extractor, metadata.ExtractorKey);
        var availability = MediaAvailabilityPolicy.Classify(metadata);
        var kind = metadata.MediaType is "playlist" or "multi_video" ? SourceMediaKind.Playlist : availability switch
        {
            MediaAvailabilityKind.ActiveLive => SourceMediaKind.ActiveLive,
            MediaAvailabilityKind.Upcoming => SourceMediaKind.Upcoming,
            MediaAvailabilityKind.Processing => SourceMediaKind.Processing,
            _ => metadata.WasLive == true || metadata.LiveStatus == "was_live" ? SourceMediaKind.Replay : SourceMediaKind.Video
        };
        return new(identity, kind, metadata.Availability, metadata.LiveStatus, metadata.Formats.Count,
            metadata.Subtitles.Count, metadata.AutomaticCaptions.Count, metadata.Chapters.Count,
            kind == SourceMediaKind.Playlist || metadata.AdditionalData.Any(pair => pair.Key is "playlist_id" or "playlist_title" &&
                pair.Value.ValueKind is not (System.Text.Json.JsonValueKind.Null or System.Text.Json.JsonValueKind.Undefined)));
    }
}
