using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Models;

namespace ModernTubeDownloader.Services;

public sealed class YtDlpProcessRunner(
    AsyncProcessRunner processRunner,
    ToolManager toolManager,
    IAppLogger logger)
{
    public async Task<ProcessRunResult> RunAsync(
        IReadOnlyList<string> arguments,
        Action<string>? standardOutputLine = null,
        Action<string>? standardErrorLine = null,
        CancellationToken cancellationToken = default,
        bool requireFfmpeg = false)
    {
        await using var tool = await toolManager.AcquireAsync(ExternalToolKind.YtDlp, cancellationToken).ConfigureAwait(false);
        await using var javaScriptRuntime = await toolManager.AcquireAsync(ExternalToolKind.Deno, cancellationToken).ConfigureAwait(false);
        var effectiveArguments = BuildEffectiveArguments(javaScriptRuntime.ExecutablePath, arguments);
        if (requireFfmpeg)
        {
            await using var ffmpeg = await toolManager.AcquireAsync(ExternalToolKind.Ffmpeg, cancellationToken).ConfigureAwait(false);
            return await RunWithFfmpegAsync(ffmpeg.ExecutablePath, tool.ExecutablePath, effectiveArguments,
                standardOutputLine, standardErrorLine, cancellationToken).ConfigureAwait(false);
        }
        return await RunProcessAsync(tool.ExecutablePath, effectiveArguments, standardOutputLine, standardErrorLine, cancellationToken).ConfigureAwait(false);
    }

    private Task<ProcessRunResult> RunWithFfmpegAsync(string ffmpegPath, string ytDlpPath,
        IReadOnlyList<string> arguments, Action<string>? standardOutputLine, Action<string>? standardErrorLine,
        CancellationToken cancellationToken)
    {
        var effective = new List<string>(arguments.Count + 2) { "--ffmpeg-location", Path.GetDirectoryName(ffmpegPath)! };
        effective.AddRange(arguments);
        return RunProcessAsync(ytDlpPath, effective, standardOutputLine, standardErrorLine, cancellationToken);
    }

    private async Task<ProcessRunResult> RunProcessAsync(string executablePath, IReadOnlyList<string> effectiveArguments,
        Action<string>? standardOutputLine, Action<string>? standardErrorLine, CancellationToken cancellationToken)
    {
        var result = await processRunner.RunAsync(
            new ProcessRunRequest(executablePath, effectiveArguments, StandardOutputLine: standardOutputLine, StandardErrorLine: standardErrorLine),
            cancellationToken).ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            var message = ErrorMessageTranslator.FromYtDlp(result.StandardErrorText);
            logger.Warning($"yt-dlp failed with exit code {result.ExitCode}: {message}");
            throw new ExternalProcessException(message, result);
        }

        return result;
    }

    internal static IReadOnlyList<string> BuildEffectiveArguments(string denoPath, IReadOnlyList<string> arguments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(denoPath);
        var effective = new List<string>(arguments.Count + 2) { "--js-runtimes", $"deno:{denoPath}" };
        effective.AddRange(arguments);
        return effective;
    }
}
