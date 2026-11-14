using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Services;

namespace ModernTubeDownloader.Shell;

/// <summary>Shell-neutral targets; payloads remain validated by their canonical integration boundary.</summary>
internal static class AppNavigation
{
    public static AppPageId ForNotification(NotificationNavigation request) => request.View switch
    { "live" => AppPageId.Live, "history" => AppPageId.History, _ => AppPageId.Downloads };
    public static AppPageId ForExternalRequest(ExternalMediaRequest request)
    {
        if (request.Intent is null && Uri.TryCreate(request.SourceUrl, UriKind.Absolute, out var uri) &&
            uri.Host is "youtube.com" or "www.youtube.com" or "m.youtube.com" && uri.AbsolutePath.StartsWith("/live/", StringComparison.OrdinalIgnoreCase))
            return AppPageId.Live;
        return AppPageId.Downloads;
    }
}
