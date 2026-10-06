using System.Collections.Concurrent;
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

    private async Task<SKBitmap?> LoadAsync(string url, CancellationToken cancellationToken)
    {
        try
        {
            Directory.CreateDirectory(paths.ThumbnailCacheDirectory);
            var cacheFile = Path.Combine(paths.ThumbnailCacheDirectory, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url))) + ".img");
            byte[] bytes;
            if (File.Exists(cacheFile))
            {
                bytes = await File.ReadAllBytesAsync(cacheFile, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                bytes = await client.GetByteArrayAsync(url, cancellationToken).ConfigureAwait(false);
                if (bytes.Length > 12 * 1024 * 1024)
                    throw new InvalidDataException("Thumbnail exceeds the 12 MiB cache limit.");
                await File.WriteAllBytesAsync(cacheFile, bytes, cancellationToken).ConfigureAwait(false);
            }

            var bitmap = SKBitmap.Decode(bytes);
            if (bitmap is null)
                throw new InvalidDataException("The thumbnail data is not a supported image.");
            lock (lifetimeSync)
            {
                if (disposed)
                {
                    bitmap.Dispose();
                    return null;
                }
                memory[url] = bitmap;
                return bitmap;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.Warning($"Could not load thumbnail: {ex.Message}");
            return null;
        }
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
