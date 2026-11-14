namespace ModernTubeDownloader.Services;

internal enum StartupUpdateOutcome { NotRequested, Deferred, Current, Ready, Failed, TimedOut }

public sealed partial class ApplicationUpdateService
{
    private string? pendingStartupResultKey;

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
        CancellationToken token = default, TimeSpan? budget = null)
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
                await CheckAsync(linked.Token).ConfigureAwait(false);
                if (StatusKey == "Updates.Failed") return await Finish(StartupUpdateOutcome.Failed, "Updates.StartupFailed");
                if (State.Available is null) return await Finish(StartupUpdateOutcome.Current, "Updates.Current");
                if (!StillAllowed()) return await Finish(StartupUpdateOutcome.Deferred, "Updates.StartupDeferred");
                await DownloadAsync(linked.Token).ConfigureAwait(false);
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
