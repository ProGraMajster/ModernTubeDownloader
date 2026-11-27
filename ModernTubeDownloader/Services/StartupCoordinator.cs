using System.Diagnostics;
using ModernTubeDownloader.Infrastructure;

namespace ModernTubeDownloader.Services;

internal enum StartupPhase { Initialize, Check, Available, Download, Verify, PrepareInstall, Offline, UpdateFailed, StartServices, Ready }

internal sealed class StartupCoordinator(ApplicationUpdateService updates, IAppLogger logger, Guid operationId,
    TimeSpan? budget = null) : IDisposable
{
    private readonly TimeSpan startupBudget = budget ?? TimeSpan.FromSeconds(8);
    private readonly CancellationTokenSource cancellation = new();
    private readonly Stopwatch elapsed = Stopwatch.StartNew();
    private long lastTransition = Stopwatch.GetTimestamp();
    internal CancellationTokenSource Cancellation => cancellation;
    internal TimeSpan Remaining
    {
        get { var remaining = startupBudget - elapsed.Elapsed; return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero; }
    }
    internal StartupPhase Phase { get; private set; } = StartupPhase.Initialize;
    internal StartupUpdateOutcome Outcome { get; private set; }
    internal string? TargetVersion => updates.StartupTargetVersion;
    internal StartupTransfer? Transfer => updates.StartupProgress;
    internal event EventHandler? Changed;

    internal async Task<StartupUpdateOutcome> PrepareAsync(bool installAtStartup, Func<CancellationToken, Task<bool>> readPendingWork,
        Func<bool> hasPendingWork)
    {
        updates.Changed += UpdateChanged;
        Record(DiagnosticStage.Startup, DiagnosticOutcome.Started);
        using var deadline = new CancellationTokenSource(Remaining);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token, deadline.Token);
        try
        {
            if (installAtStartup)
            {
                var pending = await readPendingWork(linked.Token);
                Outcome = await updates.PrepareStartupAsync(true, () => pending || hasPendingWork(), linked.Token, operationId: operationId);
            }
            else Outcome = await updates.CheckForStartupAsync(linked.Token, operationId);

            switch (Outcome)
            {
                case StartupUpdateOutcome.Available: Report(StartupPhase.Available); break;
                case StartupUpdateOutcome.TimedOut:
                case StartupUpdateOutcome.Offline: Report(StartupPhase.Offline); break;
                case StartupUpdateOutcome.Failed: Report(StartupPhase.UpdateFailed); break;
                case StartupUpdateOutcome.Ready: Report(StartupPhase.PrepareInstall); break;
            }
            Record(DiagnosticStage.Check, Outcome switch
            {
                StartupUpdateOutcome.NotRequested or StartupUpdateOutcome.Deferred => DiagnosticOutcome.Skipped,
                StartupUpdateOutcome.Failed => DiagnosticOutcome.Failed,
                StartupUpdateOutcome.Offline => DiagnosticOutcome.Unavailable,
                _ => DiagnosticOutcome.Completed
            }, new(Code: (int)Outcome));
        }
        catch (OperationCanceledException)
        {
            Outcome = cancellation.IsCancellationRequested ? StartupUpdateOutcome.Deferred : StartupUpdateOutcome.TimedOut;
            if (installAtStartup) await updates.RecordStartupOutcomeAsync(Outcome);
            if (Outcome == StartupUpdateOutcome.TimedOut) Report(StartupPhase.Offline);
            Record(DiagnosticStage.Check, cancellation.IsCancellationRequested ? DiagnosticOutcome.Cancelled : DiagnosticOutcome.Unavailable,
                new(Code: (int)Outcome));
        }
        finally { updates.Changed -= UpdateChanged; }
        return Outcome;
    }

    internal void Report(StartupPhase phase)
    {
        if (Phase != phase)
        {
            var now = Stopwatch.GetTimestamp();
            logger.Event(new(DiagnosticModule.Application, DiagnosticStage.Startup, DiagnosticOutcome.Progress,
                OperationId: operationId, DurationMs: (long)Stopwatch.GetElapsedTime(lastTransition, now).TotalMilliseconds,
                Counts: new(Code: (int)phase, Context: (int)Phase)));
            lastTransition = now;
            Phase = phase;
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateChanged(object? sender, EventArgs args)
    {
        switch (updates.StatusKey)
        {
            case "Updates.Checking": Report(StartupPhase.Check); break;
            case "Updates.Available" when TargetVersion is not null: Report(StartupPhase.Available); break;
            case "Updates.Downloading": Report(StartupPhase.Download); break;
            case "Startup.Verify": Report(StartupPhase.Verify); break;
        }
    }

    private void Record(DiagnosticStage stage, DiagnosticOutcome outcome, DiagnosticCounters? counts = null) =>
        logger.Event(new(DiagnosticModule.Application, stage, outcome, OperationId: operationId,
            DurationMs: elapsed.ElapsedMilliseconds, Counts: counts));

    internal void Complete() { Report(StartupPhase.Ready); Record(DiagnosticStage.Startup, DiagnosticOutcome.Completed); }

    public void Dispose() => cancellation.Dispose();
}
