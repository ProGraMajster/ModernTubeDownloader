using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Models;
using ModernTubeDownloader.Utilities;

namespace ModernTubeDownloader.Services;

public sealed class YtDlpDownloadService(YtDlpProcessRunner runner, SettingsService settings, IAppLogger logger)
{
    private const string FilePrefix = "MTD_FILE|";

    public async Task<string> DownloadFormatAsync(
        string sourceUrl,
        string formatId,
        string outputDirectory,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken,
        MediaTimeRange? requestedRange = null,
        SponsorBlockOptions? sponsorBlock = null,
        string? mergeOutputFormat = null,
        LiveStartPolicy? liveStartPolicy = null,
        string? outputTemplate = null)
    {
        requestedRange ??= MediaTimeRange.Full;
        if (requestedRange.Validate(null) is { } error)
            throw new ArgumentException(error, nameof(requestedRange));
        Directory.CreateDirectory(outputDirectory);
        var arguments = new List<string>
        {
            "--ignore-config",
            "--no-playlist",
            "--newline",
            "--progress",
            "--progress-delta", "0.2",
            "--progress-template", YtDlpProgressParser.Template,
            "--windows-filenames",
            "--trim-filenames", "180",
            "--no-mtime",
            "--no-overwrites"
        };

        if (liveStartPolicy is { } policy)
        {
            // ffmpeg consumes video and audio HLS inputs concurrently. yt-dlp's
            // regular multi-format path can wait for the video LIVE stream to end
            // before it starts downloading audio.
            arguments.AddRange(["--downloader", "ffmpeg"]);
            if (policy == LiveStartPolicy.FromStart)
                arguments.Add("--live-from-start");
        }

        arguments.AddRange(CommandLineArgumentTokenizer.ParseSafeYtDlpArguments(settings.Current.CustomYtDlpArguments));
        CookieFileService.AppendCookieArguments(arguments, settings.Current.UseCookieFile, settings.Current.CookieFilePath);
        if (requestedRange.Mode == MediaRangeMode.Custom)
        {
            arguments.AddRange(["--download-sections", requestedRange.ToYtDlpSection()]);
            logger.Info($"Range mode=yt-dlp-sections Requested={requestedRange.DisplayText}; no full-download fallback.");
        }
        if (sponsorBlock?.ToYtDlpCategories() is { } categories)
        {
            arguments.AddRange([sponsorBlock.Mode == SponsorBlockMode.Mark
                ? "--sponsorblock-mark" : "--sponsorblock-remove", categories]);
            if (sponsorBlock.Mode == SponsorBlockMode.Mark)
                arguments.Add("--embed-chapters");
        }
        if (!string.IsNullOrWhiteSpace(mergeOutputFormat))
            arguments.AddRange(["--merge-output-format", mergeOutputFormat]);
        arguments.AddRange([
            "-f", formatId,
            "-P", outputDirectory,
            "-o", outputTemplate ?? settings.Current.FilenameTemplate,
            "--print", $"after_move:{FilePrefix}%(filepath)s",
            "--no-simulate",
            "--",
            sourceUrl
        ]);

        string? reportedFile = null;
        void HandleLine(string line)
        {
            if (YtDlpProgressParser.TryParse(line, out var parsed))
                progress?.Report(parsed);
            else if (line.StartsWith(FilePrefix, StringComparison.Ordinal))
                reportedFile = line[FilePrefix.Length..].Trim();
        }

        await runner.RunAsync(arguments, HandleLine, line => logger.Info($"yt-dlp: {line}"), cancellationToken,
            requireFfmpeg: liveStartPolicy is not null || requestedRange.Mode == MediaRangeMode.Custom ||
                sponsorBlock?.Mode != SponsorBlockMode.Off && sponsorBlock is not null).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(reportedFile) && File.Exists(reportedFile))
            return Path.GetFullPath(reportedFile);

        var files = Directory.EnumerateFiles(outputDirectory)
            .Where(path => !path.EndsWith(".part", StringComparison.OrdinalIgnoreCase) &&
                           !path.EndsWith(".ytdl", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .ToArray();

        return files.Length == 1
            ? Path.GetFullPath(files[0])
            : throw new InvalidDataException("yt-dlp completed but the downloaded file could not be identified.");
    }
}
