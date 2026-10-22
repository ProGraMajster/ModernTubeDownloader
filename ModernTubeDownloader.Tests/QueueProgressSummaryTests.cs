using ModernTubeDownloader.Models;
using ModernTubeDownloader.Services;
using ModernTubeDownloader.Views;

namespace ModernTubeDownloader.Tests;

public sealed class QueueProgressSummaryTests
{
    private static QueueProgressItem Row(DownloadStatus status, long? size = 1000, double? speed = null)
        => new(Guid.NewGuid(), status, 90, size, null, null, speed);

    [Fact]
    public void UnknownSizeAndPausedQueueNeverInventAnEtaOrZeroSize()
    {
        var summary = QueueProgressSummary.Build([Row(DownloadStatus.Completed), Row(DownloadStatus.DownloadingVideo, null, 100)], false);
        Assert.Equal(50, summary.Percent); Assert.Equal(1, summary.UnknownSizes); Assert.Null(summary.TransferRemaining);
        Assert.Equal(1000, summary.KnownBytes);
        Assert.Null(QueueProgressSummary.Build([Row(DownloadStatus.DownloadingVideo, 1000, 100)], true).TransferRemaining);
        Assert.Equal(TimeSpan.FromSeconds(20), QueueProgressSummary.Build([Row(DownloadStatus.DownloadingVideo, 1000, 100), Row(DownloadStatus.Queued)], false).TransferRemaining);
    }
    [Fact]
    public void FailedIsProcessedButNeverSuccessfulAndNewAudioPercentageCannotReverseTotalProgress()
    {
        var summary = QueueProgressSummary.Build([Row(DownloadStatus.Completed), Row(DownloadStatus.Failed), Row(DownloadStatus.Merging)], false);
        Assert.Equal(2, summary.Settled); Assert.Equal(1, summary.Completed); Assert.Equal(1, summary.Failed);
        Assert.Equal(200d / 3, summary.Percent, 8);
        Assert.Equal(0, QueueProgressSummary.Build([], false).Percent);
    }
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(50)] [InlineData(51)] [InlineData(1300)] [InlineData(50000)]
    public void NativeCardWindowAlwaysContainsAtMostFiftyItems(int count)
    {
        var page = QueueCardPage.Clamp(999999, count);
        Assert.InRange(Enumerable.Range(0, count).Skip(page * QueueCardPage.Size).Take(QueueCardPage.Size).Count(), 0, 50);
        if (count > 0) Assert.Equal(page, QueueCardPage.ForIndex(count - 1));
    }
    [Fact]
    public void MetadataSizeEstimatesIncludeBothSelectedStreamsAndRespectCustomRange()
    {
        var metadata = new VideoMetadata { DurationSeconds = 100, Formats = [new() { Id = "v", FileSize = 8000 }, new() { Id = "a", ApproximateFileSize = 2000 }] };
        var selection = new FormatSelection("v", "a", "mp4", "m4a", false, true);
        Assert.Equal(10000, MediaSizeEstimator.Estimate(metadata, selection, MediaTimeRange.Full));
        Assert.Equal(2000, MediaSizeEstimator.Estimate(metadata, selection, new(MediaRangeMode.Custom, TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(40))));
        metadata.Formats[0].FileSize = null;
        Assert.Null(MediaSizeEstimator.Estimate(metadata, selection, MediaTimeRange.Full));
    }
}
