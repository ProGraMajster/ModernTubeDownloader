using System.Diagnostics;
using System.Text.Json;
using ModernTubeDownloader.Updates;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (args.Length != 2 || args[0] != "--request" || !Path.IsPathFullyQualified(args[1])) return 2;
        var requestPath = args[1];
        try
        {
            var request = JsonSerializer.Deserialize<UpdaterRequest>(await File.ReadAllTextAsync(requestPath))
                ?? throw new InvalidDataException("Empty updater request.");
            await WaitForParentAsync(request);
            await UpdatePackageSecurity.VerifyAsync(request.Archive, request.Sha256);
            var extracted = Path.Combine(Path.GetDirectoryName(requestPath)!, "extracted-" + Guid.NewGuid().ToString("N"));
            var files = await UpdatePackageSecurity.ExtractAsync(request.Archive, extracted);
            UpdateTransaction.Install(request.Installation, extracted, files);
            await File.WriteAllTextAsync(requestPath + ".result.json", JsonSerializer.Serialize(new { success = true }));
            if (request.Restart)
            {
                var start = new ProcessStartInfo(Path.Combine(request.Installation, "ModernTubeDownloader.exe")) { UseShellExecute = false, WorkingDirectory = request.Installation };
                Process.Start(start)?.Dispose(); // The application itself enforces its canonical single-instance contract.
            }
            return 0;
        }
        catch (Exception error)
        {
            await File.WriteAllTextAsync(requestPath + ".result.json", JsonSerializer.Serialize(new { success = false, exceptionType = error.GetType().Name, error = error.ToString() }));
            return 1;
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
    public sealed record UpdaterRequest(string Installation, string Archive, string Sha256, int ParentProcessId, long ParentStartUtcTicks, bool Restart);
}
