using System.Collections.Concurrent;
using System.Text.Json;
using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Models;
using ModernTubeDownloader.Services;

namespace ModernTubeDownloader.Tests;

public sealed class LiveSubsystemTests
{
    [Fact]
    public void SplitStreams_OnlyRecoverKnownAlignedCommonDuration()
    {
        var video = new MediaProbeResult(8, true, false, "mp4", 0);
        var audio = new MediaProbeResult(6, false, true, "m4a", 0);
        Assert.True(LiveRecordingExecutor.TryCommonDuration(video, audio, out var duration));
        Assert.Equal(6, duration);
        Assert.False(LiveRecordingExecutor.TryCommonDuration(video, audio with { StartTimeSeconds = 2 }, out _));
        Assert.False(LiveRecordingExecutor.TryCommonDuration(video, audio with { StartTimeSeconds = null }, out _));
        Assert.False(LiveRecordingExecutor.TryCommonDuration(video with { DurationSeconds = double.PositiveInfinity }, audio, out _));
    }
    [Fact]
    public async Task FiveVodAndThreeLive_HaveIndependentThreeAndTwoWorkerOwnership()
    {
        await using var h = await Harness.CreateAsync(3, 2);
        h.AddVod(5); h.AddLive(3);
        h.Start();
        await Until(() => h.Vod.Active.Count == 3 && h.Capture.Active.Count == 2);
        Assert.Equal(5, h.Vod.Active.Count + h.Capture.Active.Count);
        Assert.Equal(3, h.Vod.Started.Count);
        Assert.Equal(2, h.Capture.Started.Count);
        Assert.Single(h.Live.Snapshot(), item => item.State == LiveSessionState.Pending);
        Assert.DoesNotContain(h.Queue.Snapshot(), item => item.LiveSession is not null);

        // Free a VOD slot: only the fourth VOD may start, never the third LIVE.
        h.Vod.Complete(h.Vod.Active.First());
        await Until(() => h.Vod.Started.Count == 4 && h.Vod.Active.Count == 3);
        Assert.Equal(2, h.Capture.Started.Count);
        Assert.Single(h.Live.Snapshot(), item => item.State == LiveSessionState.Pending);

        // Free a LIVE slot: the pending LIVE, not the fifth VOD, is admitted.
        h.Capture.Complete(h.Capture.Active.First());
        await Until(() => h.Capture.Started.Count == 3 && h.Capture.Active.Count == 2);
        Assert.Equal(4, h.Vod.Started.Count);

        h.Settings.Current.MaxSimultaneousDownloads = 1;
        await h.Settings.SaveAsync();
        foreach (var id in h.Vod.Active.Take(2)) h.Vod.Complete(id);
        await Until(() => h.Vod.Active.Count == 1);
        Assert.Equal(2, h.Capture.Active.Count);
        Assert.Equal(4, h.Vod.Started.Count);

        h.Settings.Current.MaxSimultaneousLiveRecordings = 1;
        await h.Settings.SaveAsync();
        h.AddLive(1, startIndex: 3);
        h.Capture.Complete(h.Capture.Active.First());
        await Until(() => h.Capture.Active.Count == 1);
        Assert.Equal(3, h.Capture.Started.Count);
        Assert.Single(h.Vod.Active);
        h.Settings.Current.MaxSimultaneousLiveRecordings = 3;
        await h.Settings.SaveAsync();
        await Until(() => h.Capture.Active.Count == 2);
        Assert.Single(h.Vod.Active);
        Assert.Equal(4, h.Vod.Started.Count);
        await h.Live.SaveAsync(); await h.Queue.SaveAsync();
        Assert.DoesNotContain("liveSession", await File.ReadAllTextAsync(h.Paths.QueueFile));
        Assert.Equal(4, (await new LiveSessionPersistenceService(h.Paths, h.Logger).LoadAsync()).Count);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task LiveConcurrency_AdmitsExactlyOnceAndHonorsItsOwnLimit(int limit)
    {
        await using var h = await Harness.CreateAsync(1, limit);
        h.AddLive(6); h.Start();
        await Until(() => h.Capture.Active.Count == limit);
        Assert.Equal(limit, h.Capture.Started.Count);
        while (h.Live.Snapshot().Any(item => item.State != LiveSessionState.Completed))
        {
            foreach (var id in h.Capture.Active) h.Capture.Complete(id);
            await Task.Delay(10);
        }
        Assert.Equal(6, h.Capture.Started.Count);
        Assert.All(h.Capture.Started.Values, count => Assert.Equal(1, count));
        Assert.InRange(h.Capture.MaximumActive, 1, limit);
        Assert.Empty(h.Queue.Snapshot());
    }

    [Fact]
    public async Task PausedVod_StillAllowsUpcomingLiveToPollAndRecord()
    {
        await using var h = await Harness.CreateAsync(3, 2);
        h.AddVod(5); h.AddLive(1);
        h.Queue.SetPaused(true);
        var scheduled = h.Live.Add("https://example.test/live/scheduled", null, "720", LiveStartPolicy.FromStart, waiting: true);
        h.Start();
        await Until(() => h.Capture.Active.Count == 2 && h.Probe.Calls >= 2);
        Assert.Empty(h.Vod.Active); Assert.Empty(h.Vod.Started);
        Assert.Equal(5, h.Queue.Snapshot().Count(item => item.Status == DownloadStatus.Queued));
        Assert.Equal(LiveSessionState.Recording, h.Live.Find(scheduled.Id)!.State);
        Assert.Null(h.Live.Find(scheduled.Id)!.NextCheckAt);
        Assert.Equal(1, h.Capture.Started[scheduled.Id]);
        h.Capture.Complete(scheduled.Id);
        await Until(() => h.Live.Find(scheduled.Id)!.State == LiveSessionState.Completed);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task StoppingEitherScheduler_DoesNotStopOrBlockTheOther(bool stopLive)
    {
        await using var h = await Harness.CreateAsync(1, 1);
        h.AddVod(2); h.AddLive(2); h.Start();
        await Until(() => h.Vod.Active.Count == 1 && h.Capture.Active.Count == 1);
        if (stopLive)
        {
            await h.Scheduler.StopAsync();
            Assert.Empty(h.Capture.Active); Assert.Single(h.Vod.Active);
            h.Vod.Complete(h.Vod.Active.Single());
            await Until(() => h.Vod.Started.Count == 2);
            Assert.Single(h.Vod.Active);
            Assert.Contains(h.Live.Snapshot(), item => item.State == LiveSessionState.Interrupted);
        }
        else
        {
            await h.Processor.StopAsync();
            Assert.Empty(h.Vod.Active); Assert.Single(h.Capture.Active);
            h.Capture.Complete(h.Capture.Active.Single());
            await Until(() => h.Capture.Started.Count == 2);
            Assert.Single(h.Capture.Active);
        }
    }

    [Fact]
    public async Task ScheduledIntent_EditCancelRestart_UsesOnlyLivePersistence()
    {
        await using var h = await Harness.CreateAsync(1, 1);
        var scheduled = h.Live.Add("https://example.test/live/upcoming", null, "best", LiveStartPolicy.FromNow, waiting: true);
        Assert.True(h.Live.EditWaiting(scheduled.Id, "720", LiveStartPolicy.FromStart));
        await h.Live.SaveAsync();
        var restored = new LiveSessionService(new LiveSessionPersistenceService(h.Paths, h.Logger), h.Settings, h.Logger);
        await restored.LoadAsync();
        var item = Assert.Single(restored.Snapshot());
        Assert.Equal(scheduled.Id, item.Id);
        Assert.Equal(LiveStartPolicy.FromStart, item.StartPolicy);
        Assert.Equal("720", item.QualityPresetId);
        Assert.True(item.NextCheckAt <= DateTimeOffset.UtcNow.AddSeconds(2));
        Assert.True(restored.Cancel(item.Id));
        Assert.False(restored.Cancel(item.Id));
        Assert.Equal(LiveSessionState.Cancelled, restored.Find(item.Id)!.State);
        Assert.Empty(h.Queue.Snapshot());
    }

    [Fact]
    public async Task Upcoming_RefreshesChangedScheduleWithoutDuplicateProbeOrCapture()
    {
        await using var h = await Harness.CreateAsync(1, 1);
        var shifted = DateTimeOffset.UtcNow.AddHours(2);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Probe.ResultFactory = async (call, token) =>
        {
            if (call == 1) return new(MediaAvailabilityKind.Upcoming, shifted);
            await release.Task.WaitAsync(token);
            return new(MediaAvailabilityKind.ActiveLive, shifted);
        };
        var item = h.Live.Add("https://example.test/live/shifted", null, "720", LiveStartPolicy.FromNow, waiting: true);
        h.Start();
        await Until(() => h.Probe.Calls == 2);
        Assert.Equal(shifted, h.Live.Find(item.Id)!.ScheduledStartAt);
        await Task.Delay(150); // Three synthetic polling intervals while the second probe is blocked.
        Assert.Equal(2, h.Probe.Calls);
        Assert.Empty(h.Capture.Started);
        release.SetResult();
        await Until(() => h.Capture.Active.Count == 1);
        Assert.Equal(1, h.Capture.Started[item.Id]);
        Assert.Single(h.Live.Snapshot());
        Assert.Empty(h.Queue.Snapshot());
    }

    [Theory]
    [InlineData(MediaAvailabilityKind.Private)]
    [InlineData(MediaAvailabilityKind.Unavailable)]
    public async Task Upcoming_PermanentAvailabilityFailureStopsOnlyItsLiveMonitor(MediaAvailabilityKind kind)
    {
        await using var h = await Harness.CreateAsync(1, 1);
        h.Probe.ResultFactory = (_, _) => throw new MediaAvailabilityException(kind);
        var item = h.Live.Add("https://example.test/live/lost", null, "720", LiveStartPolicy.FromNow, waiting: true);
        h.AddVod(1); h.Start();
        await Until(() => h.Live.Find(item.Id)!.State == LiveSessionState.Failed && h.Vod.Active.Count == 1);
        var failed = h.Live.Find(item.Id)!;
        Assert.Equal(kind.ToString(), failed.FailureCategory);
        Assert.NotNull(failed.FailureMessageKey);
        Assert.Null(failed.NextCheckAt);
        Assert.Empty(h.Capture.Started);
        Assert.Equal(1, h.Probe.Calls);
    }

    [Fact]
    public async Task Upcoming_TransientProbeFailureKeepsIntentAndThenStartsOnce()
    {
        await using var h = await Harness.CreateAsync(1, 1);
        h.Probe.ResultFactory = (call, _) => call == 1
            ? throw new HttpRequestException("Synthetic connection reset by peer.")
            : Task.FromResult(new LiveAvailabilityResult(MediaAvailabilityKind.ActiveLive, null));
        var item = h.Live.Add("https://example.test/live/transient", null, "720", LiveStartPolicy.FromNow, waiting: true);
        h.Start();
        await Until(() => h.Capture.Active.Count == 1);
        Assert.Equal(2, h.Probe.Calls);
        Assert.Equal(1, h.Capture.Started[item.Id]);
        Assert.Equal(LiveSessionState.Recording, h.Live.Find(item.Id)!.State);
        Assert.Empty(h.Queue.Snapshot());
    }

    [Fact]
    public async Task LegacyMigration_IsIdempotentAndPreservesNormalItemsAndVerifiedParts()
    {
        await using var h = await Harness.CreateAsync(1, 1);
        var vod = new DownloadQueueItem { SourceUrl = "https://example.test/vod", VideoId = "vod",
            RawMetadataJson = "{\"description\":\"preserve me\"}" };
        var legacy = new DownloadQueueItem { SourceUrl = "https://example.test/live/old", VideoId = "old",
            Status = DownloadStatus.WaitingForLive, LiveSession = new LiveSession { Parts = ["verified.mkv"], PartIndex = 2 } };
        await new QueuePersistenceService(h.Paths, h.Logger).SaveAsync([vod, legacy]);
        var store = new LiveSessionPersistenceService(h.Paths, h.Logger);
        await store.MigrateLegacyQueueAsync(h.Settings);
        await store.MigrateLegacyQueueAsync(h.Settings);
        var kept = Assert.Single(await new QueuePersistenceService(h.Paths, h.Logger).LoadAsync());
        Assert.Equal(vod.Id, kept.Id); Assert.Equal(vod.RawMetadataJson, kept.RawMetadataJson);
        var moved = Assert.Single(await store.LoadAsync());
        Assert.Equal(legacy.LiveSession.SessionId, moved.Id); Assert.Equal(["verified.mkv"], moved.Parts);
        Assert.Equal(LiveSessionState.WaitingForLive, moved.State);
        Assert.Contains(Path.Combine("sessions", moved.Id.ToString("N")), moved.WorkspacePath);
        Assert.Throws<InvalidOperationException>(() => h.Queue.Add(legacy));
    }

    [Fact]
    public async Task Persistence_SkipsInvalidRecordWithoutLosingValidSessions()
    {
        await using var h = await Harness.CreateAsync(1, 1);
        var item = h.Live.Add("https://example.test/live/valid", null, "720", LiveStartPolicy.FromNow);
        // Complete the service's Add-triggered atomic save before deliberately
        // corrupting its fixture file; this is not a concurrent-writer test.
        await h.Live.SaveAsync();
        var valid = JsonSerializer.Serialize(item, JsonDefaults.Options);
        await File.WriteAllTextAsync(h.Paths.LiveSessionsFile, $"[{valid},{{\"sessionId\":\"bad\"}},{{\"sourceUrl\":\"file:///private\"}}]");
        var loaded = await new LiveSessionPersistenceService(h.Paths, h.Logger).LoadAsync();
        Assert.Equal(item.Id, Assert.Single(loaded).Id);
    }

    [Fact]
    public async Task CancellationDuringStartingHandoff_IsNotLost()
    {
        await using var h = await Harness.CreateAsync(1, 1);
        var item = h.Live.Add("https://example.test/live/early", null, "720", LiveStartPolicy.FromNow);
        var cancelled = false;
        h.Live.Changed += (_, _) => { if (!cancelled && h.Live.Find(item.Id)?.State == LiveSessionState.Starting)
            { cancelled = true; h.Live.Cancel(item.Id); } };
        h.Start();
        await Until(() => h.Live.Find(item.Id)?.State == LiveSessionState.Cancelled);
        Assert.Empty(h.Queue.Snapshot());
    }

    private static async Task Until(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        while (!condition()) await Task.Delay(10, timeout.Token);
    }

    private sealed class Harness : IAsyncDisposable
    {
        public AppPaths Paths { get; private init; } = null!;
        public IAppLogger Logger { get; } = new NullAppLogger();
        public SettingsService Settings { get; private init; } = null!;
        public DownloadQueueService Queue { get; private init; } = null!;
        public LiveSessionService Live { get; private init; } = null!;
        public QueueProcessor Processor { get; private set; } = null!;
        public LiveRecordingScheduler Scheduler { get; private set; } = null!;
        public ControlledExecutor Vod { get; } = new();
        public ControlledExecutor Capture { get; } = new();
        public SequenceProbe Probe { get; } = new();
        public static async Task<Harness> CreateAsync(int vodLimit, int liveLimit)
        {
            var paths = AppPaths.Create(Path.Combine(Path.GetTempPath(), "MTD.LiveOwnership.Tests", Guid.NewGuid().ToString("N")));
            paths.EnsureCreated();
            var logger = new NullAppLogger();
            var settings = new SettingsService(paths, logger); await settings.LoadAsync();
            settings.Current.MaxSimultaneousDownloads = vodLimit; settings.Current.MaxSimultaneousLiveRecordings = liveLimit;
            await settings.SaveAsync();
            var queue = new DownloadQueueService(new QueuePersistenceService(paths, logger), logger, settings);
            var live = new LiveSessionService(new LiveSessionPersistenceService(paths, logger), settings, logger);
            var h = new Harness { Paths = paths, Settings = settings, Queue = queue, Live = live };
            h.Processor = new QueueProcessor(queue, h.Vod, settings, logger, new FastTiming());
            h.Scheduler = new LiveRecordingScheduler(live, h.Capture, h.Probe, settings, logger, new FastTiming());
            return h;
        }
        public void AddVod(int count) { for (var i = 0; i < count; i++) Queue.Add(new DownloadQueueItem { VideoId = $"vod-{i}" }); }
        public void AddLive(int count, int startIndex = 0) { for (var i = startIndex; i < startIndex + count; i++)
            Live.Add($"https://example.test/live/{i}", null, "720", LiveStartPolicy.FromNow); }
        public void Start() { Processor.Start(); Scheduler.Start(); }
        public async ValueTask DisposeAsync()
        {
            await Task.WhenAll(Processor.StopAsync(), Scheduler.StopAsync());
            await Live.SaveAsync(); await Queue.SaveAsync();
            Processor.Dispose(); Scheduler.Dispose();
            // Retain the unique test directory: queued asynchronous atomic saves may still finish.
        }
    }
    private sealed class FastTiming : IDownloadTiming
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
        public TimeSpan NextAdmissionDelay(int min, int max) => TimeSpan.FromMilliseconds(50);
        public TimeSpan RetryDelay(int attempt, DownloadFailureCategory category) => TimeSpan.Zero;
        public Task DelayAsync(TimeSpan delay, CancellationToken token) => Task.Delay(delay, token);
    }
    private sealed class SequenceProbe : ILiveAvailabilityProbe
    {
        private int calls;
        public int Calls => Volatile.Read(ref calls);
        public Func<int, CancellationToken, Task<LiveAvailabilityResult>>? ResultFactory { get; set; }
        public Task<LiveAvailabilityResult> ProbeAsync(string url, CancellationToken token)
        {
            var call = Interlocked.Increment(ref calls);
            return ResultFactory?.Invoke(call, token) ?? Task.FromResult(new LiveAvailabilityResult(call == 1
                ? MediaAvailabilityKind.Upcoming : MediaAvailabilityKind.ActiveLive, DateTimeOffset.UtcNow.AddMinutes(1)));
        }
    }
    private sealed class ControlledExecutor : IDownloadJobExecutor, ILiveRecordingExecutor
    {
        private readonly ConcurrentDictionary<Guid, TaskCompletionSource> completions = [];
        public ConcurrentDictionary<Guid, int> Started { get; } = [];
        public ConcurrentDictionary<Guid, byte> Workers { get; } = [];
        public IReadOnlyCollection<Guid> Active => Workers.Keys.ToArray();
        public int MaximumActive { get; private set; }
        public async Task ExecuteAsync(DownloadQueueItem item, Action<Action<DownloadQueueItem>, bool> update, CancellationToken token)
        {
            update(current => current.Status = DownloadStatus.DownloadingVideo, false);
            await Run(item.Id, token);
            update(current => current.Status = DownloadStatus.Completed, true);
        }
        public async Task ExecuteAsync(LiveSession item, Action<Action<LiveSession>, bool> update, CancellationToken token)
        {
            update(current => current.State = LiveSessionState.Recording, true);
            await Run(item.Id, token);
            update(current => current.State = LiveSessionState.Completed, true);
        }
        private async Task Run(Guid id, CancellationToken token)
        {
            Started.AddOrUpdate(id, 1, (_, count) => count + 1); Workers[id] = 0;
            lock (Workers) MaximumActive = Math.Max(MaximumActive, Workers.Count);
            try { await completions.GetOrAdd(id, _ => new(TaskCreationOptions.RunContinuationsAsynchronously)).Task.WaitAsync(token); }
            finally { Workers.TryRemove(id, out _); }
        }
        public void Complete(Guid id) => completions.GetOrAdd(id, _ => new(TaskCreationOptions.RunContinuationsAsynchronously)).TrySetResult();
    }
}
