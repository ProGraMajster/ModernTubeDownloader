namespace ModernTubeDownloader.Settings;

public enum NotificationSound { Default, Silent }
public enum NotificationPresentation { Normal, CenterOnly }

/// <summary>Persisted user intent; native support/permission are queried independently.</summary>
public sealed class NotificationSettings
{
    public bool Enabled { get; set; } = true;
    public bool PrivacyMode { get; set; }
    public bool ShowThumbnails { get; set; } = true;
    public NotificationSound Sound { get; set; }
    public NotificationPresentation Presentation { get; set; }
    public bool DismissOnCancel { get; set; }
    public bool DownloadStarted { get; set; }
    public bool DownloadCompleted { get; set; } = true;
    public bool DownloadFailed { get; set; } = true;
    public bool DownloadRetry { get; set; }
    public bool DownloadCancelled { get; set; }
    public bool QueueCompleted { get; set; } = true;
    public bool DownloadProgress { get; set; }
    public bool LiveWaiting { get; set; } = true;
    public bool LiveStarted { get; set; } = true;
    public bool LiveRecording { get; set; } = true;
    public bool LiveLost { get; set; } = true;
    public bool LiveReconnected { get; set; } = true;
    public bool LivePart { get; set; }
    public bool LiveCompleted { get; set; } = true;
    public bool LivePartial { get; set; } = true;
    public bool LiveFailed { get; set; } = true;
    public bool LiveCancelled { get; set; }
    public bool CriticalErrors { get; set; } = true;
    public bool UpdateReady { get; set; } = true;
    public bool BatchAnalysisCompleted { get; set; }
}
