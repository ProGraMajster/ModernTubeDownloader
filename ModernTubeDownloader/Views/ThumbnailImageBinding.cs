using System.Runtime.CompilerServices;
using ModernFormsNext;
using ModernTubeDownloader.Services;
using SkiaSharp;

namespace ModernTubeDownloader.Views;

// PictureBox does not own externally supplied bitmaps. This adapter owns exactly one
// displayed copy and at most one queued result; late callbacks cannot retarget a recycled row.
internal sealed class ThumbnailImageBinding : IDisposable
{
    private static readonly ConditionalWeakTable<PictureBox, ThumbnailImageBinding> bindings = new();
    private static long ownedBytes;
    private readonly PictureBox target;
    private readonly object sync = new();
    private CancellationTokenSource? cancellation;
    private SKBitmap? displayed;
    private SKBitmap? pending;
    private int version;
    private bool disposed;
    internal static long OwnedBytes => Interlocked.Read(ref ownedBytes);
    internal static ThumbnailImageBinding For(PictureBox target) => bindings.GetValue(target, item => new(item));
    private ThumbnailImageBinding(PictureBox target)
    {
        this.target = target;
        target.Disposed += TargetDisposed;
    }

    internal async Task LoadAsync(ThumbnailCacheService cache, string? url, CancellationToken lifetime,
        Action? loaded = null, Func<bool>? accept = null, Action? unavailable = null)
    {
        Clear();
        int expected;
        CancellationToken token;
        lock (sync)
        {
            if (disposed) return;
            expected = version;
            cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
            token = cancellation.Token;
        }
        SKBitmap? bitmap;
        try { bitmap = await cache.GetAsync(url, token).ConfigureAwait(false); }
        catch (OperationCanceledException) { return; }
        if (bitmap is null)
        {
            Application.RunOnUIThread(() =>
            {
                lock (sync) if (!disposed && version == expected && !token.IsCancellationRequested && accept?.Invoke() != false) unavailable?.Invoke();
            });
            return;
        }
        lock (sync)
        {
            if (disposed || version != expected || token.IsCancellationRequested) { bitmap.Dispose(); return; }
            pending = bitmap;
            Interlocked.Add(ref ownedBytes, Bytes(bitmap));
        }
        Application.RunOnUIThread(() =>
        {
            lock (sync)
            {
                if (disposed || version != expected) return;
                if (token.IsCancellationRequested || accept?.Invoke() == false) { Release(ref pending); return; }
                displayed = pending; pending = null;
                target.Image = displayed;
                loaded?.Invoke();
            }
        });
    }

    internal void Clear()
    {
        lock (sync)
        {
            version++;
            cancellation?.Cancel(); cancellation?.Dispose(); cancellation = null;
            if (!disposed) target.Image = null;
            Release(ref displayed); Release(ref pending);
        }
    }
    private static long Bytes(SKBitmap bitmap) => (long)bitmap.RowBytes * bitmap.Height;
    private static void Release(ref SKBitmap? bitmap)
    {
        if (bitmap is null) return;
        Interlocked.Add(ref ownedBytes, -Bytes(bitmap));
        bitmap.Dispose(); bitmap = null;
    }
    private void TargetDisposed(object? sender, EventArgs e) => Dispose();
    public void Dispose()
    {
        lock (sync)
        {
            if (disposed) return;
            Clear(); disposed = true;
            target.Disposed -= TargetDisposed;
        }
    }
}
