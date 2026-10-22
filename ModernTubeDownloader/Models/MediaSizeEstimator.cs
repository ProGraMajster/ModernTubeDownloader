namespace ModernTubeDownloader.Models;

/// <summary>Advisory selected-stream estimate, not a disk-space guarantee or final mux size.</summary>
public static class MediaSizeEstimator
{
    public static long? Estimate(VideoMetadata metadata, FormatSelection selection, MediaTimeRange range)
    {
        decimal bytes = 0;
        foreach (var id in selection.FormatIds.Distinct())
        {
            var format = metadata.Formats.FirstOrDefault(value => value.Id == id);
            if (format is null) return null;
            var size = format.FileSize ?? format.ApproximateFileSize;
            if (size is > 0) bytes += size.Value;
            else if (format.TotalBitrateKbps is > 0 and < 1_000_000 && metadata.DurationSeconds is > 0 and < 2_592_001)
                bytes += (decimal)format.TotalBitrateKbps.Value * 1000 / 8 * (decimal)metadata.DurationSeconds.Value;
            else return null;
        }
        if (range.Mode != MediaRangeMode.Full && metadata.DurationSeconds is > 0 and < 2_592_001)
        {
            var end = Math.Min(range.End?.TotalSeconds ?? metadata.DurationSeconds.Value, metadata.DurationSeconds.Value);
            bytes *= (decimal)Math.Clamp((end - (range.Start?.TotalSeconds ?? 0)) / metadata.DurationSeconds.Value, 0, 1);
        }
        return bytes is > 0 and < long.MaxValue ? (long)bytes : null;
    }
}
