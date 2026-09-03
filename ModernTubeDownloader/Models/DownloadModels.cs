namespace ModernTubeDownloader.Models;

public enum DownloadStatus
{
    Queued,
    Waiting,
    DownloadingVideo,
    DownloadingAudio,
    Merging,
    Finalizing,
    Completed,
    Failed,
    Cancelled
}

public sealed class DownloadQueueItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string SourceUrl { get; set; } = string.Empty;
    public string VideoId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Channel { get; set; } = string.Empty;
    public string? ThumbnailUrl { get; set; }
    public double? DurationSeconds { get; set; }
    public string QualityPresetId { get; set; } = "best";
    public FormatSelection? SelectedFormats { get; set; }
    public double ProgressPercent { get; set; }
    public long? DownloadedBytes { get; set; }
    public long? TotalBytes { get; set; }
    public double? SpeedBytesPerSecond { get; set; }
    public TimeSpan? Eta { get; set; }
    public DownloadStatus Status { get; set; } = DownloadStatus.Queued;
    public string StatusMessage { get; set; } = "Queued";
    public List<string> TemporaryFiles { get; set; } = [];
    public string? FinalFile { get; set; }
    public string? ErrorMessage { get; set; }
    public bool HasPostProcessingWarnings { get; set; }
    public string RawMetadataJson { get; set; } = string.Empty;
    public DateTimeOffset AddedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }

    public static DownloadQueueItem FromMetadata(VideoMetadata metadata, QualityPreset preset, FormatSelection selection) => new()
    {
        SourceUrl = string.IsNullOrWhiteSpace(metadata.WebpageUrl) ? metadata.OriginalUrl ?? string.Empty : metadata.WebpageUrl,
        VideoId = metadata.Id,
        Title = metadata.Title,
        Channel = metadata.DisplayChannel,
        ThumbnailUrl = metadata.ThumbnailUrl,
        DurationSeconds = metadata.DurationSeconds,
        QualityPresetId = preset.Id,
        SelectedFormats = selection,
        RawMetadataJson = metadata.RawJson
    };
}

public sealed record DownloadProgress(
    double Percent,
    long? DownloadedBytes,
    long? TotalBytes,
    double? SpeedBytesPerSecond,
    TimeSpan? Eta,
    string Status);

public sealed record DownloadHistoryEntry(
    Guid QueueItemId,
    string Title,
    string SourceUrl,
    string VideoId,
    string Channel,
    string Quality,
    string FinalPath,
    DateTimeOffset CompletedAt,
    string Status,
    string? ThumbnailUrl = null,
    string? QualityPresetId = null);

public enum QueueChangeKind
{
    Collection,
    Item,
    Order,
    Pause
}

public sealed class QueueChangedEventArgs(QueueChangeKind kind, Guid? itemId = null) : EventArgs
{
    public QueueChangeKind Kind { get; } = kind;
    public Guid? ItemId { get; } = itemId;
}
