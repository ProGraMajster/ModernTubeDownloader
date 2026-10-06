using ModernTubeDownloader.Models;

namespace ModernTubeDownloader.Services;

public sealed record LiveAvailabilityResult(MediaAvailabilityKind Kind, DateTimeOffset? ScheduledStartAt);

public interface ILiveAvailabilityProbe
{
    Task<LiveAvailabilityResult> ProbeAsync(string sourceUrl, CancellationToken cancellationToken);
}

public sealed class YtDlpLiveAvailabilityProbe(YtDlpMetadataService metadata) : ILiveAvailabilityProbe
{
    public async Task<LiveAvailabilityResult> ProbeAsync(string sourceUrl, CancellationToken cancellationToken)
    {
        try
        {
            var result = await metadata.AnalyzeAsync(sourceUrl, cancellationToken).ConfigureAwait(false);
            DateTimeOffset? scheduledStart = result.ReleaseTimestamp is { } timestamp && timestamp > 0
                ? DateTimeOffset.FromUnixTimeSeconds(timestamp) : null;
            return new LiveAvailabilityResult(MediaAvailabilityPolicy.Classify(result), scheduledStart);
        }
        catch (Exception ex) when (ex is not OperationCanceledException &&
                                   DownloadFailureClassifier.Classify(ex).Category == DownloadFailureCategory.Upcoming)
        {
            // Some YouTube upcoming pages exit before emitting metadata JSON.
            return new LiveAvailabilityResult(MediaAvailabilityKind.Upcoming, null);
        }
    }
}
