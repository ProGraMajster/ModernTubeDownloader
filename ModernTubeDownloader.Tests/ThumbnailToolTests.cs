using System.Net;
using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Models;
using ModernTubeDownloader.Services;
using SkiaSharp;

namespace ModernTubeDownloader.Tests;

public sealed class ThumbnailToolTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "mtd-thumbnail-tool-" + Guid.NewGuid().ToString("N"));
    private static byte[] ImageBytes()
    {
        using var bitmap = new SKBitmap(320, 180); bitmap.Erase(SKColors.CornflowerBlue);
        using var image = SKImage.FromBitmap(bitmap); using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    [Fact]
    public async Task PreviewAndRealPngJpegExportSurviveReloadWithoutOverwritingFiles()
    {
        var paths = AppPaths.Create(root); paths.EnsureCreated();
        var calls = 0;
        using var client = new HttpClient(new Handler((_, _) => { calls++; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(ImageBytes()) }); }));
        using var cache = new ThumbnailCacheService(paths, new NullAppLogger(), () => false, client);
        var service = new ThumbnailToolService((url, _) => Task.FromResult(new VideoMetadata { Id = "../unsafe:name?", Title = "Łódź 日本語", ThumbnailUrl = "https://example.test/image" }), cache, paths, new NullAppLogger());
        Assert.Null(await cache.GetAsync("https://example.test/image"));
        using var preview = await service.PreviewAsync("https://example.test/video");
        Assert.Equal(1, calls); Assert.Equal(320, preview.Bitmap.Width);
        Assert.DoesNotContain("..", preview.SuggestedFileName); Assert.DoesNotContain(':', preview.SuggestedFileName);
        foreach (var format in new[] { ThumbnailExportFormat.Png, ThumbnailExportFormat.Jpeg })
        {
            var target = Path.Combine(root, "Łódź 日本語." + (format == ThumbnailExportFormat.Png ? "png" : "jpg"));
            var result = await service.ExportAsync(preview, target, format); Assert.True(result.HistorySaved);
            var original = await File.ReadAllBytesAsync(target);
            using var data = SKData.CreateCopy(original); using var codec = SKCodec.Create(data);
            Assert.Equal(format == ThumbnailExportFormat.Png ? SKEncodedImageFormat.Png : SKEncodedImageFormat.Jpeg, codec.EncodedFormat);
            Assert.Equal(320, codec.Info.Width); Assert.Equal(180, codec.Info.Height);
            await Assert.ThrowsAsync<IOException>(() => service.ExportAsync(preview, target, format));
            Assert.Equal(original, await File.ReadAllBytesAsync(target));
        }
        var reopened = new ThumbnailToolService((_, _) => throw new InvalidOperationException(), cache, paths, new NullAppLogger());
        Assert.Equal(2, (await reopened.ReadRecentAsync()).Count);
        await Assert.ThrowsAsync<ArgumentException>(() => reopened.PreviewAsync("file:///invalid"));
        await Assert.ThrowsAsync<ArgumentException>(() => service.ExportAsync(preview, Path.Combine(root, "wrong.bmp"), ThumbnailExportFormat.Png));
        await Assert.ThrowsAsync<DirectoryNotFoundException>(() => service.ExportAsync(preview, Path.Combine(root, "missing", "image.png"), ThumbnailExportFormat.Png));
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        var cancelledTarget = Path.Combine(root, "cancelled.png");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ExportAsync(preview, cancelledTarget, ThumbnailExportFormat.Png, cancelled.Token));
        Assert.False(File.Exists(cancelledTarget)); Assert.Empty(Directory.GetFiles(root, ".mtd-thumbnail-*.tmp"));
    }

    [Fact]
    public async Task ExplicitCancellationStopsTheRequestAndMissingThumbnailIsReported()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelledRequest = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var client = new HttpClient(new Handler(async (_, token) =>
        {
            started.TrySetResult();
            try { await Task.Delay(Timeout.Infinite, token); }
            catch (OperationCanceledException) { cancelledRequest.TrySetResult(); throw; }
            throw new InvalidOperationException();
        }));
        var paths = AppPaths.Create(root); paths.EnsureCreated();
        using var cache = new ThumbnailCacheService(paths, new NullAppLogger(), client: client);
        var service = new ThumbnailToolService((_, _) => Task.FromResult(new VideoMetadata { ThumbnailUrl = "https://example.test/image" }), cache, paths, new NullAppLogger());
        using var cancellation = new CancellationTokenSource();
        var pending = service.PreviewAsync("https://example.test/video", cancellation.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5)); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        await cancelledRequest.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Empty(Directory.GetFiles(paths.ThumbnailCacheDirectory));
        var noImage = new ThumbnailToolService((_, _) => Task.FromResult(new VideoMetadata()), cache, paths, new NullAppLogger());
        Assert.Equal("ThumbnailTool.NoImage", (await Assert.ThrowsAsync<InvalidDataException>(() => noImage.PreviewAsync("https://example.test/video"))).Message);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DeclaredAndChunkedOversizedImagesAreRejectedBeforeCaching(bool contentLength)
    {
        var paths = AppPaths.Create(root); paths.EnsureCreated();
        using var client = new HttpClient(new Handler((_, _) =>
        {
            Stream stream = new MemoryStream(new byte[ThumbnailCacheService.MaximumImageBytes + 1]);
            if (!contentLength) stream = new NonSeekableStream(stream);
            var content = new StreamContent(stream);
            if (contentLength) content.Headers.ContentLength = ThumbnailCacheService.MaximumImageBytes + 1;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }));
        using var cache = new ThumbnailCacheService(paths, new NullAppLogger(), client: client);
        Assert.Null(await cache.GetExportCopyAsync("https://example.test/huge"));
        Assert.Empty(Directory.GetFiles(paths.ThumbnailCacheDirectory));
    }

    [Fact]
    public async Task HistoryFailureAfterFileCommitStillReturnsSuccessfulExport()
    {
        var paths = AppPaths.Create(root); paths.EnsureCreated();
        Directory.CreateDirectory(Path.Combine(root, "thumbnail-exports.json"));
        using var cache = new ThumbnailCacheService(paths, new NullAppLogger());
        var service = new ThumbnailToolService((_, _) => throw new InvalidOperationException(), cache, paths, new NullAppLogger());
        using var preview = new ThumbnailPreview("QA", "source", new SKBitmap(32, 18));
        var target = Path.Combine(root, "export.png");
        var result = await service.ExportAsync(preview, target, ThumbnailExportFormat.Png);
        Assert.False(result.HistorySaved);
        Assert.Equal(target, result.OutputPath);
        // SKFileStream's CRT handle can be inherited by an unrelated concurrent
        // child launch even though Decode(path) closes it synchronously here.
        // Read via a non-inheritable managed handle, as the production cache does.
        using var decoded = SKBitmap.Decode(await File.ReadAllBytesAsync(target));
        Assert.Equal(32, decoded.Width); Assert.Equal(18, decoded.Height);
        using (var exclusive = new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.None))
            Assert.True(exclusive.Length > 0);
        Assert.Empty(Directory.GetFiles(root, ".mtd-thumbnail-*.tmp"));
    }

    [Fact]
    public async Task SmallEncodedImageWithExcessiveDimensionsIsRejected()
    {
        using var bitmap = new SKBitmap(ThumbnailCacheService.MaximumImageDimension + 1, 1);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        var paths = AppPaths.Create(root); paths.EnsureCreated();
        using var client = new HttpClient(new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(data.ToArray()) })));
        using var cache = new ThumbnailCacheService(paths, new NullAppLogger(), client: client);
        Assert.Null(await cache.GetExportCopyAsync("https://example.test/too-wide.png"));
        Assert.Empty(Directory.GetFiles(paths.ThumbnailCacheDirectory));
    }

    [Fact]
    public async Task OversizedExportHistoryIsRejectedBeforeDeserialization()
    {
        var paths = AppPaths.Create(root); paths.EnsureCreated();
        await File.WriteAllBytesAsync(Path.Combine(root, "thumbnail-exports.json"), new byte[1024 * 1024 + 1]);
        using var cache = new ThumbnailCacheService(paths, new NullAppLogger());
        var service = new ThumbnailToolService((_, _) => throw new InvalidOperationException(), cache, paths, new NullAppLogger());
        await Assert.ThrowsAsync<InvalidDataException>(() => service.ReadRecentAsync());
    }

    [Fact]
    public void CleanupDiagnosticsDoNotSuppressALockedFileFailure()
    {
        Directory.CreateDirectory(root);
        var file = Path.Combine(root, "fixture.lock");
        using var held = new FileStream(file, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
        var error = Assert.Throws<IOException>(Dispose);
        Assert.IsType<IOException>(error.InnerException);
        Assert.Contains($"currentPid={Environment.ProcessId}", error.Message, StringComparison.Ordinal);
        Assert.Contains("fixture.lock", error.Message, StringComparison.Ordinal);
        Assert.Contains("RestartManager owners=", error.Message, StringComparison.Ordinal);
        Assert.True(File.Exists(file));
    }

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> action) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => action(request, cancellationToken);
    }
    private sealed class NonSeekableStream(Stream inner) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => inner.ReadAsync(buffer, cancellationToken);
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); base.Dispose(disposing); }
    }
    public void Dispose()
    {
        if (!Directory.Exists(root)) return;
        try
        {
            Directory.Delete(root, true);
        }
        catch (IOException error)
        {
            // Observe a failing cleanup once, without retrying deletion or
            // suppressing the original exception. Owners are a point-in-time
            // observation; "none-reported" does not disprove a transient lock.
            string diagnostics;
            try
            {
                diagnostics = string.Join(Environment.NewLine,
                    Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                        .Take(32)
                        .Select(file => $"file={file}; RestartManager owners={FileLockDiagnostics.Owners(file)}"));
            }
            catch (Exception diagnosticError)
            {
                diagnostics = $"Lock observation unavailable: {diagnosticError.GetType().Name}: {diagnosticError.Message}";
            }
            throw new IOException(
                $"Thumbnail fixture cleanup failed; utc={DateTimeOffset.UtcNow:O}; root={root}; currentPid={Environment.ProcessId}; " +
                $"originalHResult=0x{error.HResult:X8}; retained-file observations (at most 32):{Environment.NewLine}{diagnostics}", error);
        }
    }
}
