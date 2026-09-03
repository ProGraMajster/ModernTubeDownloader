namespace ModernTubeDownloader.Models;

public enum ExternalToolKind
{
    YtDlp,
    Ffmpeg
}

public enum ToolStatus
{
    Missing,
    Downloading,
    Installing,
    Ready,
    CheckingForUpdates,
    UpdateAvailable,
    Updating,
    UpdatePending,
    Failed
}

public sealed record ToolInfo
{
    public required ExternalToolKind Kind { get; init; }
    public required string ToolName { get; init; }
    public ToolStatus Status { get; init; } = ToolStatus.Missing;
    public bool Installed { get; init; }
    public string? InstalledVersion { get; init; }
    public string? LatestVersion { get; init; }
    public string? ExecutablePath { get; init; }
    public string? FfprobePath { get; init; }
    public DateTimeOffset? LastChecked { get; init; }
    public bool UpdateAvailable { get; init; }
    public bool ManagedByApplication { get; init; } = true;
    public string? ErrorMessage { get; init; }
    public long? DownloadedBytes { get; init; }
    public long? TotalBytes { get; init; }
    public double? ProgressPercent { get; init; }

    // Compatibility aliases used by older application code and persisted tests.
    public bool IsDetected => Installed && !string.IsNullOrWhiteSpace(ExecutablePath);
    public string? Path => ExecutablePath;
    public string? Version => InstalledVersion;
    public string? Error => ErrorMessage;

    public static ToolInfo Missing(ExternalToolKind kind, string message) => new()
    {
        Kind = kind,
        ToolName = kind == ExternalToolKind.YtDlp ? "yt-dlp" : "FFmpeg",
        Status = ToolStatus.Missing,
        ErrorMessage = message
    };
}

public enum DownloadEngineStatus
{
    Preparing,
    Ready,
    Failed
}
