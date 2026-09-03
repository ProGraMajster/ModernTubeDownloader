using System.Text.Json;
using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Models;

namespace ModernTubeDownloader.Services;

public sealed class YtDlpMetadataService(YtDlpProcessRunner runner, IAppLogger logger)
{
    public async Task<VideoMetadata> AnalyzeAsync(string sourceUrl, CancellationToken cancellationToken = default)
    {
        if (!TryValidateSourceUrl(sourceUrl, out var normalizedUrl, out var validationError))
            throw new ArgumentException(validationError, nameof(sourceUrl));

        var arguments = new[]
        {
            "--ignore-config",
            "--no-playlist",
            "--skip-download",
            "--dump-single-json",
            "--no-warnings",
            "--encoding", "utf-8",
            "--",
            normalizedUrl
        };

        var result = await runner.RunAsync(arguments, cancellationToken: cancellationToken).ConfigureAwait(false);
        var json = result.StandardOutput.LastOrDefault(line => !string.IsNullOrWhiteSpace(line));
        if (string.IsNullOrWhiteSpace(json))
            throw new InvalidDataException("yt-dlp completed without returning metadata JSON.");

        try
        {
            var metadata = JsonSerializer.Deserialize<VideoMetadata>(json, JsonDefaults.Options)
                ?? throw new InvalidDataException("yt-dlp returned an empty metadata object.");
            metadata.RawJson = json;
            metadata.OriginalUrl ??= normalizedUrl;
            if (string.IsNullOrWhiteSpace(metadata.WebpageUrl))
                metadata.WebpageUrl = normalizedUrl;
            logger.Info($"Analyzed media id={metadata.Id}, formats={metadata.Formats.Count}.");
            return metadata;
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("yt-dlp returned malformed metadata JSON.", ex);
        }
    }

    public static bool TryValidateSourceUrl(string? value, out string normalized, out string error)
    {
        normalized = value?.Trim() ?? string.Empty;
        if (!Uri.TryCreate(normalized, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https") || string.IsNullOrWhiteSpace(uri.Host))
        {
            error = "Enter a valid absolute HTTP or HTTPS media URL.";
            return false;
        }

        normalized = uri.AbsoluteUri;
        error = string.Empty;
        return true;
    }
}
