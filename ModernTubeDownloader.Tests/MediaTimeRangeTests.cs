using ModernTubeDownloader.Models;
using ModernTubeDownloader.Services;

namespace ModernTubeDownloader.Tests;

public sealed class MediaTimeRangeTests
{
    [Fact]
    public void FullRange_LeavesOrdinaryDownloadsUnchanged()
    {
        Assert.Null(MediaTimeRange.Full.Validate(100));
        Assert.Equal(string.Empty, MediaTimeRange.Full.DisplayText);
    }

    [Theory]
    [InlineData("00:05:00", "00:25:00", "*300-1500")]
    [InlineData("00:05:00", null, "*300-inf")]
    [InlineData(null, "00:25:00", "*0-1500")]
    public void CustomRange_BuildsYtDlpSection(string? from, string? to, string expected)
    {
        Assert.True(MediaTimeRange.TryParseTime(from, out var start));
        Assert.True(MediaTimeRange.TryParseTime(to, out var end));
        var range = new MediaTimeRange(MediaRangeMode.Custom, start, end);
        Assert.Null(range.Validate(1800));
        Assert.Equal(expected, range.ToYtDlpSection());
    }

    [Fact]
    public void CustomRange_RejectsInvalidValuesButAllowsUnknownDuration()
    {
        Assert.Equal("Range.Error.Order", new MediaTimeRange(MediaRangeMode.Custom, TimeSpan.FromMinutes(25), TimeSpan.FromMinutes(5)).Validate(null));
        Assert.Equal("Range.Error.Duration", new MediaTimeRange(MediaRangeMode.Custom, TimeSpan.FromMinutes(5)).Validate(200));
        Assert.Equal("Range.Error.Duration", new MediaTimeRange(MediaRangeMode.Custom, End: TimeSpan.FromMinutes(25)).Validate(1000));
        Assert.Null(new MediaTimeRange(MediaRangeMode.Custom, TimeSpan.FromMinutes(5)).Validate(null));
        Assert.Equal("Range.Error.ActiveLive", new MediaTimeRange(MediaRangeMode.Custom, End: TimeSpan.FromSeconds(30)).Validate(null, true));
        Assert.False(MediaTimeRange.TryParseTime("-00:05:00", out _));
    }

    [Fact]
    public void Playlist_RejectsTooShortKnownEntryWithoutPartiallyEnqueueing()
    {
        var playlist = new PlaylistMetadata("playlist", "Playlist", null, "https://example.test/playlist", null,
        [
            new PlaylistEntry("one", "One", "https://example.test/watch?id=one", 1, 30, null, null, null, null),
            new PlaylistEntry("two", "Two", "https://example.test/watch?id=two", 2, 60, null, null, null, null)
        ]);
        var selection = new PlaylistSelection(playlist);
        Assert.Throws<ArgumentException>(() => selection.CreateQueueItems(QualityPreset.Find("best"),
            requestedRange: new MediaTimeRange(MediaRangeMode.Custom, End: TimeSpan.FromSeconds(45))));
        var range = new MediaTimeRange(MediaRangeMode.Custom, End: TimeSpan.FromSeconds(20));
        Assert.All(selection.CreateQueueItems(QualityPreset.Find("best"), requestedRange: range), item => Assert.Equal(range, item.RequestedRange));
    }
}
