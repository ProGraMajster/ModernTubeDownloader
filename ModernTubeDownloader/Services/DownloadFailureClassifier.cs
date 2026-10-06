using System.Text.RegularExpressions;
using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Models;

namespace ModernTubeDownloader.Services;

public enum DownloadFailureCategory
{
    Unknown, HttpForbidden, HttpNotFound, RateLimited, ServerError, Timeout,
    Network, Private, Unavailable, Authentication, Unsupported, Format,
    Ffmpeg, Disk, ActiveLive, Upcoming, ArchiveProcessing, Cancelled,
    CookieProfileUnavailable, CookieDatabaseLocked, CookieUnavailable,
    CookieFileMissing, CookieFileUnreadable, CookieFileUnsupported, CookieFileRejected,
    LiveDisconnected
}

public sealed record DownloadFailure(
    DownloadFailureCategory Category, int? HttpStatus, bool Retryable,
    string UserMessageKey, string TechnicalSummary, int? ExitCode);

public static partial class DownloadFailureClassifier
{
    [GeneratedRegex(@"\b(?:HTTP(?:\s+Error)?\s*[: ]\s*|status(?:\s+code)?\s*[:= ]\s*)([1-5][0-9]{2})\b", RegexOptions.IgnoreCase)]
    private static partial Regex HttpCode();

    public static DownloadFailure Classify(Exception error, DownloadStatus stage = DownloadStatus.Waiting,
        bool isLiveCapture = false)
    {
        var process = error as ExternalProcessException;
        var exitCode = process?.Result?.ExitCode;
        var raw = process?.Result?.StandardErrorText ?? error.Message;
        var status = error is HttpRequestException http ? (int?)http.StatusCode : null;
        if (status is null && HttpCode().Match(raw) is { Success: true } match &&
            int.TryParse(match.Groups[1].Value, out var parsed))
            status = parsed;

        DownloadFailureCategory category;
        if (error is OperationCanceledException) category = DownloadFailureCategory.Cancelled;
        else if (error is DownloadOptionException) category = DownloadFailureCategory.Unsupported;
        else if (error is MediaAvailabilityException media) category = media.Kind switch
        {
            MediaAvailabilityKind.ActiveLive => DownloadFailureCategory.ActiveLive,
            MediaAvailabilityKind.Upcoming => DownloadFailureCategory.Upcoming,
            MediaAvailabilityKind.Processing => DownloadFailureCategory.ArchiveProcessing,
            MediaAvailabilityKind.Private => DownloadFailureCategory.Private,
            _ => DownloadFailureCategory.Unavailable
        };
        else if (error is CookieFileException cookieFile) category = cookieFile.Reason switch
        {
            CookieFileError.Missing => DownloadFailureCategory.CookieFileMissing,
            CookieFileError.Unreadable => DownloadFailureCategory.CookieFileUnreadable,
            CookieFileError.UnsupportedFormat => DownloadFailureCategory.CookieFileUnsupported,
            _ => DownloadFailureCategory.CookieFileRejected
        };
        else if (Contains(raw, "does not look like a netscape", "invalid cookie file", "cookies file must be in netscape",
                     "cookies file is not valid", "could not load cookies", "failed to load cookies"))
            category = DownloadFailureCategory.CookieFileRejected;
        else if (Contains(raw, "cookie database is locked", "database is locked", "could not copy chrome cookie database")) category = DownloadFailureCategory.CookieDatabaseLocked;
        else if (Contains(raw, "could not find browser", "browser profile not found", "profile does not exist", "could not find firefox cookies database")) category = DownloadFailureCategory.CookieProfileUnavailable;
        else if (Contains(raw, "could not decrypt cookies", "failed to decrypt cookies", "failed to decrypt with dpapi", "could not read cookies", "no cookies found")) category = DownloadFailureCategory.CookieUnavailable;
        else if (Contains(raw, "private video", "private playlist", "playlist is private", "this video is private", "members-only")) category = DownloadFailureCategory.Private;
        else if (Contains(raw, "sign in", "login required", "only works when logged-in", "log in to", "age-restricted", "confirm your age", "authentication required")) category = DownloadFailureCategory.Authentication;
        else if (Contains(raw, "unsupported url")) category = DownloadFailureCategory.Unsupported;
        else if (Contains(raw, "this live event will begin")) category = DownloadFailureCategory.Upcoming;
        else if (Contains(raw, "no downloadable", "no stream-copy-compatible", "no audio stream", "requested format is not available", "no video formats")) category = DownloadFailureCategory.Format;
        else if (Contains(raw, "video unavailable", "video is unavailable", "not available", "has been removed", "deleted video")) category = DownloadFailureCategory.Unavailable;
        else if (status == 403) category = DownloadFailureCategory.HttpForbidden;
        else if (status == 404) category = DownloadFailureCategory.HttpNotFound;
        else if (status == 429) category = DownloadFailureCategory.RateLimited;
        else if (status is >= 500 and <= 599) category = DownloadFailureCategory.ServerError;
        else if (error is TimeoutException || Contains(raw, "timed out", "timeout")) category = DownloadFailureCategory.Timeout;
        else if (Contains(raw, "connection reset", "connection aborted", "network is unreachable", "temporary failure", "network error", "network failure")) category = DownloadFailureCategory.Network;
        else if (Contains(raw, "no space left", "disk full", "access to the path", "permission denied") || error is IOException) category = DownloadFailureCategory.Disk;
        else if (stage == DownloadStatus.Merging) category = DownloadFailureCategory.Ffmpeg;
        else if (isLiveCapture && process is not null && exitCode is { } liveExit && liveExit != 0)
            category = DownloadFailureCategory.LiveDisconnected;
        else category = DownloadFailureCategory.Unknown;

        var retryable = category is DownloadFailureCategory.HttpForbidden or DownloadFailureCategory.RateLimited or
            DownloadFailureCategory.ServerError or DownloadFailureCategory.Timeout or DownloadFailureCategory.Network or
            DownloadFailureCategory.LiveDisconnected;
        var key = error is DownloadOptionException option
            ? DownloadOptionCompatibilityValidator.MessageKey(option.Issue)
            : $"Error.Download.{category}";
        var summary = status switch
        {
            403 => "HTTP 403 Forbidden",
            404 => "HTTP 404 Not Found",
            429 => "HTTP 429 Too Many Requests",
            >= 500 and <= 599 => $"HTTP {status} Server Error",
            { } code => $"HTTP {code}",
            _ => category.ToString()
        };
        if (exitCode is { } exit) summary += $"; yt-dlp/FFmpeg exit code {exit}";
        summary += $"; stage {stage}";
        return new DownloadFailure(category, status, retryable, key, summary, exitCode);
    }

    private static bool Contains(string text, params string[] fragments) =>
        fragments.Any(fragment => text.Contains(fragment, StringComparison.OrdinalIgnoreCase));
}
