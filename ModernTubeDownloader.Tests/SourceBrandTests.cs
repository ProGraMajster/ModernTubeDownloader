using ModernTubeDownloader.Services;

namespace ModernTubeDownloader.Tests;

public sealed class SourceBrandTests
{
    [Theory]
    [InlineData("https://www.youtube.com/watch?v=abc&list=xyz", "YouTube")]
    [InlineData("https://m.youtube.com/shorts/abc", "Shorts")]
    [InlineData("https://youtube.com/playlist?list=abc", "Playlist")]
    [InlineData("https://youtube.com/@creator/videos", "Channel")]
    [InlineData("https://youtu.be/abc", "YouTube")]
    [InlineData("https://WWW.TWITCH.TV/creator", "Twitch")]
    [InlineData("https://clips.twitch.tv/abc", "Twitch")]
    [InlineData("https://fb.watch/abc", "Facebook")]
    [InlineData("https://vimeo.com/123", "Vimeo")]
    [InlineData("https://kick.com/creator", "Kick")]
    [InlineData("https://youtube.com.evil.test/watch", "Other")]
    [InlineData("https://notyoutube.com/watch", "Other")]
    [InlineData("https://youtube.com@evil.test/watch", "Other")]
    [InlineData("file:///youtube.com/watch", "Other")]
    [InlineData("invalid", "Other")]
    [InlineData(null, "Other")]
    public void IdentityUsesExactWebHostBoundariesAndKeepsMediaKind(string? url, string expected)
        => Assert.Equal(expected, SourceBrandLookup.Resolve(url).Brand.ToString());

    [Fact]
    public void UnknownHostRemainsHonestAndQueryDoesNotLeakIntoCaption()
    {
        var source = SourceBrandLookup.Resolve("https://example.test/video?token=secret");
        Assert.Equal(SourceBrand.Other, source.Brand);
        Assert.Equal("example.test", source.Name);
    }
}
