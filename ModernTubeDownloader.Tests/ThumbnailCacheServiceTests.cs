using System.Net;
using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Services;

namespace ModernTubeDownloader.Tests;

public sealed class ThumbnailCacheServiceTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "ModernTubeDownloader.Thumbnail.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task CancellingOneViewDoesNotCancelSharedThumbnailLoad()
    {
        var handler = new DelayedImageHandler();
        using var client = new HttpClient(handler);
        using var cache = new ThumbnailCacheService(AppPaths.Create(root), new NullAppLogger(), client: client);
        using var firstCancellation = new CancellationTokenSource();

        var first = cache.GetAsync("https://example.test/image.png", firstCancellation.Token);
        var second = cache.GetAsync("https://example.test/image.png");
        await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        firstCancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        handler.Release.TrySetResult();

        Assert.NotNull(await second.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task DisposalCancelsPendingLoadAndPreventsLateCacheInsertion()
    {
        var handler = new DelayedImageHandler();
        using var client = new HttpClient(handler);
        var cache = new ThumbnailCacheService(AppPaths.Create(Path.Combine(root, "dispose")), new NullAppLogger(), client: client);
        var pending = cache.GetAsync("https://example.test/image.png");
        await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        cache.Dispose();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Null(await cache.GetAsync("https://example.test/image.png"));
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
            Directory.Delete(root, recursive: true);
    }

    private sealed class DelayedImageHandler : HttpMessageHandler
    {
        private static readonly byte[] Png = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");

        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int RequestCount { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            Started.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Png) };
        }
    }
}
