using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Models;
using ModernTubeDownloader.Services;
using Xunit.Abstractions;

namespace ModernTubeDownloader.Tests;

public sealed class AtomicJsonFileIsolationTests(ITestOutputHelper output)
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "mtd-json-path-isolation-" + Guid.NewGuid().ToString("N"));
    private static readonly ConcurrentDictionary<Guid, HeldRead> readers = new();
    private static readonly TimeSpan TestBound = TimeSpan.FromSeconds(5);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HeldReadBlocksItsNormalizedPathButNotIndependentPublication(bool rawJson)
    {
        var heldPath = Path.Combine(root, "held.json");
        var independentPath = Path.Combine(root, "other-profile", "settings.json");
        var key = Guid.NewGuid();
        var oldSnapshot = new Snapshot(key, 1, "original");
        var newSnapshot = new Snapshot(key, 2, "replacement");
        await AtomicJsonFile.WriteAsync(heldPath, oldSnapshot);
        Directory.CreateDirectory(Path.Combine(root, "alias"));
        var samePathAlias = Path.Combine(root, "alias", "..", OperatingSystem.IsWindows() ? "HELD.JSON" : "held.json");
        if (OperatingSystem.IsWindows()) samePathAlias = samePathAlias.Replace('\\', '/');
        using var held = new HeldRead();
        Assert.True(readers.TryAdd(key, held));
        var originalRead = Task.Run(() => AtomicJsonFile.ReadAsync<Snapshot>(heldPath));
        Task? samePublication = null;
        Task? independentPublication = null;
        try
        {
            await held.Entered.Task.WaitAsync(TestBound);
            Assert.False(originalRead.IsCompleted);
            samePublication = AtomicJsonFile.WriteAsync(samePathAlias, newSnapshot);
            Assert.False(samePublication.IsCompleted);

            using var cancellation = new CancellationTokenSource();
            var canceledRead = AtomicJsonFile.ReadAsync<Snapshot>(samePathAlias, cancellation.Token);
            Assert.False(canceledRead.IsCompleted);
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceledRead.WaitAsync(TestBound));
            Assert.False(samePublication.IsCompleted);

            const string independent = "independent store remains available";
            independentPublication = rawJson
                ? AtomicJsonFile.WriteRawJsonAsync(independentPath, JsonSerializer.Serialize(independent))
                : AtomicJsonFile.WriteAsync(independentPath, independent);
            await independentPublication.WaitAsync(TestBound);
            Assert.Equal(independent, await AtomicJsonFile.ReadAsync<string>(independentPath).WaitAsync(TestBound));
            Assert.False(originalRead.IsCompleted);
            Assert.False(samePublication.IsCompleted);
            Assert.Empty(Directory.EnumerateFiles(root, "*.tmp", SearchOption.AllDirectories));
        }
        catch
        {
            output.WriteLine($"HeldReadCompleted={originalRead.IsCompleted}; SamePathPublicationCompleted={samePublication?.IsCompleted}; IndependentPublicationCompleted={independentPublication?.IsCompleted}; RawJson={rawJson}");
            throw;
        }
        finally
        {
            held.Release.Set();
            try { await Task.WhenAll(originalRead, samePublication ?? Task.CompletedTask, independentPublication ?? Task.CompletedTask).WaitAsync(TestBound); }
            finally { readers.TryRemove(key, out _); }
        }
        Assert.Equal(oldSnapshot, await originalRead);
        Assert.Equal(newSnapshot, await AtomicJsonFile.ReadAsync<Snapshot>(heldPath).WaitAsync(TestBound));
        Assert.Equal(newSnapshot, await AtomicJsonFile.ReadAsync<Snapshot>(samePathAlias).WaitAsync(TestBound));
    }

    [Fact]
    public async Task HeldUnrelatedReadDoesNotKeepCompletedLiveWorkerOrBlockPendingAdmission()
    {
        var paths = AppPaths.Create(Path.Combine(root, "live-profile"));
        paths.EnsureCreated();
        var logger = new NullAppLogger();
        var settings = new SettingsService(paths, logger);
        await settings.LoadAsync();
        settings.Current.MaxSimultaneousLiveRecordings = 2;
        await settings.SaveAsync();
        var live = new LiveSessionService(new LiveSessionPersistenceService(paths, logger), settings, logger);
        await live.LoadAsync();
        var executor = new ControlledLiveExecutor();
        using var scheduler = new LiveRecordingScheduler(live, executor, new UnexpectedProbe(), settings, logger);
        var first = live.Add("https://example.test/live/first", null, "720", LiveStartPolicy.FromNow);
        var second = live.Add("https://example.test/live/second", null, "720", LiveStartPolicy.FromNow);
        var third = live.Add("https://example.test/live/third", null, "720", LiveStartPolicy.FromNow);
        var key = Guid.NewGuid();
        var heldPath = Path.Combine(root, "unrelated-profile", "history.json");
        var unrelated = new Snapshot(key, 1, "unrelated history must remain readable");
        await AtomicJsonFile.WriteAsync(heldPath, unrelated);
        using var held = new HeldRead();
        Task<Snapshot?>? originalRead = null;
        scheduler.Start();
        try
        {
            await Task.WhenAll(executor.Started(first.Id), executor.Started(second.Id)).WaitAsync(TestBound);
            await live.SaveAsync().WaitAsync(TestBound);
            Assert.Equal(2, scheduler.ActiveIds.Count);
            Assert.Equal(LiveSessionState.Pending, live.Find(third.Id)!.State);
            Assert.True(readers.TryAdd(key, held));
            originalRead = Task.Run(() => AtomicJsonFile.ReadAsync<Snapshot>(heldPath));
            await held.Entered.Task.WaitAsync(TestBound);

            executor.Complete(first.Id);
            await executor.PublishedCompleted(first.Id).WaitAsync(TestBound);
            Assert.Equal(LiveSessionState.Completed, live.Find(first.Id)!.State);
            await executor.Started(third.Id).WaitAsync(TestBound);

            // Admission requires retirement after the canonical final LIVE save.
            // The unrelated reader stays held for the complete assertion boundary.
            Assert.False(originalRead.IsCompleted);
            Assert.DoesNotContain(first.Id, scheduler.ActiveIds);
            Assert.Equal(2, scheduler.ActiveIds.Count);
            Assert.Contains(second.Id, scheduler.ActiveIds);
            Assert.Contains(third.Id, scheduler.ActiveIds);
            Assert.Equal(LiveSessionState.Recording, live.Find(second.Id)!.State);
            Assert.Equal(LiveSessionState.Recording, live.Find(third.Id)!.State);
            var persisted = await AtomicJsonFile.ReadAsync<LiveSession[]>(paths.LiveSessionsFile).WaitAsync(TestBound);
            Assert.NotNull(persisted);
            Assert.Equal(3, persisted.Length);
            Assert.Equal(LiveSessionState.Completed, persisted.Single(item => item.Id == first.Id).State);
            Assert.Equal(new[] { first.Id, second.Id, third.Id }.Order(), persisted.Select(item => item.Id).Order());
        }
        catch
        {
            output.WriteLine($"HeldReadCompleted={originalRead?.IsCompleted}; ActiveLIVE={string.Join(',', scheduler.ActiveIds)}");
            output.WriteLine("LIVE states: " + string.Join(", ", live.Snapshot().Select(item => $"{item.Id}:{item.State}")));
            throw;
        }
        finally
        {
            held.Release.Set();
            try
            {
                if (originalRead is not null) await originalRead.WaitAsync(TestBound);
            }
            finally
            {
                readers.TryRemove(key, out _);
                await scheduler.StopAsync().WaitAsync(TestBound);
                await live.SaveAsync().WaitAsync(TestBound);
            }
        }
        Assert.Empty(scheduler.ActiveIds);
        Assert.Equal(unrelated, await originalRead!);
    }

    [JsonConverter(typeof(SnapshotConverter))]
    public sealed record Snapshot(Guid Key, int Revision, string Payload);

    public sealed class SnapshotConverter : JsonConverter<Snapshot>
    {
        public override Snapshot Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var document = JsonDocument.ParseValue(ref reader);
            var value = document.RootElement;
            var result = new Snapshot(value.GetProperty("key").GetGuid(), value.GetProperty("revision").GetInt32(), value.GetProperty("payload").GetString()!);
            if (result.Revision == 1 && readers.TryGetValue(result.Key, out var held))
            {
                held.Entered.TrySetResult();
                if (!held.Release.Wait(TimeSpan.FromSeconds(10)))
                    throw new TimeoutException("The fixture did not release its held JSON reader.");
            }
            return result;
        }

        public override void Write(Utf8JsonWriter writer, Snapshot value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteString("key", value.Key); writer.WriteNumber("revision", value.Revision); writer.WriteString("payload", value.Payload);
            writer.WriteEndObject();
        }
    }

    private sealed class HeldRead : IDisposable
    {
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal ManualResetEventSlim Release { get; } = new(false);
        public void Dispose() => Release.Dispose();
    }

    private sealed class ControlledLiveExecutor : ILiveRecordingExecutor
    {
        private readonly ConcurrentDictionary<Guid, TaskCompletionSource> started = new();
        private readonly ConcurrentDictionary<Guid, TaskCompletionSource> completed = new();
        private readonly ConcurrentDictionary<Guid, TaskCompletionSource> published = new();
        private static TaskCompletionSource Signal(ConcurrentDictionary<Guid, TaskCompletionSource> map, Guid id) =>
            map.GetOrAdd(id, _ => new(TaskCreationOptions.RunContinuationsAsynchronously));
        public Task Started(Guid id) => Signal(started, id).Task;
        public Task PublishedCompleted(Guid id) => Signal(published, id).Task;
        public void Complete(Guid id) => Signal(completed, id).TrySetResult();
        public async Task ExecuteAsync(LiveSession session, Action<Action<LiveSession>, bool> update, CancellationToken token)
        {
            update(item => item.State = LiveSessionState.Recording, true);
            Signal(started, session.Id).TrySetResult();
            await Signal(completed, session.Id).Task.WaitAsync(token);
            update(item => item.State = LiveSessionState.Completed, true);
            Signal(published, session.Id).TrySetResult();
        }
    }

    private sealed class UnexpectedProbe : ILiveAvailabilityProbe
    {
        public Task<LiveAvailabilityResult> ProbeAsync(string sourceUrl, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("No scheduled monitor belongs to this LIVE ownership fixture.");
    }
}
