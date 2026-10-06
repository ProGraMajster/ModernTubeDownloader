using System.Text.RegularExpressions;
using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Models;
using ModernTubeDownloader.Utilities;

namespace ModernTubeDownloader.Services;

public sealed class SubtitleDownloadService(YtDlpProcessRunner runner, SettingsService settings, IAppLogger logger)
{
    public async Task<IReadOnlyList<string>> DownloadAsync(VideoMetadata metadata, SubtitleOptions options,
        string outputDirectory, string container, CancellationToken cancellationToken)
    {
        options.Validate();
        if (!options.Enabled) return [];
        var tracks = SubtitleTrackReader.Read(metadata).Where(track => options.Source switch
        {
            SubtitleSource.Manual => !track.IsAutomatic,
            SubtitleSource.Automatic => track.IsAutomatic,
            _ => true
        }).ToArray();
        if (!tracks.Any(track => options.Languages.Any(language =>
                Regex.IsMatch(track.LanguageCode, $"^{language}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))))
        {
            logger.Warning($"No matching subtitle language MediaId={metadata.Id}.");
            return [];
        }

        Directory.CreateDirectory(outputDirectory);
        var arguments = new List<string>
        {
            "--ignore-config", "--no-playlist", "--skip-download", "--no-simulate",
            "--windows-filenames", "-P", outputDirectory, "-o", "subtitle.%(ext)s"
        };
        arguments.AddRange(CommandLineArgumentTokenizer.ParseSafeYtDlpArguments(settings.Current.CustomYtDlpArguments));
        CookieFileService.AppendCookieArguments(arguments, settings.Current.UseCookieFile, settings.Current.CookieFilePath);
        if (options.Source is SubtitleSource.Manual or SubtitleSource.Both) arguments.Add("--write-subs");
        if (options.Source is SubtitleSource.Automatic or SubtitleSource.Both) arguments.Add("--write-auto-subs");
        arguments.AddRange(["--sub-langs", string.Join(',', options.Languages)]);
        var format = options.Format switch
        {
            SubtitleFormat.Srt => "srt",
            SubtitleFormat.Vtt => "vtt",
            SubtitleFormat.Ass => "ass",
            _ when options.Embed && container == "webm" => "vtt",
            _ when options.Embed => "srt",
            _ => null
        };
        if (format is not null)
            arguments.AddRange(["--sub-format", $"{format}/best", "--convert-subs", format]);
        arguments.AddRange(["--", metadata.WebpageUrl]);
        await runner.RunAsync(arguments, standardErrorLine: line => logger.Info($"yt-dlp subtitles: {line}"),
            cancellationToken: cancellationToken, requireFfmpeg: format is not null).ConfigureAwait(false);
        return Directory.EnumerateFiles(outputDirectory, "subtitle.*", SearchOption.TopDirectoryOnly)
            .Where(path => format is null
                ? Path.GetExtension(path).ToLowerInvariant() is ".srt" or ".vtt" or ".ass"
                : Path.GetExtension(path).Equals($".{format}", StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.OrdinalIgnoreCase).ToArray();
    }
}
