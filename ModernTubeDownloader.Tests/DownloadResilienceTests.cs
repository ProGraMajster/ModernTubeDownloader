using System.Collections.Concurrent;
using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Localization;
using ModernTubeDownloader.Models;
using ModernTubeDownloader.Services;

namespace ModernTubeDownloader.Tests;

public sealed class DownloadResilienceTests
{
    [Theory]
    [InlineData("is_live", MediaAvailabilityKind.ActiveLive)]
    [InlineData("is_upcoming", MediaAvailabilityKind.Upcoming)]
    [InlineData("post_live", MediaAvailabilityKind.Processing)]
    [InlineData("was_live", MediaAvailabilityKind.Ready)]
    [InlineData("not_live", MediaAvailabilityKind.Ready)]
    public void ExplicitLiveStatusControlsDownloadability(string status, MediaAvailabilityKind expected)
    {
        var metadata = new VideoMetadata { LiveStatus = status, IsLive = true };
        Assert.Equal(expected, MediaAvailabilityPolicy.Classify(metadata));
        if (expected != MediaAvailabilityKind.Ready)
            Assert.Equal(expected, Assert.Throws<MediaAvailabilityException>(() => MediaAvailabilityPolicy.EnsureDownloadable(metadata)).Kind);
    }

    [Fact]
    public void PrivateAvailabilityOverridesCompletedLiveStatus()
    {
        var metadata = new VideoMetadata { LiveStatus = "was_live", Availability = "private" };
        Assert.Equal(MediaAvailabilityKind.Private, MediaAvailabilityPolicy.Classify(metadata));
        metadata.Availability = "unavailable";
        Assert.Equal(MediaAvailabilityKind.Unavailable, MediaAvailabilityPolicy.Classify(metadata));
        metadata.Availability = "needs_auth";
        Assert.Equal(MediaAvailabilityKind.Private, MediaAvailabilityPolicy.Classify(metadata));
    }

    [Theory]
    [InlineData("HTTP Error 403: Forbidden", DownloadFailureCategory.HttpForbidden, true)]
    [InlineData("HTTP Error 404: Not Found", DownloadFailureCategory.HttpNotFound, false)]
    [InlineData("HTTP Error 429: Too Many Requests", DownloadFailureCategory.RateLimited, true)]
    [InlineData("HTTP Error 500: Internal Server Error", DownloadFailureCategory.ServerError, true)]
    [InlineData("HTTP Error 503: Unavailable", DownloadFailureCategory.ServerError, true)]
    [InlineData("Private video", DownloadFailureCategory.Private, false)]
    [InlineData("Video is unavailable", DownloadFailureCategory.Unavailable, false)]
    [InlineData("Unsupported URL", DownloadFailureCategory.Unsupported, false)]
    [InlineData("Sign in to confirm your age", DownloadFailureCategory.Authentication, false)]
    [InlineData("ERROR: [youtube] RU6gEobXVHA: This live event will begin in 28 hours.", DownloadFailureCategory.Upcoming, false)]
    [InlineData("Requested format is not available", DownloadFailureCategory.Format, false)]
    public void FailureClassifierDifferentiatesRetryableAndPermanentErrors(
        string stderr, DownloadFailureCategory expected, bool retryable)
    {
        var process = new ProcessRunResult(1, [], [stderr], TimeSpan.FromMilliseconds(42));
        var failure = DownloadFailureClassifier.Classify(new ExternalProcessException("yt-dlp failed", process));
        Assert.Equal(expected, failure.Category);
        Assert.Equal(retryable, failure.Retryable);
        Assert.Equal($"Error.Download.{expected}", failure.UserMessageKey);
        Assert.DoesNotContain(stderr, failure.TechnicalSummary, StringComparison.Ordinal);
    }

    [Fact]
    public void LogsRedactUrlsAndSecretsWithoutDiscardingTechnicalCategory()
    {
        var root = Path.Combine(Path.GetTempPath(), "ModernTubeDownloader.LogRedaction.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            using (var logger = new FileAppLogger(root))
                logger.Error("Download failed HTTP 403 at https://youtube.com/watch?v=private; token=secret123",
                    new InvalidOperationException("Cookie: user-private-data"));
            var log = string.Join('\n', Directory.EnumerateFiles(root, "*.log").Select(File.ReadAllText));
            Assert.Contains("HTTP 403", log);
            Assert.DoesNotContain("private", log, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("secret123", log);
            Assert.DoesNotContain("user-private-data", log);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void TimeoutFfmpegDiskAndCancellationHaveDistinctCategories()
    {
        Assert.Equal(DownloadFailureCategory.Timeout, DownloadFailureClassifier.Classify(new TimeoutException()).Category);
        Assert.Equal(DownloadFailureCategory.Ffmpeg,
            DownloadFailureClassifier.Classify(new ExternalProcessException("ffmpeg failed"), DownloadStatus.Merging).Category);
        Assert.Equal(DownloadFailureCategory.Disk, DownloadFailureClassifier.Classify(new IOException("disk full")).Category);
        Assert.Equal(DownloadFailureCategory.Cancelled,
            DownloadFailureClassifier.Classify(new OperationCanceledException()).Category);
    }

    [Fact]
    public void UnexpectedLiveToolExitIsRetryableAfterPartFinalization()
    {
        var process = new ProcessRunResult(-1, [], [], TimeSpan.FromSeconds(7));
        var error = new ExternalProcessException("yt-dlp exited unexpectedly", process);
        var live = DownloadFailureClassifier.Classify(error, DownloadStatus.Finalizing, isLiveCapture: true);
        Assert.Equal(DownloadFailureCategory.LiveDisconnected, live.Category);
        Assert.True(live.Retryable);
        Assert.Equal(DownloadFailureCategory.Unknown,
            DownloadFailureClassifier.Classify(error, DownloadStatus.Finalizing).Category);
    }

    [Fact]
    public void EveryFailureCategoryHasPolishAndEnglishUserText()
    {
        var english = new LocalizationService("en");
        var polish = new LocalizationService("pl");
        foreach (var category in Enum.GetValues<DownloadFailureCategory>())
        {
            var key = $"Error.Download.{category}";
            Assert.NotEqual($"[{key}]", english[key]);
            Assert.NotEqual($"[{key}]", polish[key]);
            Assert.NotEqual(english[key], polish[key]);
        }
    }

    [Fact]
    public async Task RetryAndPacingSettingsPersistAndNormalizeDamagedValues()
    {
        var root = Path.Combine(Path.GetTempPath(), "ModernTubeDownloader.ResilienceSettings.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var paths = AppPaths.Create(root);
            paths.EnsureCreated();
            var settings = new SettingsService(paths, new NullAppLogger());
            await settings.LoadAsync();
            Assert.True(settings.Current.RetryFailedDownloads);
            Assert.Equal(2, settings.Current.AdditionalRetryAttempts);
            Assert.False(settings.Current.EnableInterDownloadDelay);
            Assert.Equal((3, 20), (settings.Current.MinimumInterDownloadDelaySeconds, settings.Current.MaximumInterDownloadDelaySeconds));
            settings.Current.EnableInterDownloadDelay = true;
            settings.Current.AdditionalRetryAttempts = 4;
            settings.Current.MinimumInterDownloadDelaySeconds = 7;
            settings.Current.MaximumInterDownloadDelaySeconds = 9;
            await settings.SaveAsync();
            var reloaded = new SettingsService(paths, new NullAppLogger());
            await reloaded.LoadAsync();
            Assert.True(reloaded.Current.EnableInterDownloadDelay);
            Assert.Equal((4, 7, 9), (reloaded.Current.AdditionalRetryAttempts,
                reloaded.Current.MinimumInterDownloadDelaySeconds, reloaded.Current.MaximumInterDownloadDelaySeconds));
            await File.WriteAllTextAsync(paths.SettingsFile,
                "{\"AdditionalRetryAttempts\":99,\"MinimumInterDownloadDelaySeconds\":-10,\"MaximumInterDownloadDelaySeconds\":999}");
            var normalized = new SettingsService(paths, new NullAppLogger());
            await normalized.LoadAsync();
            Assert.Equal(5, normalized.Current.AdditionalRetryAttempts);
            Assert.Equal(0, normalized.Current.MinimumInterDownloadDelaySeconds);
            Assert.Equal(300, normalized.Current.MaximumInterDownloadDelaySeconds);
            await File.WriteAllTextAsync(paths.SettingsFile,
                "{\"MinimumInterDownloadDelaySeconds\":30,\"MaximumInterDownloadDelaySeconds\":5}");
            await normalized.LoadAsync();
            Assert.Equal(30, normalized.Current.MaximumInterDownloadDelaySeconds);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void InclusiveAdmissionRangeHonorsFixedAndVariableBounds()
    {
        var timing = new DownloadTiming();
        Assert.Equal(TimeSpan.FromSeconds(4), timing.NextAdmissionDelay(4, 4));
        for (var attempt = 0; attempt < 100; attempt++)
            Assert.InRange(timing.NextAdmissionDelay(3, 5), TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(5));
    }

    [Theory]
    [InlineData("HTTP Error 403: Forbidden", 2, true, 2, DownloadStatus.Completed)]
    [InlineData("HTTP Error 500: Internal Server Error", 2, true, 3, DownloadStatus.Completed)]
    [InlineData("HTTP Error 500: exhausted", 2, true, 3, DownloadStatus.Failed)]
    [InlineData("connection timed out", 2, true, 2, DownloadStatus.Completed)]
    [InlineData("Private video", 2, true, 1, DownloadStatus.Failed)]
    [InlineData("Unsupported URL", 2, true, 1, DownloadStatus.Failed)]
    [InlineData("HTTP Error 403: Forbidden", 0, true, 1, DownloadStatus.Failed)]
    [InlineData("HTTP Error 403: Forbidden", 2, false, 1, DownloadStatus.Failed)]
    public async Task RetryLoopRespectsCategoryAndConfiguredAttemptLimit(
        string stderr, int extraAttempts, bool retryEnabled, int expectedAttempts, DownloadStatus expectedStatus)
    {
        var root = Path.Combine(Path.GetTempPath(), "ModernTubeDownloader.Retry.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var paths = AppPaths.Create(root);
            paths.EnsureCreated();
            var logger = new NullAppLogger();
            var settings = new SettingsService(paths, logger);
            await settings.LoadAsync();
            settings.Current.RetryFailedDownloads = retryEnabled;
            settings.Current.AdditionalRetryAttempts = extraAttempts;
            await settings.SaveAsync();
            var queue = new DownloadQueueService(new QueuePersistenceService(paths, logger), logger, settings);
            await queue.LoadAsync();
            var item = new DownloadQueueItem { VideoId = "test", Title = "Test" };
            queue.Add(item);
            var failures = stderr.Contains("exhausted", StringComparison.Ordinal) ? 100
                : stderr.StartsWith("HTTP Error 500", StringComparison.Ordinal) ? 2 : 1;
            var executor = new SequenceExecutor(stderr, failures);
            using var processor = new QueueProcessor(queue, executor, settings, logger, new FixedTiming());
            processor.Start();
            await WaitUntilAsync(() => queue.Find(item.Id)?.Status is DownloadStatus.Completed or DownloadStatus.Failed,
                TimeSpan.FromSeconds(5));
            Assert.Equal(expectedStatus, queue.Find(item.Id)!.Status);
            Assert.Equal(expectedAttempts, executor.Attempts);
            Assert.Equal(expectedAttempts, queue.Find(item.Id)!.AttemptCount);
            await processor.StopAsync();
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task CancelDuringRetryBackoffStopsAdditionalAttempts()
    {
        var root = Path.Combine(Path.GetTempPath(), "ModernTubeDownloader.RetryCancel.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var paths = AppPaths.Create(root);
            paths.EnsureCreated();
            var logger = new NullAppLogger();
            var settings = new SettingsService(paths, logger);
            await settings.LoadAsync();
            var queue = new DownloadQueueService(new QueuePersistenceService(paths, logger), logger, settings);
            await queue.LoadAsync();
            var item = new DownloadQueueItem { VideoId = "test", Title = "Test" };
            queue.Add(item);
            var executor = new SequenceExecutor("HTTP Error 403: Forbidden", 100);
            var timing = new BlockingRetryTiming();
            using var processor = new QueueProcessor(queue, executor, settings, logger, timing);
            processor.Start();
            await timing.RetryWaitStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(processor.CancelActive(item.Id));
            await WaitUntilAsync(() => queue.Find(item.Id)?.Status == DownloadStatus.Cancelled, TimeSpan.FromSeconds(5));
            Assert.Equal(1, executor.Attempts);
            await processor.StopAsync();
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task ShutdownDuringRetryBackoffDoesNotWaitForTheTimer()
    {
        var root = Path.Combine(Path.GetTempPath(), "ModernTubeDownloader.RetryShutdown.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var paths = AppPaths.Create(root);
            paths.EnsureCreated();
            var logger = new NullAppLogger();
            var settings = new SettingsService(paths, logger);
            await settings.LoadAsync();
            var queue = new DownloadQueueService(new QueuePersistenceService(paths, logger), logger, settings);
            await queue.LoadAsync();
            var item = new DownloadQueueItem { VideoId = "test", Title = "Test" };
            queue.Add(item);
            var timing = new BlockingRetryTiming();
            using var processor = new QueueProcessor(queue, new SequenceExecutor("HTTP Error 403: Forbidden", 100),
                settings, logger, timing);
            processor.Start();
            await timing.RetryWaitStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await processor.StopAsync().WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal(DownloadStatus.Interrupted, queue.Find(item.Id)!.Status);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task PauseDuringAdmissionDelayPreventsNewStartUntilResume()
    {
        var root = Path.Combine(Path.GetTempPath(), "ModernTubeDownloader.PacingPause.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var paths = AppPaths.Create(root);
            paths.EnsureCreated();
            var logger = new NullAppLogger();
            var settings = new SettingsService(paths, logger);
            await settings.LoadAsync();
            settings.Current.MaxSimultaneousDownloads = 2;
            settings.Current.EnableInterDownloadDelay = true;
            await settings.SaveAsync();
            var queue = new DownloadQueueService(new QueuePersistenceService(paths, logger), logger, settings);
            await queue.LoadAsync();
            queue.Add(new DownloadQueueItem { VideoId = "a" });
            queue.Add(new DownloadQueueItem { VideoId = "b" });
            var executor = new HoldExecutor();
            using var processor = new QueueProcessor(queue, executor, settings, logger, new FixedTiming());
            processor.Start();
            await WaitUntilAsync(() => executor.Starts.Count == 1, TimeSpan.FromSeconds(3));
            queue.SetPaused(true);
            await Task.Delay(650);
            Assert.Single(executor.Starts);
            queue.SetPaused(false);
            await WaitUntilAsync(() => executor.Starts.Count == 2, TimeSpan.FromSeconds(3));
            executor.Release();
            await WaitUntilAsync(() => queue.Snapshot().All(item => item.Status == DownloadStatus.Completed), TimeSpan.FromSeconds(3));
            await processor.StopAsync();
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task SingleWorkerStillWaitsBetweenDistinctItemsWhenPacingIsOn()
    {
        var root = Path.Combine(Path.GetTempPath(), "ModernTubeDownloader.SingleWorkerPacing.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var paths = AppPaths.Create(root);
            paths.EnsureCreated();
            var logger = new NullAppLogger();
            var settings = new SettingsService(paths, logger);
            await settings.LoadAsync();
            settings.Current.MaxSimultaneousDownloads = 1;
            settings.Current.EnableInterDownloadDelay = true;
            await settings.SaveAsync();
            var queue = new DownloadQueueService(new QueuePersistenceService(paths, logger), logger, settings);
            await queue.LoadAsync();
            queue.Add(new DownloadQueueItem { VideoId = "first" });
            queue.Add(new DownloadQueueItem { VideoId = "second" });
            var executor = new HoldExecutor();
            using var processor = new QueueProcessor(queue, executor, settings, logger, new FixedTiming());
            processor.Start();
            await WaitUntilAsync(() => executor.Starts.Count == 1, TimeSpan.FromSeconds(3));
            executor.Release();
            await WaitUntilAsync(() => executor.Starts.Count == 2, TimeSpan.FromSeconds(3));
            var starts = executor.Starts.OrderBy(value => value).ToArray();
            Assert.True(starts[1] - starts[0] >= TimeSpan.FromMilliseconds(350));
            await WaitUntilAsync(() => queue.Snapshot().All(item => item.Status == DownloadStatus.Completed), TimeSpan.FromSeconds(3));
            await processor.StopAsync();
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task PacingSpacesQueueAdmissionsWithoutReducingConcurrency()
    {
        var root = Path.Combine(Path.GetTempPath(), "ModernTubeDownloader.Pacing.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var paths = AppPaths.Create(root);
            paths.EnsureCreated();
            var logger = new NullAppLogger();
            var settings = new SettingsService(paths, logger);
            await settings.LoadAsync();
            settings.Current.MaxSimultaneousDownloads = 3;
            settings.Current.EnableInterDownloadDelay = true;
            settings.Current.MinimumInterDownloadDelaySeconds = 1;
            settings.Current.MaximumInterDownloadDelaySeconds = 1;
            await settings.SaveAsync();
            var queue = new DownloadQueueService(new QueuePersistenceService(paths, logger), logger, settings);
            await queue.LoadAsync();
            for (var index = 0; index < 3; index++)
                queue.Add(new DownloadQueueItem { VideoId = $"video-{index}", Title = $"Video {index}" });
            var executor = new HoldExecutor();
            using var processor = new QueueProcessor(queue, executor, settings, logger, new FixedTiming());
            var startedAt = DateTimeOffset.UtcNow;
            processor.Start();
            await WaitUntilAsync(() => executor.Starts.Count == 3, TimeSpan.FromSeconds(5));
            var starts = executor.Starts.OrderBy(value => value).ToArray();
            Assert.True(starts[0] - startedAt < TimeSpan.FromMilliseconds(350));
            Assert.True(starts[1] - starts[0] >= TimeSpan.FromMilliseconds(350));
            Assert.True(starts[2] - starts[1] >= TimeSpan.FromMilliseconds(350));
            Assert.Equal(3, processor.ActiveItemIds.Count);
            executor.Release();
            await WaitUntilAsync(() => queue.Snapshot().All(item => item.Status == DownloadStatus.Completed), TimeSpan.FromSeconds(5));
            await processor.StopAsync();
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        using var cancellation = new CancellationTokenSource(timeout);
        while (!condition()) await Task.Delay(10, cancellation.Token);
    }

    private sealed class FixedTiming : IDownloadTiming
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
        public TimeSpan NextAdmissionDelay(int minimumSeconds, int maximumSeconds) => TimeSpan.FromMilliseconds(500);
        public TimeSpan RetryDelay(int failedAttempt, DownloadFailureCategory category) => TimeSpan.FromMilliseconds(1);
        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) => Task.Delay(delay, cancellationToken);
    }

    private sealed class HoldExecutor : IDownloadJobExecutor
    {
        private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ConcurrentBag<DateTimeOffset> Starts { get; } = [];

        public async Task ExecuteAsync(DownloadQueueItem item, Action<Action<DownloadQueueItem>, bool> update, CancellationToken cancellationToken)
        {
            Starts.Add(DateTimeOffset.UtcNow);
            await release.Task.WaitAsync(cancellationToken);
            update(value => value.Status = DownloadStatus.Completed, true);
        }

        public void Release() => release.TrySetResult();
    }

    private sealed class SequenceExecutor(string stderr, int failures) : IDownloadJobExecutor
    {
        private int attempts;
        public int Attempts => Volatile.Read(ref attempts);

        public Task ExecuteAsync(DownloadQueueItem item, Action<Action<DownloadQueueItem>, bool> update, CancellationToken cancellationToken)
        {
            var attempt = Interlocked.Increment(ref attempts);
            update(value => value.Status = DownloadStatus.DownloadingVideo, false);
            if (attempt <= failures)
                throw new ExternalProcessException("synthetic download failure",
                    new ProcessRunResult(1, [], [stderr], TimeSpan.FromMilliseconds(10)));
            update(value => value.Status = DownloadStatus.Completed, true);
            return Task.CompletedTask;
        }
    }

    private sealed class BlockingRetryTiming : IDownloadTiming
    {
        public TaskCompletionSource RetryWaitStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
        public TimeSpan NextAdmissionDelay(int minimumSeconds, int maximumSeconds) => TimeSpan.Zero;
        public TimeSpan RetryDelay(int failedAttempt, DownloadFailureCategory category) => TimeSpan.FromSeconds(30);
        public async Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            RetryWaitStarted.TrySetResult();
            await Task.Delay(delay, cancellationToken);
        }
    }
}
