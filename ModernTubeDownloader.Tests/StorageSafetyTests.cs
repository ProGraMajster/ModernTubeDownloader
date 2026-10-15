using System.Collections.Concurrent;
using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Models;
using ModernTubeDownloader.Services;
using ModernTubeDownloader.Authentication;

namespace ModernTubeDownloader.Tests;

public sealed class StorageSafetyTests
{
    private static IOException Full() => new("synthetic localized storage failure", unchecked((int)0x80070070));

    [Fact]
    public void CookieJarWriteExhaustionKeepsNativeErrorAndCleansOnlyItsOwnedLeaseFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), "mtd-cookie-storage-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var error = Assert.Throws<IOException>(() => CookieJarLease.Create(root, [], 1, (_, _) => throw Full()));
            Assert.Equal(StorageFailureKind.DiskFull, StorageFailureClassifier.Classify(error));
            Assert.Empty(Directory.EnumerateFileSystemEntries(root));
            Assert.DoesNotContain("Cookie:", error.ToString());
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root); }
    }

    [Theory]
    [InlineData(unchecked((int)0x80070070))]
    [InlineData(unchecked((int)0x80070027))]
    public void NativeDiskFullCodesDoNotDependOnLanguage(int hr)
        => Assert.Equal(StorageFailureKind.DiskFull, StorageFailureClassifier.Classify(new IOException("非英語", hr)));

    [Theory]
    [InlineData("No space left on device")]
    [InlineData("unable to write data: [Errno 28]")]
    public void ExternalProcessDiskFullIsRecognized(string stderr)
        => Assert.Equal(StorageFailureKind.DiskFull, StorageFailureClassifier.Classify(
            new ExternalProcessException("process", new(-28, [], [stderr], TimeSpan.Zero))));

    [Fact]
    public void OtherIoErrorsAndEmptyExternalDiagnosticsAreNotInventedDiskFull()
    {
        Assert.Equal(StorageFailureKind.None, StorageFailureClassifier.Classify(new IOException("sharing violation", unchecked((int)0x80070020))));
        Assert.Equal(StorageFailureKind.None, StorageFailureClassifier.Classify(new ExternalProcessException("failed")));
        Assert.Equal(StorageFailureKind.DiskFull, StorageFailureClassifier.Classify(new Exception("wrapper", Full())));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DiskFullStopsNextJobAndExplicitCheckpointRestoresAdmission(bool cookieJarFailure)
    {
        var store = new Store(); var probe = new Probe(); var safety = new QueueStorageSafety(probe);
        var queue = new DownloadQueueService(store, new NullAppLogger(), storage: safety);
        var first = new DownloadQueueItem { VideoId = "one" }; var second = new DownloadQueueItem { VideoId = "two" };
        queue.Add(first); queue.Add(second);
        var executor = new Executor((item, update) =>
        {
            if (item.Id == first.Id)
            {
                if (!cookieJarFailure) update(value => value.Status = DownloadStatus.Merging, true);
                throw cookieJarFailure ? Full() : new ExternalProcessException("merge failed", new(-28, [], ["No space left on device"], TimeSpan.Zero));
            }
            update(value => value.Status = DownloadStatus.Completed, true);
        });
        var settings = Settings(); settings.Current.MaxSimultaneousDownloads = 1;
        using var processor = new QueueProcessor(queue, executor, settings, new NullAppLogger());
        processor.Start();
        await Until(() => first.Status == DownloadStatus.Failed && processor.ActiveItemIds.Count == 0);
        Assert.Equal(new[] { first.Id }, executor.Starts.ToArray());
        Assert.True(queue.IsPaused); Assert.Equal(StorageFailureKind.DiskFull, safety.State.Kind);
        Assert.Equal(DownloadStatus.Queued, second.Status);
        probe.Error = Full(); Assert.False(await queue.ResumeAsync());
        Assert.Single(executor.Starts);
        probe.Error = null;
        var writesBeforeResume = store.Writes;
        Assert.True(await queue.ResumeAsync());
        await Until(() => second.Status == DownloadStatus.Completed);
        Assert.True(store.Writes > writesBeforeResume);
        Assert.Equal(2, executor.Starts.Count);
        await processor.StopAsync();
    }

    [Fact]
    public async Task FailedPersistenceRetainsDirtyRuntimeAndDoesNotRetryEachMutation()
    {
        var store = new Store { Error = Full() }; var queue = new DownloadQueueService(store, new NullAppLogger());
        queue.Add(new() { VideoId = "kept-in-memory" });
        Assert.True(queue.IsPaused); Assert.True(queue.HasUnsavedChanges); Assert.True(queue.Storage.State.PersistenceDegraded);
        var calls = store.Writes;
        for (var i = 0; i < 30; i++) queue.Add(new() { VideoId = "pending-" + i });
        Assert.Equal(calls, store.Writes); Assert.Equal(31, queue.Snapshot().Count);
        var executor = new Executor((_, update) => update(item => item.Status = DownloadStatus.Completed, true));
        using var processor = new QueueProcessor(queue, executor, Settings(), new NullAppLogger()); processor.Start();
        Assert.False(await queue.ResumeAsync()); Assert.Empty(executor.Starts);
        store.Error = null;
        Assert.True(await queue.ResumeAsync());
        await Until(() => queue.Snapshot().All(item => item.Status == DownloadStatus.Completed));
        await processor.StopAsync();
        Assert.False(queue.HasUnsavedChanges); Assert.Equal(31, store.LastCount);
    }

    [Fact]
    public async Task SaveFailureBeforeExecutionCannotStartAdmittedItem()
    {
        var store = new Store(); var queue = new DownloadQueueService(store, new NullAppLogger());
        var item = new DownloadQueueItem(); queue.Add(item); store.Error = new UnauthorizedAccessException("test");
        var executor = new Executor((_, _) => throw new InvalidOperationException("must not run"));
        using var processor = new QueueProcessor(queue, executor, Settings(), new NullAppLogger()); processor.Start();
        await Until(() => queue.Storage.State.PersistenceDegraded && processor.ActiveItemIds.Count == 0);
        Assert.Empty(executor.Starts); Assert.Equal(DownloadStatus.Queued, item.Status); Assert.True(queue.IsPaused);
        await processor.StopAsync();
    }

    [Fact]
    public async Task ConcurrentFailureDuringCheckpointCannotBeClearedByResume()
    {
        var store = new Store(); var safety = new QueueStorageSafety(); var queue = new DownloadQueueService(store, new NullAppLogger(), storage: safety);
        queue.Add(new()); safety.Report(Full());
        store.DuringWrite = () => safety.Report(Full());
        Assert.False(await queue.ResumeAsync()); Assert.True(queue.IsPaused);
        store.DuringWrite = null;
        Assert.True(await queue.ResumeAsync());
    }

    [Fact]
    public void UnknownSizeIsAdvisoryButKnownAllocationIncludesReserve()
    {
        var safety = new QueueStorageSafety(new Probe { Available = 10 });
        safety.Preflight("synthetic-volume", null); Assert.False(safety.State.IsBlocked);
        Assert.Throws<StorageSpaceException>(() => safety.Preflight("synthetic-volume", 100));
        Assert.Equal(StorageFailureKind.InsufficientSpace, safety.State.Kind);
    }

    private static SettingsService Settings()
    {
        var settings = new SettingsService(AppPaths.Create(Path.Combine(Path.GetTempPath(), "mtd-storage-test-" + Guid.NewGuid().ToString("N"))), new NullAppLogger());
        settings.Current.MaxSimultaneousDownloads = 1;
        return settings;
    }
    private static async Task Until(Func<bool> condition)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!condition()) await Task.Delay(10, deadline.Token);
    }
    private sealed class Store : IQueuePersistence
    {
        public Exception? Error; public Action? DuringWrite; public int Writes, LastCount;
        public Task<IReadOnlyList<DownloadQueueItem>> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<DownloadQueueItem>>([]);
        public Task SaveAsync(IReadOnlyList<DownloadQueueItem> items, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref Writes); DuringWrite?.Invoke();
            if (Error is { } error) return Task.FromException(error);
            LastCount = items.Count; return Task.CompletedTask;
        }
    }
    private sealed class Probe : IQueueStorageProbe
    {
        public Exception? Error; public long? Available;
        public long? AvailableBytes(string directory) => Available;
        public Task CheckWritableAsync(IEnumerable<string> directories, CancellationToken token)
            => Error is { } error ? Task.FromException(error) : Task.CompletedTask;
    }
    private sealed class Executor(Action<DownloadQueueItem, Action<Action<DownloadQueueItem>, bool>> execute) : IDownloadJobExecutor
    {
        public ConcurrentQueue<Guid> Starts { get; } = new();
        public Task ExecuteAsync(DownloadQueueItem item, Action<Action<DownloadQueueItem>, bool> update, CancellationToken cancellationToken)
        { Starts.Enqueue(item.Id); execute(item, update); return Task.CompletedTask; }
    }
}
