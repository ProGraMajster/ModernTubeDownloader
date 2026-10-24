using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using ModernTubeDownloader.Infrastructure;
using SkiaSharp;

namespace ModernTubeDownloader.Services;

public sealed class ThumbnailCacheService : IDisposable
{
    internal const int MaximumImageBytes = 12 * 1024 * 1024;
    internal const int MaximumImageDimension = 8192;
    internal const long MaximumImagePixels = 16 * 1024 * 1024;
    private readonly AppPaths paths;
    private readonly IAppLogger logger;
    private readonly HttpClient client;
    private readonly bool ownsClient;
    private readonly Func<bool>? isEnabled;
    private readonly ConcurrentDictionary<string, Lazy<Task<SKBitmap?>>> pending = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, SKBitmap> memory = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource lifetimeCancellation = new();
    private readonly object lifetimeSync = new();
    private bool disposed;

    public ThumbnailCacheService(AppPaths paths, IAppLogger logger, Func<bool>? isEnabled = null, HttpClient? client = null)
    {
        this.paths = paths;
        this.logger = logger;
        this.isEnabled = isEnabled;
        this.client = client ?? new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        ownsClient = client is null;
        if (!this.client.DefaultRequestHeaders.UserAgent.Any())
            this.client.DefaultRequestHeaders.UserAgent.ParseAdd("ModernTubeDownloader/1.0");
    }

    public async Task<SKBitmap?> GetAsync(string? url, CancellationToken cancellationToken = default)
    {
        if (disposed || isEnabled?.Invoke() == false || string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            return null;
        if (memory.TryGetValue(url, out var cached))
            return cached;

        var lazy = pending.GetOrAdd(url, key => new Lazy<Task<SKBitmap?>>(
            () => LoadAndReleaseAsync(key), LazyThreadSafetyMode.ExecutionAndPublication));
        return await lazy.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<SKBitmap?> LoadAndReleaseAsync(string url)
    {
        try { return await LoadAsync(url, lifetimeCancellation.Token).ConfigureAwait(false); }
        finally { pending.TryRemove(url, out _); }
    }

    /// <summary>An explicit tool request is independent of automatic thumbnail display.
    /// The caller owns the returned bitmap and cancellation stops its own I/O.</summary>
    public async Task<SKBitmap?> GetExportCopyAsync(string url, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) return null;
        CancellationTokenSource linked;
        lock (lifetimeSync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            cancellationToken.ThrowIfCancellationRequested();
            if (memory.TryGetValue(url, out var cached)) return cached.Copy();
            linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetimeCancellation.Token);
        }
        using (linked) return await LoadAsync(url, linked.Token, retain: false).ConfigureAwait(false);
    }

    // A paged preview owns these small copies. Paging must not grow the shared
    // in-memory cache to the size of an entire playlist.
    internal async Task<SKBitmap?> GetPreviewCopyAsync(string? url, CancellationToken token)
    {
        if (isEnabled?.Invoke() == false || string.IsNullOrWhiteSpace(url)) return null;
        using var original = await GetExportCopyAsync(url, token).ConfigureAwait(false);
        if (original is null) return null;
        var ratio = Math.Min(1d, 320d / Math.Max(original.Width, original.Height));
        return original.Resize(new SKImageInfo(Math.Max(1, (int)(original.Width * ratio)),
            Math.Max(1, (int)(original.Height * ratio))), new SKSamplingOptions(SKFilterMode.Linear));
    }

    private static async Task<byte[]> ReadBoundedAsync(Stream source, CancellationToken token)
    {
        using var buffer = new MemoryStream(); var chunk = new byte[81920];
        while (await source.ReadAsync(chunk, token).ConfigureAwait(false) is > 0 and var count)
        {
            if (buffer.Length + count > MaximumImageBytes) throw new InvalidDataException("Thumbnail exceeds the 12 MiB cache limit.");
            buffer.Write(chunk, 0, count);
        }
        return buffer.ToArray();
    }

    private async Task<SKBitmap?> LoadAsync(string url, CancellationToken cancellationToken, bool retain = true)
    {
        try
        {
            Directory.CreateDirectory(paths.ThumbnailCacheDirectory);
            var cacheFile = CachePath(url);
            byte[] bytes;
            var fromCache = File.Exists(cacheFile);
            if (fromCache)
            {
                await using var file = new FileStream(cacheFile, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
                if (file.Length > MaximumImageBytes) throw new InvalidDataException("Cached thumbnail exceeds the 12 MiB limit.");
                bytes = await ReadBoundedAsync(file, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength > MaximumImageBytes)
                    throw new InvalidDataException("Thumbnail exceeds the 12 MiB cache limit.");
                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                bytes = await ReadBoundedAsync(stream, cancellationToken).ConfigureAwait(false);
            }

            using var data = SKData.CreateCopy(bytes);
            using var codec = SKCodec.Create(data);
            if (codec is null || codec.Info.Width <= 0 || codec.Info.Height <= 0 ||
                codec.Info.Width > MaximumImageDimension || codec.Info.Height > MaximumImageDimension || (long)codec.Info.Width * codec.Info.Height > MaximumImagePixels)
                throw new InvalidDataException("Invalid thumbnail or image dimensions exceed the limit.");
            cancellationToken.ThrowIfCancellationRequested();
            var bitmap = SKBitmap.Decode(codec);
            if (bitmap is null)
                throw new InvalidDataException("The thumbnail data is not a supported image.");
            try
            {
                if (!fromCache)
                {
                    var temporary = cacheFile + "." + Guid.NewGuid().ToString("N") + ".tmp";
                    try
                    {
                        await File.WriteAllBytesAsync(temporary, bytes, cancellationToken).ConfigureAwait(false);
                        try { File.Move(temporary, cacheFile, overwrite: false); }
                        catch (IOException) when (File.Exists(cacheFile)) { /* A concurrent cache request won. */ }
                    }
                    finally { if (File.Exists(temporary)) File.Delete(temporary); }
                }
                cancellationToken.ThrowIfCancellationRequested();
            }
            catch { bitmap.Dispose(); throw; }
            lock (lifetimeSync)
            {
                if (disposed)
                {
                    bitmap.Dispose();
                    return null;
                }
                if (retain) memory[url] = bitmap;
                return bitmap;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.Warning($"Could not load thumbnail: {ex.Message}");
            return null;
        }
    }

    private string CachePath(string url) => Path.Combine(paths.ThumbnailCacheDirectory,
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url))) + ".img");

    private const int NotificationImageLimit = 3 * 1024 * 1024;
    private const long NotificationPixelLimit = 4 * 1024 * 1024;

    /// <summary>Reuse the persistent cache, normalizing locally to a bounded Windows-compatible PNG.
    /// Never downloads an image; the derived file survives service disposal and native presentation.</summary>
    public string? GetCachedNotificationPath(string? url)
    {
        if (disposed || isEnabled?.Invoke() == false || string.IsNullOrWhiteSpace(url) || pending.ContainsKey(url)) return null;
        var file = CachePath(url);
        try
        {
            // Serializes creation with disposal and concurrent notification lookups. No network I/O.
            lock (lifetimeSync)
            {
                if (disposed) return null;
                var exists = File.Exists(file);
                var size = exists ? new FileInfo(file).Length : 0;
                logger.Info($"Notification thumbnail cache: key={Path.GetFileNameWithoutExtension(file)}, path={file}, exists={exists}, extension={Path.GetExtension(file)}, bytes={size}.");
                if (!exists || size is <= 0 or > NotificationImageLimit) return null;
                // SKFileStream uses an inheritable CRT file handle on Windows. A concurrent
                // child-process launch can inherit it even after codec disposal here.
                // Managed FileStream is non-inheritable; close it BEFORE entering Skia.
                using var sourceData = ReadNotificationData(file);
                if (sourceData is null) return null;
                using var codec = SKCodec.Create(sourceData);
                if (codec is null) { logger.Warning("Notification thumbnail rejected: invalid image header."); return null; }
                logger.Info($"Notification thumbnail decoded header: format={codec.EncodedFormat}, width={codec.Info.Width}, height={codec.Info.Height}.");
                if (codec.Info.Width <= 0 || codec.Info.Height <= 0 || (long)codec.Info.Width * codec.Info.Height > NotificationPixelLimit) return null;
                var output = Path.ChangeExtension(file, ".notification.png");
                if (File.Exists(output) && new FileInfo(output).Length is > 0 and <= NotificationImageLimit)
                {
                    using var existingData = ReadNotificationData(output);
                    using var existing = existingData is null ? null : SKCodec.Create(existingData);
                    if (existing?.EncodedFormat == SKEncodedImageFormat.Png && existing.Info.Width <= 512 && existing.Info.Height <= 512)
                        return output;
                }
                using var bitmap = SKBitmap.Decode(codec);
                if (bitmap is null) return null;
                var scale = Math.Min(1d, 512d / Math.Max(bitmap.Width, bitmap.Height));
                using var resized = scale < 1 ? bitmap.Resize(new SKImageInfo(Math.Max(1, (int)(bitmap.Width * scale)),
                    Math.Max(1, (int)(bitmap.Height * scale))), new SKSamplingOptions(SKFilterMode.Linear)) : null;
                using var image = SKImage.FromBitmap(resized ?? bitmap);
                using var png = image.Encode(SKEncodedImageFormat.Png, 100);
                if (png is null || png.Size > NotificationImageLimit) return null;
                var pendingFile = output + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    using (var stream = File.Create(pendingFile)) png.SaveTo(stream);
                    File.Move(pendingFile, output, overwrite: true);
                }
                finally { if (File.Exists(pendingFile)) File.Delete(pendingFile); }
                logger.Info($"Notification thumbnail normalized: path={output}, extension=.png, format=Png, bytes={new FileInfo(output).Length}; persistent application-owned cache.");
                return output;
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { logger.Warning($"Notification thumbnail cache unavailable: {error.GetType().Name}."); return null; }
    }

    private static SKData? ReadNotificationData(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length is <= 0 or > NotificationImageLimit) return null;
        var bytes = new byte[(int)stream.Length];
        stream.ReadExactly(bytes);
        return SKData.CreateCopy(bytes);
    }

    public void Dispose()
    {
        lock (lifetimeSync)
        {
            if (disposed)
                return;
            disposed = true;
            lifetimeCancellation.Cancel();
            if (ownsClient)
                client.Dispose();
            foreach (var bitmap in memory.Values.Distinct())
                bitmap.Dispose();
            memory.Clear();
        }
        lifetimeCancellation.Dispose();
    }
}
