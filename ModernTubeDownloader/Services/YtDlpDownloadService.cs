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
        CancellationToken cancellationToken)
    {
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

        arguments.AddRange(CommandLineArgumentTokenizer.ParseSafeYtDlpArguments(settings.Current.CustomYtDlpArguments));
        arguments.AddRange([
            "-f", formatId,
            "-P", outputDirectory,
            "-o", settings.Current.FilenameTemplate,
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

        await runner.RunAsync(arguments, HandleLine, line => logger.Info($"yt-dlp: {line}"), cancellationToken).ConfigureAwait(false);

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
