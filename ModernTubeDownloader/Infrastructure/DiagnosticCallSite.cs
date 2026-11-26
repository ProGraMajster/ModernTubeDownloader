namespace ModernTubeDownloader.Infrastructure;

internal static class DiagnosticCallSite
{
    internal static (DiagnosticModule Module, string? CallSite) Classify(string file, string member)
    {
        var name = Path.GetFileNameWithoutExtension(file);
        var module = name switch
        {
            var s when s.Contains("Organizer", StringComparison.Ordinal) => DiagnosticModule.Organizer,
            var s when s.Contains("Batch", StringComparison.Ordinal) => DiagnosticModule.Batch,
            var s when s.Contains("Update", StringComparison.Ordinal) => DiagnosticModule.Updater,
            var s when s.Contains("Notification", StringComparison.Ordinal) => DiagnosticModule.Notifications,
            var s when s.Contains("History", StringComparison.Ordinal) => DiagnosticModule.History,
            var s when s.Contains("Queue", StringComparison.Ordinal) => DiagnosticModule.Queue,
            var s when s.Contains("Live", StringComparison.Ordinal) => DiagnosticModule.Live,
            var s when s.Contains("Remote", StringComparison.Ordinal) => DiagnosticModule.WebRemote,
            var s when s.Contains("Settings", StringComparison.Ordinal) => DiagnosticModule.Settings,
            var s when s.Contains("Appearance", StringComparison.Ordinal) || s.Contains("Theme", StringComparison.Ordinal) => DiagnosticModule.Appearance,
            var s when s.Contains("Localization", StringComparison.Ordinal) => DiagnosticModule.Localization,
            var s when s.Contains("Tool", StringComparison.Ordinal) || s == "AsyncProcessRunner" => DiagnosticModule.Tools,
            var s when s.Contains("Store", StringComparison.Ordinal) || s.Contains("Storage", StringComparison.Ordinal) ||
                s.Contains("Cache", StringComparison.Ordinal) || s.Contains("Persistence", StringComparison.Ordinal) => DiagnosticModule.Storage,
            var s when s.Contains("Download", StringComparison.Ordinal) || s.StartsWith("YtDlp", StringComparison.Ordinal) ||
                s.StartsWith("Ffmpeg", StringComparison.Ordinal) => DiagnosticModule.Downloads,
            _ => DiagnosticModule.Application
        };
        var site = name.Length > 0 && name.Length <= 80 && member.Length <= 80 &&
            name.All(IsIdentifier) && member.All(IsIdentifier) ? name + "." + member : null;
        return (module, site);
    }

    private static bool IsIdentifier(char c) => char.IsAsciiLetterOrDigit(c) || c is '_' or '.';

    // An allowlist of statuses already emitted by the downloader, never user text.
    internal static int? HttpStatus(string message)
    {
        if (!message.StartsWith("Download failed", StringComparison.Ordinal)) return null;
        var source = message.AsSpan(0, Math.Min(message.Length, 512));
        foreach (var marker in new[] { "HttpStatus=", "HTTP " })
        {
            var offset = source.IndexOf(marker, StringComparison.Ordinal);
            if (offset >= 0 && offset + marker.Length + 3 <= source.Length &&
                int.TryParse(source.Slice(offset + marker.Length, 3), out var code) && code is >= 400 and <= 599) return code;
        }
        return null;
    }
}
