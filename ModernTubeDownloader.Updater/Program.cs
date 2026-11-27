using System.Diagnostics;
using System.Text.Json;
using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Updates;

internal static class Program
{
    [STAThread]
    private static async Task<int> Main(string[] args)
    {
        if (args is ["--progress", ..]) return InstallerProgressWindow.Run(args);
        if (args.Length != 2 || args[0] != "--request" || !Path.IsPathFullyQualified(args[1])) return 2;
        var requestPath = args[1];
        var session = Guid.NewGuid();
        var parentFolder = Path.GetFileName(Path.GetDirectoryName(requestPath));
        var id = parentFolder is { Length: 39 } && parentFolder.StartsWith("runner-", StringComparison.Ordinal) &&
            Guid.TryParseExact(parentFolder[7..], "N", out var parsed) ? parsed : session;
        var stage = DiagnosticStage.Load;
        var elapsed = Stopwatch.StartNew();
        var logGate = new object();
        InstallerProgressChannel? progress = null;
        var version = DiagnosticRecord.ExecutingVersion;

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
                lock (logGate)
                {
                    if (File.Exists(file) && new FileInfo(file).Length >= 64 * 1024) return;
                    File.AppendAllText(file, JsonSerializer.Serialize(entry, DiagnosticRecord.Json) + "\n");
                }
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
            if (request.ProgressLanguage is { Length: > 0 and <= 16 } language && id != Guid.Empty && Environment.UserInteractive)
            {
                Record(DiagnosticStage.Presentation, DiagnosticOutcome.Started);
                progress = new(id, language, outcome => Record(DiagnosticStage.Presentation, outcome));
                progress.Report(InstallerPhase.Wait);
                progress.Start();
            }
            Record(stage = DiagnosticStage.WaitForParent, DiagnosticOutcome.Started);
            await WaitForParentAsync(request);
            Record(stage, DiagnosticOutcome.Completed);
            progress?.Report(InstallerPhase.Check);
            Record(stage = DiagnosticStage.Validate, DiagnosticOutcome.Started);
            StartupUpdateAdmission.Validate(requestPath, request);
            Record(stage, DiagnosticOutcome.Completed);
            Record(stage = DiagnosticStage.Checksum, DiagnosticOutcome.Started);
            await UpdatePackageSecurity.VerifyAsync(request.Archive, request.Sha256);
            Record(stage, DiagnosticOutcome.Completed);
            var extracted = Path.Combine(Path.GetDirectoryName(requestPath)!, "extracted-" + Guid.NewGuid().ToString("N"));
            Record(stage = DiagnosticStage.Extract, DiagnosticOutcome.Started);
            progress?.Report(InstallerPhase.Extract);
            var files = await UpdatePackageSecurity.ExtractAsync(request.Archive, extracted,
                progress: progress is null ? null : (done, total) => progress.Report(InstallerPhase.Extract, done, total));
            Record(stage, DiagnosticOutcome.Completed, new(Files: files.Count));
            if (request.ExpectedProductVersion is { } expectedVersion)
            {
                Record(stage = DiagnosticStage.Validate, DiagnosticOutcome.Started);
                UpdatePackageSecurity.VerifyProductVersion(extracted, expectedVersion);
                Record(stage, DiagnosticOutcome.Completed);
            }
            stage = DiagnosticStage.Commit;
            UpdateTransaction.Install(request.Installation, extracted, files, beforeCopy: null, observe: ObserveTransaction);
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
                (DiagnosticStage.Validate, UpdatePackageSecurity.ProductVersionMismatch) => UpdatePackageSecurity.ProductVersionMismatch,
                (DiagnosticStage.Validate, "The parent did not commit this startup installation; existing application files remain unchanged.")
                    => "The parent did not commit this startup installation.",
                _ => "Update failed. Refer to the structured installer journal."
            };
            var published = PublishResult(new
            {
                success = false, operationId = id, stage = stage.ToString(),
                recovery = recovery.ToString().ToLowerInvariant(), exceptionType = original.GetType().Name,
                hresult = original.HResult, error = detail,
                failure = SafeFailure(primary), recoveryFailureCount = secondary.Count,
                recoveryFailures = secondary.Take(16).Select(SafeFailure).ToArray()
            });
            var recoveryRequired = recovery == UpdateRecoveryStatus.Partial ||
                primary?.Operation is UpdateOperation.ReadJournal or UpdateOperation.RecoverPreviousTransaction ||
                secondary.Count > 0 && primary?.Operation is UpdateOperation.InstallFile or UpdateOperation.VerifyInstalledFile or UpdateOperation.ReplaceJournal;
            await Finish(published ? recovery == UpdateRecoveryStatus.Restored ? InstallerPhase.Restored
                : recoveryRequired ? InstallerPhase.RecoveryRequired : InstallerPhase.Failed : InstallerPhase.Disconnected);
            return 1;
        }

        // Record the committed payload before attempting any launch. An old GUI still
        // understands the success flag. A new GUI can observe the terminal restart status.
        if (!PublishResult(new { success = true, operationId = id, restartPending = request.Restart }))
        {
            await Finish(InstallerPhase.Disconnected);
            return 1;
        }
        if (!request.Restart)
        {
            await Finish(InstallerPhase.Complete);
            return 0;
        }
        progress?.Report(InstallerPhase.Restart);
        Record(DiagnosticStage.Restart, DiagnosticOutcome.Started);
        int? restartExitCode = null;
        try
        {
            var start = new ProcessStartInfo(Path.Combine(request.Installation, "ModernTubeDownloader.exe"))
            { UseShellExecute = false, WorkingDirectory = request.Installation };
            start.Environment[UpdaterRequest.RestartEnvironmentKey] = id.ToString("N");
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
            var published = PublishResult(new { success = true, operationId = id,
                restartError = SafeFailure(new(UpdateOperation.Restart, error)), restartExitCode });
            await Finish(published ? InstallerPhase.RestartFailed : InstallerPhase.Disconnected);
            return 3;
        }
        var restartedSuccessfully = PublishResult(new { success = true, operationId = id, restartPending = false });
        await Finish(restartedSuccessfully ? InstallerPhase.Complete : InstallerPhase.Disconnected);
        return restartedSuccessfully ? 0 : 3;

        Task Finish(InstallerPhase phase) => progress?.FinishAsync(phase) ?? Task.CompletedTask;

        void ObserveTransaction(DiagnosticStage current, DiagnosticOutcome outcome, DiagnosticCounters? counts, Exception? error)
        {
            Record(current, outcome, counts, error);
            if (outcome == DiagnosticOutcome.Failed) return;
            var phase = current switch
            {
                DiagnosticStage.Backup => InstallerPhase.Backup,
                DiagnosticStage.VerifyBackup => InstallerPhase.VerifyBackup,
                DiagnosticStage.Replace => InstallerPhase.Replace,
                DiagnosticStage.VerifyInstallation => InstallerPhase.VerifyInstallation,
                DiagnosticStage.Commit => InstallerPhase.Commit,
                DiagnosticStage.Rollback or DiagnosticStage.Recover => InstallerPhase.Rollback,
                _ => (InstallerPhase)0
            };
            if (phase != 0 && progress is not null)
            {
                var total = (int)(counts?.Total ?? 0);
                var done = total == 0 || outcome == DiagnosticOutcome.Started ? 0 : (int)(counts?.Files ?? 0);
                progress.Report(phase, done, total);
            }
        }
    }

    private static object? SafeFailure(UpdateFailure? failure) => failure is null ? null : new
    {
        operation = JsonNamingPolicy.KebabCaseLower.ConvertName(failure.Operation.ToString()),
        exceptionType = failure.Error.GetType().Name,
        hresult = "0x" + unchecked((uint)failure.Error.HResult).ToString("X8")
    };

    private static DiagnosticStage FailureStage(UpdateOperation? operation, DiagnosticStage fallback) => operation switch
    {
        UpdateOperation.VerifyInstalledFile => DiagnosticStage.VerifyInstallation,
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
