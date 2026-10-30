using System.Net;
using System.Security.Cryptography;
using System.Text;
using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Services;
using SkiaSharp;
using ModernFormsNext.Notifications;
using ModernFormsNext.WindowKit.Backend.Windows.Notifications;

namespace ModernTubeDownloader.Tests;

public sealed class ThumbnailCacheServiceTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "ModernTubeDownloader.Thumbnail.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task PlaylistPreviewOwnsBoundedCopiesAndRespectsAutomaticImageSetting()
    {
        const string url = "https://example.test/playlist-thumbnail";
        var paths = AppPaths.Create(root);
        Directory.CreateDirectory(paths.ThumbnailCacheDirectory);
        var file = Path.Combine(paths.ThumbnailCacheDirectory, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url))) + ".img");
        using var original = new SKBitmap(1280, 720);
        original.Erase(SKColors.CornflowerBlue);
        using var image = SKImage.FromBitmap(original);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 90);
        await File.WriteAllBytesAsync(file, encoded.ToArray());
        var enabled = true;
        using var client = new HttpClient(new RejectNetworkHandler());
        using var cache = new ThumbnailCacheService(paths, new NullAppLogger(), () => enabled, client);
        using (var preview = await cache.GetPreviewCopyAsync(url, CancellationToken.None))
        {
            Assert.NotNull(preview);
            Assert.Equal((320, 180), (preview.Width, preview.Height));
        }
        var shared = await cache.GetAsync(url);
        Assert.NotNull(shared);
        using (var second = await cache.GetPreviewCopyAsync(url, CancellationToken.None)) Assert.NotSame(shared, second);
        Assert.Equal((1280, 720), (shared.Width, shared.Height));
        Assert.Equal(SKColors.CornflowerBlue, shared.GetPixel(0, 0));
        enabled = false;
        Assert.Null(await cache.GetPreviewCopyAsync(url, CancellationToken.None));
        enabled = true;
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cache.GetPreviewCopyAsync(url, cancelled.Token));
    }

    [Theory]
    [InlineData(SKEncodedImageFormat.Jpeg)]
    [InlineData(SKEncodedImageFormat.Png)]
    [InlineData(SKEncodedImageFormat.Webp)]
    public void NotificationUsesPersistentBoundedPngWithoutHttpOrDeletingSource(SKEncodedImageFormat format)
    {
        const string url = "https://example.test/image?private=signed";
        var paths = AppPaths.Create(root);
        Directory.CreateDirectory(paths.ThumbnailCacheDirectory);
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url)));
        var source = Path.Combine(paths.ThumbnailCacheDirectory, key + ".img");
        using var bitmap = new SKBitmap(1280, 720);
        bitmap.Erase(SKColors.CornflowerBlue);
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(format, 90);
        File.WriteAllBytes(source, encoded.ToArray());
        var capabilities = new SystemNotificationCapabilities("Windows", SystemNotificationFeatures.Basic | SystemNotificationFeatures.Images)
            { Platform = new WindowsSystemNotificationCapabilities(WindowsSystemNotificationFeatures.AppLogo | WindowsSystemNotificationFeatures.HeroImages) };
        var original = WindowsToastContent.Create(new SystemNotification { Id = "raw", Title = "Raw cached image", Images = [new(source, SystemNotificationImageRole.Thumbnail)] }, capabilities);
        if (format == SKEncodedImageFormat.Webp)
        {
            Assert.Contains(original.Warnings, warning => warning.Code == "InvalidImage");
            Assert.DoesNotContain("<image", original.Xml);
        }
        else Assert.Empty(original.Warnings);
        using var client = new HttpClient(new RejectNetworkHandler());
        var cache = new ThumbnailCacheService(paths, new NullAppLogger(), client: client);
        var output = cache.GetCachedNotificationPath(url);
        Assert.Equal(Path.Combine(paths.ThumbnailCacheDirectory, key + ".notification.png"), output);
        using var outputData = SKData.CreateCopy(File.ReadAllBytes(output!));
        using var codec = SKCodec.Create(outputData);
        Assert.Equal(SKEncodedImageFormat.Png, codec.EncodedFormat);
        Assert.Equal(512, codec.Info.Width); Assert.Equal(288, codec.Info.Height);
        var normalized = WindowsToastContent.Create(new SystemNotification { Id = "normalized", Title = "Normalized cache", Images =
            [new(output!, SystemNotificationImageRole.Thumbnail) { PlatformOptions = new(new WindowsSystemNotificationImageOptions { Placement = WindowsNotificationImagePlacement.AppLogo }) }] }, capabilities);
        Assert.Empty(normalized.Warnings);
        Assert.Contains("placement=\"appLogoOverride\"", normalized.Xml);
        var modified = File.GetLastWriteTimeUtc(output!);
        Assert.Equal(output, cache.GetCachedNotificationPath(url));
        Assert.Equal(modified, File.GetLastWriteTimeUtc(output!));
        cache.Dispose();
        Assert.True(File.Exists(output)); Assert.True(File.Exists(source));
        Assert.Empty(Directory.GetFiles(paths.ThumbnailCacheDirectory, "*.tmp"));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("invalid")]
    [InlineData("oversized")]
    public void MissingInvalidOrOversizeNotificationCacheDegradesWithoutNetwork(string scenario)
    {
        const string url = "https://example.test/unavailable";
        var paths = AppPaths.Create(root);
        Directory.CreateDirectory(paths.ThumbnailCacheDirectory);
        var file = Path.Combine(paths.ThumbnailCacheDirectory, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url))) + ".img");
        if (scenario == "invalid") File.WriteAllBytes(file, [1, 2, 3, 4]);
        if (scenario == "oversized") File.WriteAllBytes(file, new byte[3 * 1024 * 1024 + 1]);
        using var client = new HttpClient(new RejectNetworkHandler());
        using var cache = new ThumbnailCacheService(paths, new NullAppLogger(), client: client);
        Assert.Null(cache.GetCachedNotificationPath(url));
        Assert.Empty(Directory.GetFiles(paths.ThumbnailCacheDirectory, "*.notification.png"));
    }

    private sealed class RejectNetworkHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new InvalidOperationException("Notification lookup must never fetch an image.");
    }

    [Fact]
    public void InvalidNotificationCacheReleasesHandlesAcrossFiveHundredCycles()
    {
        // No GC, delay, deletion retry or suppression: a held handle fails immediately.
        for (var cycle = 0; cycle < 500; cycle++)
        {
            var paths = AppPaths.Create(Path.Combine(root, cycle.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            Directory.CreateDirectory(paths.ThumbnailCacheDirectory);
            const string url = "https://example.test/invalid-image";
            var file = Path.Combine(paths.ThumbnailCacheDirectory, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url))) + ".img");
            File.WriteAllBytes(file, [1, 2, 3, 4]);
            using var client = new HttpClient(new RejectNetworkHandler());
            using (var cache = new ThumbnailCacheService(paths, new NullAppLogger(), client: client)) Assert.Null(cache.GetCachedNotificationPath(url));
            try
            {
                using (var exclusive = new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) Assert.Equal(4, exclusive.Length);
                File.Delete(file);
            }
            catch (IOException error)
            {
                throw new IOException($"Thumbnail lock cycle={cycle}; currentPid={Environment.ProcessId}; RestartManager owners={FileLockDiagnostics.Owners(file)}", error);
            }
            Directory.Delete(paths.RootDirectory, recursive: true);
        }
    }

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
        Assert.Null(cache.GetCachedNotificationPath("https://example.test/image.png"));
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
