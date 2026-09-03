namespace ModernTubeDownloader.Services;

internal static class ErrorMessageTranslator
{
    public static string FromYtDlp(string error)
    {
        var value = error ?? string.Empty;
        if (value.Contains("Private video", StringComparison.OrdinalIgnoreCase)) return "This video is private.";
        if (value.Contains("not available", StringComparison.OrdinalIgnoreCase)) return "The requested media is not available.";
        if (value.Contains("age", StringComparison.OrdinalIgnoreCase) && value.Contains("confirm", StringComparison.OrdinalIgnoreCase)) return "The media is age restricted and may require authentication.";
        if (value.Contains("region", StringComparison.OrdinalIgnoreCase) || value.Contains("country", StringComparison.OrdinalIgnoreCase)) return "The media is not available in this region.";
        if (value.Contains("Sign in", StringComparison.OrdinalIgnoreCase) || value.Contains("login", StringComparison.OrdinalIgnoreCase)) return "The service requires authentication for this media.";
        if (value.Contains("Unsupported URL", StringComparison.OrdinalIgnoreCase)) return "The URL is not supported by the installed yt-dlp version.";
        if (value.Contains("HTTP Error", StringComparison.OrdinalIgnoreCase) || value.Contains("network", StringComparison.OrdinalIgnoreCase)) return "A network error occurred while contacting the media service.";
        return FirstUsefulLine(value) ?? "yt-dlp could not complete the operation. See the application log for details.";
    }

    public static string FromFfmpeg(string error) =>
        FirstUsefulLine(error) ?? "FFmpeg could not create the final file. Temporary source files were kept.";

    private static string? FirstUsefulLine(string value) => value
        .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .LastOrDefault(line => line.Contains("ERROR", StringComparison.OrdinalIgnoreCase))
        ?? value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault();
}
