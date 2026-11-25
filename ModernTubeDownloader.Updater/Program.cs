using System.Diagnostics;
using System.Text.Json;
using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Updates;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (args.Length != 2 || args[0] != "--request" || !Path.IsPathFullyQualified(args[1])) return 2;
        var requestPath = args[1];
        var session = Guid.NewGuid();
        var parentFolder = Path.GetFileName(Path.GetDirectoryName(requestPath));
        var id = parentFolder is { Length: 39 } && parentFolder.StartsWith("runner-", StringComparison.Ordinal) &&
            Guid.TryParseExact(parentFolder[7..], "N", out var parsed) ? parsed : session;
        var stage = DiagnosticStage.Load;
        var elapsed = Stopwatch.StartNew();
        var version = typeof(Program).Assembly.GetName().Version!.ToString();
        void Record(DiagnosticStage current, DiagnosticOutcome outcome, int? count = null, Exception? error = null)
        {
            try
            {
                var entry = DiagnosticRecord.Create(new(DiagnosticModule.Updater, current, outcome,
                    error is null ? DiagnosticLevel.Info : DiagnosticLevel.Error, id,
                    DurationMs: elapsed.ElapsedMilliseconds, Exception: error, Counts: count is { } n ? new(Files: n) : null), session, version);
                var file = requestPath + ".diagnostics.jsonl";
                UpdatePackageSecurity.EnsureNoReparsePoints(file);
                if (File.Exists(file) && new FileInfo(file).Length >= 64 * 1024) return;
                File.AppendAllText(file, JsonSerializer.Serialize(entry, DiagnosticRecord.Json) + "\n");
            }
            catch (Exception logError) when (logError is IOException or UnauthorizedAccessException or InvalidDataException)
            { /* Installation and recovery are independent of diagnostics. */ }
        }

        Record(stage, DiagnosticOutcome.Started);
        try
        {
            var request = JsonSerializer.Deserialize<UpdaterRequest>(await File.ReadAllTextAsync(requestPath))
                ?? throw new InvalidDataException("Empty updater request.");
            id = request.OperationId ?? id;
            Record(stage, DiagnosticOutcome.Completed);
            Record(stage = DiagnosticStage.WaitForParent, DiagnosticOutcome.Started);
            await WaitForParentAsync(request);
            Record(stage, DiagnosticOutcome.Completed);
            Record(stage = DiagnosticStage.Validate, DiagnosticOutcome.Started);
            StartupUpdateAdmission.Validate(requestPath, request);
            Record(stage = DiagnosticStage.Checksum, DiagnosticOutcome.Started);
            await UpdatePackageSecurity.VerifyAsync(request.Archive, request.Sha256);
            Record(stage, DiagnosticOutcome.Completed);
            var extracted = Path.Combine(Path.GetDirectoryName(requestPath)!, "extracted-" + Guid.NewGuid().ToString("N"));
            Record(stage = DiagnosticStage.Extract, DiagnosticOutcome.Started);
            var files = await UpdatePackageSecurity.ExtractAsync(request.Archive, extracted);
            Record(stage, DiagnosticOutcome.Completed, files.Count);
            stage = DiagnosticStage.Commit;
            UpdateTransaction.Install(request.Installation, extracted, files, observe: Record);
            stage = DiagnosticStage.Result;
            await File.WriteAllTextAsync(requestPath + ".result.json", JsonSerializer.Serialize(new { success = true }));
            Record(stage, DiagnosticOutcome.Completed);
            if (request.Restart)
            {
                Record(stage = DiagnosticStage.Restart, DiagnosticOutcome.Started);
                var start = new ProcessStartInfo(Path.Combine(request.Installation, "ModernTubeDownloader.exe")) { UseShellExecute = false, WorkingDirectory = request.Installation };
                using var restarted = Process.Start(start) ?? throw new IOException("The installed application process could not be started.");
                Record(stage, DiagnosticOutcome.Completed);
            }
            return 0;
        }
        catch (Exception error)
        {
            Record(stage, DiagnosticOutcome.Failed, error: error);
            if (stage == DiagnosticStage.Restart) return 3;
            // The result contract is retained for older GUI clients. Never
            // persist exception text containing an installation or profile path.
            var detail = (stage, error.Message) switch
            {
                (DiagnosticStage.Checksum, "Update checksum mismatch.") => "Update checksum mismatch.",
                (DiagnosticStage.Validate, "The parent did not commit this startup installation; existing application files remain unchanged.")
                    => "The parent did not commit this startup installation.",
                _ => "Update failed. Refer to the structured installer journal."
            };
            try
            {
                await File.WriteAllTextAsync(requestPath + ".result.json", JsonSerializer.Serialize(new
                { success = false, stage = stage.ToString(), exceptionType = error.GetType().Name, hresult = error.HResult, error = detail }));
            }
            catch (Exception writeError) when (writeError is IOException or UnauthorizedAccessException)
            { Record(DiagnosticStage.Result, DiagnosticOutcome.Failed, error: writeError); }
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
