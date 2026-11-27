namespace ModernTubeDownloader.Organizer;

internal sealed class OrganizerPathException(string contextKey, string? listName, Exception cause)
    : IOException(DownloadOrganizerService.SafeError(cause), cause)
{
    internal string ContextKey { get; } = contextKey;
    internal string? ListName { get; } = listName;
}
