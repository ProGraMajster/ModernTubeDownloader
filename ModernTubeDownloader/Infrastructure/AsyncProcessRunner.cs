using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;

namespace ModernTubeDownloader.Infrastructure;

public sealed class AsyncProcessRunner(IAppLogger logger)
{
    public async Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.FileName);
        cancellationToken.ThrowIfCancellationRequested();

        var startInfo = new ProcessStartInfo
        {
            FileName = request.FileName,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            CreateNoWindow = true,
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false),
            WorkingDirectory = request.WorkingDirectory ?? AppContext.BaseDirectory
        };

        foreach (var argument in request.Arguments)
            startInfo.ArgumentList.Add(argument);

        logger.Info($"Starting process: {Path.GetFileName(request.FileName)} {RedactArguments(request.Arguments)}");
        var standardOutput = new ConcurrentQueue<string>();
        var standardError = new ConcurrentQueue<string>();
        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        WindowsProcessJob? processJob = null;
        var stopwatch = Stopwatch.StartNew();

        try
        {
            if (!process.Start())
                throw new ExternalProcessException($"Could not start {Path.GetFileName(request.FileName)}.");
            processJob = WindowsProcessJob.TryAssign(process, logger);
            process.StandardInput.Close();
        }
        catch (Exception ex) when (ex is not ExternalProcessException)
        {
            throw new ExternalProcessException($"Could not start {request.FileName}: {ex.Message}", null, ex);
        }

        var stdoutTask = ReadLinesAsync(process.StandardOutput, standardOutput, request.StandardOutputLine);
        var stderrTask = ReadLinesAsync(process.StandardError, standardError, request.StandardErrorLine);
        using var cancellationRegistration = cancellationToken.Register(() => TryKillProcessTree(process));

        try
        {
            await process.WaitForExitAsync().ConfigureAwait(false);
            // A tool can exit while a descendant still owns its redirected
            // pipes. Closing the job terminates descendants and lets both
            // line readers reach EOF, including after an external tool kill.
            processJob?.Dispose();
            processJob = null;
            await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false);
        }
        finally
        {
            processJob?.Dispose();
            stopwatch.Stop();
        }

        cancellationToken.ThrowIfCancellationRequested();
        var result = new ProcessRunResult(process.ExitCode, standardOutput.ToArray(), standardError.ToArray(), stopwatch.Elapsed);
        logger.Info($"Process ended: {Path.GetFileName(request.FileName)}, exit={result.ExitCode}, duration={result.Duration.TotalSeconds:F2}s");
        return result;
    }

    private async Task ReadLinesAsync(StreamReader reader, ConcurrentQueue<string> target, Action<string>? callback)
    {
        var callbackFailureLogged = false;
        while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
        {
            target.Enqueue(line);
            try { callback?.Invoke(line); }
            catch (Exception ex)
            {
                if (!callbackFailureLogged)
                {
                    callbackFailureLogged = true;
                    logger.Error("A process output callback failed; output draining will continue.", ex);
                }
            }
        }
    }

    private static void TryKillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { }
    }

    internal static string RedactArguments(IEnumerable<string> arguments)
    {
        var source = arguments.ToArray();
        var redacted = new List<string>(source.Length);
        string? redactNext = null;

        foreach (var argument in source)
        {
            if (redactNext is not null)
            {
                redacted.Add(redactNext);
                redactNext = null;
                continue;
            }

            if (argument is "--username" or "--password" or "--cookies-from-browser" or "--netrc-location")
            {
                redacted.Add(argument);
                redactNext = "<redacted>";
                continue;
            }

            if (argument == "--cookies")
            {
                redacted.Add(argument);
                redactNext = "<cookie-file>";
                continue;
            }

            if (ContainsSensitiveInlineValue(argument))
                redacted.Add("<redacted>");
            else if (Uri.TryCreate(argument, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
                redacted.Add($"<{uri.Scheme}-url>");
            else
                redacted.Add(argument);
        }

        return string.Join(' ', redacted.Select(QuoteForLog));
    }

    private static string QuoteForLog(string value) =>
        value.Any(char.IsWhiteSpace) ? $"\"{value.Replace("\"", "\\\"")}\"" : value;

    private static bool ContainsSensitiveInlineValue(string value) =>
        value.Contains("authorization:", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("cookie:", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("username=", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("password=", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("token=", StringComparison.OrdinalIgnoreCase);
}
