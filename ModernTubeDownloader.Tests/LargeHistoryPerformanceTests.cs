using System.Globalization;
using System.Net;
using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Models;
using ModernTubeDownloader.Services;
using SkiaSharp;

namespace ModernTubeDownloader.Tests;

public sealed class LargeHistoryPerformanceTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "MTD.Performance.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void FullHistoryIsFilteredBeforePagingIncludingUnicodeAndLastRecord()
    {
        var entries = Enumerable.Range(0, 10000).Select(index => new DownloadHistoryEntry(Guid.NewGuid(),
            index == 9999 ? "日本語 Zażółć" : "Title " + index, "https://example.test/", "", "Channel", "1080p", "", DateTimeOffset.UtcNow, "Completed")).ToArray();
        Assert.Equal(entries, HistoryPage.Filter(entries, "", CultureInfo.GetCultureInfo("pl"), default));
        Assert.Equal(entries[9999], Assert.Single(HistoryPage.Filter(entries, "ZAŻÓŁĆ", CultureInfo.GetCultureInfo("pl"), default)));
        Assert.Equal(entries[9999], Assert.Single(HistoryPage.Filter(entries, "日本語", CultureInfo.GetCultureInfo("en"), default)));
        Assert.Equal(250, HistoryPage.Count(entries.Length));
        Assert.Equal(249, HistoryPage.Clamp(250, entries.Length));
        Assert.Equal(0, HistoryPage.Clamp(249, 0));
        Assert.Throws<OperationCanceledException>(() => HistoryPage.Filter(entries, "Title", CultureInfo.InvariantCulture, new CancellationToken(true)));
    }

    [Fact]
    public async Task ThousandUniqueImagesStayWithinByteBudgetAndEvictionCannotInvalidateDisplayedCopies()
    {
        using var handler = new ImageHandler();
        using var client = new HttpClient(handler);
        var budget = 2L * 512 * 288 * 4;
        var cache = new ThumbnailCacheService(AppPaths.Create(root), new NullAppLogger(), client: client, memoryBudgetBytes: budget);
        using var first = await cache.GetAsync("https://example.test/first");
        Assert.NotNull(first); Assert.Equal(512, first.Width); Assert.Equal(288, first.Height);
        for (var index = 0; index < 1000; index++)
        {
            using var bitmap = await cache.GetAsync("https://example.test/" + index);
            Assert.NotNull(bitmap);
            Assert.InRange(cache.CachedBytes, 1, budget);
            Assert.InRange(cache.CachedCount, 1, 2);
            Assert.Equal(cache.CachedBytes, cache.DecodedBytes);
        }
        cache.Dispose(); cache.Dispose();
        Assert.Equal(0, cache.DecodedBytes); Assert.Equal(0, cache.CachedCount);
        Assert.Equal(SKColors.CornflowerBlue, first.GetPixel(20, 20));
    }

    [Fact]
    public async Task SharedLoadProducesIndependentOwnedCopiesAndRetainsRecentlyUsedEntry()
    {
        using var handler = new ImageHandler(block: true);
        using var client = new HttpClient(handler);
        using var cache = new ThumbnailCacheService(AppPaths.Create(root), new NullAppLogger(), client: client, memoryBudgetBytes: 2L * 512 * 288 * 4);
        var waiting = Enumerable.Range(0, 32).Select(_ => cache.GetAsync("https://example.test/one")).ToArray();
        await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        handler.Release.TrySetResult();
        var copies = await Task.WhenAll(waiting).WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            Assert.Equal(1, handler.Calls);
            Assert.All(copies, bitmap => Assert.NotNull(bitmap));
            Assert.Equal(32, copies.Distinct().Count());
            copies[0]!.Erase(SKColors.Red);
            Assert.Equal(SKColors.CornflowerBlue, copies[1]!.GetPixel(20, 20));
        }
        finally { foreach (var copy in copies) copy?.Dispose(); }
        using var second = await cache.GetAsync("https://example.test/two");
        using var touch = await cache.GetAsync("https://example.test/one");
        using var third = await cache.GetAsync("https://example.test/three");
        using var retained = await cache.GetAsync("https://example.test/one");
        Assert.Equal(3, handler.Calls);
    }

    [Fact]
    public async Task CancelledQueuedRequestNeverStartsNetworkAndParallelismIsBounded()
    {
        using var handler = new ImageHandler(block: true);
        using var client = new HttpClient(handler);
        using var cache = new ThumbnailCacheService(AppPaths.Create(root), new NullAppLogger(), client: client, maximumParallelLoads: 1);
        var first = cache.GetAsync("https://example.test/one");
        await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        using var cancellation = new CancellationTokenSource();
        var queued = cache.GetAsync("https://example.test/two", cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queued);
        handler.Release.TrySetResult();
        using var image = await first.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.NotNull(image);
        Assert.Equal(1, handler.Calls);
        Assert.Equal(0, cache.PendingCount);
        Assert.Equal(0, cache.ActiveLoads);
    }

    [Fact]
    public async Task OversizedEncodedResponseIsRejectedBeforeReadingItsBody()
    {
        using var client = new HttpClient(new OversizedHandler());
        using var cache = new ThumbnailCacheService(AppPaths.Create(root), new NullAppLogger(), client: client);
        Assert.Null(await cache.GetAsync("https://example.test/oversized"));
        Assert.Equal(0, cache.DecodedBytes);
        Assert.Empty(Directory.EnumerateFiles(AppPaths.Create(root).ThumbnailCacheDirectory));
    }

    [Fact]
    public async Task InvalidImageDoesNotPoisonPersistentCache()
    {
        using var client = new HttpClient(new InvalidImageHandler());
        using var cache = new ThumbnailCacheService(AppPaths.Create(root), new NullAppLogger(), client: client);
        Assert.Null(await cache.GetAsync("https://example.test/invalid"));
        Assert.Equal(0, cache.DecodedBytes);
        Assert.Empty(Directory.EnumerateFiles(AppPaths.Create(root).ThumbnailCacheDirectory));
    }
    private sealed class InvalidImageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3, 4]) });
    }

    private sealed class OversizedHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var content = new ByteArrayContent([0]); content.Headers.ContentLength = 12 * 1024 * 1024 + 1;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }
    }
    private sealed class ImageHandler : HttpMessageHandler
    {
        private readonly byte[] png;
        private readonly bool block;
        private int calls;
        internal int Calls => Volatile.Read(ref calls);
        internal TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal ImageHandler(bool block = false)
        {
            this.block = block;
            using var bitmap = new SKBitmap(640, 360); bitmap.Erase(SKColors.CornflowerBlue);
            using var image = SKImage.FromBitmap(bitmap); using var data = image.Encode(SKEncodedImageFormat.Png, 100); png = data.ToArray();
        }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref calls); Started.TrySetResult();
            if (block) await Release.Task.WaitAsync(cancellationToken);
            return new(HttpStatusCode.OK) { Content = new ByteArrayContent(png) };
        }
    }
    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}
