using ModernTubeDownloader.Models;

namespace ModernTubeDownloader.Services;

/// <summary>Copied under the queue lock; never exposes mutable queue records to the Batch UI.</summary>
public sealed record QueueProgressItem(Guid Id, DownloadStatus Status, double Percent, long? EstimatedBytes,
    long? StreamTotalBytes, long? StreamDownloadedBytes, double? BytesPerSecond);

public sealed record QueueProgressSummary(int Total, int Completed, int Failed, int Settled,
    double Percent, long KnownBytes, int UnknownSizes, TimeSpan? TransferRemaining)
{
    public static QueueProgressSummary Build(IEnumerable<QueueProgressItem> source, bool paused)
    {
        var rows = source.ToArray();
        var completed = 0; var failed = 0; var settled = 0; var unknown = 0;
        double progress = 0, speed = 0; decimal bytes = 0, remaining = 0; var remainingUnknown = false;
        foreach (var row in rows)
        {
            var done = row.Status is DownloadStatus.Completed or DownloadStatus.Failed or DownloadStatus.Cancelled or DownloadStatus.Partial;
            if (row.Status == DownloadStatus.Completed) completed++;
            if (row.Status is DownloadStatus.Failed or DownloadStatus.Cancelled or DownloadStatus.Partial) failed++;
            if (done) settled++;
            // yt-dlp percentages restart for audio/merge. Count only settled jobs
            // toward total completion rather than fabricating monotonic media progress.
            if (done) progress++;
            var size = row.EstimatedBytes is > 0 ? row.EstimatedBytes : row.StreamTotalBytes;
            if (size is > 0) bytes += size.Value; else unknown++;
            if (!done)
            {
                if (size is > 0) remaining += size.Value; else remainingUnknown = true;
                if (row.Status is DownloadStatus.DownloadingVideo or DownloadStatus.DownloadingAudio &&
                    row.BytesPerSecond is > 0 and < 1e15 && double.IsFinite(row.BytesPerSecond.Value))
                    speed += row.BytesPerSecond.Value;
            }
        }
        // Deliberately conservative: no subtraction of a different stream's byte
        // counter; excludes merge/remux/retries and is explicitly labelled approximate.
        var seconds = speed > 0 ? (double)remaining / speed : double.NaN;
        TimeSpan? eta = settled == rows.Length ? TimeSpan.Zero : !paused && !remainingUnknown &&
            double.IsFinite(seconds) && seconds is >= 0 and < 31_536_000 ? TimeSpan.FromSeconds(seconds) : null;
        return new(rows.Length, completed, failed, settled, rows.Length == 0 ? 0 : 100 * progress / rows.Length,
            (long)Math.Min(bytes, long.MaxValue), unknown, eta);
    }
}
