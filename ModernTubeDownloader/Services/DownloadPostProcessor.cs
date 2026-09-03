using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Models;
using ModernTubeDownloader.Settings;

namespace ModernTubeDownloader.Services;

internal sealed record DownloadPostProcessingResult(
    string? MetadataSidecarPath,
    IReadOnlyList<string> Warnings);

internal static class DownloadPostProcessor
{
    public static async Task<DownloadPostProcessingResult> ProcessAsync(
        string finalPath,
        DownloadQueueItem item,
        AppSettings settings,
        IAppLogger logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(finalPath);
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(logger);

        var warnings = new List<string>();
        string? sidecarPath = null;

        if (settings.SaveMetadataJsonSidecar && !string.IsNullOrWhiteSpace(item.RawMetadataJson))
        {
            sidecarPath = Path.ChangeExtension(finalPath, ".json");
            try
            {
                await AtomicJsonFile.WriteRawJsonAsync(sidecarPath, item.RawMetadataJson, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                sidecarPath = null;
                warnings.Add("metadata sidecar could not be saved");
                logger.Error($"Could not save metadata sidecar for queue item {item.Id}.", ex);
            }
        }

        if (settings.SetFileCreationTimeFromMediaPublishDate &&
            MediaPublishTimeResolver.TryResolve(item.RawMetadataJson, out var publishTime))
        {
            try
            {
                ApplyCreationTime(finalPath, publishTime);
            }
            catch (Exception ex)
            {
                warnings.Add("file creation date could not be set");
                logger.Error($"Could not set the creation date for queue item {item.Id} from {publishTime.SourceField}.", ex);
            }
        }

        return new DownloadPostProcessingResult(sidecarPath, warnings);
    }

    internal static void ApplyCreationTime(string path, MediaPublishTime publishTime)
    {
        if (publishTime.Precision == MediaPublishTimePrecision.ExactInstant)
        {
            File.SetCreationTimeUtc(path, publishTime.Instant.UtcDateTime);
            return;
        }

        // A YYYYMMDD value has no time zone or clock time. Treat it as the local calendar date
        // at midnight so Windows displays the same date without inventing a UTC offset.
        var calendarDate = DateTime.SpecifyKind(publishTime.Instant.Date, DateTimeKind.Unspecified);
        File.SetCreationTime(path, calendarDate);
    }
}
