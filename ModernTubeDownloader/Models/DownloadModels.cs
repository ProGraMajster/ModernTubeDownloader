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
    Cancelled,
    WaitingForLive,
    RecordingLive,
    ReconnectingLive,
    Partial,
    Interrupted
}

public sealed class DownloadQueueItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string SourceUrl { get; set; } = string.Empty;
    public string VideoId { get; set; } = string.Empty;
    public string? PlaylistId { get; set; }
    public string? PlaylistTitle { get; set; }
    public int? PlaylistIndex { get; set; }
    public LiveSession? LiveSession { get; set; }
    public Guid JobSessionId { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = string.Empty;
    public string Channel { get; set; } = string.Empty;
    public string? ThumbnailUrl { get; set; }
    public double? DurationSeconds { get; set; }
    public string QualityPresetId { get; set; } = "best";
    public PreferredVideoContainer PreferredVideoContainer { get; set; } = PreferredVideoContainer.Auto;
    public MediaTimeRange RequestedRange { get; set; } = MediaTimeRange.Full;
    public SubtitleOptions SubtitleOptions { get; set; } = new();
    public SponsorBlockOptions SponsorBlockOptions { get; set; } = new();
    public FormatSelection? SelectedFormats { get; set; }
    public double ProgressPercent { get; set; }
    public long? DownloadedBytes { get; set; }
    public long? TotalBytes { get; set; }
    public double? SpeedBytesPerSecond { get; set; }
    public TimeSpan? Eta { get; set; }
    public DownloadStatus Status { get; set; } = DownloadStatus.Queued;
    public string StatusMessage { get; set; } = "Queued";
    public string? StatusMessageKey { get; set; } = "Queue.Detail.Queued";
    public List<string> TemporaryFiles { get; set; } = [];
    public string? FinalFile { get; set; }
    public string? ErrorMessage { get; set; }
    public string? FailureMessageKey { get; set; }
    public string? FailureTechnicalSummary { get; set; }
    public string? FailureCategory { get; set; }
    public int? FailureHttpStatus { get; set; }
    public int AttemptCount { get; set; }
    public int MaximumAttempts { get; set; }
    public int RetryDelaySeconds { get; set; }
    public bool HasPostProcessingWarnings { get; set; }
    public string RawMetadataJson { get; set; } = string.Empty;
    public DateTimeOffset AddedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }

    public static DownloadQueueItem FromMetadata(
        VideoMetadata metadata,
        QualityPreset preset,
        FormatSelection selection,
        PreferredVideoContainer preferredVideoContainer = PreferredVideoContainer.Auto,
        MediaTimeRange? requestedRange = null,
        SubtitleOptions? subtitles = null,
        SponsorBlockOptions? sponsorBlock = null,
        LiveSession? liveSession = null) => new()
    {
        SourceUrl = string.IsNullOrWhiteSpace(metadata.WebpageUrl) ? metadata.OriginalUrl ?? string.Empty : metadata.WebpageUrl,
        VideoId = metadata.Id,
        Title = metadata.Title,
        Channel = metadata.DisplayChannel,
        ThumbnailUrl = metadata.ThumbnailUrl,
        DurationSeconds = metadata.DurationSeconds,
        QualityPresetId = preset.Id,
        PreferredVideoContainer = preferredVideoContainer,
        RequestedRange = requestedRange ?? MediaTimeRange.Full,
        SubtitleOptions = subtitles?.Copy() ?? new(),
        SponsorBlockOptions = sponsorBlock?.Copy() ?? new(),
        SelectedFormats = selection,
        // Active manifests often contain short-lived signed stream URLs. LIVE
        // retries always reanalyze, so persisting that JSON is unnecessary.
        RawMetadataJson = liveSession is null ? metadata.RawJson : string.Empty,
        LiveSession = liveSession
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
    string? QualityPresetId = null,
    string? PlaylistId = null,
    string? PlaylistTitle = null,
    int? PlaylistIndex = null,
    MediaTimeRange? RequestedRange = null,
    int? LivePartIndex = null,
    bool WasLiveRecording = false,
    DateTimeOffset? RecordingStartedAt = null,
    DateTimeOffset? RecordingEndedAt = null,
    TimeSpan? RecordedDuration = null,
    bool WasPartial = false,
    int? PartsCount = null);

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
