namespace ModernTubeDownloader.Infrastructure;

/// <summary>Diagnostic correlation only; it does not own processes or introduce another retry/execution path.</summary>
internal static class ProcessLogContext
{
    private sealed record Context(Guid JobId, int Attempt, string Role);
    private static readonly AsyncLocal<Context?> current = new();
    internal static IDisposable Begin(Guid jobId, int attempt, string role)
    {
        var previous = current.Value; current.Value = new(jobId, attempt, role);
        return new Scope(() => current.Value = previous);
    }
    internal static string Describe(ProcessRunRequest request)
    {
        var context = current.Value;
        var role = request.Arguments.Contains("--dump-single-json") ? "metadata" :
            request.Arguments.Any(value => value is "--write-subs" or "--write-auto-subs") ? "subtitle" :
            request.Arguments.Contains("-progress") ? "ffmpeg" : context?.Role ?? "tool";
        return $"JobId={context?.JobId.ToString("N") ?? "none"} Attempt={context?.Attempt ?? 0} Role={role} OperationId={Guid.NewGuid():N}";
    }
    private sealed class Scope(Action close) : IDisposable
    {
        public void Dispose() => close();
    }
}
