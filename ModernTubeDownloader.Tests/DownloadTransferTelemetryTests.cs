using System.Globalization;
using System.Text.Json;
using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Models;
using ModernTubeDownloader.Services;
using ModernTubeDownloader.Utilities;
using ModernTubeDownloader.Views;

namespace ModernTubeDownloader.Tests;

public sealed class DownloadTransferTelemetryTests
{
    private const long MiB = 1024 * 1024;
    private static DownloadProgress Progress(long? bytes, string status = "downloading", double? reportedRate = null) =>
        new(50, bytes, null, reportedRate, null, status);

    [Fact]
    public void RecentCounterDeltaTracksAccelerationInsteadOfTheWholeRequestAverage()
    {
        var clock = new Clock(); var estimator = new DownloadTransferEstimator(clock);
        Assert.Equal(0, estimator.Observe(Progress(0)).ObservedBytesDelta);
        clock.Advance(1);
        Assert.Equal(MiB, estimator.Observe(Progress(MiB, reportedRate: MiB)).SpeedBytesPerSecond);
        clock.Advance(1);
        var fast = estimator.Observe(Progress(5 * MiB, reportedRate: 2.5 * MiB));
        Assert.Equal(4 * MiB, fast.ObservedBytesDelta);
        Assert.Equal(4 * MiB, fast.SpeedBytesPerSecond);
    }

    [Fact]
    public void ResumeUnknownCountersAndRollbackNeverCreateHistoricalTraffic()
    {
        var clock = new Clock(); var estimator = new DownloadTransferEstimator(clock);
        Assert.Equal(0, estimator.Observe(Progress(500 * MiB)).ObservedBytesDelta);
        clock.Advance(1);
        Assert.Equal(2 * MiB, estimator.Observe(Progress(502 * MiB)).ObservedBytesDelta);
        clock.Advance(1);
        var reset = estimator.Observe(Progress(20));
        Assert.Equal(0, reset.ObservedBytesDelta); Assert.Null(reset.SpeedBytesPerSecond);
        clock.Advance(1);
        Assert.Equal(30, estimator.Observe(Progress(50)).ObservedBytesDelta);
        clock.Advance(1);
        Assert.Equal(0, estimator.Observe(Progress(null)).ObservedBytesDelta);
        clock.Advance(1);
        Assert.Equal(0, estimator.Observe(Progress(1000)).ObservedBytesDelta);
        clock.Advance(1);
        Assert.Equal(0, estimator.Observe(Progress(-100)).ObservedBytesDelta);
    }

    [Fact]
    public void TerminalAverageIsNotCurrentSpeedAndNextCombinedStreamStartsANewBaseline()
    {
        var clock = new Clock(); var estimator = new DownloadTransferEstimator(clock);
        estimator.Observe(Progress(0)); clock.Advance(.25);
        var finished = estimator.Observe(Progress(MiB, "finished", 4 * MiB));
        Assert.Equal(MiB, finished.ObservedBytesDelta); Assert.Null(finished.SpeedBytesPerSecond);
        var item = new DownloadQueueItem();
        var report = DownloadJobExecutor.CreateDownloadProgress((change, _) => change(item), DownloadStatus.DownloadingVideo, "");
        report.Report(finished with { SpeedBytesPerSecond = 4 * MiB, Eta = TimeSpan.FromSeconds(3) });
        Assert.Null(item.SpeedBytesPerSecond); Assert.Null(item.Eta); Assert.Equal(MiB, item.ObservedTransferBytes);
        clock.Advance(.25);
        Assert.Equal(0, estimator.Observe(Progress(30)).ObservedBytesDelta);
        clock.Advance(.5);
        Assert.Equal(10, estimator.Observe(Progress(40)).ObservedBytesDelta);
        // An already-downloaded file can emit only 'finished': its existing bytes are not traffic.
        Assert.Equal(0, new DownloadTransferEstimator(clock).Observe(Progress(500 * MiB, "finished", 500 * MiB)).ObservedBytesDelta);
    }

    [Fact]
    public void GraphKeepsConcurrentShortTransfersThatFinishAndAreRemovedBeforeItsTick()
    {
        var clock = new Clock(); var sampler = new DownloadTransferSampler(clock);
        var queue = new DownloadQueueService(new Store(), new NullAppLogger());
        var first = new DownloadQueueItem(); var second = new DownloadQueueItem();
        queue.Add(first); queue.Add(second); sampler.Reset(queue.ObservedTransferBytes);
        var firstEstimator = new DownloadTransferEstimator(clock); var secondEstimator = new DownloadTransferEstimator(clock);
        var firstReport = DownloadJobExecutor.CreateDownloadProgress((change, persist) => queue.Update(first.Id, change, persist), DownloadStatus.DownloadingVideo, "");
        var secondReport = DownloadJobExecutor.CreateDownloadProgress((change, persist) => queue.Update(second.Id, change, persist), DownloadStatus.DownloadingAudio, "");
        firstReport.Report(firstEstimator.Observe(Progress(0))); secondReport.Report(secondEstimator.Observe(Progress(0)));
        clock.Advance(.1);
        firstReport.Report(firstEstimator.Observe(Progress(MiB, "finished", 10 * MiB)));
        secondReport.Report(secondEstimator.Observe(Progress(2 * MiB, "finished", 20 * MiB)));
        queue.Update(first.Id, item => item.Status = DownloadStatus.Completed);
        queue.Update(second.Id, item => item.Status = DownloadStatus.Completed);
        Assert.True(queue.Remove(first.Id)); Assert.True(queue.Remove(second.Id));
        clock.Advance(.9);
        Assert.Empty(queue.Snapshot()); Assert.Equal(3 * MiB, sampler.Sample(queue.ObservedTransferBytes));
        clock.Advance(1);
        Assert.Equal(0, sampler.Sample(queue.ObservedTransferBytes));
        Assert.Equal(3 * MiB, queue.ObservedTransferBytes);
    }

    [Fact]
    public async Task LoadingHistoricalCompletedBytesDoesNotChangeRuntimeCounterOrSerializedContract()
    {
        var item = new DownloadQueueItem { Status = DownloadStatus.Completed, DownloadedBytes = 1000 * MiB, ObservedTransferBytes = 800 * MiB };
        var json = JsonSerializer.Serialize(item, JsonDefaults.Options);
        Assert.DoesNotContain("observedTransferBytes", json, StringComparison.OrdinalIgnoreCase);
        var restored = JsonSerializer.Deserialize<DownloadQueueItem>(json, JsonDefaults.Options)!;
        Assert.Equal(0, restored.ObservedTransferBytes);
        var queue = new DownloadQueueService(new Store([restored]), new NullAppLogger());
        await queue.LoadAsync();
        Assert.Equal(0, queue.ObservedTransferBytes);
        Assert.Equal(1000 * MiB, queue.Snapshot().Single().DownloadedBytes);
    }

    [Fact]
    public void SamplerUsesElapsedTimeAndRepeatedReadsCannotConsumeBytes()
    {
        var clock = new Clock(); var sampler = new DownloadTransferSampler(clock);
        sampler.Reset(1000); Assert.Equal(0, sampler.Sample(2000));
        clock.Advance(2);
        Assert.Equal(500, sampler.Sample(2000));
        Assert.Equal(500, sampler.CurrentRate); Assert.Equal(500, sampler.Sample(3000));
        clock.Advance(1);
        Assert.Equal(1000, sampler.Sample(3000));
        sampler.Reset(9000); clock.Advance(1);
        Assert.Equal(0, sampler.Sample(9000));
    }

    [Fact]
    public void GraphPositionsReflectElapsedTimeAndOldPointsCannotPretendToBeRecent()
    {
        var samples = new SpeedSamples(120);
        samples.Add(10, 1); samples.Add(20, 1.25); samples.Add(30, .75);
        Assert.Equal(118d / 120, samples.HorizontalFraction(0), 10);
        Assert.Equal(119.25 / 120, samples.HorizontalFraction(1), 10);
        Assert.Equal(1, samples.HorizontalFraction(2));
        samples.Add(40, 121);
        Assert.Equal([40d], samples.Snapshot()); Assert.Equal(1, samples.HorizontalFraction(0));
        samples.Clear(); samples.Add(50, 1);
        Assert.Equal([50d], samples.Snapshot()); Assert.Equal(1, samples.HorizontalFraction(0));
    }

    [Fact]
    public void MissedGraphTicksExposeAGapAndVisibilityResetDiscardsTheOldBaseline()
    {
        var clock = new Clock(); var sampler = new DownloadTransferSampler(clock);
        sampler.Reset(1000); clock.Advance(1.2); sampler.Sample(2200);
        Assert.False(sampler.HasHistoryGap); Assert.Equal(1.2, sampler.LastIntervalSeconds);
        clock.Advance(3); Assert.Equal(1000, sampler.Sample(5200)); Assert.True(sampler.HasHistoryGap);
        sampler.Reset(10000); Assert.False(sampler.HasHistoryGap); Assert.Equal(0, sampler.CurrentRate);
        clock.Advance(1); Assert.Equal(0, sampler.Sample(10000));
    }

    [Theory]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("-1")]
    [InlineData("NA")]
    public void InvalidToolValuesCannotPoisonTheCurrentRateOrThrowFromEta(string value)
    {
        Assert.True(YtDlpProgressParser.TryParse($"MTD_PROGRESS|downloading|10|100|NA|{value}|{value}|{value}", out var progress));
        Assert.Null(progress.SpeedBytesPerSecond); Assert.Null(progress.Eta); Assert.Equal(10, progress.Percent);
    }

    [Theory]
    [InlineData(0, "0 B/s")]
    [InlineData(1024, "1 KiB/s")]
    [InlineData(2097152, "2 MiB/s")]
    [InlineData(1073741824, "1 GiB/s")]
    public void BinaryByteRatesHaveExplicitUnits(double rate, string expected) =>
        Assert.Equal(expected, TransferRateFormatter.Format(rate, CultureInfo.InvariantCulture));

    private sealed class Clock : TimeProvider
    {
        private long timestamp;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => timestamp;
        public void Advance(double seconds) => timestamp += TimeSpan.FromSeconds(seconds).Ticks;
    }
    private sealed class Store(IReadOnlyList<DownloadQueueItem>? initial = null) : IQueuePersistence
    {
        public Task<IReadOnlyList<DownloadQueueItem>> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(initial ?? []);
        public Task SaveAsync(IReadOnlyList<DownloadQueueItem> items, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
