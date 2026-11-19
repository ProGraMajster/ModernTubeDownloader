using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Services;
using ModernTubeDownloader.Settings;

namespace ModernTubeDownloader.Tests;

public sealed partial class ApplicationUpdateTests
{
    [Theory]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    public async Task StartupInstallRequiresForegroundAndBothExplicitPolicies(bool foreground, bool automatic, bool startup)
    {
        using var fixture = new Fixture(); using var http = new HttpClient();
        var paths = AppPaths.Create(Path.Combine(fixture.Root, "data"));
        var settings = new SettingsService(paths, new NullAppLogger()); await settings.LoadAsync();
        Assert.False(settings.Current.Updates.InstallAtStartup);
        settings.Current.Updates.AutomaticChecks = automatic; settings.Current.Updates.InstallAtStartup = startup;
        var source = new CountingReleaseSource(Release());
        using var service = new ApplicationUpdateService(paths, settings, http, new NullAppLogger(), source, "1.1.0");
        Assert.Equal(StartupUpdateOutcome.NotRequested, await service.PrepareStartupAsync(foreground, () => false));
        Assert.Equal(0, source.Calls); Assert.False(service.Ready); Assert.False(service.InstallationPending);
    }

    [Fact]
    public async Task StartupOptInIsDurableAndDoesNotChangeExitPolicyOrOldOptOut()
    {
        using var fixture = new Fixture(); using var http = new HttpClient();
        var paths = AppPaths.Create(Path.Combine(fixture.Root, "data"));
        var settings = new SettingsService(paths, new NullAppLogger()); await settings.LoadAsync();
        using var service = new ApplicationUpdateService(paths, settings, http, new NullAppLogger(), new Source(null), "1.1.0");
        await service.ApplyPolicyAsync(false, UpdateCheckFrequency.Weekly, UpdateInstallMode.NotifyOnly, installAtStartup: true);
        var restored = new SettingsService(paths, new NullAppLogger()); await restored.LoadAsync();
        Assert.True(restored.Current.Updates.InstallAtStartup); Assert.False(restored.Current.Updates.AutomaticChecks);
        Assert.Equal(UpdateInstallMode.NotifyOnly, restored.Current.Updates.Mode);
        Assert.Equal(UpdateCheckFrequency.Weekly, restored.Current.Updates.Frequency);
        Assert.Equal(StartupUpdateOutcome.NotRequested, await service.PrepareStartupAsync(true, () => false));
    }

    [Fact]
    public async Task StartupPendingWorkDefersBeforeNetworkAndNeverCreatesInstallIntent()
    {
        using var fixture = new Fixture(); using var http = new HttpClient();
        var paths = AppPaths.Create(Path.Combine(fixture.Root, "data"));
        var settings = new SettingsService(paths, new NullAppLogger()); await settings.LoadAsync();
        settings.Current.Updates.InstallAtStartup = true;
        var source = new CountingReleaseSource(Release());
        using var service = new ApplicationUpdateService(paths, settings, http, new NullAppLogger(), source, "1.1.0");
        Assert.Equal(StartupUpdateOutcome.Deferred, await service.PrepareStartupAsync(true, () => true));
        Assert.Equal(0, source.Calls); Assert.False(service.InstallationPending);
        Assert.Equal("Updates.StartupDeferred", service.State.LastStartupResultKey);
    }

    [Fact]
    public async Task StartupStagesVerifiedPackageThenRestoresWithoutNetworkOrLaunchingInstaller()
    {
        using var fixture = new Fixture();
        var bytes = await File.ReadAllBytesAsync(fixture.Zip(("ModernTubeDownloader.exe", "synthetic")));
        using var handler = new CountingDownloadHandler(bytes); using var http = new HttpClient(handler);
        var paths = AppPaths.Create(Path.Combine(fixture.Root, "data"));
        var settings = new SettingsService(paths, new NullAppLogger()); await settings.LoadAsync();
        settings.Current.Updates.InstallAtStartup = true;
        var source = new CountingReleaseSource(Release());
        using var service = new ApplicationUpdateService(paths, settings, http, new NullAppLogger(), source, "1.1.0");
        Assert.Equal(StartupUpdateOutcome.Ready, await service.PrepareStartupAsync(true, () => false));
        Assert.True(service.CanInstallAtStartup); Assert.False(service.InstallationPending);
        Assert.Equal("synthetic", await ReadStagedExecutable(service.State.StagedArchive!));
        using var restored = new ApplicationUpdateService(paths, settings, http, new NullAppLogger(), source, "1.1.0");
        Assert.Equal(StartupUpdateOutcome.Ready, await restored.PrepareStartupAsync(true, () => false));
        Assert.Equal(1, source.Calls); Assert.Equal(2, handler.Calls);
        Assert.Empty(Directory.EnumerateDirectories(Path.Combine(paths.RootDirectory, "Updates"), "runner-*"));
    }

    [Theory]
    [InlineData("skip")]
    [InlineData("later")]
    [InlineData("attempt")]
    [InlineData("failed")]
    [InlineData("pending")]
    [InlineData("work")]
    public async Task StartupRechecksPreparedPackageAndCannotLoopOrOverrideUserChoice(string reason)
    {
        using var fixture = new Fixture();
        var bytes = await File.ReadAllBytesAsync(fixture.Zip(("ModernTubeDownloader.exe", "synthetic")));
        using var http = new HttpClient(new CountingDownloadHandler(bytes));
        var paths = AppPaths.Create(Path.Combine(fixture.Root, "data"));
        var settings = new SettingsService(paths, new NullAppLogger()); await settings.LoadAsync(); settings.Current.Updates.InstallAtStartup = true;
        using var service = new ApplicationUpdateService(paths, settings, http, new NullAppLogger(), new Source(Release()), "1.1.0");
        await service.CheckAsync(); await service.DownloadAsync(); Assert.True(service.Ready);
        if (reason is "skip" or "later") await service.DeferAsync(reason == "skip");
        if (reason == "attempt") service.State.LastStartupAttemptVersion = "1.2.0";
        if (reason == "failed") service.State.InstallationFailed = true;
        if (reason == "pending") service.State.LastInstallerRequest = Path.Combine(paths.RootDirectory, "Updates", "runner-test", "request.json");
        Assert.Equal(StartupUpdateOutcome.Deferred, await service.PrepareStartupAsync(true, () => reason == "work"));
        Assert.Empty(Directory.EnumerateDirectories(Path.Combine(paths.RootDirectory, "Updates"), "runner-*"));
        if (reason != "work") Assert.False(service.CanInstallAtStartup);
        if (reason == "attempt")
        {
            using var restored = new ApplicationUpdateService(paths, settings, http, new NullAppLogger(), new Source(Release()), "1.1.0");
            Assert.Equal(StartupUpdateOutcome.Deferred, await restored.PrepareStartupAsync(true, () => false));
            Assert.Equal("1.2.0", restored.State.LastStartupAttemptVersion);
        }
    }

    [Fact]
    public async Task StartupHasBoundedDeadlineAndNormalChecksRemainUsableAfterTimeout()
    {
        using var fixture = new Fixture(); using var http = new HttpClient();
        var paths = AppPaths.Create(Path.Combine(fixture.Root, "data"));
        var settings = new SettingsService(paths, new NullAppLogger()); await settings.LoadAsync(); settings.Current.Updates.InstallAtStartup = true;
        var clock = new DeadlineTestClock(); var source = new TimeoutThenReadySource();
        using var service = new ApplicationUpdateService(paths, settings, http, new NullAppLogger(), source, "1.1.0", checkClock: clock);
        var attempt = service.PrepareStartupAsync(true, () => false);
        await source.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        clock.Advance(TimeSpan.FromSeconds(8));
        Assert.Equal(StartupUpdateOutcome.TimedOut, await attempt.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.False(service.InstallationPending); Assert.Null(service.State.LastStartupAttemptVersion);
        using (var restored = new ApplicationUpdateService(paths, settings, http, new NullAppLogger(), new Source(null), "1.1.0"))
        {
            await restored.InitializeAsync();
            Assert.Equal("Updates.StartupTimedOut", restored.State.LastStartupResultKey);
        }
        await service.CheckAsync(); Assert.Equal("1.2.0", service.State.Available?.Version);
    }
}
