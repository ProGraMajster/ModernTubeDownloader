using System.Globalization;
using System.Text.Json;
using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Models;

namespace ModernTubeDownloader.Services;

public sealed record MediaProbeResult(double? DurationSeconds, bool HasVideo, bool HasAudio, string FormatName,
    double? StartTimeSeconds = null);

public sealed class FfmpegService(
    AsyncProcessRunner processRunner,
    ToolManager toolManager,
    IAppLogger logger)
{
    public async Task EmbedSubtitlesAsync(string mediaFile, IReadOnlyList<string> subtitleFiles,
        string outputFile, CancellationToken cancellationToken)
    {
        var arguments = BuildSubtitleEmbedArguments(mediaFile, subtitleFiles, outputFile);
        await using var tool = await toolManager.AcquireAsync(ExternalToolKind.Ffmpeg, cancellationToken).ConfigureAwait(false);
        var result = await processRunner.RunAsync(new ProcessRunRequest(tool.ExecutablePath, arguments), cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
            throw new ExternalProcessException(ErrorMessageTranslator.FromFfmpeg(result.StandardErrorText), result);
        if (!File.Exists(outputFile) || new FileInfo(outputFile).Length == 0)
            throw new InvalidDataException("FFmpeg did not create the subtitle-embedded file.");
    }

    internal static IReadOnlyList<string> BuildSubtitleEmbedArguments(string mediaFile,
        IReadOnlyList<string> subtitleFiles, string outputFile)
    {
        if (subtitleFiles.Count == 0) throw new ArgumentException("No subtitle files were downloaded.", nameof(subtitleFiles));
        var container = Path.GetExtension(outputFile).ToLowerInvariant();
        var subtitleCodec = container switch
        {
            ".mp4" => "mov_text",
            ".mkv" => "srt",
            ".webm" => "webvtt",
            _ => throw new NotSupportedException($"Subtitle embedding is unavailable for {container}.")
        };
        var arguments = new List<string> { "-hide_banner", "-nostdin", "-y", "-i", mediaFile };
        foreach (var subtitle in subtitleFiles)
            arguments.AddRange(["-i", subtitle]);
        arguments.AddRange(["-map", "0"]);
        for (var index = 0; index < subtitleFiles.Count; index++)
            arguments.AddRange(["-map", $"{index + 1}:0"]);
        arguments.AddRange(["-c:v", "copy", "-c:a", "copy", "-c:s", subtitleCodec]);
        for (var index = 0; index < subtitleFiles.Count; index++)
        {
            var language = SubtitleLanguageFromFileName(subtitleFiles[index]);
            if (language is not null)
                arguments.AddRange([$"-metadata:s:s:{index}", $"language={language}"]);
        }
        if (container == ".mp4") arguments.AddRange(["-movflags", "+faststart"]);
        arguments.AddRange(["-progress", "pipe:1", "-nostats"]);
        arguments.Add(outputFile);
        return arguments;
    }

    private static string? SubtitleLanguageFromFileName(string subtitleFile)
    {
        var name = Path.GetFileNameWithoutExtension(subtitleFile);
        var separator = name.LastIndexOf('.');
        if (separator < 0) return null;
        var primary = name[(separator + 1)..].Split(['-', '_'], 2)[0];
        if (primary.Length is not (2 or 3) || !primary.All(c => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z'))
            return null;
        try { return CultureInfo.GetCultureInfo(primary).ThreeLetterISOLanguageName; }
        catch (CultureNotFoundException) { return primary.Length == 3 ? primary.ToLowerInvariant() : null; }
    }

    public async Task MergeAsync(
        string videoFile,
        string audioFile,
        string outputFile,
        double? durationSeconds,
        IProgress<double>? progress,
        CancellationToken cancellationToken,
        double? maximumDurationSeconds = null)
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
        if (maximumDurationSeconds is { } cap)
        {
            if (!double.IsFinite(cap) || cap <= 0) throw new ArgumentOutOfRangeException(nameof(maximumDurationSeconds));
            arguments.AddRange(["-t", cap.ToString("0.######", CultureInfo.InvariantCulture), "-shortest"]);
        }
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

    public async Task RemuxAsync(
        string inputFile,
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
            "-i", inputFile,
            "-map", "0",
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
            throw new InvalidDataException("FFmpeg reported success but did not create a non-empty remuxed file.");
    }

    public async Task<double?> TryProbeDurationAsync(string filePath)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await using var tool = await toolManager.AcquireAsync(ExternalToolKind.Ffmpeg, timeout.Token).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(tool.FfprobePath)) return null;
            var result = await processRunner.RunAsync(new ProcessRunRequest(tool.FfprobePath,
                ["-v", "error", "-show_entries", "format=duration", "-of", "default=noprint_wrappers=1:nokey=1", filePath]),
                timeout.Token).ConfigureAwait(false);
            var value = result.StandardOutput.FirstOrDefault();
            return result.IsSuccess && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) &&
                   double.IsFinite(seconds) && seconds >= 0 ? seconds : null;
        }
        catch (Exception ex)
        {
            logger.Warning($"Could not probe final media duration: {ex.GetType().Name}");
            return null;
        }
    }

    public async Task<MediaProbeResult?> TryProbeMediaAsync(string filePath, CancellationToken cancellationToken = default)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            await using var tool = await toolManager.AcquireAsync(ExternalToolKind.Ffmpeg, timeout.Token).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(tool.FfprobePath)) return null;
            var result = await processRunner.RunAsync(new ProcessRunRequest(tool.FfprobePath,
                ["-v", "error", "-show_entries", "format=duration,start_time,format_name:stream=codec_type", "-of", "json", filePath]),
                timeout.Token).ConfigureAwait(false);
            if (!result.IsSuccess) return null;
            using var document = JsonDocument.Parse(result.StandardOutputText);
            var root = document.RootElement;
            var streams = root.TryGetProperty("streams", out var values) && values.ValueKind == JsonValueKind.Array
                ? values.EnumerateArray().Select(value => value.TryGetProperty("codec_type", out var type) ? type.GetString() : null).ToArray()
                : [];
            var format = root.TryGetProperty("format", out var formatElement) ? formatElement : default;
            var duration = format.ValueKind == JsonValueKind.Object && format.TryGetProperty("duration", out var durationElement) &&
                double.TryParse(durationElement.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) &&
                double.IsFinite(seconds) && seconds > 0 ? seconds : (double?)null;
            var formatName = format.ValueKind == JsonValueKind.Object && format.TryGetProperty("format_name", out var nameElement)
                ? nameElement.GetString() ?? string.Empty : string.Empty;
            var start = format.ValueKind == JsonValueKind.Object && format.TryGetProperty("start_time", out var startElement) &&
                double.TryParse(startElement.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var startSeconds) &&
                double.IsFinite(startSeconds) ? startSeconds : (double?)null;
            return new MediaProbeResult(duration, streams.Contains("video"), streams.Contains("audio"), formatName, start);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            logger.Warning($"Could not probe recoverable media: {ex.GetType().Name}");
            return null;
        }
    }

    public static string ChooseContainer(
        FormatSelection selection,
        PreferredVideoContainer preferredContainer = PreferredVideoContainer.Auto)
    {
        if (!selection.AudioOnly && preferredContainer != PreferredVideoContainer.Auto)
            return preferredContainer.ToProtocolValue();
        if (!selection.RequiresMerge)
            return selection.AudioOnly ? NormalizeExtension(selection.AudioExtension) : NormalizeExtension(selection.VideoExtension);

        var video = NormalizeExtension(selection.VideoExtension);
        var audio = NormalizeExtension(selection.AudioExtension);
        if (video is "mp4" or "m4v" && audio is "m4a" or "mp4") return "mp4";
        if (video == "webm" && audio == "webm") return "webm";
        return "mkv";
    }

    public static bool RequiresRemux(FormatSelection selection, PreferredVideoContainer preferredContainer)
    {
        if (selection.AudioOnly || selection.RequiresMerge || preferredContainer == PreferredVideoContainer.Auto)
            return false;
        var source = NormalizeExtension(selection.VideoExtension);
        var target = ChooseContainer(selection, preferredContainer);
        return target switch
        {
            "mp4" => source is not ("mp4" or "m4v"),
            _ => source != target
        };
    }

    private static string NormalizeExtension(string? extension)
    {
        var value = (extension ?? "bin").Trim().TrimStart('.').ToLowerInvariant();
        return value.All(char.IsLetterOrDigit) ? value : "bin";
    }
}
