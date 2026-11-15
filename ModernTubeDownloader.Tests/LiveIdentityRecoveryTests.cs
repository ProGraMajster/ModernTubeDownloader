using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Models;
using ModernTubeDownloader.Services;

namespace ModernTubeDownloader.Tests;

public sealed class LiveIdentityRecoveryTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "mtd-live-identity-" + Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData("{\"sourceUrl\":\"https://example.test/live\"}")]
    [InlineData("{\"sessionId\":null,\"sourceUrl\":\"https://example.test/live\"}")]
    [InlineData("{\"sessionId\":123,\"sourceUrl\":\"https://example.test/live\"}")]
    [InlineData("{\"sessionId\":\"00000000-0000-0000-0000-000000000000\",\"sourceUrl\":\"https://example.test/live\"}")]
    [InlineData("{\"sessionId\":\"11111111-1111-1111-1111-111111111111\",\"SessionId\":\"22222222-2222-2222-2222-222222222222\",\"sourceUrl\":\"https://example.test/live\"}")]
    [InlineData("null")]
    public async Task DamagedSessionCannotAcquireANewIdentityOrEraseItsValidNeighbor(string damaged)
    {
        var paths = AppPaths.Create(root); paths.EnsureCreated();
        var valid = new LiveSession { SourceUrl = "https://example.test/live/retained", State = LiveSessionState.Completed };
        var original = "[" + System.Text.Json.JsonSerializer.Serialize(valid, JsonDefaults.Options) + "," + damaged + "]";
        await File.WriteAllTextAsync(paths.LiveSessionsFile, original);
        var bytes = await File.ReadAllBytesAsync(paths.LiveSessionsFile);
        for (var reopen = 0; reopen < 2; reopen++)
        {
            var store = new LiveSessionPersistenceService(paths, new NullAppLogger());
            var loaded = await store.LoadAsync();
            Assert.Equal(valid.Id, Assert.Single(loaded).Id);
            Assert.True(store.LoadFailed);
            await Assert.ThrowsAsync<IOException>(() => store.SaveAsync(loaded));
            Assert.Equal(bytes, await File.ReadAllBytesAsync(paths.LiveSessionsFile));
        }
    }

    [Fact]
    public async Task ValidCaseInsensitiveSavedIdentityRemainsStable()
    {
        var paths = AppPaths.Create(root); paths.EnsureCreated();
        var id = Guid.NewGuid();
        await File.WriteAllTextAsync(paths.LiveSessionsFile,
            "[{\"SessionId\":\"" + id + "\",\"sourceUrl\":\"https://example.test/live\"}]");
        var store = new LiveSessionPersistenceService(paths, new NullAppLogger());
        var loaded = await store.LoadAsync();
        Assert.False(store.LoadFailed); Assert.Equal(id, Assert.Single(loaded).Id);
        await store.SaveAsync(loaded);
        Assert.Equal(id, Assert.Single(await new LiveSessionPersistenceService(paths, new NullAppLogger()).LoadAsync()).Id);
    }

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
