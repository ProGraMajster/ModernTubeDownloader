using ModernTubeDownloader.Batch;
using ModernTubeDownloader.Models;
using ModernTubeDownloader.Services;

namespace ModernTubeDownloader.Tests;

public sealed class BatchTransferEstimateTests
{
    [Theory]
    [InlineData(BatchEntryStatus.Pending)] [InlineData(BatchEntryStatus.Validating)]
    [InlineData(BatchEntryStatus.Analyzing)] [InlineData(BatchEntryStatus.Ready)]
    public void CompletedSubsetCannotBecomeWholeListEtaWhenLaterPageAwaitsAdmission(BatchEntryStatus status)
    {
        var session = new BatchImportSession();
        var jobs = Enumerable.Range(1, 100).Select(ordinal =>
        {
            var id = Guid.NewGuid();
            session.Entries.Add(new() { Id = id, Ordinal = ordinal, Status = BatchEntryStatus.Completed, QueueItemId = id });
            return new QueueProgressItem(id, DownloadStatus.Completed, 100, 1000, null, null, null);
        }).ToArray();
        session.Entries.Add(new() { Ordinal = 101, Status = status, EstimatedSizeBytes = null });
        var completedSubset = QueueProgressSummary.Build(jobs, false);
        Assert.Equal(TimeSpan.Zero, completedSubset.TransferRemaining);
        Assert.Null(BatchImportService.TransferRemaining(session, completedSubset));
        Assert.Equal(100, completedSubset.Completed); Assert.Equal(100, completedSubset.Total);
    }

    [Fact]
    public void SettledRealQueueKeepsZeroWhenOtherRowsAreExcludedOrRemoved()
    {
        var id = Guid.NewGuid();
        var session = new BatchImportSession();
        session.Entries.AddRange([
            new() { Id = id, QueueItemId = id, Status = BatchEntryStatus.Completed },
            new() { Status = BatchEntryStatus.Duplicate }, new() { Status = BatchEntryStatus.Skipped },
            new() { Status = BatchEntryStatus.Pending, Removed = true }
        ]);
        var summary = QueueProgressSummary.Build([new(id, DownloadStatus.Completed, 100, 1000, null, null, null)], false);
        Assert.Equal(TimeSpan.Zero, BatchImportService.TransferRemaining(session, summary));
    }

    [Fact]
    public void AdmittedTransferUsesCanonicalEstimateAndPreservesUnknownSizeAndPause()
    {
        var id = Guid.NewGuid(); var session = new BatchImportSession();
        session.Entries.Add(new() { Id = id, QueueItemId = id, Status = BatchEntryStatus.Downloading });
        var job = new QueueProgressItem(id, DownloadStatus.DownloadingVideo, 0, 1000, null, null, 100);
        Assert.Equal(TimeSpan.FromSeconds(10), BatchImportService.TransferRemaining(session, QueueProgressSummary.Build([job], false)));
        Assert.Null(BatchImportService.TransferRemaining(session, QueueProgressSummary.Build([job], true)));
        Assert.Null(BatchImportService.TransferRemaining(session, QueueProgressSummary.Build([job with { EstimatedBytes = null }], false)));
        Assert.Null(BatchImportService.TransferRemaining(session, QueueProgressSummary.Build([], false)));
    }
}
