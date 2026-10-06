using ModernTubeDownloader.Models;

namespace ModernTubeDownloader.Services;

public static class FormatSelector
{
    public static FormatSelection Select(
        VideoMetadata metadata,
        QualityPreset preset,
        PreferredVideoContainer preferredContainer = PreferredVideoContainer.Auto)
        => SelectCore(metadata, preset, preferredContainer, allowActiveLive: false);

    public static FormatSelection SelectLive(
        VideoMetadata metadata,
        QualityPreset preset,
        PreferredVideoContainer preferredContainer = PreferredVideoContainer.Auto)
    {
        if (MediaAvailabilityPolicy.Classify(metadata) != MediaAvailabilityKind.ActiveLive)
            throw new MediaAvailabilityException(MediaAvailabilityPolicy.Classify(metadata));
        return SelectCore(metadata, preset, preferredContainer, allowActiveLive: true);
    }

    private static FormatSelection SelectCore(
        VideoMetadata metadata,
        QualityPreset preset,
        PreferredVideoContainer preferredContainer,
        bool allowActiveLive)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(preset);
        if (!allowActiveLive)
            MediaAvailabilityPolicy.EnsureDownloadable(metadata);

        if (preset.AudioOnly)
        {
            var audioOnlyFormat = SelectAudio(metadata.Formats, PreferredVideoContainer.Auto, videoExtension: null)
                ?? throw new InvalidOperationException("No downloadable audio format is available.");
            return new FormatSelection(null, audioOnlyFormat.Id, string.Empty, audioOnlyFormat.Extension, AudioOnly: true, RequiresMerge: false);
        }

        var videos = metadata.Formats.Where(format =>
            format.HasVideo && IsVideoCompatible(format, preferredContainer) &&
            (!format.HasAudio || IsAudioCompatible(format, preferredContainer)));
        if (preset.MaximumHeight is { } maximumHeight)
            videos = videos.Where(format => format.Height is null || format.Height <= maximumHeight);

        var rankedVideos = videos
            .OrderByDescending(format => format.Height ?? 0)
            .ThenByDescending(format => format.Fps ?? 0)
            .ThenByDescending(format => format.VideoBitrateKbps ?? format.TotalBitrateKbps ?? 0)
            .ThenByDescending(format => format.FileSize ?? format.ApproximateFileSize ?? 0)
            .ToArray();
        foreach (var video in rankedVideos)
        {
            if (video.HasAudio)
                return new FormatSelection(video.Id, null, video.Extension, null, AudioOnly: false, RequiresMerge: false);
            var audio = SelectAudio(metadata.Formats, preferredContainer, video.Extension);
            if (audio is not null)
                return new FormatSelection(video.Id, audio.Id, video.Extension, audio.Extension, AudioOnly: false, RequiresMerge: true);
        }
        if (!allowActiveLive && (preferredContainer is PreferredVideoContainer.Auto or PreferredVideoContainer.Mkv))
        {
            var direct = metadata.Formats.Where(IsOpaqueDirectVideo)
                .Where(format => preset.MaximumHeight is null || format.Height <= preset.MaximumHeight)
                .OrderByDescending(format => format.Height)
                .ThenByDescending(format => format.FileSize ?? format.ApproximateFileSize ?? 0)
                .FirstOrDefault();
            if (direct is not null)
                return new FormatSelection(direct.Id, null, direct.Extension, null, AudioOnly: false, RequiresMerge: false)
                    { RequiresStreamProbe = true };
        }
        throw new InvalidOperationException(
            rankedVideos.Length == 0
                ? $"No stream-copy-compatible video format is available for {preset.DisplayName} in {preferredContainer}."
                : $"No audio stream can be copied into the requested {preferredContainer} container.");
    }

    public static IReadOnlyList<QualityPreset> GetAvailablePresets(
        VideoMetadata metadata,
        PreferredVideoContainer preferredContainer = PreferredVideoContainer.Auto,
        bool allowActiveLive = false)
    {
        var maximumHeight = metadata.Formats
            .Where(format => (format.HasVideo && IsVideoCompatible(format, preferredContainer)) ||
                (!allowActiveLive && (preferredContainer is PreferredVideoContainer.Auto or PreferredVideoContainer.Mkv) &&
                 IsOpaqueDirectVideo(format)))
            .Select(format => format.Height ?? 0)
            .DefaultIfEmpty(0)
            .Max();
        return QualityPreset.All.Where(preset =>
                (preset.MaximumHeight is null || preset.MaximumHeight <= maximumHeight) &&
                CanSelect(metadata, preset, preferredContainer, allowActiveLive))
            .ToArray();
    }

    private static bool CanSelect(VideoMetadata metadata, QualityPreset preset,
        PreferredVideoContainer preferredContainer, bool allowActiveLive)
    {
        try { _ = SelectCore(metadata, preset, preferredContainer, allowActiveLive); return true; }
        catch (InvalidOperationException) { return false; }
    }

    private static bool IsOpaqueDirectVideo(MediaFormat format) =>
        !string.IsNullOrWhiteSpace(format.Id) && format.Width > 0 && format.Height > 0 &&
        string.IsNullOrWhiteSpace(format.VideoCodec) && string.IsNullOrWhiteSpace(format.AudioCodec) &&
        (format.Extension?.ToLowerInvariant() is "mp4" or "m4v" or "webm" or "mkv" or "mov" or "avi" or "ogv") &&
        (format.Protocol is "http" or "https") &&
        (!format.AdditionalData.TryGetValue("has_drm", out var drm) || drm.ValueKind != System.Text.Json.JsonValueKind.True) &&
        format.AdditionalData.TryGetValue("url", out var url) && url.ValueKind == System.Text.Json.JsonValueKind.String &&
        Uri.TryCreate(url.GetString(), UriKind.Absolute, out var uri) && (uri.Scheme is "http" or "https");

    private static MediaFormat? SelectAudio(
        IEnumerable<MediaFormat> formats,
        PreferredVideoContainer preferredContainer,
        string? videoExtension)
    {
        var audioFormats = formats.Where(format =>
            format.HasAudio && !format.HasVideo && IsAudioCompatible(format, preferredContainer));
        return audioFormats
            .OrderByDescending(format => ContainerCompatibility(format, videoExtension))
            .ThenByDescending(format => format.AudioBitrateKbps ?? format.TotalBitrateKbps ?? 0)
            .ThenByDescending(format => format.AudioSampleRate ?? 0)
            .ThenByDescending(format => format.FileSize ?? format.ApproximateFileSize ?? 0)
            .FirstOrDefault();
    }

    private static bool IsVideoCompatible(MediaFormat format, PreferredVideoContainer preferredContainer)
    {
        if (preferredContainer is PreferredVideoContainer.Auto or PreferredVideoContainer.Mkv)
            return true;
        var extension = format.Extension.ToLowerInvariant();
        var codec = format.VideoCodec?.ToLowerInvariant() ?? string.Empty;
        return preferredContainer switch
        {
            PreferredVideoContainer.Mp4 => extension is "mp4" or "m4v" &&
                (codec.StartsWith("avc1") || codec.StartsWith("h264") || codec.StartsWith("hev1") ||
                 codec.StartsWith("hvc1") || codec.StartsWith("hevc") || codec.StartsWith("av01")),
            PreferredVideoContainer.WebM => extension == "webm" &&
                (codec.StartsWith("vp8") || codec.StartsWith("vp9") || codec.StartsWith("vp0") || codec.StartsWith("av01")),
            _ => false
        };
    }

    private static bool IsAudioCompatible(MediaFormat format, PreferredVideoContainer preferredContainer)
    {
        if (preferredContainer is PreferredVideoContainer.Auto or PreferredVideoContainer.Mkv)
            return true;
        var extension = format.Extension.ToLowerInvariant();
        var codec = format.AudioCodec?.ToLowerInvariant() ?? string.Empty;
        return preferredContainer switch
        {
            PreferredVideoContainer.Mp4 => (extension is "m4a" or "mp4" or "m4v") &&
                (codec.StartsWith("mp4a") || codec.StartsWith("aac")),
            PreferredVideoContainer.WebM => extension == "webm" && (codec.StartsWith("opus") || codec.StartsWith("vorbis")),
            _ => false
        };
    }

    private static int ContainerCompatibility(MediaFormat audio, string? videoExtension)
    {
        if (string.IsNullOrWhiteSpace(videoExtension))
            return 0;

        var video = videoExtension.ToLowerInvariant();
        var audioExtension = audio.Extension.ToLowerInvariant();
        var codec = audio.AudioCodec?.ToLowerInvariant() ?? string.Empty;
        if (video is "mp4" or "m4v" && (audioExtension is "m4a" or "mp4" || codec.StartsWith("mp4a"))) return 2;
        if (video == "webm" && (audioExtension == "webm" || codec is "opus" or "vorbis")) return 2;
        return 0;
    }
}
