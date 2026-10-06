using System.Text.Json;
using ModernTubeDownloader.Models;
using ModernTubeDownloader.Services;

namespace ModernTubeDownloader.Tests;

public sealed class FormatSelectionStabilizationTests
{
    [Theory]
    [InlineData(PreferredVideoContainer.Auto)]
    [InlineData(PreferredVideoContainer.Mkv)]
    public void DirectVideoWithUnknownCodecs_CanBeSelectedWithoutInventingCodecFacts(PreferredVideoContainer container)
    {
        var metadata = DirectVideo();
        var selection = FormatSelector.Select(metadata, QualityPreset.Find("best"), container);
        Assert.Equal("archive-direct", selection.VideoFormatId);
        Assert.False(selection.RequiresMerge);
        Assert.True(selection.RequiresStreamProbe);
        Assert.False(metadata.Formats[0].HasVideo);
        Assert.False(metadata.Formats[0].HasAudio);
        Assert.Contains(FormatSelector.GetAvailablePresets(metadata, container), preset => preset.Id == "720");
    }

    [Theory]
    [InlineData(PreferredVideoContainer.Mp4)]
    [InlineData(PreferredVideoContainer.WebM)]
    public void UnknownCodecs_DoNotPromiseExplicitContainerCompatibility(PreferredVideoContainer container) =>
        Assert.Throws<InvalidOperationException>(() => FormatSelector.Select(DirectVideo(), QualityPreset.Find("best"), container));

    [Theory]
    [InlineData("none", null, "https", "mp4", 720, "https://media.example.test/video.mp4")]
    [InlineData(null, "none", "https", "mp4", 720, "https://media.example.test/video.mp4")]
    [InlineData(null, null, "m3u8_native", "mp4", 720, "https://media.example.test/video.m3u8")]
    [InlineData(null, null, "https", "bin", 720, "https://media.example.test/video.bin")]
    [InlineData(null, null, "https", "mp4", 0, "https://media.example.test/video.mp4")]
    [InlineData(null, null, "https", "mp4", 720, "file:///video.mp4")]
    [InlineData(null, null, "https", "mp4", 720, "")]
    public void OpaqueFallback_RequiresPositiveDirectMediaEvidence(string? videoCodec, string? audioCodec,
        string protocol, string extension, int height, string url)
    {
        var metadata = DirectVideo();
        var format = metadata.Formats[0];
        format.VideoCodec = videoCodec;
        format.AudioCodec = audioCodec;
        format.Protocol = protocol;
        format.Extension = extension;
        format.Height = height;
        format.AdditionalData["url"] = JsonSerializer.SerializeToElement(url);
        Assert.Throws<InvalidOperationException>(() => FormatSelector.Select(metadata, QualityPreset.Find("best")));
        Assert.Throws<InvalidOperationException>(() => FormatSelector.Select(metadata, QualityPreset.Find("audio")));
    }

    [Theory]
    [InlineData(PreferredVideoContainer.Mp4, "mp4", "opus")]
    [InlineData(PreferredVideoContainer.WebM, "webm", "aac")]
    public void FileExtension_DoesNotOverrideContradictoryAudioCodec(PreferredVideoContainer container,
        string extension, string audioCodec)
    {
        var metadata = new VideoMetadata();
        metadata.Formats.Add(new MediaFormat { Id = "contradictory", Extension = extension,
            Height = 720, VideoCodec = container == PreferredVideoContainer.Mp4 ? "avc1" : "vp9", AudioCodec = audioCodec });
        Assert.Throws<InvalidOperationException>(() => FormatSelector.Select(metadata, QualityPreset.Find("best"), container));
    }

    [Fact]
    public void VerifiedStreams_ArePreferredToOpaqueCandidates()
    {
        var metadata = DirectVideo();
        metadata.Formats.Add(new MediaFormat { Id = "verified", Extension = "mp4", Height = 360,
            VideoCodec = "avc1", AudioCodec = "mp4a" });
        var selection = FormatSelector.Select(metadata, QualityPreset.Find("best"));
        Assert.Equal("verified", selection.VideoFormatId);
        Assert.False(selection.RequiresStreamProbe);
    }

    [Fact]
    public void OpaqueFallback_DoesNotEnterTheLiveCaptureSubsystem()
    {
        var metadata = DirectVideo();
        metadata.IsLive = true;
        metadata.LiveStatus = "is_live";
        Assert.Throws<InvalidOperationException>(() => FormatSelector.SelectLive(metadata, QualityPreset.Find("best")));
    }

    internal static VideoMetadata DirectVideo() => new()
    {
        Id = "archive-direct", Title = "Direct media", DurationSeconds = 8,
        WebpageUrl = "https://example.test/source/opaque",
        Formats = [new MediaFormat { Id = "archive-direct", Extension = "mp4", Width = 1280, Height = 720,
            Protocol = "https", AdditionalData = new() { ["url"] = JsonSerializer.SerializeToElement("https://media.example.test/video.mp4") } }]
    };
}
