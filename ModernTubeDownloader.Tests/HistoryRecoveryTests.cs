using System.Text.Json;
using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Models;
using ModernTubeDownloader.Services;

namespace ModernTubeDownloader.Tests;

public sealed class HistoryRecoveryTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "mtd-history-recovery-" + Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData("{truncated")]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("[null]")]
    [InlineData("[{}]")]
    [InlineData("[{\"queueItemId\":\"00000000-0000-0000-0000-000000000000\"}]")]
    [InlineData("[{\"queueItemId\":\"invalid\"}]")]
    [InlineData("[{\"queueItemId\":\"11111111-1111-1111-1111-111111111111\",\"title\":\"retained valid row\"},null]")]
    [InlineData("[{\"queueItemId\":\"11111111-1111-1111-1111-111111111111\"},{\"queueItemId\":\"11111111-1111-1111-1111-111111111111\",\"title\":\"duplicate VOD\"}]")]
    [InlineData("[{\"queueItemId\":\"11111111-1111-1111-1111-111111111111\",\"livePartIndex\":2},{\"queueItemId\":\"11111111-1111-1111-1111-111111111111\",\"livePartIndex\":2}]")]
    [InlineData("[{\"queueItemId\":\"11111111-1111-1111-1111-111111111111\",\"queueItemId\":\"22222222-2222-2222-2222-222222222222\"}]")]
    public async Task InvalidHistoryCannotLoseRowsThroughSaveAddOrRelocation(string original)
    {
        var paths = CreatePaths();
        await File.WriteAllTextAsync(paths.HistoryFile, original);
        var bytes = await File.ReadAllBytesAsync(paths.HistoryFile);
        var history = new HistoryService(paths, new NullAppLogger());
        await history.LoadAsync();

        Assert.True(history.LoadFailed);
        Assert.Empty(history.Snapshot());
        await AssertMutationsBlockedAsync(history);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(paths.HistoryFile));
        Assert.False(File.Exists(paths.HistoryFile + ".tmp"));
    }

    [Fact]
    public async Task FailedReloadRetainsPreviouslyPublishedRowsAndCannotBeUnlatchedByAnotherLoad()
    {
        var paths = CreatePaths();
        var entry = Entry();
        var original = JsonSerializer.Serialize(new[] { entry }, JsonDefaults.Options);
        await File.WriteAllTextAsync(paths.HistoryFile, original);
        var history = new HistoryService(paths, new NullAppLogger());
        await history.LoadAsync();
        Assert.False(history.LoadFailed);
        await File.WriteAllTextAsync(paths.HistoryFile, "[null]");
        await history.LoadAsync();
        Assert.True(history.LoadFailed);
        Assert.Equal(entry, Assert.Single(history.Snapshot()));
        await AssertMutationsBlockedAsync(history);
        Assert.Equal("[null]", await File.ReadAllTextAsync(paths.HistoryFile));

        // Explicit recovery of this synthetic file must not silently unlock the
        // running service. A newly opened runtime can accept the repaired store.
        await File.WriteAllTextAsync(paths.HistoryFile, original);
        await history.LoadAsync();
        Assert.True(history.LoadFailed);
        await AssertMutationsBlockedAsync(history);
        Assert.Equal(original, await File.ReadAllTextAsync(paths.HistoryFile));
        var reopened = new HistoryService(paths, new NullAppLogger());
        await reopened.LoadAsync();
        Assert.False(reopened.LoadFailed);
        Assert.Equal(entry, Assert.Single(reopened.Snapshot()));
    }

    [Fact]
    public async Task LockedHistoryStaysBlockedAfterUnlockUntilTheRuntimeIsReopened()
    {
        var paths = CreatePaths();
        var entry = Entry();
        await AtomicJsonFile.WriteAsync(paths.HistoryFile, new[] { entry });
        var bytes = await File.ReadAllBytesAsync(paths.HistoryFile);
        var history = new HistoryService(paths, new NullAppLogger());
        using (var locked = new FileStream(paths.HistoryFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            await history.LoadAsync();
        Assert.True(history.LoadFailed);
        await history.LoadAsync();
        await AssertMutationsBlockedAsync(history);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(paths.HistoryFile));
        var reopened = new HistoryService(paths, new NullAppLogger());
        await reopened.LoadAsync();
        Assert.False(reopened.LoadFailed);
        Assert.Equal(entry, Assert.Single(reopened.Snapshot()));
    }

    [Fact]
    public async Task ADirectoryAtTheHistoryPathIsNotAnEmptyNewProfile()
    {
        var paths = CreatePaths();
        Directory.CreateDirectory(paths.HistoryFile);
        var history = new HistoryService(paths, new NullAppLogger());
        await history.LoadAsync();
        Assert.True(history.LoadFailed);
        await AssertMutationsBlockedAsync(history);
        Assert.True(Directory.Exists(paths.HistoryFile));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingNewProfileAndExplicitEmptyHistoryRemainWritable(bool emptyFile)
    {
        var paths = CreatePaths();
        if (emptyFile) await File.WriteAllTextAsync(paths.HistoryFile, "[]");
        var history = new HistoryService(paths, new NullAppLogger());
        await history.LoadAsync();
        Assert.False(history.LoadFailed);
        Assert.Empty(history.Snapshot());
        var entry = Entry();
        await history.AddAsync(entry);
        await history.SaveAsync();
        var reopened = new HistoryService(paths, new NullAppLogger());
        await reopened.LoadAsync();
        Assert.False(reopened.LoadFailed);
        Assert.Equal(entry, Assert.Single(reopened.Snapshot()));
    }

    [Fact]
    public async Task MissingPreviouslyObservedHistoryCannotBeRecreatedByAStaleRuntime()
    {
        var paths = CreatePaths();
        var history = new HistoryService(paths, new NullAppLogger());
        var entry = Entry();
        await history.AddAsync(entry);
        var preserved = paths.HistoryFile + ".preserved";
        File.Move(paths.HistoryFile, preserved);
        var bytes = await File.ReadAllBytesAsync(preserved);
        await history.LoadAsync();
        Assert.True(history.LoadFailed);
        Assert.Equal(entry, Assert.Single(history.Snapshot()));
        await AssertMutationsBlockedAsync(history);
        Assert.False(File.Exists(paths.HistoryFile));
        Assert.Equal(bytes, await File.ReadAllBytesAsync(preserved));
    }

    [Fact]
    public async Task Legacy121RowsAndDistinctLivePartsKeepCanonicalIdentitiesAcrossSaveAndReopen()
    {
        var paths = CreatePaths();
        var id = Guid.NewGuid();
        var original = $$"""
            [{"QueueItemId":"{{id}}","Title":"Legacy Łódź 日本語","SourceUrl":"https://example.test/video",
              "VideoId":"video","Channel":"Channel","Quality":"1080p","FinalPath":"missing.mp4",
              "CompletedAt":"2026-01-01T00:00:00Z","Status":"Completed"},
             {"queueItemId":"{{id}}","title":"LIVE first","livePartIndex":1,"wasLiveRecording":true},
             {"queueItemId":"{{id}}","title":"LIVE second","livePartIndex":2,"wasLiveRecording":true}]
            """;
        await File.WriteAllTextAsync(paths.HistoryFile, original);
        var history = new HistoryService(paths, new NullAppLogger());
        await history.LoadAsync();
        Assert.False(history.LoadFailed);
        Assert.Equal(original, await File.ReadAllTextAsync(paths.HistoryFile)); // A read never rewrites legacy bytes.
        var legacy = Assert.Single(history.Snapshot(), entry => entry.LivePartIndex is null);
        Assert.Equal(id, legacy.QueueItemId);
        Assert.Equal("Legacy Łódź 日本語", legacy.Title);
        Assert.Null(legacy.DurationSeconds);
        Assert.Null(legacy.FileSizeBytes);
        var part = Assert.Single(history.Snapshot(), entry => entry.LivePartIndex == 1);
        await history.AddAsync(part with { Title = "LIVE first updated" });
        await history.SaveAsync();
        var reopened = new HistoryService(paths, new NullAppLogger());
        await reopened.LoadAsync();
        Assert.False(reopened.LoadFailed);
        Assert.Equal(3, reopened.Snapshot().Count);
        Assert.Equal(legacy, Assert.Single(reopened.Snapshot(), entry => entry.LivePartIndex is null));
        Assert.All(reopened.Snapshot(), entry => Assert.Equal(id, entry.QueueItemId));
        Assert.Equal("LIVE first updated", Assert.Single(reopened.Snapshot(), entry => entry.LivePartIndex == 1).Title);
        Assert.Equal("LIVE second", Assert.Single(reopened.Snapshot(), entry => entry.LivePartIndex == 2).Title);
    }

    private AppPaths CreatePaths()
    {
        var paths = AppPaths.Create(root);
        paths.EnsureCreated();
        return paths;
    }

    private static DownloadHistoryEntry Entry() => new(Guid.NewGuid(), "Retained Łódź 日本語", "https://example.test/video",
        "video", "Channel", "1080p", "original.mp4", DateTimeOffset.UnixEpoch, "Completed");

    private static async Task AssertMutationsBlockedAsync(HistoryService history)
    {
        await Assert.ThrowsAsync<IOException>(() => history.SaveAsync());
        await Assert.ThrowsAsync<IOException>(() => history.AddAsync(Entry()));
        await Assert.ThrowsAsync<IOException>(() => history.RelocatePathsAsync(new Dictionary<string, string>
        {
            ["original.mp4"] = "relocated.mp4"
        }));
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}
