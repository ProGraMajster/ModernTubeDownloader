using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace ModernTubeDownloader.Infrastructure;

public static partial class LogSanitizer
{
    private static readonly ConcurrentDictionary<string, byte> CookieFilePaths =
        new(StringComparer.OrdinalIgnoreCase);

    public static void RegisterCookieFilePath(string? path)
    {
        if (!string.IsNullOrWhiteSpace(path)) CookieFilePaths.TryAdd(path.Trim(), 0);
    }

    [GeneratedRegex("(?:https?|file)://[^\\s\\\"'<>]+", RegexOptions.IgnoreCase)]
    private static partial Regex Url();
    [GeneratedRegex(@"(?i)\bBearer\s+[A-Za-z0-9._~+/=-]+")]
    private static partial Regex BearerToken();
    [GeneratedRegex(@"(?i)\b(?:authorization|cookie|set-cookie)\s*:\s*[^\r\n]+")]
    private static partial Regex SensitiveHeader();
    [GeneratedRegex(@"(?i)(authorization|cookie|password|token|api[_-]?key)\s*[:=]\s*[^\s,;]+")]
    private static partial Regex Secret();
    [GeneratedRegex(@"(?im)^.*(?:extracting cookies|cookie database|cookies database|cookies-from-browser|failed to decrypt with dpapi|(?:failed|could not) (?:decrypt|read) cookies?|no cookies found|browser profile not found|profile does not exist|could not find browser|could not find (?:chrome|firefox|edge|brave|opera|chromium|vivaldi) cookies?).*$")]
    private static partial Regex BrowserCookieDiagnostic();

    public static string Redact(string text)
    {
        foreach (var path in CookieFilePaths.Keys.OrderByDescending(value => value.Length))
            text = text.Replace(path, "<cookie-file>", StringComparison.OrdinalIgnoreCase);
        return BrowserCookieDiagnostic().Replace(
            Secret().Replace(BearerToken().Replace(
                SensitiveHeader().Replace(Url().Replace(text, "<url>"), "<sensitive-header>"), "Bearer <redacted>"), "$1=<redacted>"),
            "<browser-cookie-diagnostic>");
    }
}
