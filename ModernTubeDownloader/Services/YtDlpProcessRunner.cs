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
        CancellationToken cancellationToken = default)
    {
        await using var tool = await toolManager.AcquireAsync(ExternalToolKind.YtDlp, cancellationToken).ConfigureAwait(false);
        var result = await processRunner.RunAsync(
            new ProcessRunRequest(tool.ExecutablePath, arguments, StandardOutputLine: standardOutputLine, StandardErrorLine: standardErrorLine),
            cancellationToken).ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            var message = ErrorMessageTranslator.FromYtDlp(result.StandardErrorText);
            logger.Warning($"yt-dlp failed with exit code {result.ExitCode}: {message}");
            throw new ExternalProcessException(message, result);
        }

        return result;
    }
}
