using ModernTubeDownloader.Models;

namespace ModernTubeDownloader.Services;

/// <summary>
/// One downloader invocation's observed payload deltas, not OS network traffic.
/// The first counter is a baseline: it can include a resumed partial file. No
/// persisted bytes, protocol overhead or unseen initial block is invented.
/// </summary>
internal sealed class DownloadTransferEstimator(TimeProvider? timeProvider = null)
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;
    private long? previousBytes;
    private long previousTimestamp;

    public DownloadProgress Observe(DownloadProgress progress)
    {
        var now = clock.GetTimestamp();
        var downloading = progress.Status.Equals("downloading", StringComparison.OrdinalIgnoreCase);
        var bytes = progress.DownloadedBytes is >= 0 ? progress.DownloadedBytes : null;
        long delta = 0;
        double? rate = null;
        if (bytes is { } current && previousBytes is { } previous && current >= previous)
        {
            delta = current - previous;
            var elapsed = clock.GetElapsedTime(previousTimestamp, now).TotalSeconds;
            if (downloading && elapsed > 0) rate = delta / elapsed;
        }
        else if (downloading && previousBytes is null && progress.SpeedBytesPerSecond is >= 0 and < 1e15 &&
                 double.IsFinite(progress.SpeedBytesPerSecond.Value))
        {
            // Before two counters exist, the downloader's own value is the only
            // observed rate. Never derive it from a resumed cumulative counter.
            rate = progress.SpeedBytesPerSecond;
        }

        previousBytes = downloading ? bytes : null;
        previousTimestamp = now;
        return progress with { SpeedBytesPerSecond = rate, ObservedBytesDelta = delta };
    }
}

/// <summary>Presentation sampling of the canonical queue counter using its existing timer.</summary>
internal sealed class DownloadTransferSampler(TimeProvider? timeProvider = null)
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;
    private long previousBytes;
    private long previousTimestamp;
    public double CurrentRate { get; private set; }
    public double LastIntervalSeconds { get; private set; }
    // A two-second interval means at least one nominal one-second graph tick
    // was missed. Do not draw an uninterrupted historical curve across it.
    public bool HasHistoryGap => LastIntervalSeconds >= 2;

    public void Reset(long total)
    {
        previousBytes = total;
        previousTimestamp = clock.GetTimestamp();
        CurrentRate = 0;
        LastIntervalSeconds = 0;
    }

    public double Sample(long total)
    {
        var now = clock.GetTimestamp();
        var elapsed = clock.GetElapsedTime(previousTimestamp, now).TotalSeconds;
        if (elapsed <= 0) return CurrentRate;
        LastIntervalSeconds = elapsed;
        CurrentRate = total >= previousBytes ? (total - previousBytes) / elapsed : 0;
        previousBytes = total;
        previousTimestamp = now;
        return CurrentRate;
    }
}
