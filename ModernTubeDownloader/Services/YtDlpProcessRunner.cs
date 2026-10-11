using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Models;

namespace ModernTubeDownloader.Services;

public sealed class YtDlpProcessRunner(
    AsyncProcessRunner processRunner,
    ToolManager toolManager,
    IAppLogger logger,
    YtDlpAuthenticationProvider? authentication = null)
{
    public IDisposable? BeginAuthenticationSession(string url) => authentication?.BeginSession(url);
    public Task ExecuteAuthenticationAttemptAsync(string url, Func<Task> attempt, CancellationToken token) =>
        authentication is null ? attempt() : authentication.ExecuteAsync(url, async () =>
        { await attempt().ConfigureAwait(false); return true; }, token, retryAtAttemptBoundary: true);
    public async Task<ProcessRunResult> RunAsync(
        IReadOnlyList<string> arguments,
        Action<string>? standardOutputLine = null,
        Action<string>? standardErrorLine = null,
        CancellationToken cancellationToken = default,
        bool requireFfmpeg = false)
    {
        var boundary = arguments.ToList().IndexOf("--");
        var sourceUrl = boundary >= 0 && boundary + 1 < arguments.Count ? arguments[boundary + 1] : null;
        return authentication is not null && sourceUrl is not null
            ? await authentication.ExecuteAsync(sourceUrl, () => RunInvocationAsync(arguments, standardOutputLine, standardErrorLine, cancellationToken, requireFfmpeg), cancellationToken).ConfigureAwait(false)
            : await RunInvocationAsync(arguments, standardOutputLine, standardErrorLine, cancellationToken, requireFfmpeg).ConfigureAwait(false);
    }
    private async Task<ProcessRunResult> RunInvocationAsync(IReadOnlyList<string> arguments, Action<string>? standardOutputLine,
        Action<string>? standardErrorLine, CancellationToken cancellationToken, bool requireFfmpeg)
    {
        await using var tool = await toolManager.AcquireAsync(ExternalToolKind.YtDlp, cancellationToken).ConfigureAwait(false);
        await using var javaScriptRuntime = await toolManager.AcquireAsync(ExternalToolKind.Deno, cancellationToken).ConfigureAwait(false);
        var boundary = arguments.ToList().IndexOf("--");
        var sourceUrl = boundary >= 0 && boundary + 1 < arguments.Count ? arguments[boundary + 1] : null;
        using var auth = sourceUrl is not null ? authentication?.Prepare(sourceUrl) : null;
        var authenticated = arguments.ToList();
        if (auth is not null)
        {
            // All production builders delegate auth to this layer; reject accidental double ownership.
            if (authenticated.Any(a => a is "--cookies" or "--no-cookies" or "--cookies-from-browser" or "--no-cookies-from-browser"))
                throw new InvalidOperationException("Authentication arguments must have a single owner.");
            if (auth.CookiePath is { } cookiePath) authenticated.InsertRange(boundary, ["--cookies", cookiePath]);
        }
        Action<string>? safeError = standardErrorLine is null ? null : line => standardErrorLine(auth?.Redact(line) ?? line);
        Action<string>? safeOutput = standardOutputLine is null ? null : line => standardOutputLine(auth?.SanitizeOutput(line) ?? line);
        var effectiveArguments = BuildEffectiveArguments(javaScriptRuntime.ExecutablePath, authenticated);
        if (requireFfmpeg)
        {
            await using var ffmpeg = await toolManager.AcquireAsync(ExternalToolKind.Ffmpeg, cancellationToken).ConfigureAwait(false);
            return await RunWithFfmpegAsync(ffmpeg.ExecutablePath, tool.ExecutablePath, effectiveArguments,
                safeOutput, safeError, cancellationToken, auth).ConfigureAwait(false);
        }
        return await RunProcessAsync(tool.ExecutablePath, effectiveArguments, safeOutput, safeError, cancellationToken, auth).ConfigureAwait(false);
    }

    private Task<ProcessRunResult> RunWithFfmpegAsync(string ffmpegPath, string ytDlpPath,
        IReadOnlyList<string> arguments, Action<string>? standardOutputLine, Action<string>? standardErrorLine,
        CancellationToken cancellationToken, AuthenticationInvocation? auth)
    {
        var effective = new List<string>(arguments.Count + 2) { "--ffmpeg-location", Path.GetDirectoryName(ffmpegPath)! };
        effective.AddRange(arguments);
        return RunProcessAsync(ytDlpPath, effective, standardOutputLine, standardErrorLine, cancellationToken, auth);
    }

    private async Task<ProcessRunResult> RunProcessAsync(string executablePath, IReadOnlyList<string> effectiveArguments,
        Action<string>? standardOutputLine, Action<string>? standardErrorLine, CancellationToken cancellationToken, AuthenticationInvocation? auth)
    {
        var result = await processRunner.RunAsync(
            new ProcessRunRequest(executablePath, effectiveArguments, StandardOutputLine: standardOutputLine, StandardErrorLine: standardErrorLine),
            cancellationToken).ConfigureAwait(false);

        if (auth is not null) result = result with { StandardError = result.StandardError.Select(auth.Redact).ToArray(),
            StandardOutput = result.StandardOutput.Select(auth.SanitizeOutput).ToArray() };
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
