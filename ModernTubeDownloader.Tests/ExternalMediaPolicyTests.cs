using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Models;
using ModernTubeDownloader.Services;

namespace ModernTubeDownloader.Tests;

public sealed class ExternalMediaPolicyTests
{
    [Theory]
    [InlineData("not_live", false, false, ExternalMediaRoute.QuickQueue)]
    [InlineData("was_live", false, true, ExternalMediaRoute.QuickQueue)]
    [InlineData("is_live", true, false, ExternalMediaRoute.LiveConfirmation)]
    [InlineData("is_upcoming", false, false, ExternalMediaRoute.LiveConfirmation)]
    public void LiveUrlShapeNeverOverridesMetadataOrTurnsPlayerTimestampIntoDownloadRange(
        string status, bool active, bool replay, ExternalMediaRoute expected)
    {
        const string url = "https://www.youtube.com/live/MBxOLJe-ZqE?t=1409s&list=PL123";
        Assert.True(ExternalMediaRequest.TryParseProtocol("moderntubedownloader://open?v=2&action=quick-queue&quality=1080&url=" + Uri.EscapeDataString(url), out var request));
        var metadata = new VideoMetadata { Id = "MBxOLJe-ZqE", WebpageUrl = url, LiveStatus = status, IsLive = active, WasLive = replay,
            Formats = [new() { Id = "720", Extension = "mp4", Height = 720, VideoCodec = "h264", AudioCodec = "aac" },
                       new() { Id = "2160", Extension = "mp4", Height = 2160, VideoCodec = "h264", AudioCodec = "aac" }] };
        var facts = ExtractorIdentityService.Facts(metadata);
        var check = new SourceCheckResult(url, metadata, null, facts, OpenLive: facts.Kind is SourceMediaKind.ActiveLive or SourceMediaKind.Upcoming);
        Assert.Equal(expected, ExternalMediaPolicy.Route(request!, check));
        Assert.Equal(url, request!.SourceUrl); Assert.False(request.AutoQueuePlaylist);
        if (expected == ExternalMediaRoute.QuickQueue)
        {
            var quality = QualityPreset.Find(request.RequestedQualityPresetId);
            var item = DownloadQueueItem.FromMetadata(metadata, quality, FormatSelector.Select(metadata, quality));
            Assert.Equal("720", item.SelectedFormats!.VideoFormatId);
            Assert.Equal(MediaTimeRange.Full, item.RequestedRange); Assert.Null(item.LiveSession);
        }
        else Assert.Throws<MediaAvailabilityException>(() => FormatSelector.Select(metadata, QualityPreset.Find("1080")));
    }

    [Theory]
    [InlineData("https://vimeo.com/123")]
    [InlineData("https://www.twitch.tv/channel")]
    [InlineData("https://kick.com/channel")]
    [InlineData("https://www.tiktok.com/@creator/video/123")]
    [InlineData("https://archive.org/details/movie")]
    [InlineData("http://localhost:8090/media#part")]
    public void VersionTwoAcceptsUniversalHttpWithoutImplyingExtractorSupport(string url)
    {
        var payload = $"moderntubedownloader://open?v=2&action=analyze&url={Uri.EscapeDataString(url)}";
        Assert.True(ExternalMediaRequest.TryParseProtocol(payload, out var request));
        Assert.Equal(ExternalMediaIntent.Analyze, request!.Intent);
        Assert.False(request.AutoQueue);
        Assert.True(request.AutoAnalyze);
        Assert.True(ExternalMediaRequest.TryValidateSource(url, out _));
    }

    [Theory]
    [InlineData("file:///private")]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,test")]
    [InlineData("https://user:password@example.com/media")]
    [InlineData("https://example.com/%ZZ")]
    public void UniversalValidationRejectsUnsafeSchemesCredentialsAndEscapes(string url)
        => Assert.False(ExternalMediaRequest.TryValidateSource(url, out _));

    [Theory]
    [InlineData("action=unknown")]
    [InlineData("action=analyze&queue=1")]
    [InlineData("action=analyze&output=C%3A%2Fprivate")]
    [InlineData("action=analyze&args=--exec")]
    [InlineData("action=analyze&executable=cmd.exe")]
    [InlineData("action=analyze&action=quick-queue")]
    [InlineData("action=quick-queue&playlist=1")]
    public void VersionTwoHasNoPathsArgumentsOrConflictingLegacyFlags(string fields)
        => Assert.False(ExternalMediaRequest.TryParseProtocol("moderntubedownloader://open?v=2&url=https%3A%2F%2Fvimeo.com%2F123&" + fields, out _));

    [Fact]
    public void MetadataClassifiesLivePlaylistReplayAndAuthenticationBeforeQueueing()
    {
        var request = new ExternalMediaRequest("https://example.com/media", true, true, Intent: ExternalMediaIntent.QuickQueue);
        var vod = new VideoMetadata { LiveStatus = "not_live" };
        var result = new SourceCheckResult(request.SourceUrl, vod, null, null);
        Assert.Equal(ExternalMediaRoute.QuickQueue, ExternalMediaPolicy.Route(request, result));
        Assert.Equal(ExternalMediaRoute.VideoPreview, ExternalMediaPolicy.Route(request with { AutoQueue = false }, result));
        Assert.Equal(ExternalMediaRoute.LiveConfirmation, ExternalMediaPolicy.Route(request, result with { OpenLive = true }));
        Assert.Equal(ExternalMediaRoute.PlaylistSelection, ExternalMediaPolicy.Route(request, result with
        { Playlist = new PlaylistMetadata("list", "List", null, request.SourceUrl, null, []) }));
        Assert.Equal(ExternalMediaRoute.Error, ExternalMediaPolicy.Route(request, result with { AuthenticationMayBeRequired = true }));
        Assert.Equal(ExternalMediaRoute.Error, ExternalMediaPolicy.Route(request, result with { Metadata = null, MessageKey = "Sources.Unsupported" }));
        var replay = new VideoMetadata { LiveStatus = "was_live", WasLive = true };
        Assert.Equal(ExternalMediaRoute.QuickQueue, ExternalMediaPolicy.Route(request, result with { Metadata = replay }));
        Assert.Equal(ExternalMediaRoute.Open, ExternalMediaPolicy.Route(request with { Intent = ExternalMediaIntent.Open }, result));
    }
}
