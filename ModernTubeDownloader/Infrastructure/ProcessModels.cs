namespace ModernTubeDownloader.Infrastructure;

public sealed record ProcessRunRequest(
    string FileName,
    IReadOnlyList<string> Arguments,
    string? WorkingDirectory = null,
    Action<string>? StandardOutputLine = null,
    Action<string>? StandardErrorLine = null);

public sealed record ProcessRunResult(
    int ExitCode,
    IReadOnlyList<string> StandardOutput,
    IReadOnlyList<string> StandardError,
    TimeSpan Duration)
{
    public bool IsSuccess => ExitCode == 0;
    public string StandardOutputText => string.Join(Environment.NewLine, StandardOutput);
    public string StandardErrorText => string.Join(Environment.NewLine, StandardError);
}

public sealed class ExternalProcessException(string message, ProcessRunResult? result = null, Exception? innerException = null)
    : Exception(message, innerException)
{
    public ProcessRunResult? Result { get; } = result;
}
