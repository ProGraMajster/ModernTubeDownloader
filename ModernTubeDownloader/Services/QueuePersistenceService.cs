using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Models;

namespace ModernTubeDownloader.Services;

public sealed class QueuePersistenceService(AppPaths paths, IAppLogger logger)
{
    private readonly SemaphoreSlim gate = new(1, 1);

    public async Task<IReadOnlyList<DownloadQueueItem>> LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var loaded = await AtomicJsonFile.ReadAsync<List<DownloadQueueItem?>>(paths.QueueFile, cancellationToken).ConfigureAwait(false) ?? [];
            return loaded.OfType<DownloadQueueItem>().Select(Normalize).ToArray();
        }
        catch (Exception ex)
        {
            logger.Error("Could not read the saved download queue.", ex);
            return [];
        }
    }

    private static DownloadQueueItem Normalize(DownloadQueueItem item)
    {
        if (item.Id == Guid.Empty)
            item.Id = Guid.NewGuid();
        if (item.JobSessionId == Guid.Empty)
            item.JobSessionId = item.Id;
        if (item.LiveSession is { } live)
        {
            if (live.SessionId == Guid.Empty) live.SessionId = item.JobSessionId;
            if (!Enum.IsDefined(live.StartPolicy)) live.StartPolicy = LiveStartPolicy.FromNow;
            live.PartIndex = Math.Max(1, live.PartIndex);
            live.Parts ??= [];
            if (live.RecordedDuration < TimeSpan.Zero) live.RecordedDuration = TimeSpan.Zero;
            // An older saved session may contain signed HLS/DASH URLs from raw
            // extractor JSON. The session never needs them for reconnect.
            item.RawMetadataJson = string.Empty;
        }
        item.SourceUrl ??= string.Empty;
        item.VideoId ??= string.Empty;
        item.Title ??= string.Empty;
        item.Channel ??= string.Empty;
        item.QualityPresetId = QualityPreset.Find(item.QualityPresetId ?? "best").Id;
        item.RequestedRange ??= MediaTimeRange.Full;
        item.SubtitleOptions ??= new();
        item.SubtitleOptions.Languages ??= [];
        item.SponsorBlockOptions ??= new();
        item.SponsorBlockOptions.Categories ??= ["sponsor"];
        if (!Enum.IsDefined(item.PreferredVideoContainer))
            item.PreferredVideoContainer = PreferredVideoContainer.Auto;
        item.TemporaryFiles ??= [];
        item.RawMetadataJson ??= string.Empty;
        if (!Enum.IsDefined(item.Status))
            item.Status = DownloadStatus.Queued;
        item.StatusMessage ??= string.Empty;
        item.StatusMessageKey = string.IsNullOrWhiteSpace(item.StatusMessageKey)
            ? item.Status switch
            {
                DownloadStatus.Queued => "Queue.Detail.Queued",
                DownloadStatus.Waiting => "Queue.Detail.Preparing",
                DownloadStatus.DownloadingVideo => "Queue.Detail.DownloadingVideo",
                DownloadStatus.DownloadingAudio => "Queue.Detail.DownloadingAudio",
                DownloadStatus.Merging => "Queue.Detail.Merging",
                DownloadStatus.Finalizing => "Queue.Detail.Finalizing",
                DownloadStatus.Completed when item.HasPostProcessingWarnings => "Queue.Detail.CompletedWarnings",
                DownloadStatus.Completed => "Queue.Detail.Completed",
                DownloadStatus.Failed => "Queue.Detail.Failed",
                DownloadStatus.Cancelled => "Queue.Detail.Cancelled",
                DownloadStatus.WaitingForLive => "Queue.Detail.WaitingForLive",
                DownloadStatus.RecordingLive => "Queue.Detail.RecordingLive",
                DownloadStatus.ReconnectingLive => "Queue.Detail.ReconnectingLive",
                DownloadStatus.Partial => "Queue.Detail.Partial",
                DownloadStatus.Interrupted => "Queue.Detail.Interrupted",
                _ => null
            }
            : item.StatusMessageKey;
        return item;
    }

    public async Task SaveAsync(IReadOnlyList<DownloadQueueItem> items, CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await AtomicJsonFile.WriteAsync(paths.QueueFile, items, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }
}
