using System.Collections.Concurrent;
using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Models;
using ModernTubeDownloader.Services;

namespace ModernTubeDownloader.Tests;

public sealed class QueueProcessorTests : IAsyncLifetime
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "ModernTubeDownloader.QueueProcessor.Tests", Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task ConcurrencyLimit_BoundsStartsWithoutDoubleStartingItems(int limit)
    {
        var harness = await CreateAsync(limit, 5);
        using var processor = harness.Processor;
        processor.Start();

        await WaitUntilAsync(() => harness.Executor.ActiveCount == limit, TimeSpan.FromSeconds(5));
        Assert.Equal(limit, harness.Executor.StartedCount);
        Assert.InRange(harness.Executor.MaximumActiveCount, 1, limit);

        await CompleteAllAsync(harness);

        Assert.Equal(5, harness.Executor.StartedCount);
        Assert.All(harness.Executor.StartCounts.Values, count => Assert.Equal(1, count));
        Assert.All(harness.Queue.Snapshot(), item => Assert.Equal(DownloadStatus.Completed, item.Status));
        await processor.StopAsync();
    }

    [Fact]
    public async Task PlaylistBatch_UsesTheSameThreeWorkerAdmissionLimit()
    {
        var harness = await CreateAsync(3, 8, playlist: true);
        using var processor = harness.Processor;
        Assert.Equal(8, harness.Queue.Snapshot().Count);
        Assert.Equal(Enumerable.Range(1, 8), harness.Queue.Snapshot().Select(item => item.PlaylistIndex!.Value));

        processor.Start();
        await WaitUntilAsync(() => harness.Executor.ActiveCount == 3, TimeSpan.FromSeconds(5));
        Assert.Equal(3, harness.Executor.StartedCount);
        await CompleteAllAsync(harness);

        Assert.Equal(8, harness.Executor.StartedCount);
        Assert.Equal(3, harness.Executor.MaximumActiveCount);
        Assert.All(harness.Queue.Snapshot(), item => Assert.Equal(DownloadStatus.Completed, item.Status));
        await processor.StopAsync();
    }

    [Fact]
    public async Task DynamicLimitChanges_AdjustAdmissionWithoutCancellingRunningJobs()
    {
        var harness = await CreateAsync(3, 5);
        using var processor = harness.Processor;
        processor.Start();
        await WaitUntilAsync(() => harness.Executor.ActiveCount == 3, TimeSpan.FromSeconds(5));

        harness.Settings.Current.MaxSimultaneousDownloads = 1;
        await harness.Settings.SaveAsync();
        var initial = processor.ActiveItemIds.ToArray();
        harness.Executor.Complete(initial[0]);
        await WaitUntilAsync(() => harness.Executor.ActiveCount == 2, TimeSpan.FromSeconds(5));
        Assert.Equal(3, harness.Executor.StartedCount);
        harness.Executor.Complete(initial[1]);
        await WaitUntilAsync(() => harness.Executor.ActiveCount == 1, TimeSpan.FromSeconds(5));
        Assert.Equal(3, harness.Executor.StartedCount);
        harness.Executor.Complete(initial[2]);
        await WaitUntilAsync(() => harness.Executor.StartedCount == 4, TimeSpan.FromSeconds(5));
        Assert.Equal(1, harness.Executor.ActiveCount);

        harness.Settings.Current.MaxSimultaneousDownloads = 3;
        await harness.Settings.SaveAsync();
        await WaitUntilAsync(() => harness.Executor.ActiveCount == 2, TimeSpan.FromSeconds(5));
        Assert.Equal(5, harness.Executor.StartedCount);

        await CompleteAllAsync(harness);
        await processor.StopAsync();
        Assert.Equal(3, harness.Executor.MaximumActiveCount);
    }

    [Fact]
    public async Task CancelAndFailure_AffectOnlyTheirItemAndQueueKeepsAdvancing()
    {
        var harness = await CreateAsync(2, 3);
        using var processor = harness.Processor;
        processor.Start();
        await WaitUntilAsync(() => harness.Executor.ActiveCount == 2, TimeSpan.FromSeconds(5));
        var firstTwo = processor.ActiveItemIds.ToArray();

        Assert.True(processor.CancelActive(firstTwo[0]));
        await WaitUntilAsync(() => harness.Queue.Find(firstTwo[0])?.Status == DownloadStatus.Cancelled, TimeSpan.FromSeconds(5));
        await WaitUntilAsync(() => harness.Executor.StartedCount == 3, TimeSpan.FromSeconds(5));
        Assert.Contains(firstTwo[1], processor.ActiveItemIds);

        harness.Executor.Fail(firstTwo[1]);
        await WaitUntilAsync(() => harness.Queue.Find(firstTwo[1])?.Status == DownloadStatus.Failed, TimeSpan.FromSeconds(5));
        // Failed is published before the final durable save retires the worker.
        // Observe scheduler retirement, not just the item's visible status.
        await WaitUntilAsync(() => !processor.ActiveItemIds.Contains(firstTwo[1]), TimeSpan.FromSeconds(5));
        var remaining = Assert.Single(processor.ActiveItemIds);
        Assert.DoesNotContain(remaining, firstTwo);
        harness.Executor.Complete(remaining);
        await WaitUntilAsync(() => harness.Queue.Find(remaining)?.Status == DownloadStatus.Completed, TimeSpan.FromSeconds(5));
        await processor.StopAsync();
        Assert.Equal(DownloadStatus.Cancelled, harness.Queue.Find(firstTwo[0])!.Status);
        Assert.Equal(DownloadStatus.Failed, harness.Queue.Find(firstTwo[1])!.Status);
        Assert.Empty(processor.ActiveItemIds);
        Assert.All(harness.Executor.StartCounts.Values, count => Assert.Equal(1, count));
    }

    [Fact]
    public async Task PauseStopsNewAdmissionsAndShutdownCancelsEveryActiveJob()
    {
        var harness = await CreateAsync(2, 4);
        using var processor = harness.Processor;
        processor.Start();
        await WaitUntilAsync(() => harness.Executor.ActiveCount == 2, TimeSpan.FromSeconds(5));

        harness.Queue.SetPaused(true);
        foreach (var id in processor.ActiveItemIds)
            harness.Executor.Complete(id);
        await WaitUntilAsync(() => harness.Executor.ActiveCount == 0, TimeSpan.FromSeconds(5));
        Assert.Equal(2, harness.Executor.StartedCount);
        Assert.Equal(2, harness.Queue.Snapshot().Count(item => item.Status == DownloadStatus.Queued));

        harness.Queue.SetPaused(false);
        await WaitUntilAsync(() => harness.Executor.ActiveCount == 2, TimeSpan.FromSeconds(5));
        await processor.StopAsync();
        Assert.Equal(0, harness.Executor.ActiveCount);
        Assert.Equal(2, harness.Queue.Snapshot().Count(item => item.Status == DownloadStatus.Interrupted));
    }

    [Fact]
    public async Task CancelDuringWaitingToActiveHandoff_IsNotLost()
    {
        var harness = await CreateAsync(1, 1);
        using var processor = harness.Processor;
        var item = harness.Queue.Snapshot().Single();
        var cancellationAccepted = false;
        harness.Queue.Changed += (_, change) =>
        {
            if (change.ItemId == item.Id && harness.Queue.Find(item.Id)?.Status == DownloadStatus.Waiting)
                cancellationAccepted = processor.CancelActive(item.Id);
        };

        processor.Start();

        await WaitUntilAsync(() => harness.Queue.Find(item.Id)?.Status == DownloadStatus.Cancelled, TimeSpan.FromSeconds(5));
        Assert.True(cancellationAccepted);
        // Cancellation may win before the executor starts; the item must still finish cancelled.
        await processor.StopAsync();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task CountdownReflectsSchedulerGateAndHidesForPauseOrDisabledPacing(int limit)
    {
        var harness = await CreateAsync(limit, limit + 2, timing: new LongPacingTiming());
        using var processor = harness.Processor;
        harness.Settings.Current.EnableInterDownloadDelay = true;
        await harness.Settings.SaveAsync();
        processor.Start();
        await WaitUntilAsync(() => harness.Executor.StartedCount == 1, TimeSpan.FromSeconds(5));
        if (limit == 1)
        {
            Assert.Null(processor.RemainingAdmissionDelay); // The worker slot is occupied.
            await WaitUntilAsync(() => processor.ActiveItemIds.Count == 1, TimeSpan.FromSeconds(5));
            harness.Executor.Complete(processor.ActiveItemIds.Single());
        }
        await WaitUntilAsync(() => processor.RemainingAdmissionDelay is not null, TimeSpan.FromSeconds(5));
        Assert.InRange(processor.RemainingAdmissionDelay!.Value.TotalSeconds, 0.1, 3.1);
        Assert.NotNull(processor.NextAdmissionAt);

        harness.Queue.SetPaused(true);
        Assert.Null(processor.RemainingAdmissionDelay);
        harness.Queue.SetPaused(false);
        await WaitUntilAsync(() => processor.RemainingAdmissionDelay is not null, TimeSpan.FromSeconds(5));

        harness.Settings.Current.EnableInterDownloadDelay = false;
        await harness.Settings.SaveAsync();
        Assert.Null(processor.NextAdmissionAt);
        await CompleteAllAsync(harness);
        await processor.StopAsync();
    }

    private async Task<Harness> CreateAsync(int limit, int itemCount, bool playlist = false, IDownloadTiming? timing = null)
    {
        var paths = AppPaths.Create(root);
        paths.EnsureCreated();
        var logger = new NullAppLogger();
        var settings = new SettingsService(paths, logger);
        await settings.LoadAsync();
        settings.Current.MaxSimultaneousDownloads = limit;
        await settings.SaveAsync();
        var queue = new DownloadQueueService(new QueuePersistenceService(paths, logger), logger, settings);
        await queue.LoadAsync();
        var items = Enumerable.Range(0, itemCount).Select(index => new DownloadQueueItem
        {
            VideoId = $"video-{index}", Title = $"Video {index}",
            PlaylistId = playlist ? "test-playlist" : null,
            PlaylistIndex = playlist ? index + 1 : null
        }).ToArray();
        if (playlist)
            queue.AddRangeSkippingDuplicates(items);
        else
            foreach (var item in items) queue.Add(item);
        var executor = new ControlledExecutor();
        return new Harness(settings, queue, executor, new QueueProcessor(queue, executor, settings, logger, timing));
    }

    private static async Task CompleteAllAsync(Harness harness)
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (harness.Queue.Snapshot().Any(item => item.Status is not (DownloadStatus.Completed or DownloadStatus.Failed or DownloadStatus.Cancelled)))
        {
            cancellation.Token.ThrowIfCancellationRequested();
            foreach (var id in harness.Processor.ActiveItemIds)
                harness.Executor.Complete(id);
            await Task.Delay(10, cancellation.Token);
        }
    }

    private static async Task WaitUntilAsync(Func<bool> predicate, TimeSpan timeout)
    {
        using var cancellation = new CancellationTokenSource(timeout);
        while (!predicate())
            await Task.Delay(10, cancellation.Token);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        if (Directory.Exists(root))
            Directory.Delete(root, recursive: true);
        return Task.CompletedTask;
    }

    private sealed record Harness(SettingsService Settings, DownloadQueueService Queue, ControlledExecutor Executor, QueueProcessor Processor);

    private sealed class LongPacingTiming : IDownloadTiming
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
        public TimeSpan NextAdmissionDelay(int minimumSeconds, int maximumSeconds) => TimeSpan.FromSeconds(3);
        public TimeSpan RetryDelay(int failedAttempt, DownloadFailureCategory category) => TimeSpan.Zero;
        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) => Task.Delay(delay, cancellationToken);
    }

    private sealed class ControlledExecutor : IDownloadJobExecutor
    {
        private readonly ConcurrentDictionary<Guid, TaskCompletionSource<bool>> completions = [];
        private int activeCount;
        private int maximumActiveCount;

        public ConcurrentDictionary<Guid, int> StartCounts { get; } = [];
        public int ActiveCount => Volatile.Read(ref activeCount);
        public int MaximumActiveCount => Volatile.Read(ref maximumActiveCount);
        public int StartedCount => StartCounts.Count;

        public async Task ExecuteAsync(DownloadQueueItem item, Action<Action<DownloadQueueItem>, bool> update, CancellationToken cancellationToken)
        {
            StartCounts.AddOrUpdate(item.Id, 1, (_, count) => count + 1);
            var active = Interlocked.Increment(ref activeCount);
            UpdateMaximum(active);
            update(value => { value.Status = DownloadStatus.DownloadingVideo; value.StatusMessageKey = "Queue.Detail.DownloadingVideo"; }, false);
            var completion = completions.GetOrAdd(item.Id, _ => new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously));
            try
            {
                var success = await completion.Task.WaitAsync(cancellationToken);
                if (!success)
                    throw new InvalidOperationException("Synthetic item failure.");
                update(value => { value.Status = DownloadStatus.Completed; value.StatusMessageKey = "Queue.Detail.Completed"; }, true);
            }
            finally
            {
                Interlocked.Decrement(ref activeCount);
            }
        }

        public void Complete(Guid id) => completions.GetOrAdd(id, _ => NewCompletion()).TrySetResult(true);
        public void Fail(Guid id) => completions.GetOrAdd(id, _ => NewCompletion()).TrySetResult(false);

        private static TaskCompletionSource<bool> NewCompletion() => new(TaskCreationOptions.RunContinuationsAsynchronously);

        private void UpdateMaximum(int value)
        {
            while (true)
            {
                var current = Volatile.Read(ref maximumActiveCount);
                if (current >= value || Interlocked.CompareExchange(ref maximumActiveCount, value, current) == current)
                    return;
            }
        }
    }
}
