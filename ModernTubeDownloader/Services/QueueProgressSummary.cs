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
        var accumulator = new Accumulator();
        foreach (var row in source) accumulator.Add(row.Status, row.EstimatedBytes, row.StreamTotalBytes, row.BytesPerSecond);
        return accumulator.Complete(paused);
    }

    internal struct Accumulator
    {
        private int total, completed, failed, settled, unknown;
        private double speed;
        private decimal bytes, remaining;
        private bool remainingUnknown;

        internal void Add(DownloadStatus status, long? estimated, long? streamTotal, double? bytesPerSecond)
        {
            total++;
            if (status == DownloadStatus.Completed) completed++;
            if (status is DownloadStatus.Failed or DownloadStatus.Cancelled or DownloadStatus.Partial) failed++;
            var done = status is DownloadStatus.Completed or DownloadStatus.Failed or DownloadStatus.Cancelled or DownloadStatus.Partial;
            if (done) settled++;
            var size = estimated is > 0 ? estimated : streamTotal;
            if (size is > 0) bytes += size.Value; else unknown++;
            if (done) return;

            if (size is > 0) remaining += size.Value; else remainingUnknown = true;
            if (status is DownloadStatus.DownloadingVideo or DownloadStatus.DownloadingAudio &&
                bytesPerSecond is > 0 and < 1e15 && double.IsFinite(bytesPerSecond.Value))
                speed += bytesPerSecond.Value;
        }

        internal readonly QueueProgressSummary Complete(bool paused)
        {
            // Percent counts settled jobs and the conservative ETA excludes stream switches.
            var seconds = speed > 0 ? (double)remaining / speed : double.NaN;
            TimeSpan? eta = total == 0 ? null : settled == total ? TimeSpan.Zero : !paused && !remainingUnknown &&
                double.IsFinite(seconds) && seconds is >= 0 and < 31_536_000 ? TimeSpan.FromSeconds(seconds) : null;
            return new(total, completed, failed, settled, total == 0 ? 0 : 100d * settled / total,
                (long)Math.Min(bytes, long.MaxValue), unknown, eta);
        }
    }
}
