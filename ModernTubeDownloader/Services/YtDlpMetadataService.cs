using System.Text.Json;
using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Models;
using ModernTubeDownloader.Utilities;

namespace ModernTubeDownloader.Services;

public sealed class YtDlpMetadataService(YtDlpProcessRunner runner, SettingsService settings, IAppLogger logger)
{
    private static readonly TimeSpan PlaylistAnalysisTimeout = TimeSpan.FromMinutes(5);

    public Task<VideoMetadata> AnalyzeAsync(string sourceUrl, CancellationToken cancellationToken = default)
        => AnalyzeCoreAsync(sourceUrl, false, cancellationToken);

    public Task<VideoMetadata> AnalyzeSourceAsync(string sourceUrl, CancellationToken cancellationToken = default)
        => AnalyzeCoreAsync(sourceUrl, true, cancellationToken);

    private async Task<VideoMetadata> AnalyzeCoreAsync(string sourceUrl, bool checkSource, CancellationToken cancellationToken)
    {
        if (!TryValidateSourceUrl(sourceUrl, out var normalizedUrl, out var validationError))
            throw new ArgumentException(validationError, nameof(sourceUrl));

        var arguments = BuildArguments(normalizedUrl, settings.Current.CustomYtDlpArguments,
            settings.Current.UseCookieFile, settings.Current.CookieFilePath);
        if (checkSource)
        {
            var bounded = arguments.ToList();
            var end = bounded.IndexOf("--");
            bounded.InsertRange(end, ["--flat-playlist", "--playlist-end", "50", "--socket-timeout", "20"]);
            if (IsPlaylistUrl(normalizedUrl)) bounded[bounded.IndexOf("--no-playlist")] = "--yes-playlist";
            arguments = bounded;
        }

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

    public async Task<PlaylistMetadata> AnalyzePlaylistAsync(string sourceUrl, CancellationToken cancellationToken = default,
        bool detectedPlaylist = false)
    {
        if (!TryValidateSourceUrl(sourceUrl, out var normalizedUrl, out var validationError))
            throw new ArgumentException(validationError, nameof(sourceUrl));
        if (!detectedPlaylist && !IsPlaylistUrl(normalizedUrl))
            throw new ArgumentException("The URL does not explicitly identify a playlist.", nameof(sourceUrl));

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(PlaylistAnalysisTimeout);
        try
        {
            var result = await runner.RunAsync(BuildPlaylistArguments(normalizedUrl, settings.Current.CustomYtDlpArguments,
                    settings.Current.UseCookieFile, settings.Current.CookieFilePath),
                cancellationToken: timeout.Token).ConfigureAwait(false);
            var json = result.StandardOutput.LastOrDefault(line => !string.IsNullOrWhiteSpace(line));
            if (string.IsNullOrWhiteSpace(json))
                throw new InvalidDataException("yt-dlp completed without returning playlist metadata JSON.");
            var playlist = PlaylistMetadata.Parse(json, normalizedUrl);
            logger.Info($"Analyzed playlist id={playlist.Id}, entries={playlist.EntryCount}.");
            return playlist;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("Playlist analysis exceeded the five-minute limit.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("yt-dlp returned malformed playlist metadata JSON.", ex);
        }
    }

    internal static IReadOnlyList<string> BuildArguments(string normalizedUrl, string? customArguments,
        bool useCookieFile = false, string? cookieFilePath = null)
    {
        var arguments = new List<string>
        {
            "--ignore-config",
            "--no-playlist",
            "--skip-download",
            "--dump-single-json",
            "--no-warnings",
            "--encoding", "utf-8"
        };
        arguments.AddRange(CommandLineArgumentTokenizer.ParseSafeYtDlpArguments(customArguments));
        CookieFileService.AppendCookieArguments(arguments, useCookieFile, cookieFilePath);
        arguments.Add("--");
        arguments.Add(normalizedUrl);
        return arguments;
    }

    internal static IReadOnlyList<string> BuildPlaylistArguments(string normalizedUrl, string? customArguments,
        bool useCookieFile = false, string? cookieFilePath = null)
    {
        var arguments = new List<string>
        {
            "--ignore-config", "--yes-playlist", "--flat-playlist", "--skip-download",
            "--dump-single-json", "--ignore-errors", "--no-warnings", "--encoding", "utf-8"
        };
        arguments.AddRange(CommandLineArgumentTokenizer.ParseSafeYtDlpArguments(customArguments));
        CookieFileService.AppendCookieArguments(arguments, useCookieFile, cookieFilePath);
        arguments.Add("--");
        arguments.Add(normalizedUrl);
        return arguments;
    }

    public static bool IsPlaylistUrl(string sourceUrl)
    {
        if (!TryValidateSourceUrl(sourceUrl, out var normalized, out _)) return false;
        var uri = new Uri(normalized);
        var path = uri.AbsolutePath.TrimEnd('/');
        var leaf = path[(path.LastIndexOf('/') + 1)..];
        if (leaf.Equals("watch", StringComparison.OrdinalIgnoreCase) || leaf.Equals("shorts", StringComparison.OrdinalIgnoreCase))
            return false;
        if (leaf.Equals("playlist", StringComparison.OrdinalIgnoreCase) ||
            leaf.Equals("playlists", StringComparison.OrdinalIgnoreCase) ||
            leaf.Equals("sets", StringComparison.OrdinalIgnoreCase) ||
            leaf.Equals("videoseries", StringComparison.OrdinalIgnoreCase))
            return true;
        return path.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Any(segment => segment.Equals("playlist", StringComparison.OrdinalIgnoreCase) ||
                            segment.Equals("playlists", StringComparison.OrdinalIgnoreCase) ||
                            segment.Equals("sets", StringComparison.OrdinalIgnoreCase));
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
