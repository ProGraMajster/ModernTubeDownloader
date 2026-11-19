using ModernTubeDownloader.Services;

namespace ModernTubeDownloader.Tests;

public sealed class SourceBrandTests
{
    [Theory]
    [InlineData("https://www.youtube.com/watch?v=abc&list=xyz", "YouTube")]
    [InlineData("https://m.youtube.com/shorts/abc", "YouTube")]
    [InlineData("https://youtube.com/playlist?list=abc", "YouTube")]
    [InlineData("https://youtube.com/@creator/videos", "YouTube")]
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

    [Theory]
    [InlineData("https://M.YouTube.COM./shorts/isolated", "YouTube", "Shorts")]
    [InlineData("https://www.youtube.com./playlist/?list=isolated", "YouTube", "Playlist")]
    [InlineData("https://youtube.com/channel/isolated", "YouTube", "Channel")]
    [InlineData("https://youtu.be/shorts/isolated", "YouTube", "Video")]
    [InlineData("https://youtube.com/watch?v=isolated&list=abc", "YouTube", "Video")]
    [InlineData("https://m.twitch.tv./isolated", "Twitch", "Video")]
    [InlineData("https://kick.com/isolated", "Kick", "Video")]
    [InlineData("https://player.vimeo.com/video/123", "Vimeo", "Video")]
    [InlineData("https://vm.tiktok.com/isolated", "TikTok", "Video")]
    [InlineData("https://www.instagram.com/reel/isolated/", "Instagram", "Video")]
    [InlineData("https://m.facebook.com/watch/?v=123", "Facebook", "Video")]
    [InlineData("https://m.vk.com/video123", "Vk", "Video")]
    [InlineData("https://X.COM/isolated/status/123", "Twitter", "Video")]
    [InlineData("https://twitter.com/isolated/status/123", "Twitter", "Video")]
    [InlineData("https://youtube.com.evil.example./shorts/abc", "Other", "Video")]
    [InlineData("https://evil-youtube.com/watch", "Other", "Video")]
    [InlineData("https://yоutube.com/watch", "Other", "Video")]
    [InlineData("https://xn--yutube-wqf.com/watch", "Other", "Video")]
    [InlineData("https://bücher.example/watch", "Other", "Video")]
    [InlineData("https://youtube.com../watch", "Other", "Video")]
    [InlineData("javascript:https://youtube.com/watch", "Other", "Video")]
    [InlineData("", "Other", "Video")]
    public void CanonicalDnsIdentityIsSeparateFromContentKind(string url, string provider, string kind)
    {
        var identity = SourceBrandLookup.Resolve(url);
        Assert.Equal(provider, identity.Brand.ToString()); Assert.Equal(kind, identity.Kind.ToString());
    }

    [Fact]
    public void UnknownIdnUsesAsciiCaptionWithoutCredentialsPathOrQuery()
        => Assert.Equal("xn--bcher-kva.example", SourceBrandLookup.Resolve("https://user:secret@bücher.example/private?token=secret").Name);
}
