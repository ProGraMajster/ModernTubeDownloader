using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Models;
using ModernTubeDownloader.Services;

namespace ModernTubeDownloader.Tests;

public sealed class QueueRecoveryTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "mtd-queue-recovery-" + Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData("{truncated")]
    [InlineData("")]
    [InlineData("null")]
    public async Task InvalidQueueCannotBeReplacedByAnEmptyCheckpointOrResume(string original)
    {
        var paths = AppPaths.Create(root); paths.EnsureCreated();
        await File.WriteAllTextAsync(paths.QueueFile, original);
        var bytes = await File.ReadAllBytesAsync(paths.QueueFile);
        var store = new QueuePersistenceService(paths, new NullAppLogger());
        var queue = new DownloadQueueService(store, new NullAppLogger());
        await queue.LoadAsync();
        Assert.True(store.LoadFailed); Assert.True(queue.LoadFailed);
        Assert.True(queue.IsPaused); Assert.True(queue.Storage.State.IsBlocked);
        Assert.Throws<IOException>(() => queue.Add(new()));
        Assert.Throws<IOException>(() => queue.AddRangeSkippingDuplicates([new()]));
        await Assert.ThrowsAsync<IOException>(() => queue.SaveAsync());
        await Assert.ThrowsAsync<IOException>(() => store.SaveAsync([]));
        await Assert.ThrowsAsync<IOException>(() => queue.RelocateCompletedPathsAsync(new Dictionary<string, string>()));
        Assert.False(await queue.ResumeAsync());
        Assert.Equal(bytes, await File.ReadAllBytesAsync(paths.QueueFile));
    }

    [Fact]
    public async Task LockedQueueRequiresReopenAfterUnlockAndRetainsCanonicalIds()
    {
        var paths = AppPaths.Create(root); paths.EnsureCreated();
        var item = new DownloadQueueItem { Title = "日本語 — Łódź", VideoId = "stable", OutputDirectory = Path.Combine(root, "list-A") };
        await new QueuePersistenceService(paths, new NullAppLogger()).SaveAsync([item]);
        var bytes = await File.ReadAllBytesAsync(paths.QueueFile);
        var queue = new DownloadQueueService(new QueuePersistenceService(paths, new NullAppLogger()), new NullAppLogger());
        using (var locked = new FileStream(paths.QueueFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            await queue.LoadAsync();
        Assert.True(queue.LoadFailed);
        Assert.False(await queue.ResumeAsync());
        await Assert.ThrowsAsync<IOException>(() => queue.SaveAsync());
        Assert.Equal(bytes, await File.ReadAllBytesAsync(paths.QueueFile));
        var reopened = new DownloadQueueService(new QueuePersistenceService(paths, new NullAppLogger()), new NullAppLogger());
        await reopened.LoadAsync();
        var restored = Assert.Single(reopened.Snapshot());
        Assert.False(reopened.LoadFailed); Assert.False(reopened.Storage.State.IsBlocked);
        Assert.Equal(item.Id, restored.Id); Assert.Equal(item.OutputDirectory, restored.OutputDirectory);
        Assert.Equal(item.JobSessionId, restored.JobSessionId);
        Assert.Equal(item.Title, restored.Title);
    }

    [Fact]
    public async Task ADirectoryAtQueuePathIsNotANewProfile()
    {
        var paths = AppPaths.Create(root); paths.EnsureCreated();
        Directory.CreateDirectory(paths.QueueFile);
        var queue = new DownloadQueueService(new QueuePersistenceService(paths, new NullAppLogger()), new NullAppLogger());
        await queue.LoadAsync();
        Assert.True(queue.LoadFailed); Assert.True(queue.Storage.State.IsBlocked);
        Assert.False(await queue.ResumeAsync());
        Assert.True(Directory.Exists(paths.QueueFile));
    }

    [Fact]
    public async Task NewProfileWithoutQueueRemainsWritable()
    {
        var paths = AppPaths.Create(root); paths.EnsureCreated();
        var queue = new DownloadQueueService(new QueuePersistenceService(paths, new NullAppLogger()), new NullAppLogger());
        await queue.LoadAsync();
        Assert.False(queue.LoadFailed);
        var item = new DownloadQueueItem(); queue.Add(item); await queue.SaveAsync();
        Assert.Equal(item.Id, Assert.Single(await new QueuePersistenceService(paths, new NullAppLogger()).LoadAsync()).Id);
    }

    [Theory]
    [InlineData("[null]")]
    [InlineData("[{}]")]
    [InlineData("[{\"id\":\"00000000-0000-0000-0000-000000000000\"}]")]
    [InlineData("[{\"id\":\"invalid\"}]")]
    [InlineData("[{\"id\":\"11111111-1111-1111-1111-111111111111\",\"title\":\"retained valid row\"},null]")]
    [InlineData("[{\"id\":\"11111111-1111-1111-1111-111111111111\",\"videoId\":\"one\"},{\"id\":\"11111111-1111-1111-1111-111111111111\",\"videoId\":\"two\"}]")]
    [InlineData("[{\"id\":\"11111111-1111-1111-1111-111111111111\",\"jobSessionId\":\"33333333-3333-3333-3333-333333333333\"},{\"id\":\"22222222-2222-2222-2222-222222222222\",\"jobSessionId\":\"33333333-3333-3333-3333-333333333333\"}]")]
    [InlineData("[{\"id\":\"11111111-1111-1111-1111-111111111111\",\"id\":\"22222222-2222-2222-2222-222222222222\"}]")]
    [InlineData("[{\"id\":\"11111111-1111-1111-1111-111111111111\",\"Id\":\"22222222-2222-2222-2222-222222222222\"}]")]
    [InlineData("[{\"id\":\"11111111-1111-1111-1111-111111111111\",\"status\":999}]")]
    [InlineData("[{\"id\":\"11111111-1111-1111-1111-111111111111\",\"status\":-1}]")]
    public async Task StructurallyValidJsonWithDamagedQueueRowsRetainsAllOriginalBytes(string original)
    {
        var paths = AppPaths.Create(root); paths.EnsureCreated();
        await File.WriteAllTextAsync(paths.QueueFile, original);
        var bytes = await File.ReadAllBytesAsync(paths.QueueFile);
        var store = new QueuePersistenceService(paths, new NullAppLogger());
        var queue = new DownloadQueueService(store, new NullAppLogger());
        await queue.LoadAsync();
        Assert.True(store.LoadFailed); Assert.True(queue.LoadFailed);
        Assert.Empty(queue.Snapshot()); Assert.True(queue.IsPaused);
        Assert.Throws<IOException>(() => queue.Add(new()));
        Assert.Throws<IOException>(() => queue.AddRangeSkippingDuplicates([new()]));
        await Assert.ThrowsAsync<IOException>(() => queue.SaveAsync());
        await Assert.ThrowsAsync<IOException>(() => store.SaveAsync([]));
        Assert.False(await queue.ResumeAsync());
        Assert.Equal(bytes, await File.ReadAllBytesAsync(paths.QueueFile));
    }

    [Theory]
    [InlineData("")]
    [InlineData(",\"jobSessionId\":\"00000000-0000-0000-0000-000000000000\"")]
    public async Task LegacyQueueWithoutWorkspaceIdentityUsesStableQueueIdAcrossReopens(string workspaceProperty)
    {
        var paths = AppPaths.Create(root); paths.EnsureCreated();
        var id = Guid.NewGuid();
        var original = "[{\"id\":\"" + id + "\",\"title\":\"Legacy Łódź 日本語\"" + workspaceProperty + "}]";
        await File.WriteAllTextAsync(paths.QueueFile, original);
        for (var reopen = 0; reopen < 2; reopen++)
        {
            var store = new QueuePersistenceService(paths, new NullAppLogger());
            var item = Assert.Single(await store.LoadAsync());
            Assert.False(store.LoadFailed); Assert.Equal(id, item.Id); Assert.Equal(id, item.JobSessionId);
            Assert.Equal("Legacy Łódź 日本語", item.Title);
            Assert.Equal(original, await File.ReadAllTextAsync(paths.QueueFile));
        }
        var queue = new DownloadQueueService(new QueuePersistenceService(paths, new NullAppLogger()), new NullAppLogger());
        await queue.LoadAsync(); await queue.SaveAsync();
        var persisted = Assert.Single(await new QueuePersistenceService(paths, new NullAppLogger()).LoadAsync());
        Assert.Equal(id, persisted.Id); Assert.Equal(id, persisted.JobSessionId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("33333333-3333-3333-3333-333333333333")]
    public async Task LegacyNestedLiveIdentityIsStableBeforeAndAfterCheckpoint(string sessionId)
    {
        var paths = AppPaths.Create(root); paths.EnsureCreated();
        var id = Guid.NewGuid(); var workspaceId = Guid.NewGuid();
        var sessionProperty = sessionId.Length == 0 ? "" : "\"sessionId\":\"" + sessionId + "\",";
        var original = "[{\"id\":\"" + id + "\",\"jobSessionId\":\"" + workspaceId +
            "\",\"sourceUrl\":\"https://example.test/live\",\"liveSession\":{" + sessionProperty + "\"partIndex\":1}}]";
        var expectedSessionId = !Guid.TryParse(sessionId, out var savedId) || savedId == Guid.Empty ? workspaceId : savedId;
        await File.WriteAllTextAsync(paths.QueueFile, original);
        for (var reopen = 0; reopen < 2; reopen++)
        {
            var store = new QueuePersistenceService(paths, new NullAppLogger());
            var loaded = Assert.Single(await store.LoadAsync());
            Assert.False(store.LoadFailed); Assert.Equal(id, loaded.Id); Assert.Equal(workspaceId, loaded.JobSessionId);
            Assert.Equal(expectedSessionId, Assert.IsType<LiveSession>(loaded.LiveSession).Id);
            Assert.Equal(original, await File.ReadAllTextAsync(paths.QueueFile));
        }
        var checkpointStore = new QueuePersistenceService(paths, new NullAppLogger());
        await checkpointStore.SaveAsync(await checkpointStore.LoadAsync());
        var restored = Assert.Single(await new QueuePersistenceService(paths, new NullAppLogger()).LoadAsync());
        Assert.Equal(id, restored.Id); Assert.Equal(workspaceId, restored.JobSessionId);
        Assert.Equal(expectedSessionId, Assert.IsType<LiveSession>(restored.LiveSession).Id);
    }

    [Theory]
    [InlineData("{truncated")]
    [InlineData("null")]
    [InlineData("[{\"sourceUrl\":\"file:///private\"}]")]
    public async Task InvalidLiveStorageIsRetainedAcrossLoadAndShutdown(string original)
    {
        var paths = AppPaths.Create(root); paths.EnsureCreated();
        await File.WriteAllTextAsync(paths.LiveSessionsFile, original);
        var bytes = await File.ReadAllBytesAsync(paths.LiveSessionsFile);
        var settings = new SettingsService(paths, new NullAppLogger());
        var live = new LiveSessionService(new(paths, new NullAppLogger()), settings, new NullAppLogger());
        await live.LoadAsync();
        Assert.True(live.LoadFailed);
        Assert.Throws<IOException>(() => live.Add("https://example.test/live", null, "best", LiveStartPolicy.FromNow));
        await Assert.ThrowsAsync<IOException>(() => live.SaveAsync());
        Assert.False(live.Resume(Guid.NewGuid()));
        Assert.Equal(bytes, await File.ReadAllBytesAsync(paths.LiveSessionsFile));
    }

    [Fact]
    public async Task InvalidLiveRecordDoesNotEraseValidOrUnknownRecords()
    {
        var paths = AppPaths.Create(root); paths.EnsureCreated();
        var valid = new LiveSession { SourceUrl = "https://example.test/live", State = LiveSessionState.Pending };
        var original = "[" + System.Text.Json.JsonSerializer.Serialize(valid, JsonDefaults.Options) + ", {\"sessionId\":\"unrecognized-future-value\"}]";
        await File.WriteAllTextAsync(paths.LiveSessionsFile, original);
        var store = new LiveSessionPersistenceService(paths, new NullAppLogger());
        var loaded = await store.LoadAsync();
        Assert.Equal(valid.Id, Assert.Single(loaded).Id);
        Assert.True(store.LoadFailed);
        await Assert.ThrowsAsync<IOException>(() => store.SaveAsync(loaded));
        Assert.Equal(original, await File.ReadAllTextAsync(paths.LiveSessionsFile));
    }

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
