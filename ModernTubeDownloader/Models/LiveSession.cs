using System.Text.Json.Serialization;

namespace ModernTubeDownloader.Models;

public enum LiveStartPolicy
{
    FromNow,
    FromStart
}

public enum LiveSessionState
{
    Pending, WaitingForLive, Starting, Recording, Reconnecting, Finalizing,
    Completed, Partial, Failed, Cancelled, Interrupted
}

/// <summary>Independent durable LIVE intent; never admitted by the VOD queue.</summary>
public sealed class LiveSession
{
    public Guid SessionId { get; set; } = Guid.NewGuid();
    [JsonIgnore] public Guid Id { get => SessionId; set => SessionId = value; }
    public string SourceUrl { get; set; } = string.Empty;
    public string MediaId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Channel { get; set; } = string.Empty;
    public string? ThumbnailUrl { get; set; }
    public string? SourceLiveStatus { get; set; }
    public LiveSessionState State { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? MonitoringStartedAt { get; set; }
    public DateTimeOffset? RecordingEndedAt { get; set; }
    public string QualityPresetId { get; set; } = "best";
    public PreferredVideoContainer Container { get; set; } = PreferredVideoContainer.Mkv;
    public string? WorkspacePath { get; set; }
    public long BytesWritten { get; set; }
    public double? CurrentSpeed { get; set; }
    public int AttemptCount { get; set; }
    public int MaximumAttempts { get; set; }
    public DateTimeOffset? RetryAt { get; set; }
    public string? FailureCategory { get; set; }
    public string? FailureMessageKey { get; set; }
    public string? StatusMessageKey { get; set; }
    public bool PreservePartial { get; set; } = true;
    public bool ResumeEnabled { get; set; } = true;
    public bool PreservePartialOnCancel { get; set; }
    public LiveStartPolicy StartPolicy { get; set; }
    public bool WaitForScheduledStart { get; set; }
    public DateTimeOffset? ScheduledStartAt { get; set; }
    public DateTimeOffset? NextCheckAt { get; set; }
    public DateTimeOffset? RecordingStartedAt { get; set; }
    public TimeSpan RecordedDuration { get; set; }
    public int ResumeCount { get; set; }
    public int PartIndex { get; set; } = 1;
    public List<string> Parts { get; set; } = [];
    public bool StopRequested { get; set; }
    [JsonIgnore] public bool CancelRequested { get; set; }
    public bool Interrupted { get; set; }
    public string? RecoveryWarningKey { get; set; }

    public LiveSession Copy()
    {
        var copy = (LiveSession)MemberwiseClone();
        copy.Parts = [.. Parts];
        return copy;
    }

    [JsonIgnore] public bool IsActive => State is LiveSessionState.Starting or LiveSessionState.Recording or
        LiveSessionState.Reconnecting or LiveSessionState.Finalizing;
    [JsonIgnore] public bool CanResume => State is LiveSessionState.Partial or LiveSessionState.Interrupted or
        LiveSessionState.Failed or LiveSessionState.Cancelled;
}
