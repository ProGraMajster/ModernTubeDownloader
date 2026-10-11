using ModernTubeDownloader.Models;

namespace ModernTubeDownloader.Services;

/// <summary>Evaluates an allowlisted intent against current trusted application state,
/// never a path or URL from the native payload. Missing/stale state is navigation only.</summary>
public static class NotificationActionPolicy
{
    public static string? Resolve(NotificationNavigation request, DownloadStatus? download,
        string? trustedPath, Func<string?, bool> fileExists, LiveSessionState? live)
    {
        if (!NotificationNavigation.TryParse(request.Encode(), out _)) return null;
        if (request.Group == "downloads")
        {
            if (request.Action == "retry" && download == DownloadStatus.Failed) return "retry";
            if (request.Action is "open-file" or "open-folder" && !string.IsNullOrWhiteSpace(trustedPath) && fileExists(trustedPath)) return request.Action;
        }
        else if (request.Group == "live" && request.Action == "stop-save" && live == LiveSessionState.Recording) return "stop-save";
        return null;
    }
}
