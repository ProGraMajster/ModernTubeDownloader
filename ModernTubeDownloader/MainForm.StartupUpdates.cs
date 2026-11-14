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

    private async Task StartApplicationWorkAsync(bool background)
    {
        try
        {
            if (!services.WorkStarted && startupUpdateEligible && !background &&
                services.Settings.Current.Updates is { AutomaticChecks: true, InstallAtStartup: true })
            {
                using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                startupUpdateCancellation = cancellation;
                using var window = new StartupUpdateForm(services, cancellation, async token =>
                {
                    var unfinishedBatch = await services.Batch.HasUnfinishedSessionsAsync(token);
                    var pendingRecovery = await services.Organizer.HasPendingRecoveryAsync(token);
                    return await services.Updates.PrepareStartupAsync(true, () => unfinishedBatch || pendingRecovery || StartupHasPendingWork(), token);
                }, () => startupInterrupted || shutdownStarted);
                await window.ShowDialog(this);
                if (window.Outcome == StartupUpdateOutcome.Ready && !StartupHasPendingWork() && services.Updates.CanInstallAtStartup)
                {
                    try
                    {
                        await services.Updates.LaunchStartupInstallerAsync(() => !StartupHasPendingWork(), () =>
                        {
                            startupInstallerLaunched = startupInstallRequested = updateInstallRequested = true;
                            BeginShutdown();
                        }, cancellation.Token);
                        return;
                    }
                    catch (Exception error)
                    {
                        // No workers have been stopped. The service cancels its
                        // own waiting updater before returning a launch failure.
                        services.Logger.Error("Startup installer handoff failed.", error);
                        if (services.Updates.StartupAbortBlocked)
                        {
                            shellHost.SetStatus(text["Updates.StartupAbortBlocked"]);
                            using var blocked = new ModernFormsNext.MessageBoxForm(text["Updates.StartupTitle"], text["Updates.StartupAbortBlocked"]);
                            await blocked.ShowDialog(this);
                            return;
                        }
                    }
                }
                startupUpdateCancellation = null;
            }
            if (shutdownStarted) return;
            await services.StartWorkAsync();
            if (shutdownStarted) return;
            services.Updates.Start();
            experienceRuntimeStarted = true;
            TryStartExperience();
        }
        catch (Exception error)
        {
            services.Logger.Error("Startup admission failed; no automatic installation was started.", error);
            if (!shutdownStarted)
            {
                await services.StartWorkAsync();
                if (shutdownStarted) return;
                services.Updates.Start();
                experienceRuntimeStarted = true;
                TryStartExperience();
            }
        }
        finally { startupUpdateCancellation = null; }
    }
}
