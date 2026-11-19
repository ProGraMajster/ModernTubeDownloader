using ModernTubeDownloader.Infrastructure;

namespace ModernTubeDownloader.Services;

internal static class HistoryOutputFacts
{
    // Capture the completed output, never a remote size estimate. Losing access to the
    // file after completion must not prevent saving the rest of its history record.
    internal static long? TryFileSize(string path, IAppLogger logger)
    {
        try { return new FileInfo(path).Length; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { logger.Warning($"Could not read completed output size: {error.GetType().Name}"); return null; }
    }
}
