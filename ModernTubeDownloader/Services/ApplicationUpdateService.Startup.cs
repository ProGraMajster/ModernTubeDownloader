using ModernTubeDownloader.Settings;

namespace ModernTubeDownloader.Services;

internal enum StartupUpdateOutcome { NotRequested, Deferred, Current, Ready, Failed, TimedOut, Available, Offline }

internal sealed record StartupTransfer(long Bytes, long? Total, long BytesPerSecond)
{
    internal int? Percent => Total is > 0 ? (int)Math.Clamp(Bytes * 100d / Total.Value, 0, 100) : null;
}

public sealed partial class ApplicationUpdateService
{
    private string? pendingStartupResultKey;
    private DateTimeOffset lastStartupCheckAttempt;
    internal StartupTransfer? StartupProgress { get; private set; }
    internal string? StartupTargetVersion => IsDeferred ? null : State.Available?.Version;

    private bool CheckIsDue(TimeSpan interval) =>
        !State.LastCheck.HasValue || DateTimeOffset.UtcNow - State.LastCheck >= interval;

    internal async Task<StartupUpdateOutcome> CheckForStartupAsync(CancellationToken token, Guid operationId)
    {
        var policy = settings.Current.Updates;
        if (!policy.AutomaticChecks || policy.Frequency != UpdateCheckFrequency.StartupDaily)
            return StartupUpdateOutcome.NotRequested;
        await InitializeAsync(token).ConfigureAwait(false);
        if (StateUnavailable) return StartupUpdateOutcome.Failed;
        if (InstallationPending || State.InstallationFailed) return StartupUpdateOutcome.Deferred;
        if (!CheckIsDue(TimeSpan.FromDays(1)))
            return State.Available is null || IsDeferred ? StartupUpdateOutcome.Current : StartupUpdateOutcome.Available;
        lastStartupCheckAttempt = DateTimeOffset.UtcNow;
        var outcome = await CheckForUpdateAsync(token, operationId).ConfigureAwait(false);
        return outcome == StartupUpdateOutcome.Available && IsDeferred ? StartupUpdateOutcome.Deferred : outcome;
    }

    internal bool CanInstallAtStartup => settings.Current.Updates.AutomaticChecks && settings.Current.Updates.InstallAtStartup &&
        Ready && !IsDeferred && !InstallationPending && !State.InstallationFailed &&
        State.AutomaticInstallSuppressedVersion != State.Available?.Version &&
        State.LastStartupAttemptVersion != State.Available?.Version;

    /// <summary>
    /// A bounded admission check before the existing workers start. It never launches an
    /// installer: the owner must recheck work admission and use the normal graceful exit.
    /// Each update operation still uses the same serialized state/staging implementation.
    /// </summary>
    internal async Task<StartupUpdateOutcome> PrepareStartupAsync(bool manualForeground, Func<bool> hasPendingWork,
        CancellationToken token = default, TimeSpan? budget = null, Guid? operationId = null)
    {
        if (!manualForeground || !settings.Current.Updates.InstallAtStartup || !settings.Current.Updates.AutomaticChecks)
            return StartupUpdateOutcome.NotRequested;
        var limit = budget ?? TimeSpan.FromSeconds(8);
        if (limit <= TimeSpan.Zero || limit > TimeSpan.FromSeconds(30)) throw new ArgumentOutOfRangeException(nameof(budget));
        using var deadline = new CancellationTokenSource(limit, checkClock);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token, deadline.Token);
        try
        {
            await InitializeAsync(linked.Token).ConfigureAwait(false);
            if (StateUnavailable) return StartupUpdateOutcome.Failed;
            if (hasPendingWork()) return await Finish(StartupUpdateOutcome.Deferred, "Updates.StartupDeferred");
            if (InstallationPending || State.InstallationFailed ||
                State.Available is { } prior && State.LastStartupAttemptVersion == prior.Version)
                return await Finish(StartupUpdateOutcome.Deferred, "Updates.StartupDeferred");
            if (!Ready)
            {
                if (operationId is not null) lastStartupCheckAttempt = DateTimeOffset.UtcNow;
                var check = await CheckForUpdateAsync(linked.Token, operationId).ConfigureAwait(false);
                if (check is StartupUpdateOutcome.Failed or StartupUpdateOutcome.Offline)
                    return await Finish(operationId is null ? StartupUpdateOutcome.Failed : check, "Updates.StartupFailed");
                if (check == StartupUpdateOutcome.TimedOut)
                    return await Finish(operationId is null ? StartupUpdateOutcome.Failed : check, "Updates.StartupTimedOut");
                if (State.Available is null) return await Finish(StartupUpdateOutcome.Current, "Updates.Current");
                if (!StillAllowed()) return await Finish(StartupUpdateOutcome.Deferred, "Updates.StartupDeferred");
                await DownloadCoreAsync(linked.Token, operationId).ConfigureAwait(false);
            }
            if (!Ready) return await Finish(StartupUpdateOutcome.Failed, "Updates.StartupFailed");
            var ready = CanInstallAtStartup && StillAllowed();
            return await Finish(ready ? StartupUpdateOutcome.Ready : StartupUpdateOutcome.Deferred,
                ready ? "Updates.StartupVerified" : "Updates.StartupDeferred");
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested && !token.IsCancellationRequested && !lifetime.IsCancellationRequested)
        {
            // No installation intent is created when the startup budget expires. Normal
            // startup proceeds; the existing monitor may continue its configured policy.
            await RecordStartupOutcomeAsync(StartupUpdateOutcome.TimedOut).ConfigureAwait(false);
            return StartupUpdateOutcome.TimedOut;
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            logger.Error("Startup update preparation failed; normal startup will continue.", error);
            await RecordStartupOutcomeAsync(StartupUpdateOutcome.Failed).ConfigureAwait(false);
            return StartupUpdateOutcome.Failed;
        }

        bool StillAllowed() => settings.Current.Updates.AutomaticChecks && settings.Current.Updates.InstallAtStartup &&
            !hasPendingWork() && !IsDeferred && !InstallationPending && !State.InstallationFailed &&
            State.AutomaticInstallSuppressedVersion != State.Available?.Version && State.LastStartupAttemptVersion != State.Available?.Version;

        async Task<StartupUpdateOutcome> Finish(StartupUpdateOutcome outcome, string key)
        {
            await gate.WaitAsync(linked.Token).ConfigureAwait(false);
            try
            {
                if (outcome == StartupUpdateOutcome.Ready && (!CanInstallAtStartup || !StillAllowed()))
                { outcome = StartupUpdateOutcome.Deferred; key = "Updates.StartupDeferred"; }
                State.LastStartupResultKey = key;
                await SaveAsync(linked.Token).ConfigureAwait(false);
                Publish(key);
                return outcome;
            }
            finally { gate.Release(); }
        }
    }

    internal async Task RecordStartupOutcomeAsync(StartupUpdateOutcome outcome)
    {
        var key = outcome switch
        {
            StartupUpdateOutcome.TimedOut => "Updates.StartupTimedOut",
            StartupUpdateOutcome.Failed => "Updates.StartupFailed",
            StartupUpdateOutcome.Deferred => "Updates.StartupDeferred",
            _ => throw new ArgumentOutOfRangeException(nameof(outcome))
        };
        // The operation has already unwound its cancelled token. Only persist its
        // small result here; never restart network work or package verification.
        await gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            if (StateUnavailable) return; // Preserve unreadable state unchanged.
            if (!initialized) pendingStartupResultKey = key;
            else
            {
                State.LastStartupResultKey = key;
                await SaveAsync(CancellationToken.None).ConfigureAwait(false);
            }
            Publish(key);
        }
        catch (Exception error) { logger.Error("The startup update result could not be persisted.", error); }
        finally { gate.Release(); }
    }
}
