using System.Globalization;
using ModernTubeDownloader.Models;

namespace ModernTubeDownloader.Services;

public static class YtDlpProgressParser
{
    public const string Prefix = "MTD_PROGRESS|";
    public const string Template = "download:MTD_PROGRESS|%(progress.status)s|%(progress.downloaded_bytes)s|%(progress.total_bytes)s|%(progress.total_bytes_estimate)s|%(progress.speed)s|%(progress.eta)s|%(progress._percent_str)s";

    public static bool TryParse(string line, out DownloadProgress progress)
    {
        progress = new DownloadProgress(0, null, null, null, null, string.Empty);
        if (!line.StartsWith(Prefix, StringComparison.Ordinal))
            return false;

        var parts = line[Prefix.Length..].Split('|');
        if (parts.Length < 7)
            return false;

        var downloaded = ParseLong(parts[1]);
        var total = ParseLong(parts[2]) ?? ParseLong(parts[3]);
        var speed = ParseDouble(parts[4]);
        var etaSeconds = ParseDouble(parts[5]);
        var percent = ParsePercent(parts[6]);
        if (percent is null && downloaded is { } downloadedValue && total is > 0)
            percent = downloadedValue * 100d / total.Value;

        progress = new DownloadProgress(
            Math.Clamp(percent ?? (parts[0].Equals("finished", StringComparison.OrdinalIgnoreCase) ? 100 : 0), 0, 100),
            downloaded,
            total,
            speed,
            etaSeconds is null ? null : TimeSpan.FromSeconds(Math.Max(0, etaSeconds.Value)),
            parts[0]);
        return true;
    }

    private static long? ParseLong(string value) =>
        long.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var result) ? result : null;

    private static double? ParseDouble(string value) =>
        double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var result) ? result : null;

    private static double? ParsePercent(string value) =>
        ParseDouble(value.Trim().TrimEnd('%'));
}
