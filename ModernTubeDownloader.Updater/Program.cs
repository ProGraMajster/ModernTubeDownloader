using System.Diagnostics;
using System.Text.Json;
using ModernTubeDownloader.Updates;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (args.Length != 2 || args[0] != "--request" || !Path.IsPathFullyQualified(args[1])) return 2;
        var requestPath = args[1];
        UpdaterRequest request;
        try
        {
            request = JsonSerializer.Deserialize<UpdaterRequest>(await File.ReadAllTextAsync(requestPath))
                ?? throw new InvalidDataException("Empty updater request.");
            await WaitForParentAsync(request);
            StartupUpdateAdmission.Validate(requestPath, request);
            await UpdatePackageSecurity.VerifyAsync(request.Archive, request.Sha256);
            var extracted = Path.Combine(Path.GetDirectoryName(requestPath)!, "extracted-" + Guid.NewGuid().ToString("N"));
            var files = await UpdatePackageSecurity.ExtractAsync(request.Archive, extracted);
            UpdateTransaction.Install(request.Installation, extracted, files);
        }
        catch (Exception error)
        {
            var transaction = error as UpdateTransactionException;
            var failure = transaction?.Failure ?? new UpdateFailure("validate-update", error);
            var recoveryFailures = transaction?.RecoveryFailures ?? [];
            await TryWriteResultAsync(requestPath, new
            {
                success = false,
                recovery = transaction?.Recovery switch
                {
                    UpdateRecoveryStatus.Restored => "restored",
                    UpdateRecoveryStatus.Partial => "partial",
                    _ => "unknown"
                },
                exceptionType = failure.Error.GetType().Name,
                error = Limit(failure.Error.ToString(), 1000),
                failure = FormatFailure(failure),
                recoveryFailureCount = recoveryFailures.Count,
                recoveryFailures = recoveryFailures.Take(16).Select(FormatFailure).ToArray()
            });
            return 1; // A failed installation never starts the application, even after a verified rollback.
        }

        // Commit the success result before starting the new executable. A missing result
        // leaves recovery unverified for the app and must never trigger an automatic launch.
        if (!await TryWriteResultAsync(requestPath, new { success = true })) return 1;
        if (request.Restart)
        {
            try
            {
                var start = new ProcessStartInfo(Path.Combine(request.Installation, "ModernTubeDownloader.exe"))
                { UseShellExecute = false, WorkingDirectory = request.Installation };
                using var restarted = Process.Start(start) ?? throw new IOException("The application process did not start.");
            }
            catch (Exception error)
            {
                // The payload committed before this separate launch error.
                await TryWriteResultAsync(requestPath, new { success = true, restartError = FormatFailure(new("restart", error)) });
                return 1;
            }
        }
        return 0;
    }

    private static object FormatFailure(UpdateFailure failure) => new
    {
        operation = failure.Operation,
        exceptionType = failure.Error.GetType().Name,
        hresult = $"0x{unchecked((uint)failure.Error.HResult):X8}",
        relativePath = failure.RelativePath,
        message = Limit(failure.Error.Message, 300)
    };

    private static string Limit(string value, int length) => value[..Math.Min(value.Length, length)];

    private static async Task<bool> TryWriteResultAsync<T>(string requestPath, T result)
    {
        var target = requestPath + ".result.json";
        var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            UpdatePackageSecurity.EnsureNoReparsePoints(target);
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await JsonSerializer.SerializeAsync(stream, result);
                stream.Flush(true);
            }
            File.Move(temporary, target, true);
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            try { File.Delete(temporary); }
            catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException) { }
            return false;
        }
    }

    private static async Task WaitForParentAsync(UpdaterRequest request)
    {
        Process parent;
        try { parent = Process.GetProcessById(request.ParentProcessId); }
        catch (ArgumentException) { return; }
        using (parent)
        {
            if (parent.StartTime.ToUniversalTime().Ticks != request.ParentStartUtcTicks) throw new InvalidDataException("Parent process identity changed.");
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            await parent.WaitForExitAsync(timeout.Token);
        }
    }
}

namespace ModernTubeDownloader.Updates
{
    public sealed record UpdaterRequest(string Installation, string Archive, string Sha256, int ParentProcessId, long ParentStartUtcTicks, bool Restart,
        string? StartupCommitToken = null);
}
