using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Models;
using ModernTubeDownloader.Services;
using ModernTubeDownloader.Views;

namespace ModernTubeDownloader;

public sealed partial class MainForm
{
    private void InterruptStartupUpdate()
    {
        startupInterrupted = true;
        startupUpdateCancellation?.Cancel();
    }

    private bool StartupHasPendingWork() => startupInterrupted || shutdownStarted || services.Batch.IsOperating || services.Organizer.IsBusy ||
        services.Queue.Snapshot().Any(item => item.Status is not (DownloadStatus.Completed or DownloadStatus.Failed or DownloadStatus.Cancelled)) ||
        services.Live.Snapshot().Any(item => item.State is not (LiveSessionState.Completed or LiveSessionState.Failed or LiveSessionState.Cancelled));

    private async Task StartApplicationWorkAsync(bool autostart, bool hiddenArgument, StartupUpdateForm? earlyWindow, Guid? startupOperation)
    {
        if (services.WorkStarted || !startupUpdateEligible || autostart || hiddenArgument)
        {
            earlyWindow?.Finish();
            tray.Start(autostart, hiddenArgument);
            await StartRuntimeWorkAsync();
            if (startupOperation is { } operation)
                services.Logger.Event(new(DiagnosticModule.Application, DiagnosticStage.Startup, DiagnosticOutcome.Completed, OperationId: operation));
            TryStartExperience();
            return;
        }

        var operationId = startupOperation ?? Guid.NewGuid();
        using var startup = new StartupCoordinator(services.Updates, services.Logger, operationId);
        using var newWindow = earlyWindow is null ? new StartupUpdateForm(services, startup) : null;
        earlyWindow?.Attach(services.Localization, startup);
        var window = earlyWindow ?? newWindow!;
        startupUpdateCancellation = startup.Cancellation;
        try
        {
            try
            {
                if (newWindow is not null) window.Show();
                var policy = services.Settings.Current.Updates;
                var outcome = await startup.PrepareAsync(policy.AutomaticChecks && policy.InstallAtStartup,
                    async token => await services.Batch.HasUnfinishedSessionsAsync(token) ||
                        await services.Organizer.HasPendingRecoveryAsync(token),
                    StartupHasPendingWork);
                if (outcome == StartupUpdateOutcome.Ready && !StartupHasPendingWork() && services.Updates.CanInstallAtStartup)
                {
                    using var handoff = CancellationTokenSource.CreateLinkedTokenSource(startup.Cancellation.Token);
                    handoff.CancelAfter(startup.Remaining);
                    await services.Updates.LaunchStartupInstallerAsync(() => !StartupHasPendingWork(), () =>
                    {
                        startupInstallerLaunched = startupInstallRequested = updateInstallRequested = true;
                        BeginShutdown();
                    }, handoff.Token, operationId, showProgress: true);
                    return;
                }
            }
            catch (Exception error)
            {
                services.Logger.Event(new(DiagnosticModule.Updater, DiagnosticStage.Startup, DiagnosticOutcome.Failed,
                    DiagnosticLevel.Warn, operationId, Exception: error));
                if (services.Updates.StartupAbortBlocked)
                {
                    window.Finish();
                    shellHost.SetStatus(text["Updates.StartupAbortBlocked"]);
                    tray.Start(autostart, hiddenArgument);
                    using var blocked = new ModernFormsNext.MessageBoxForm(text["Updates.StartupTitle"], text["Updates.StartupAbortBlocked"]);
                    await blocked.ShowDialog(this);
                    return;
                }
            }
            if (shutdownStarted) return;
            startup.Report(StartupPhase.StartServices);
            await StartRuntimeWorkAsync();
            if (!shutdownStarted) startup.Complete();
        }
        finally
        {
            startupUpdateCancellation = null;
            window.Finish();
        }
        if (shutdownStarted) return;
        tray.Start(autostart, hiddenArgument);
        if (startup.Outcome is StartupUpdateOutcome.Offline or StartupUpdateOutcome.TimedOut)
            shellHost.SetStatus(text["Startup.Offline"]);
        else if (startup.Outcome == StartupUpdateOutcome.Failed)
            shellHost.SetStatus(text["Startup.UpdateFailed"]);
        else if (startup.Outcome == StartupUpdateOutcome.Available)
            shellHost.SetStatus(text["Updates.Available"]);
        TryStartExperience();
    }

    private async Task StartRuntimeWorkAsync()
    {
        if (shutdownStarted) return;
        await services.StartWorkAsync();
        if (shutdownStarted) return;
        services.Updates.Start();
        experienceRuntimeStarted = true;
    }
}
