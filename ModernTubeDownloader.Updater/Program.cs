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

        void Record(DiagnosticStage current, DiagnosticOutcome outcome, DiagnosticCounters? counts = null, Exception? error = null)
        {
            try
            {
                var level = error is not null ? DiagnosticLevel.Error :
                    outcome == DiagnosticOutcome.Failed ? DiagnosticLevel.Warn : DiagnosticLevel.Info;
                var entry = DiagnosticRecord.Create(new(DiagnosticModule.Updater, current, outcome, level, id,
                    DurationMs: elapsed.ElapsedMilliseconds, Exception: error, Counts: counts), session, version);
                var file = requestPath + ".diagnostics.jsonl";
                UpdatePackageSecurity.EnsureNoReparsePoints(file);
                if (File.Exists(file) && new FileInfo(file).Length >= 64 * 1024) return;
                File.AppendAllText(file, JsonSerializer.Serialize(entry, DiagnosticRecord.Json) + "\n");
            }
            catch (Exception logError) when (logError is IOException or UnauthorizedAccessException or InvalidDataException)
            { /* Installation and recovery are independent of diagnostics. */ }
        }

        bool PublishResult<T>(T result)
        {
            try
            {
                UpdateFilePublication.Write(requestPath + ".result.json", result, (attempt, code) =>
                    Record(DiagnosticStage.Result, DiagnosticOutcome.Progress,
                        new(Code: (int)UpdateOperation.PublishResult, Items: attempt, Context: code)));
                Record(DiagnosticStage.Result, DiagnosticOutcome.Completed);
                return true;
            }
            catch (UpdatePublicationException error)
            {
                Record(DiagnosticStage.Result, DiagnosticOutcome.Failed,
                    new(Code: (int)UpdateOperation.PublishResult, Context: (int)error.Operation), error.InnerException);
                return false;
            }
        }

        UpdaterRequest request;
        Record(stage, DiagnosticOutcome.Started);
        try
        {
            request = JsonSerializer.Deserialize<UpdaterRequest>(await File.ReadAllTextAsync(requestPath))
                ?? throw new InvalidDataException("Empty updater request.");
            id = request.OperationId ?? id;
            Record(stage, DiagnosticOutcome.Completed);
            Record(stage = DiagnosticStage.WaitForParent, DiagnosticOutcome.Started);
            await WaitForParentAsync(request);
            Record(stage, DiagnosticOutcome.Completed);
            Record(stage = DiagnosticStage.Validate, DiagnosticOutcome.Started);
            StartupUpdateAdmission.Validate(requestPath, request);
            Record(stage, DiagnosticOutcome.Completed);
            Record(stage = DiagnosticStage.Checksum, DiagnosticOutcome.Started);
            await UpdatePackageSecurity.VerifyAsync(request.Archive, request.Sha256);
            Record(stage, DiagnosticOutcome.Completed);
            var extracted = Path.Combine(Path.GetDirectoryName(requestPath)!, "extracted-" + Guid.NewGuid().ToString("N"));
            Record(stage = DiagnosticStage.Extract, DiagnosticOutcome.Started);
            var files = await UpdatePackageSecurity.ExtractAsync(request.Archive, extracted);
            Record(stage, DiagnosticOutcome.Completed, new(Files: files.Count));
            stage = DiagnosticStage.Commit;
            UpdateTransaction.Install(request.Installation, extracted, files, beforeCopy: null, observe: Record);
        }
        catch (Exception error)
        {
            var transaction = error as UpdateTransactionException;
            var primary = transaction?.Failure;
            var recovery = transaction?.Recovery ?? UpdateRecoveryStatus.Unknown;
            var secondary = transaction?.RecoveryFailures ?? [];
            var original = primary?.Error ?? error;
            Record(FailureStage(primary?.Operation, stage), DiagnosticOutcome.Failed,
                new(Code: primary is null ? null : (int)primary.Operation, Context: (int)recovery, Errors: secondary.Count), original);
            foreach (var failure in secondary.Take(16))
                Record(DiagnosticStage.Rollback, DiagnosticOutcome.Failed, new(Code: (int)failure.Operation), failure.Error);

            var detail = (stage, error.Message) switch
            {
                (DiagnosticStage.Checksum, "Update checksum mismatch.") => "Update checksum mismatch.",
                (DiagnosticStage.Validate, "The parent did not commit this startup installation; existing application files remain unchanged.")
                    => "The parent did not commit this startup installation.",
                _ => "Update failed. Refer to the structured installer journal."
            };
            PublishResult(new
            {
                success = false, operationId = id, stage = stage.ToString(),
                recovery = recovery.ToString().ToLowerInvariant(), exceptionType = original.GetType().Name,
                hresult = original.HResult, error = detail,
                failure = SafeFailure(primary), recoveryFailureCount = secondary.Count,
                recoveryFailures = secondary.Take(16).Select(SafeFailure).ToArray()
            });
            return 1;
        }

        // Record the committed payload before attempting any launch. An old GUI still
        // understands the success flag. A new GUI can observe the terminal restart status.
        if (!PublishResult(new { success = true, operationId = id, restartPending = request.Restart })) return 1;
        if (!request.Restart) return 0;
        Record(DiagnosticStage.Restart, DiagnosticOutcome.Started);
        int? restartExitCode = null;
        try
        {
            var start = new ProcessStartInfo(Path.Combine(request.Installation, "ModernTubeDownloader.exe"))
            { UseShellExecute = false, WorkingDirectory = request.Installation };
            using var restarted = Process.Start(start) ?? throw new IOException("The installed application process could not be started.");
            using var grace = new CancellationTokenSource(TimeSpan.FromSeconds(1));
            try { await restarted.WaitForExitAsync(grace.Token); }
            catch (OperationCanceledException) when (grace.IsCancellationRequested) { }
            if (restarted.HasExited)
            {
                restartExitCode = restarted.ExitCode;
                throw new IOException("The installed application exited immediately after launch.");
            }
            Record(DiagnosticStage.Restart, DiagnosticOutcome.Completed);
        }
        catch (Exception error)
        {
            Record(DiagnosticStage.Restart, DiagnosticOutcome.Failed,
                new(Code: (int)UpdateOperation.Restart, ExitCode: restartExitCode,
                    Context: error is System.ComponentModel.Win32Exception native ? native.NativeErrorCode : null), error);
            PublishResult(new { success = true, operationId = id, restartError = SafeFailure(new(UpdateOperation.Restart, error)), restartExitCode });
            return 3;
        }
        return PublishResult(new { success = true, operationId = id, restartPending = false }) ? 0 : 3;
    }

    private static object? SafeFailure(UpdateFailure? failure) => failure is null ? null : new
    {
        operation = JsonNamingPolicy.KebabCaseLower.ConvertName(failure.Operation.ToString()),
        exceptionType = failure.Error.GetType().Name,
        hresult = "0x" + unchecked((uint)failure.Error.HResult).ToString("X8")
    };

    private static DiagnosticStage FailureStage(UpdateOperation? operation, DiagnosticStage fallback) => operation switch
    {
        UpdateOperation.InstallFile => DiagnosticStage.Replace,
        UpdateOperation.ReadJournal or UpdateOperation.RecoverPreviousTransaction => DiagnosticStage.Recover,
        UpdateOperation.InitializeJournal or UpdateOperation.WriteJournal or UpdateOperation.ReplaceJournal or
            UpdateOperation.RetireJournal or UpdateOperation.BackupFile => DiagnosticStage.Journal,
        _ => fallback
    };

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
