using ModernTubeDownloader.Models;

namespace ModernTubeDownloader.Services;

public static class FormatSelector
{
    public static FormatSelection Select(VideoMetadata metadata, QualityPreset preset)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(preset);

        if (preset.AudioOnly)
        {
            var audioOnlyFormat = SelectAudio(metadata.Formats, preferredContainer: null)
                ?? throw new InvalidOperationException("No downloadable audio format is available.");
            return new FormatSelection(null, audioOnlyFormat.Id, string.Empty, audioOnlyFormat.Extension, AudioOnly: true, RequiresMerge: false);
        }

        var videos = metadata.Formats.Where(format => format.HasVideo);
        if (preset.MaximumHeight is { } maximumHeight)
            videos = videos.Where(format => format.Height is null || format.Height <= maximumHeight);

        var video = videos
            .OrderByDescending(format => format.Height ?? 0)
            .ThenByDescending(format => format.Fps ?? 0)
            .ThenByDescending(format => format.VideoBitrateKbps ?? format.TotalBitrateKbps ?? 0)
            .ThenByDescending(format => format.FileSize ?? format.ApproximateFileSize ?? 0)
            .FirstOrDefault()
            ?? throw new InvalidOperationException($"No video format is available for {preset.DisplayName}.");

        if (video.HasAudio)
            return new FormatSelection(video.Id, null, video.Extension, null, AudioOnly: false, RequiresMerge: false);

        var audio = SelectAudio(metadata.Formats, video.Extension)
            ?? throw new InvalidOperationException("No compatible audio stream is available for the selected video quality.");

        return new FormatSelection(video.Id, audio.Id, video.Extension, audio.Extension, AudioOnly: false, RequiresMerge: true);
    }

    public static IReadOnlyList<QualityPreset> GetAvailablePresets(VideoMetadata metadata) =>
        QualityPreset.All.Where(preset =>
            CanSelect(metadata, preset) &&
            (preset.MaximumHeight is null || metadata.Formats.Any(format => format.HasVideo && format.Height == preset.MaximumHeight)))
        .ToArray();

    private static bool CanSelect(VideoMetadata metadata, QualityPreset preset)
    {
        try { _ = Select(metadata, preset); return true; }
        catch (InvalidOperationException) { return false; }
    }

    private static MediaFormat? SelectAudio(IEnumerable<MediaFormat> formats, string? preferredContainer)
    {
        var audioFormats = formats.Where(format => format.HasAudio && !format.HasVideo);
        return audioFormats
            .OrderByDescending(format => ContainerCompatibility(format, preferredContainer))
            .ThenByDescending(format => format.AudioBitrateKbps ?? format.TotalBitrateKbps ?? 0)
            .ThenByDescending(format => format.AudioSampleRate ?? 0)
            .ThenByDescending(format => format.FileSize ?? format.ApproximateFileSize ?? 0)
            .FirstOrDefault();
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
