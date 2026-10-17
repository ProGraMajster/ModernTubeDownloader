using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Models;
using ModernTubeDownloader.Services;
using ModernTubeDownloader.Theming;

namespace ModernTubeDownloader.Views;

/// <summary>A projection of canonical service state, shared by the header and status bar.</summary>
internal static class DashboardStatus
{
    public static (string Text, string Token) Read(AppServices services)
    {
        var t = services.Localization; var storage = services.Queue.Storage.State;
        if (storage.IsBlocked) return (t[storage.MessageKey], storage.Kind == StorageFailureKind.DiskFull ? AppThemeTokens.Error : AppThemeTokens.Warning);
        if (services.Tools.EngineStatus == DownloadEngineStatus.Failed) return (t["Header.EngineFailed"], AppThemeTokens.Error);
        if (services.Updates.Ready) return (t.Get("Dashboard.UpdateReady", services.Updates.State.Available?.Version ?? ""), AppThemeTokens.Info);
        if (services.Live.Snapshot().Any(s => s.IsActive)) return (t["Dashboard.LiveActive"], AppThemeTokens.Success);
        if (services.Queue.IsPaused) return (t["Footer.Paused"], AppThemeTokens.Warning);
        var active = services.Queue.Snapshot().Count(i => i.Status is DownloadStatus.DownloadingAudio or DownloadStatus.DownloadingVideo or DownloadStatus.Merging or DownloadStatus.Finalizing);
        if (active > 0) return (t.Get("Footer.Downloading", active), AppThemeTokens.Info);
        return services.Tools.CanAnalyze ? (t["Dashboard.Ready"], AppThemeTokens.Success) : (t["Header.EnginePreparing"], AppThemeTokens.Info);
    }
}
