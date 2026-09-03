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
    private readonly ConcurrentDictionary<string, Lazy<Task<SKBitmap?>>> pending = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, SKBitmap> memory = new(StringComparer.Ordinal);
    private bool disposed;

    public ThumbnailCacheService(AppPaths paths, IAppLogger logger)
    {
        this.paths = paths;
        this.logger = logger;
        client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("ModernTubeDownloader/1.0");
    }

    public Task<SKBitmap?> GetAsync(string? url, CancellationToken cancellationToken = default)
    {
        if (disposed || string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            return Task.FromResult<SKBitmap?>(null);
        if (memory.TryGetValue(url, out var cached))
            return Task.FromResult<SKBitmap?>(cached);

        var lazy = pending.GetOrAdd(url, key => new Lazy<Task<SKBitmap?>>(() => LoadAsync(key, cancellationToken), LazyThreadSafetyMode.ExecutionAndPublication));
        return AwaitAndReleaseAsync(url, lazy);
    }

    private async Task<SKBitmap?> AwaitAndReleaseAsync(string url, Lazy<Task<SKBitmap?>> lazy)
    {
        try { return await lazy.Value.ConfigureAwait(false); }
        finally { pending.TryRemove(new KeyValuePair<string, Lazy<Task<SKBitmap?>>>(url, lazy)); }
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
            memory[url] = bitmap;
            return bitmap;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.Warning($"Could not load thumbnail: {ex.Message}");
            return null;
        }
    }

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        client.Dispose();
        foreach (var bitmap in memory.Values.Distinct())
            bitmap.Dispose();
        memory.Clear();
    }
}
