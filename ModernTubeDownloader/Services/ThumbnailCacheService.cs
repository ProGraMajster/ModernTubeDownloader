using System.Security.Cryptography;
using System.Text;
using ModernTubeDownloader.Infrastructure;
using SkiaSharp;

namespace ModernTubeDownloader.Services;

public sealed class ThumbnailCacheService : IDisposable
{
    private readonly AppPaths paths;
    private readonly IAppLogger logger;
    private readonly HttpClient client;
    private readonly bool ownsClient;
    private readonly Func<bool>? isEnabled;
    private readonly Dictionary<string, Request> pending = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Entry> memory = new(StringComparer.Ordinal);
    private readonly LinkedList<string> lru = new();
    private readonly CancellationTokenSource lifetimeCancellation = new();
    private readonly object lifetimeSync = new();
    private readonly SemaphoreSlim workers;
    private readonly long memoryBudget;
    private long cachedBytes;
    private long residentBytes;
    private int activeLoads;
    private bool disposed;
    internal const int MaximumImageBytes = 12 * 1024 * 1024;
    internal const int MaximumImageDimension = 8192;
    internal const long MaximumImagePixels = 16 * 1024 * 1024;
    private const int EncodedLimit = 12 * 1024 * 1024;
    private const long PixelLimit = 16 * 1024 * 1024;
    public long DecodedBytes { get { lock (lifetimeSync) return residentBytes; } }
    public long CachedBytes { get { lock (lifetimeSync) return cachedBytes; } }
    public int CachedCount { get { lock (lifetimeSync) return memory.Count; } }
    public int PendingCount { get { lock (lifetimeSync) return pending.Count; } }
    public int ActiveLoads => Volatile.Read(ref activeLoads);

    private sealed class Entry(SKBitmap bitmap)
    {
        internal readonly SKBitmap Bitmap = bitmap;
        internal readonly long Bytes = (long)bitmap.RowBytes * bitmap.Height;
        internal int References = 1; // The in-flight request retains it until every waiter leaves.
        internal LinkedListNode<string>? Node;
    }
    private sealed class Request(CancellationToken lifetime)
    {
        internal readonly CancellationTokenSource Cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
        internal readonly TaskCompletionSource<Entry?> Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int Waiters;
        internal Entry? Entry;
    }

    public ThumbnailCacheService(AppPaths paths, IAppLogger logger, Func<bool>? isEnabled = null, HttpClient? client = null,
        long memoryBudgetBytes = 32 * 1024 * 1024, int maximumParallelLoads = 4)
    {
        if (memoryBudgetBytes <= 0) throw new ArgumentOutOfRangeException(nameof(memoryBudgetBytes));
        if (maximumParallelLoads is < 1 or > 16) throw new ArgumentOutOfRangeException(nameof(maximumParallelLoads));
        this.paths = paths; this.logger = logger; this.isEnabled = isEnabled;
        memoryBudget = memoryBudgetBytes; workers = new SemaphoreSlim(maximumParallelLoads);
        this.client = client ?? new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        ownsClient = client is null;
        if (!this.client.DefaultRequestHeaders.UserAgent.Any())
            this.client.DefaultRequestHeaders.UserAgent.ParseAdd("ModernTubeDownloader/1.0");
    }

    /// <summary>Returns a bounded, caller-owned bitmap. Dispose it after detaching it from any UI.
    /// Copies keep displayed images valid when the shared cache evicts an entry or is disposed.</summary>
    public async Task<SKBitmap?> GetAsync(string? url, CancellationToken cancellationToken = default)
    {
        if (isEnabled?.Invoke() == false || string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https")) return null;
        Request request;
        lock (lifetimeSync)
        {
            if (disposed) return null;
            cancellationToken.ThrowIfCancellationRequested();
            if (memory.TryGetValue(url, out var cached))
            {
                lru.Remove(cached.Node!); lru.AddFirst(cached.Node!);
                return cached.Bitmap.Copy();
            }
            if (!pending.TryGetValue(url, out request!))
            {
                request = new Request(lifetimeCancellation.Token);
                pending.Add(url, request);
                // Disk reads and decoding never execute synchronously on the caller's UI thread.
                _ = Task.Run(() => LoadAndPublishAsync(url, request));
            }
            request.Waiters++;
        }
        try
        {
            var entry = await request.Completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            lock (lifetimeSync)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return disposed || entry is null ? null : entry.Bitmap.Copy();
            }
        }
        finally
        {
            lock (lifetimeSync)
            {
                if (--request.Waiters == 0)
                {
                    if (pending.TryGetValue(url, out var current) && ReferenceEquals(current, request)) pending.Remove(url);
                    // Cancellation stops requests that are still waiting for a decoding slot.
                    request.Cancellation.Cancel();
                    if (request.Entry is { } entry) { Release(entry); request.Entry = null; }
                    if (request.Completion.Task.IsCompleted) request.Cancellation.Dispose();
                }
            }
        }
    }

    /// <summary>Explicit export preserves source resolution and is independent of automatic display.
    /// The caller owns the result; exports share the bounded decoding worker limit.</summary>
    public async Task<SKBitmap?> GetExportCopyAsync(string url, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) return null;
        CancellationTokenSource linked;
        lock (lifetimeSync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetimeCancellation.Token);
        }
        using (linked)
        {
            try
            {
                return await Task.Run(async () =>
                {
                    await workers.WaitAsync(linked.Token).ConfigureAwait(false);
                    Interlocked.Increment(ref activeLoads);
                    try { return await LoadBitmapAsync(url, linked.Token, MaximumImageDimension).ConfigureAwait(false); }
                    finally { Interlocked.Decrement(ref activeLoads); workers.Release(); }
                }, linked.Token).ConfigureAwait(false);
            }
            catch (Exception error) when (error is not OperationCanceledException)
            { logger.Warning($"Could not export thumbnail: {error.GetType().Name}."); return null; }
        }
    }

    internal async Task<SKBitmap?> GetPreviewCopyAsync(string? url, CancellationToken token)
    {
        using var original = await GetAsync(url, token).ConfigureAwait(false);
        if (original is null) return null;
        var ratio = Math.Min(1d, 320d / Math.Max(original.Width, original.Height));
        return original.Resize(new SKImageInfo(Math.Max(1, (int)(original.Width * ratio)),
            Math.Max(1, (int)(original.Height * ratio))), new SKSamplingOptions(SKFilterMode.Linear));
    }

    private async Task LoadAndPublishAsync(string url, Request request)
    {
        SKBitmap? bitmap = null;
        try
        {
            var token = request.Cancellation.Token;
            await workers.WaitAsync(token).ConfigureAwait(false);
            Interlocked.Increment(ref activeLoads);
            PerformanceDiagnostics.Increment("thumbnail.requests");
            using var timing = PerformanceDiagnostics.Measure("thumbnail.load");
            try { bitmap = await LoadBitmapAsync(url, token).ConfigureAwait(false); }
            finally { Interlocked.Decrement(ref activeLoads); workers.Release(); }
            lock (lifetimeSync)
            {
                token.ThrowIfCancellationRequested();
                if (bitmap is not null && !disposed && request.Waiters > 0)
                {
                    var entry = new Entry(bitmap); bitmap = null;
                    residentBytes += entry.Bytes;
                    request.Entry = entry;
                    entry.References++; entry.Node = lru.AddFirst(url); memory.Add(url, entry); cachedBytes += entry.Bytes;
                    while (cachedBytes > memoryBudget && lru.Last is { } oldest)
                    {
                        var evicted = memory[oldest.Value]; memory.Remove(oldest.Value); lru.RemoveLast();
                        cachedBytes -= evicted.Bytes; Release(evicted);
                    }
                }
                request.Completion.TrySetResult(request.Entry);
            }
        }
        catch (OperationCanceledException) { request.Completion.TrySetCanceled(); }
        catch (Exception error)
        {
            logger.Warning($"Could not load thumbnail: {error.GetType().Name}.");
            request.Completion.TrySetResult(null);
        }
        finally
        {
            bitmap?.Dispose();
            lock (lifetimeSync)
            {
                if (request.Waiters == 0) request.Cancellation.Dispose();
                PerformanceDiagnostics.Set("thumbnail.pending", pending.Count);
                PerformanceDiagnostics.Set("thumbnail.active", activeLoads);
                PerformanceDiagnostics.Set("thumbnail.cachedBytes", cachedBytes);
            }
        }
    }

    private void Release(Entry entry)
    {
        if (--entry.References != 0) return;
        residentBytes -= entry.Bytes; entry.Bitmap.Dispose();
    }

    private async Task<SKBitmap?> LoadBitmapAsync(string url, CancellationToken cancellationToken, int maximumDimension = 512)
    {
        Directory.CreateDirectory(paths.ThumbnailCacheDirectory);
        var cacheFile = CachePath(url);
        byte[] bytes;
        var cachedOnDisk = File.Exists(cacheFile);
        if (cachedOnDisk)
        {
            await using var stream = new FileStream(cacheFile, FileMode.Open, FileAccess.Read, FileShare.Read, 16384, true);
            if (stream.Length > EncodedLimit) throw new InvalidDataException("Thumbnail size limit.");
            bytes = await ReadBoundedAsync(stream, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > EncodedLimit) throw new InvalidDataException("Thumbnail size limit.");
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            bytes = await ReadBoundedAsync(stream, cancellationToken).ConfigureAwait(false);

        }
        cancellationToken.ThrowIfCancellationRequested();
        using var data = SKData.CreateCopy(bytes);
        using var codec = SKCodec.Create(data);
        if (codec is null || codec.Info.Width <= 0 || codec.Info.Height <= 0 || codec.Info.Width > MaximumImageDimension || codec.Info.Height > MaximumImageDimension || (long)codec.Info.Width * codec.Info.Height > PixelLimit)
            throw new InvalidDataException("Invalid thumbnail dimensions.");
        var dimensions = codec.GetScaledDimensions(Math.Min(1f, maximumDimension / (float)Math.Max(codec.Info.Width, codec.Info.Height)));
        using var decoded = SKBitmap.Decode(codec, new SKImageInfo(dimensions.Width, dimensions.Height));
        if (decoded is null) return null;
        cancellationToken.ThrowIfCancellationRequested();
        var scale = Math.Min(1d, maximumDimension / (double)Math.Max(decoded.Width, decoded.Height));
        var result = scale < 1 ? decoded.Resize(new SKImageInfo(Math.Max(1, (int)(decoded.Width * scale)), Math.Max(1, (int)(decoded.Height * scale))),
            new SKSamplingOptions(SKFilterMode.Linear)) : decoded.Copy();
        try
        {
            // Validate and decode before publishing to disk. Invalid images must not
            // poison the persistent cache; an existing cache file is never deleted.
            if (result is not null && !cachedOnDisk)
            {
                var temporary = cacheFile + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    await File.WriteAllBytesAsync(temporary, bytes, cancellationToken).ConfigureAwait(false);
                    File.Move(temporary, cacheFile, true);
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }
            return result;
        }
        catch { result?.Dispose(); throw; }

    }

    private static async Task<byte[]> ReadBoundedAsync(Stream source, CancellationToken token)
    {
        using var buffer = new MemoryStream();
        var block = new byte[16384];
        int count;
        while ((count = await source.ReadAsync(block, token).ConfigureAwait(false)) != 0)
        {
            if (buffer.Length + count > EncodedLimit) throw new InvalidDataException("Thumbnail size limit.");
            buffer.Write(block, 0, count);
        }
        return buffer.ToArray();
    }

    private string CachePath(string url) => Path.Combine(paths.ThumbnailCacheDirectory,
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url))) + ".img");

    private const int NotificationImageLimit = 3 * 1024 * 1024;
    private const long NotificationPixelLimit = 4 * 1024 * 1024;

    /// <summary>Reuse the persistent cache, normalizing locally to a bounded Windows-compatible PNG.
    /// Never downloads an image; the derived file survives service disposal and native presentation.</summary>
    public string? GetCachedNotificationPath(string? url)
    {
        if (disposed || isEnabled?.Invoke() == false || string.IsNullOrWhiteSpace(url)) return null;
        var file = CachePath(url);
        try
        {
            // Serializes creation with disposal and concurrent notification lookups. No network I/O.
            lock (lifetimeSync)
            {
                if (disposed || pending.ContainsKey(url)) return null;
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
            if (disposed) return;
            disposed = true;
            lifetimeCancellation.Cancel();
            foreach (var entry in memory.Values) Release(entry);
            memory.Clear(); lru.Clear(); cachedBytes = 0;
            if (ownsClient) client.Dispose();
        }
        // Active requests own linked tokens and release their slot/entry in their finally paths.
        lifetimeCancellation.Dispose();
    }
}
