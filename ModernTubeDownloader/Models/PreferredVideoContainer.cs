namespace ModernTubeDownloader.Models;

/// <summary>
/// Chooses the output container only. It does not request a video or audio codec.
/// Explicit containers are satisfied with stream copy or rejected.
/// </summary>
public enum PreferredVideoContainer
{
    Auto,
    Mp4,
    Mkv,
    WebM
}

public static class PreferredVideoContainerExtensions
{
    public static string ToProtocolValue(this PreferredVideoContainer value) => value switch
    {
        PreferredVideoContainer.Auto => "auto",
        PreferredVideoContainer.Mp4 => "mp4",
        PreferredVideoContainer.Mkv => "mkv",
        PreferredVideoContainer.WebM => "webm",
        _ => "auto"
    };

    public static bool TryParseProtocol(string? value, out PreferredVideoContainer container)
    {
        container = value?.ToLowerInvariant() switch
        {
            null or "auto" => PreferredVideoContainer.Auto,
            "mp4" => PreferredVideoContainer.Mp4,
            "mkv" => PreferredVideoContainer.Mkv,
            "webm" => PreferredVideoContainer.WebM,
            _ => (PreferredVideoContainer)(-1)
        };
        return Enum.IsDefined(container);
    }
}
