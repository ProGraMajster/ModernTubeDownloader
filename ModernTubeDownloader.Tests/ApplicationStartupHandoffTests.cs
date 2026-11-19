using System.Diagnostics;
using System.Text.Json;
using System.Security.Cryptography;
using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Services;
using ModernTubeDownloader.Updates;

namespace ModernTubeDownloader.Tests;

public sealed partial class ApplicationUpdateTests
{
    [Theory]
    [InlineData((int)StartupUpdateOutcome.TimedOut, "Updates.StartupTimedOut")]
    [InlineData((int)StartupUpdateOutcome.Deferred, "Updates.StartupDeferred")]
    [InlineData((int)StartupUpdateOutcome.Failed, "Updates.StartupFailed")]
    public async Task StartupPreflightOutcomeBeforeUpdateInitializationIsPersistedByNormalStartup(int outcome, string expected)
    {
        using var fixture = new Fixture(); using var http = new HttpClient();
        var paths = AppPaths.Create(Path.Combine(fixture.Root, "data"));
        var settings = new SettingsService(paths, new NullAppLogger()); await settings.LoadAsync();
        using var service = new ApplicationUpdateService(paths, settings, http, new NullAppLogger(), new Source(null), "1.1.0");
        await service.RecordStartupOutcomeAsync((StartupUpdateOutcome)outcome);
        await service.InitializeAsync();
        using var restored = new ApplicationUpdateService(paths, settings, http, new NullAppLogger(), new Source(null), "1.1.0");
        await restored.InitializeAsync();
        Assert.Equal(expected, restored.State.LastStartupResultKey);
        Assert.False(restored.InstallationPending);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task StartupInstallerResultReplacesInstallingStatusDurably(bool success)
    {
        using var fixture = new Fixture(); using var http = new HttpClient();
        var paths = AppPaths.Create(Path.Combine(fixture.Root, "data"));
        var settings = new SettingsService(paths, new NullAppLogger()); await settings.LoadAsync();
        var runner = Path.Combine(paths.RootDirectory, "Updates", "runner-result"); Directory.CreateDirectory(runner);
        var request = Path.Combine(runner, "request.json"); await File.WriteAllTextAsync(request, "{}");
        await File.WriteAllTextAsync(request + ".result.json", JsonSerializer.Serialize(new { success }));
        await AtomicJsonFile.WriteAsync(Path.Combine(paths.RootDirectory, "Updates", "state.json"), new ApplicationUpdateState
        {
            LastInstallerRequest = request, LastStartupAttemptVersion = "1.2.0",
            LastStartupResultKey = "Updates.StartupInstalling"
        });
        using var service = new ApplicationUpdateService(paths, settings, http, new NullAppLogger(), new Source(null), "1.2.0");
        await service.InitializeAsync();
        Assert.Equal(success ? "Updates.Current" : "Updates.InstallFailed", service.State.LastStartupResultKey);
        Assert.Equal(!success, service.State.InstallationFailed);
        Assert.False(service.InstallationPending);
        using var restored = new ApplicationUpdateService(paths, settings, http, new NullAppLogger(), new Source(null), "1.2.0");
        await restored.InitializeAsync();
        Assert.Equal(service.State.LastStartupResultKey, restored.State.LastStartupResultKey);
        Assert.Equal("1.2.0", restored.State.LastStartupAttemptVersion);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task StartupInstallerLaunchFailureNeverCommitsShutdownOrLeavesPendingInstallation(bool missingFiles)
    {
        using var fixture = new Fixture();
        var bytes = await File.ReadAllBytesAsync(fixture.Zip(("ModernTubeDownloader.exe", "synthetic")));
        using var http = new HttpClient(new CountingDownloadHandler(bytes));
        var paths = AppPaths.Create(Path.Combine(fixture.Root, "data"));
        var settings = new SettingsService(paths, new NullAppLogger()); await settings.LoadAsync(); settings.Current.Updates.InstallAtStartup = true;
        using var service = new ApplicationUpdateService(paths, settings, http, new NullAppLogger(), new Source(Release()), "1.1.0");
        Assert.Equal(StartupUpdateOutcome.Ready, await service.PrepareStartupAsync(true, () => false));
        service.InstallerSourceDirectory = Path.Combine(fixture.Root, "synthetic-updater");
        if (!missingFiles) Directory.CreateDirectory(service.InstallerSourceDirectory);
        var processStartCalled = false; var shutdownCommitted = false;
        service.InstallerProcessFactory = _ => { processStartCalled = true; throw new IOException("Synthetic process start failure."); };
        await Assert.ThrowsAnyAsync<IOException>(() => service.LaunchStartupInstallerAsync(() => true, () => shutdownCommitted = true));
        Assert.False(shutdownCommitted); Assert.Equal(!missingFiles, processStartCalled);
        Assert.True(service.Ready); Assert.False(service.InstallationPending);
        Assert.Null(service.State.LastInstallerProcessId);
        Assert.Equal("Updates.StartupFailed", service.State.LastStartupResultKey);
        using var restored = new ApplicationUpdateService(paths, settings, http, new NullAppLogger(), new Source(null), "1.1.0");
        await restored.InitializeAsync();
        Assert.False(restored.CanInstallAtStartup); Assert.Equal("Updates.StartupFailed", restored.State.LastStartupResultKey);
    }

    [Theory]
    [InlineData("work")]
    [InlineData("cancel")]
    [InlineData("policy")]
    public async Task StartupAdmissionChangeAfterProcessStartKillsOnlyOwnedWaitingChildAndDoesNotStopRuntime(string change)
    {
        using var fixture = new Fixture();
        var bytes = await File.ReadAllBytesAsync(fixture.Zip(("ModernTubeDownloader.exe", "synthetic")));
        using var http = new HttpClient(new CountingDownloadHandler(bytes));
        var paths = AppPaths.Create(Path.Combine(fixture.Root, "data"));
        var settings = new SettingsService(paths, new NullAppLogger()); await settings.LoadAsync(); settings.Current.Updates.InstallAtStartup = true;
        using var service = new ApplicationUpdateService(paths, settings, http, new NullAppLogger(), new Source(Release()), "1.1.0");
        Assert.Equal(StartupUpdateOutcome.Ready, await service.PrepareStartupAsync(true, () => false));
        service.InstallerSourceDirectory = Path.Combine(fixture.Root, "synthetic-updater"); Directory.CreateDirectory(service.InstallerSourceDirectory);
        using var cancellation = new CancellationTokenSource();
        var allowed = true; var committed = false; Process? witness = null;
        service.InstallerProcessFactory = startInfo =>
        {
            var start = new ProcessStartInfo(CoreTests.FakeToolPath()) { UseShellExecute = false, CreateNoWindow = true };
            start.ArgumentList.Add("--sleep");
            var child = Process.Start(start) ?? throw new IOException("Synthetic child did not start.");
            witness = Process.GetProcessById(child.Id);
            _ = witness.Handle; // Retain a handle to this exact child, even after the service disposes its object.
            if (change == "work") allowed = false;
            if (change == "cancel") cancellation.Cancel();
            if (change == "policy") settings.Current.Updates.InstallAtStartup = false;
            return child;
        };
        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.LaunchStartupInstallerAsync(() => allowed, () => committed = true, cancellation.Token));
            Assert.NotNull(witness); Assert.True(witness.HasExited);
            Assert.False(committed); Assert.False(service.InstallationPending); Assert.True(service.Ready);
            Assert.Equal("Updates.StartupDeferred", service.State.LastStartupResultKey);
            Assert.Equal("1.2.0", service.State.LastStartupAttemptVersion);
            Assert.Empty(Directory.EnumerateFiles(Path.Combine(paths.RootDirectory, "Updates"), "*.startup-commit", SearchOption.AllDirectories));
        }
        finally
        {
            if (witness is not null)
            {
                if (!witness.HasExited) { witness.Kill(); await witness.WaitForExitAsync(); }
                witness.Dispose();
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StartupMarkerRevocationFailureCannotSkipOwnedChildTerminationAndDualFailureBlocksExit(bool stopFails)
    {
        using var fixture = new Fixture();
        var bytes = await File.ReadAllBytesAsync(fixture.Zip(("ModernTubeDownloader.exe", "synthetic")));
        using var http = new HttpClient(new CountingDownloadHandler(bytes));
        var paths = AppPaths.Create(Path.Combine(fixture.Root, "data"));
        var settings = new SettingsService(paths, new NullAppLogger()); await settings.LoadAsync(); settings.Current.Updates.InstallAtStartup = true;
        using var service = new ApplicationUpdateService(paths, settings, http, new NullAppLogger(), new Source(Release()), "1.1.0");
        Assert.Equal(StartupUpdateOutcome.Ready, await service.PrepareStartupAsync(true, () => false));
        service.InstallerSourceDirectory = Path.Combine(fixture.Root, "synthetic-updater"); Directory.CreateDirectory(service.InstallerSourceDirectory);
        Process? witness = null; FileStream? lockedMarker = null; var stopAttempted = false;
        service.InstallerProcessFactory = startInfo =>
        {
            var start = new ProcessStartInfo(CoreTests.FakeToolPath()) { UseShellExecute = false, CreateNoWindow = true };
            start.ArgumentList.Add("--sleep");
            var child = Process.Start(start) ?? throw new IOException("Synthetic child did not start.");
            witness = Process.GetProcessById(child.Id); _ = witness.Handle;
            return child;
        };
        service.InstallerProcessStopper = child =>
        {
            stopAttempted = true;
            if (stopFails) throw new IOException("Synthetic child termination denied.");
            child.Kill();
        };
        try
        {
            await Assert.ThrowsAsync<IOException>(() => service.LaunchStartupInstallerAsync(() => true, () =>
            {
                lockedMarker = new FileStream(StartupUpdateAdmission.MarkerPath(service.State.LastInstallerRequest!), FileMode.Open, FileAccess.Read, FileShare.None);
                throw new IOException("Synthetic shutdown commitment failure with a locked marker.");
            }));
            Assert.True(stopAttempted); Assert.NotNull(witness); Assert.Equal(!stopFails, witness.HasExited);
            Assert.Equal(stopFails, service.StartupAbortBlocked);
            Assert.Equal(stopFails, service.InstallationPending);
            var persisted = await AtomicJsonFile.ReadAsync<ApplicationUpdateState>(Path.Combine(paths.RootDirectory, "Updates", "state.json"));
            Assert.NotNull(persisted);
            if (stopFails)
            {
                Assert.NotNull(persisted.LastInstallerRequest); Assert.Equal(witness.Id, persisted.LastInstallerProcessId);
                Assert.Equal("Updates.StartupAbortBlocked", persisted.LastStartupResultKey);
            }
            else
            {
                Assert.Null(persisted.LastInstallerRequest); Assert.Null(persisted.LastInstallerProcessId);
                Assert.Equal("Updates.StartupFailed", persisted.LastStartupResultKey);
            }
        }
        finally
        {
            lockedMarker?.Dispose();
            if (witness is not null)
            {
                if (!witness.HasExited) { witness.Kill(); await witness.WaitForExitAsync(); }
                Assert.False(service.StartupAbortBlocked);
                witness.Dispose();
            }
        }
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("other-request")]
    [InlineData("malformed")]
    [InlineData("committed")]
    public async Task StartupCommitMarkerIsBoundToExactRequestAndNeverChangesLegacyAdmission(string mode)
    {
        using var fixture = new Fixture();
        var requestPath = Path.Combine(fixture.Root, "request.json"); await File.WriteAllTextAsync(requestPath, "{}");
        var token = Guid.NewGuid().ToString("N");
        var request = new UpdaterRequest(fixture.Root, "synthetic.zip", "unused", Environment.ProcessId, 0, false,
            mode == "malformed" ? "../marker" : token);
        var marker = StartupUpdateAdmission.MarkerPath(requestPath);
        if (mode != "missing") await File.WriteAllTextAsync(marker, mode == "other-request" ? Guid.NewGuid().ToString("N") : token);
        if (mode == "committed") StartupUpdateAdmission.Validate(requestPath, request);
        else Assert.Throws<InvalidDataException>(() => StartupUpdateAdmission.Validate(requestPath, request));
        StartupUpdateAdmission.Validate(requestPath, request with { StartupCommitToken = null });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RealStartupUpdaterRequiresCommittedHandoffBeforeTouchingSyntheticInstallation(bool committed)
    {
        using var fixture = new Fixture();
        var install = Path.Combine(fixture.Root, "installation"); Directory.CreateDirectory(install);
        var executable = Path.Combine(install, "ModernTubeDownloader.exe"); await File.WriteAllTextAsync(executable, "original fixture");
        var zip = fixture.Zip(("ModernTubeDownloader.exe", "new fixture, never executed"));
        var hash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(zip)));
        var parentStart = new ProcessStartInfo(CoreTests.FakeToolPath()) { UseShellExecute = false, CreateNoWindow = true };
        parentStart.ArgumentList.Add("--sleep");
        using var parent = Process.Start(parentStart) ?? throw new IOException("Synthetic parent did not start.");
        var request = new UpdaterRequest(install, zip, hash, parent.Id, parent.StartTime.ToUniversalTime().Ticks, false, Guid.NewGuid().ToString("N"));
        var requestFile = Path.Combine(fixture.Root, "request.json"); await File.WriteAllTextAsync(requestFile, JsonSerializer.Serialize(request));
        if (committed) await File.WriteAllTextAsync(StartupUpdateAdmission.MarkerPath(requestFile), request.StartupCommitToken);
        var updaterStart = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "updater", "ModernTubeDownloader.Updater.exe"))
        { UseShellExecute = false, CreateNoWindow = true };
        updaterStart.ArgumentList.Add("--request"); updaterStart.ArgumentList.Add(requestFile);
        using var updater = Process.Start(updaterStart) ?? throw new IOException("Synthetic updater did not start.");
        try
        {
            parent.Kill(); await parent.WaitForExitAsync();
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            await updater.WaitForExitAsync(deadline.Token);
            Assert.Equal(committed ? 0 : 1, updater.ExitCode);
            Assert.Equal(committed ? "new fixture, never executed" : "original fixture", await File.ReadAllTextAsync(executable));
            using var result = JsonDocument.Parse(await File.ReadAllTextAsync(requestFile + ".result.json"));
            Assert.Equal(committed, result.RootElement.GetProperty("success").GetBoolean());
        }
        finally
        {
            if (!parent.HasExited) { parent.Kill(); await parent.WaitForExitAsync(); }
            if (!updater.HasExited) { updater.Kill(); await updater.WaitForExitAsync(); }
        }
    }
}
