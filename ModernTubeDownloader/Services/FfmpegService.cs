using System.Globalization;
using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Models;

namespace ModernTubeDownloader.Services;

public sealed class FfmpegService(
    AsyncProcessRunner processRunner,
    ToolManager toolManager,
    IAppLogger logger)
{
    public async Task MergeAsync(
        string videoFile,
        string audioFile,
        string outputFile,
        double? durationSeconds,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        await using var tool = await toolManager.AcquireAsync(ExternalToolKind.Ffmpeg, cancellationToken).ConfigureAwait(false);
        Directory.CreateDirectory(Path.GetDirectoryName(outputFile)!);
        var arguments = new List<string>
        {
            "-hide_banner", "-nostdin", "-y",
            "-i", videoFile,
            "-i", audioFile,
            "-map", "0:v:0",
            "-map", "1:a:0",
            "-c", "copy"
        };
        if (Path.GetExtension(outputFile).Equals(".mp4", StringComparison.OrdinalIgnoreCase))
            arguments.AddRange(["-movflags", "+faststart"]);
        arguments.AddRange(["-progress", "pipe:1", "-nostats", outputFile]);

        void HandleProgress(string line)
        {
            if (line.Equals("progress=end", StringComparison.OrdinalIgnoreCase))
            {
                progress?.Report(100);
                return;
            }

            if (durationSeconds is not > 0 || !line.StartsWith("out_time_us=", StringComparison.Ordinal))
                return;
            if (long.TryParse(line["out_time_us=".Length..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var microseconds))
                progress?.Report(Math.Clamp(microseconds / 1_000_000d / durationSeconds.Value * 100, 0, 99));
        }

        var result = await processRunner.RunAsync(
            new ProcessRunRequest(tool.ExecutablePath, arguments, StandardOutputLine: HandleProgress, StandardErrorLine: line => logger.Info($"ffmpeg: {line}")),
            cancellationToken).ConfigureAwait(false);

        if (!result.IsSuccess)
            throw new ExternalProcessException(ErrorMessageTranslator.FromFfmpeg(result.StandardErrorText), result);
        if (!File.Exists(outputFile) || new FileInfo(outputFile).Length == 0)
            throw new InvalidDataException("FFmpeg reported success but did not create a non-empty output file.");
    }

    public static string ChooseContainer(FormatSelection selection)
    {
        if (!selection.RequiresMerge)
            return selection.AudioOnly ? NormalizeExtension(selection.AudioExtension) : NormalizeExtension(selection.VideoExtension);

        var video = NormalizeExtension(selection.VideoExtension);
        var audio = NormalizeExtension(selection.AudioExtension);
        if (video is "mp4" or "m4v" && audio is "m4a" or "mp4") return "mp4";
        if (video == "webm" && audio == "webm") return "webm";
        return "mkv";
    }

    private static string NormalizeExtension(string? extension)
    {
        var value = (extension ?? "bin").Trim().TrimStart('.').ToLowerInvariant();
        return value.All(char.IsLetterOrDigit) ? value : "bin";
    }
}
