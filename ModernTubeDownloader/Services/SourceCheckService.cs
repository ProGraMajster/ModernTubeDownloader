using ModernTubeDownloader.Models;
using ModernTubeDownloader.Infrastructure;

namespace ModernTubeDownloader.Services;

public sealed class SourceCheckService(YtDlpMetadataService metadata, IAppLogger logger)
{
    public async Task<SourceCheckResult> CheckAsync(string sourceUrl, CancellationToken token = default)
    {
        if (!YtDlpMetadataService.TryValidateSourceUrl(sourceUrl, out var url, out _))
            return new(string.Empty, null, null, null, "Downloads.InvalidUrl");
        try
        {
            var result = await metadata.AnalyzeSourceAsync(url, token).ConfigureAwait(false);
            var facts = ExtractorIdentityService.Facts(result);
            var playlist = facts.Kind == SourceMediaKind.Playlist ? PlaylistMetadata.Parse(result.RawJson, url) : null;
            var restricted = facts.Availability is "private" or "needs_auth" or "premium_only" or "subscriber_only";
            return new(url, result, playlist, facts, AuthenticationMayBeRequired: restricted,
                OpenLive: facts.Kind is SourceMediaKind.ActiveLive or SourceMediaKind.Upcoming);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            logger.Error("Source URL metadata check failed.", ex);
            var failure = DownloadFailureClassifier.Classify(ex);
            var auth = failure.Category is DownloadFailureCategory.Authentication or DownloadFailureCategory.Private or
                DownloadFailureCategory.CookieFileRejected or DownloadFailureCategory.CookieFileMissing or
                DownloadFailureCategory.CookieFileUnreadable;
            return new(url, null, null, null, failure.Category == DownloadFailureCategory.Unsupported
                ? "Sources.Unsupported" : failure.UserMessageKey, auth,
                failure.Category is DownloadFailureCategory.Upcoming or DownloadFailureCategory.ActiveLive);
        }
    }
}
