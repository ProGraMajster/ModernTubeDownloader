using System.Diagnostics;
using System.Text.Json;
using ModernTubeDownloader.Updates;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (args.Length != 2 || args[0] != "--request" || !Path.IsPathFullyQualified(args[1])) return 2;
        var requestPath = args[1];
        var step = "ReadRequest";
        var elapsed = Stopwatch.StartNew();
        void Record(string stage, Exception? error = null)
        {
            try
            {
                var entry = JsonSerializer.Serialize(new
                {
                    utc = DateTimeOffset.UtcNow,
                    updaterVersion = typeof(Program).Assembly.GetName().Version?.ToString(),
                    stage, elapsedMs = elapsed.ElapsedMilliseconds,
                    exceptionType = error?.GetType().Name, hresult = error?.HResult
                });
                var diagnostics = requestPath + ".diagnostics.jsonl";
                UpdatePackageSecurity.EnsureNoReparsePoints(diagnostics);
                File.AppendAllText(diagnostics, entry + Environment.NewLine);
            }
            catch (Exception logError) when (logError is IOException or UnauthorizedAccessException or InvalidDataException)
            { /* Installation and recovery are independent of optional diagnostics. */ }
        }

        Record(step);
        try
        {
            var request = JsonSerializer.Deserialize<UpdaterRequest>(await File.ReadAllTextAsync(requestPath))
                ?? throw new InvalidDataException("Empty updater request.");
            Record(step = "WaitForParent");
            await WaitForParentAsync(request);
            Record(step = "ValidateAdmission");
            StartupUpdateAdmission.Validate(requestPath, request);
            Record(step = "VerifyPackage");
            await UpdatePackageSecurity.VerifyAsync(request.Archive, request.Sha256);
            var extracted = Path.Combine(Path.GetDirectoryName(requestPath)!, "extracted-" + Guid.NewGuid().ToString("N"));
            Record(step = "ExtractPackage");
            var files = await UpdatePackageSecurity.ExtractAsync(request.Archive, extracted);
            Record(step = "Install");
            UpdateTransaction.Install(request.Installation, extracted, files);
            Record(step = "WriteResult");
            await File.WriteAllTextAsync(requestPath + ".result.json", JsonSerializer.Serialize(new { success = true }));
            Record("Installed");
            if (request.Restart)
            {
                Record(step = "Restart");
                var start = new ProcessStartInfo(Path.Combine(request.Installation, "ModernTubeDownloader.exe")) { UseShellExecute = false, WorkingDirectory = request.Installation };
                using var restarted = Process.Start(start) ?? throw new IOException("The installed application process could not be started.");
                Record("RestartStarted"); // The application itself enforces its canonical single-instance contract.
            }
            return 0;
        }
        catch (Exception error)
        {
            Record(step + "Failed", error);
            if (step == "Restart") return 3; // Installation was committed. Keep that result available for a manual launch.
            await File.WriteAllTextAsync(requestPath + ".result.json", JsonSerializer.Serialize(new { success = false, stage = step, exceptionType = error.GetType().Name, error = error.ToString() }));
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
