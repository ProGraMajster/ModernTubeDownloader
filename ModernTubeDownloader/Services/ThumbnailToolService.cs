using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Models;
using ModernTubeDownloader.Utilities;
using SkiaSharp;

namespace ModernTubeDownloader.Services;

public enum ThumbnailExportFormat { Png, Jpeg }
public sealed record ThumbnailExportRecord(string Title, string OutputPath, DateTimeOffset CompletedAt, int Width, int Height);
public sealed record ThumbnailExportResult(string OutputPath, bool HistorySaved);

/// <summary>A caller-owned preview; the display cache retains its own independent bitmap.</summary>
public sealed record ThumbnailPreview(string Title, string MediaId, SKBitmap Bitmap) : IDisposable
{
    public void Dispose() => Bitmap.Dispose();
    public string SuggestedFileName => "thumbnail-" + FileSystemUtilities.SafeIdentifier(MediaId);
}

/// <summary>Thumbnail tooling shares metadata extraction and the bounded thumbnail cache.
/// It does not create a downloader, process runner or queue.</summary>
public sealed class ThumbnailToolService(
    Func<string, CancellationToken, Task<VideoMetadata>> analyze,
    ThumbnailCacheService thumbnails, AppPaths paths, IAppLogger logger)
{
    private readonly SemaphoreSlim historyGate = new(1, 1);
    private string HistoryPath => Path.Combine(paths.RootDirectory, "thumbnail-exports.json");

    public async Task<ThumbnailPreview> PreviewAsync(string url, CancellationToken token = default)
    {
        if (!YtDlpMetadataService.TryValidateSourceUrl(url, out var normalized, out _))
            throw new ArgumentException("ThumbnailTool.InvalidUrl");
        var metadata = await analyze(normalized, token).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(metadata.ThumbnailUrl)) throw new InvalidDataException("ThumbnailTool.NoImage");
        var bitmap = await thumbnails.GetExportCopyAsync(metadata.ThumbnailUrl, token).ConfigureAwait(false)
            ?? throw new InvalidDataException("ThumbnailTool.ImageFailed");
        if (token.IsCancellationRequested) { bitmap.Dispose(); token.ThrowIfCancellationRequested(); }
        return new(metadata.Title ?? "", metadata.Id ?? "", bitmap);
    }

    public async Task<ThumbnailExportResult> ExportAsync(ThumbnailPreview preview, string target, ThumbnailExportFormat format, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(preview);
        if (!Enum.IsDefined(format)) throw new ArgumentOutOfRangeException(nameof(format));
        target = Path.GetFullPath(target);
        var expected = format == ThumbnailExportFormat.Png ? ".png" : ".jpg";
        if (!Path.GetExtension(target).Equals(expected, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("ThumbnailTool.Extension");
        var directory = Path.GetDirectoryName(target)!;
        if (!Directory.Exists(directory)) throw new DirectoryNotFoundException(directory);
        if (File.Exists(target)) throw new IOException("ThumbnailTool.Exists");
        token.ThrowIfCancellationRequested();
        using var bitmap = preview.Bitmap.Copy();
        var pixelWidth = bitmap.Width; var pixelHeight = bitmap.Height;
        // The independent copy survives disposal of the UI preview. Work is bounded
        // by the cache's 16 MP decoded-image limit and never blocks the UI thread.
        using var encoded = await Task.Run(() =>
        {
            token.ThrowIfCancellationRequested();
            using var image = SKImage.FromBitmap(bitmap);
            return image.Encode(format == ThumbnailExportFormat.Png ? SKEncodedImageFormat.Png : SKEncodedImageFormat.Jpeg, 92)
                ?? throw new InvalidDataException("ThumbnailTool.EncodeFailed");
        }, token).ConfigureAwait(false);
        var temporary = Path.Combine(directory, ".mtd-thumbnail-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            await using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            {
                using var data = encoded.AsStream();
                await data.CopyToAsync(file, token).ConfigureAwait(false);
                await file.FlushAsync(token).ConfigureAwait(false);
            }
            token.ThrowIfCancellationRequested();
            File.Move(temporary, target, overwrite: false);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }

        // File commit is the success boundary. A history failure must not invite an
        // accidental retry/overwrite of an already successfully exported image.
        var saved = false;
        await historyGate.WaitAsync().ConfigureAwait(false);
        try
        {
            var records = (await ReadRecentAsync().ConfigureAwait(false)).Prepend(new(preview.Title, target,
                DateTimeOffset.UtcNow, pixelWidth, pixelHeight)).Take(30).ToArray();
            await AtomicJsonFile.WriteAsync(HistoryPath, records).ConfigureAwait(false); saved = true;
        }
        catch (Exception error) { logger.Error("Thumbnail saved but export history could not be saved.", error); }
        finally { historyGate.Release(); }
        return new(target, saved);
    }

    public async Task<IReadOnlyList<ThumbnailExportRecord>> ReadRecentAsync(CancellationToken token = default)
    {
        // Protect startup/UI from an unexpectedly large local history file.
        if (!File.Exists(HistoryPath)) return [];
        const int maximumBytes = 1024 * 1024;
        // Permit atomic replacement while an overview is reading the old file.
        // Bound the actual bytes read, including a file that grows after opening.
        await using var stream = new FileStream(HistoryPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 81920, true);
        if (stream.Length > maximumBytes) throw new InvalidDataException("Thumbnail export history exceeds its limit.");
        using var bytes = new MemoryStream();
        var buffer = new byte[81920];
        while (true)
        {
            var count = await stream.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, maximumBytes + 1 - (int)bytes.Length)), token).ConfigureAwait(false);
            if (count == 0) break;
            bytes.Write(buffer, 0, count);
            if (bytes.Length > maximumBytes) throw new InvalidDataException("Thumbnail export history exceeds its limit.");
        }
        return (System.Text.Json.JsonSerializer.Deserialize<ThumbnailExportRecord[]>(bytes.GetBuffer().AsSpan(0, (int)bytes.Length), JsonDefaults.Options) ?? [])
            .Where(record => record is not null && !string.IsNullOrWhiteSpace(record.OutputPath)).Take(30).ToArray();
    }
}
