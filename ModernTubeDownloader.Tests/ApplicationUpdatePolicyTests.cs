using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Services;
using ModernTubeDownloader.Settings;
using ModernTubeDownloader.Updates;

namespace ModernTubeDownloader.Tests;

public sealed partial class ApplicationUpdateTests
{
    private sealed class CountingReleaseSource(ApplicationRelease release) : IApplicationReleaseSource
    {
        public int Calls { get; private set; }
        public Task<ApplicationRelease?> GetLatestAsync(CancellationToken token) { Calls++; return Task.FromResult<ApplicationRelease?>(release); }
    }
    private sealed class CountingDownloadHandler(byte[] bytes) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = request.RequestUri!.AbsolutePath.EndsWith(".sha256")
                ? new StringContent(Convert.ToHexString(SHA256.HashData(bytes)) + "  ModernTubeDownloader-1.2.0-win-x64.zip") : new ByteArrayContent(bytes) });
        }
    }

    [Fact]
    public void NewPolicyDefaultsToVerifiedAutomaticInstallOnRealExit()
    {
        var policy = new UpdateSettings();
        Assert.True(policy.AutomaticChecks); Assert.Equal(UpdateCheckFrequency.StartupDaily, policy.Frequency);
        Assert.Equal(UpdateInstallMode.InstallOnExit, policy.Mode); Assert.Equal(0, policy.PolicyVersion);
    }

    [Theory]
    [InlineData("{broken update state")]
    [InlineData("null")]
    public async Task UnreadableUpdateStateBlocksAutomaticAndManualPackageMutationWithoutOverwritingEvidence(string original)
    {
        using var fixture = new Fixture(); var paths = AppPaths.Create(Path.Combine(fixture.Root, "data"));
        var stateFile = Path.Combine(paths.RootDirectory, "Updates", "state.json"); Directory.CreateDirectory(Path.GetDirectoryName(stateFile)!);
        await File.WriteAllTextAsync(stateFile, original);
        var settings = new SettingsService(paths, new NullAppLogger()); await settings.LoadAsync();
        var source = new CountingReleaseSource(Release()); using var handler = new CountingDownloadHandler([]); using var http = new HttpClient(handler);
        using var service = new ApplicationUpdateService(paths, settings, http, new NullAppLogger(), source, "1.1.0");
        await service.RunAutomaticCycleAsync(); await service.CheckAsync(); await service.DownloadAsync();
        Assert.True(service.StateUnavailable); Assert.False(service.Ready); Assert.False(service.ShouldInstallOnExit);
        Assert.Equal("Updates.Failed", service.StatusKey); Assert.Equal(0, source.Calls); Assert.Equal(0, handler.Calls);
        await Assert.ThrowsAsync<InvalidDataException>(() => service.DeferAsync(true));
        await Assert.ThrowsAsync<InvalidDataException>(() => service.SetInstallOnExitAsync(true));
        await Assert.ThrowsAsync<InvalidDataException>(() => service.LaunchInstallerAsync(false));
        await service.ApplyPolicyAsync(false, UpdateCheckFrequency.Weekly, UpdateInstallMode.NotifyOnly);
        Assert.False(settings.Current.Updates.AutomaticChecks);
        await service.RunAutomaticCycleAsync();
        Assert.Equal(original, await File.ReadAllTextAsync(stateFile));
        Assert.Single(Directory.EnumerateFiles(Path.GetDirectoryName(stateFile)!));
    }

    [Fact]
    public async Task ApplyingPolicyBeforeMonitorInitializesFirstAndRepeatedInitializeCannotUndoOptOut()
    {
        using var fixture = new Fixture(); var paths = AppPaths.Create(Path.Combine(fixture.Root, "data"));
        var updateRoot = Path.Combine(paths.RootDirectory, "Updates"); Directory.CreateDirectory(updateRoot);
        var archive = fixture.Zip(("ModernTubeDownloader.exe", "synthetic")); var staged = Path.Combine(updateRoot, "ModernTubeDownloader-1.2.0-win-x64.zip");
        File.Copy(archive, staged);
        var old = new ApplicationUpdateState { Available = Release(), StagedArchive = staged,
            StagedHash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(staged))), InstallOnExit = true,
            SkippedVersion = "1.1.5", LastCheck = DateTimeOffset.UtcNow };
        var stateFile = Path.Combine(updateRoot, "state.json"); await AtomicJsonFile.WriteAsync(stateFile, old);
        var settings = new SettingsService(paths, new NullAppLogger()); await settings.LoadAsync(); using var http = new HttpClient();
        using var service = new ApplicationUpdateService(paths, settings, http, new NullAppLogger(), new Source(Release()), "1.1.0");
        // This mutation starts before Start/Initialize, the ordering that previously
        // overwrote persisted Skip/Defer with the service's empty initial state.
        await service.ApplyPolicyAsync(false, UpdateCheckFrequency.Weekly, UpdateInstallMode.NotifyOnly);
        Assert.True(service.Ready); Assert.Equal("1.1.5", service.State.SkippedVersion); Assert.False(service.ShouldInstallOnExit);
        Assert.Equal(staged, service.State.StagedArchive);
        // A delayed monitor initialization must not reload an older armed snapshot.
        await AtomicJsonFile.WriteAsync(stateFile, old);
        await Task.WhenAll(service.InitializeAsync(), service.InitializeAsync(), service.RunAutomaticCycleAsync());
        Assert.False(service.ShouldInstallOnExit); Assert.False(service.State.InstallOnExit);
        Assert.False(settings.Current.Updates.AutomaticChecks); Assert.Equal("1.1.5", service.State.SkippedVersion);
    }

    [Fact]
    public async Task RestoreNotifiesPreparedStateEvenWhenAutomaticChecksAreDisabled()
    {
        using var fixture = new Fixture(); var paths = AppPaths.Create(Path.Combine(fixture.Root, "data"));
        var updateRoot = Path.Combine(paths.RootDirectory, "Updates"); Directory.CreateDirectory(updateRoot);
        var archive = fixture.Zip(("ModernTubeDownloader.exe", "synthetic")); var staged = Path.Combine(updateRoot, "ModernTubeDownloader-1.2.0-win-x64.zip");
        File.Copy(archive, staged);
        await AtomicJsonFile.WriteAsync(Path.Combine(updateRoot, "state.json"), new ApplicationUpdateState
        { Available = Release(), StagedArchive = staged, StagedHash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(staged))) });
        var settings = new SettingsService(paths, new NullAppLogger()); await settings.LoadAsync(); settings.Current.Updates.AutomaticChecks = false;
        using var http = new HttpClient(); using var service = new ApplicationUpdateService(paths, settings, http, new NullAppLogger(), new Source(null), "1.1.0");
        var observed = new List<(string Status, bool Ready)>(); service.Changed += (_, _) => observed.Add((service.StatusKey, service.Ready));
        await service.InitializeAsync();
        Assert.Contains(("Updates.Ready", true), observed);
        var count = observed.Count; await service.InitializeAsync(); Assert.Equal(count, observed.Count);
        await service.RunAutomaticCycleAsync(); Assert.True(service.Ready); Assert.False(service.ShouldInstallOnExit);
    }

    [Fact]
    public async Task AutomaticPolicyChecksDailyReusesVerifiedStagingAndNeverLaunchesInstaller()
    {
        using var fixture = new Fixture();
        var bytes = await File.ReadAllBytesAsync(fixture.Zip(("ModernTubeDownloader.exe", "synthetic")));
        using var handler = new CountingDownloadHandler(bytes); using var http = new HttpClient(handler);
        var source = new CountingReleaseSource(Release()); var paths = AppPaths.Create(Path.Combine(fixture.Root, "data"));
        var settings = new SettingsService(paths, new NullAppLogger()); await settings.LoadAsync();
        using var service = new ApplicationUpdateService(paths, settings, http, new NullAppLogger(), source, "1.1.0");
        await service.InitializeAsync(); await service.RunAutomaticCycleAsync();
        Assert.True(service.Ready); Assert.True(service.ShouldInstallOnExit);
        await Task.WhenAll(service.DownloadAsync(), service.DownloadAsync());
        await service.RunAutomaticCycleAsync();
        Assert.Equal(1, source.Calls); Assert.Equal(2, handler.Calls);
        Assert.Empty(Directory.EnumerateDirectories(Path.Combine(paths.RootDirectory, "Updates"), "runner-*"));
        Assert.Equal("synthetic", await ReadStagedExecutable(service.State.StagedArchive!));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task LaterAndSkipDisarmPreparedPackageAndSurviveRestart(bool skip)
    {
        using var fixture = new Fixture();
        var bytes = await File.ReadAllBytesAsync(fixture.Zip(("ModernTubeDownloader.exe", "synthetic")));
        using var http = new HttpClient(new CountingDownloadHandler(bytes)); var paths = AppPaths.Create(Path.Combine(fixture.Root, "data"));
        var settings = new SettingsService(paths, new NullAppLogger()); await settings.LoadAsync();
        using var service = new ApplicationUpdateService(paths, settings, http, new NullAppLogger(), new Source(Release()), "1.1.0");
        await service.RunAutomaticCycleAsync(); Assert.True(service.ShouldInstallOnExit);
        await service.DeferAsync(skip); await service.RunAutomaticCycleAsync();
        Assert.False(service.ShouldInstallOnExit); Assert.False(service.State.InstallOnExit);
        using var restored = new ApplicationUpdateService(paths, settings, http, new NullAppLogger(), new Source(Release()), "1.1.0");
        await restored.InitializeAsync(); await restored.RunAutomaticCycleAsync();
        Assert.True(restored.Ready); Assert.False(restored.ShouldInstallOnExit);
        Assert.True(restored.State.RemindAfter > DateTimeOffset.UtcNow);
        Assert.Equal(skip ? "1.2.0" : null, restored.State.SkippedVersion);
    }

    [Fact]
    public async Task SavingOptOutAndUncheckingPackageIntentCannotBeUndoneByMonitor()
    {
        using var fixture = new Fixture();
        var bytes = await File.ReadAllBytesAsync(fixture.Zip(("ModernTubeDownloader.exe", "synthetic")));
        using var http = new HttpClient(new CountingDownloadHandler(bytes)); var paths = AppPaths.Create(Path.Combine(fixture.Root, "data"));
        var settings = new SettingsService(paths, new NullAppLogger()); await settings.LoadAsync();
        using var service = new ApplicationUpdateService(paths, settings, http, new NullAppLogger(), new Source(Release()), "1.1.0");
        await service.RunAutomaticCycleAsync(); await service.SetInstallOnExitAsync(false); await service.RunAutomaticCycleAsync();
        Assert.False(service.ShouldInstallOnExit);
        using var restored = new ApplicationUpdateService(paths, settings, http, new NullAppLogger(), new Source(Release()), "1.1.0");
        await restored.InitializeAsync(); await restored.RunAutomaticCycleAsync(); Assert.False(restored.ShouldInstallOnExit);
        await restored.ApplyPolicyAsync(true, UpdateCheckFrequency.StartupDaily, UpdateInstallMode.InstallOnExit);
        Assert.True(restored.ShouldInstallOnExit);
        await restored.ApplyPolicyAsync(false, UpdateCheckFrequency.Weekly, UpdateInstallMode.NotifyOnly);
        await restored.RunAutomaticCycleAsync(); Assert.False(restored.ShouldInstallOnExit);
        var reloadedSettings = new SettingsService(paths, new NullAppLogger()); await reloadedSettings.LoadAsync();
        Assert.False(reloadedSettings.Current.Updates.AutomaticChecks); Assert.Equal(1, reloadedSettings.Current.Updates.PolicyVersion);
        Assert.Equal(UpdateInstallMode.NotifyOnly, reloadedSettings.Current.Updates.Mode);
        Assert.Equal(UpdateCheckFrequency.Weekly, reloadedSettings.Current.Updates.Frequency);
    }

    [Fact]
    public async Task FailedSettingsSaveCannotLeavePreparedPackageArmed()
    {
        using var fixture = new Fixture();
        var bytes = await File.ReadAllBytesAsync(fixture.Zip(("ModernTubeDownloader.exe", "synthetic")));
        using var http = new HttpClient(new CountingDownloadHandler(bytes)); var paths = AppPaths.Create(Path.Combine(fixture.Root, "data"));
        var settings = new SettingsService(paths, new NullAppLogger()); await settings.LoadAsync();
        using var service = new ApplicationUpdateService(paths, settings, http, new NullAppLogger(), new Source(Release()), "1.1.0");
        await service.RunAutomaticCycleAsync(); Assert.True(service.ShouldInstallOnExit);
        await File.WriteAllTextAsync(paths.SettingsFile, "{unreadable user data"); await settings.LoadAsync();
        await Assert.ThrowsAsync<InvalidDataException>(() => service.ApplyPolicyAsync(true, UpdateCheckFrequency.StartupDaily, UpdateInstallMode.InstallOnExit));
        await service.RunAutomaticCycleAsync();
        Assert.False(service.ShouldInstallOnExit); Assert.Equal("{unreadable user data", await File.ReadAllTextAsync(paths.SettingsFile));
        using var restored = new ApplicationUpdateService(paths, settings, http, new NullAppLogger(), new Source(Release()), "1.1.0");
        await restored.InitializeAsync(); await restored.RunAutomaticCycleAsync();
        Assert.True(restored.Ready); Assert.False(restored.ShouldInstallOnExit);
    }

    [Fact]
    public async Task PendingUpdaterCannotBeLaunchedOrAutomaticallyArmedTwice()
    {
        using var fixture = new Fixture();
        var bytes = await File.ReadAllBytesAsync(fixture.Zip(("ModernTubeDownloader.exe", "synthetic")));
        using var http = new HttpClient(new CountingDownloadHandler(bytes)); var paths = AppPaths.Create(Path.Combine(fixture.Root, "data"));
        var settings = new SettingsService(paths, new NullAppLogger()); await settings.LoadAsync();
        using var service = new ApplicationUpdateService(paths, settings, http, new NullAppLogger(), new Source(Release()), "1.1.0");
        await service.RunAutomaticCycleAsync();
        service.State.LastInstallerRequest = Path.Combine(paths.RootDirectory, "Updates", "runner-synthetic", "request.json");
        Assert.True(service.InstallationPending);
        await service.SetInstallOnExitAsync(true); await service.RunAutomaticCycleAsync();
        Assert.False(service.State.InstallOnExit); Assert.False(service.ShouldInstallOnExit);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.LaunchInstallerAsync(false));
        Assert.True(service.InstallationPending); Assert.False(service.State.InstallationFailed);
        Assert.Empty(Directory.EnumerateDirectories(Path.Combine(paths.RootDirectory, "Updates"), "runner-*"));
    }

    [Theory]
    [InlineData("foreign")] [InlineData("hash")] [InlineData("source")]
    public async Task RestoredUntrustedStagingCannotArmAnInstaller(string corruption)
    {
        using var fixture = new Fixture(); using var http = new HttpClient(); var paths = AppPaths.Create(Path.Combine(fixture.Root, "data"));
        var updateRoot = Path.Combine(paths.RootDirectory, "Updates"); Directory.CreateDirectory(updateRoot);
        var source = fixture.Zip(("ModernTubeDownloader.exe", "synthetic")); var staged = Path.Combine(updateRoot, "ModernTubeDownloader-1.2.0-win-x64.zip");
        File.Copy(source, staged); var hash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(staged)));
        var release = Release(); if (corruption == "source") release = release with { Page = new("https://example.test/release") };
        await AtomicJsonFile.WriteAsync(Path.Combine(updateRoot, "state.json"), new ApplicationUpdateState
        { Available = release, StagedArchive = corruption == "foreign" ? source : staged, StagedHash = corruption == "hash" ? new string('0', 64) : hash, InstallOnExit = true });
        using var service = new ApplicationUpdateService(paths, new SettingsService(paths, new NullAppLogger()), http, new NullAppLogger(), new Source(null), "1.1.0");
        await service.InitializeAsync(); Assert.False(service.Ready); Assert.False(service.ShouldInstallOnExit); Assert.Equal("Updates.Failed", service.StatusKey);
        Assert.True(File.Exists(source)); Assert.True(File.Exists(staged));
    }

    [Fact]
    public async Task FailedExternalInstallerResultIsVisibleAndDoesNotRetryAtEveryExit()
    {
        using var fixture = new Fixture();
        var bytes = await File.ReadAllBytesAsync(fixture.Zip(("ModernTubeDownloader.exe", "synthetic")));
        using var http = new HttpClient(new CountingDownloadHandler(bytes)); var paths = AppPaths.Create(Path.Combine(fixture.Root, "data"));
        var settings = new SettingsService(paths, new NullAppLogger()); await settings.LoadAsync();
        using var service = new ApplicationUpdateService(paths, settings, http, new NullAppLogger(), new Source(Release()), "1.1.0");
        await service.RunAutomaticCycleAsync();
        var runner = Path.Combine(paths.RootDirectory, "Updates", "runner-fixture"); Directory.CreateDirectory(runner);
        var request = Path.Combine(runner, "request.json"); await File.WriteAllTextAsync(request + ".result.json", "{\"success\":false}");
        service.State.LastInstallerRequest = request;
        await AtomicJsonFile.WriteAsync(Path.Combine(paths.RootDirectory, "Updates", "state.json"), service.State);
        using var restored = new ApplicationUpdateService(paths, settings, http, new NullAppLogger(), new Source(Release()), "1.1.0");
        await restored.InitializeAsync(); await restored.RunAutomaticCycleAsync();
        Assert.True(restored.Ready); Assert.False(restored.ShouldInstallOnExit); Assert.True(restored.State.InstallationFailed);
        Assert.Equal("Updates.InstallFailed", restored.StatusKey); Assert.Null(restored.State.LastInstallerRequest);
        await restored.SetInstallOnExitAsync(true); Assert.True(restored.ShouldInstallOnExit);
    }

    [Theory]
    [InlineData("directory")] [InlineData("readonly")] [InlineData("locked")]
    public async Task RealUpdaterProcessRollsBackMidCopyFailureAndPreservesPrivateData(string obstruction)
    {
        using var fixture = new Fixture(); var install = Path.Combine(fixture.Root, "installation"); Directory.CreateDirectory(install);
        var executable = Path.Combine(install, "ModernTubeDownloader.exe"); await File.WriteAllTextAsync(executable, "old executable");
        // A real filesystem failure occurs after the EXE and added DLL have been copied.
        var blocked = Path.Combine(install, "blocked.dll");
        if (obstruction == "directory") Directory.CreateDirectory(blocked);
        else await File.WriteAllTextAsync(blocked, "original blocked DLL");
        if (obstruction == "readonly") File.SetAttributes(blocked, FileAttributes.ReadOnly);
        using var held = obstruction == "locked" ? new FileStream(blocked, FileMode.Open, FileAccess.Read, FileShare.Read) : null;
        await File.WriteAllTextAsync(Path.Combine(install, "settings.json"), "private settings");
        var data = Path.Combine(fixture.Root, "AppData"); Directory.CreateDirectory(data);
        foreach (var name in new[] { "queue.json", "history.json", "cookies.txt", "live-sessions.json" }) await File.WriteAllTextAsync(Path.Combine(data, name), "private " + name);
        var before = Directory.GetFiles(data).ToDictionary(p => p, p => SHA256.HashData(File.ReadAllBytes(p)));
        var archive = fixture.Zip(("ModernTubeDownloader.exe", "new executable"), ("added.dll", "new DLL"), ("blocked.dll", "will fail"));
        var hash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(archive)));
        using var parent = Process.Start(new ProcessStartInfo(CoreTests.FakeToolPath(), "--sleep-short") { UseShellExecute = false, CreateNoWindow = true })!;
        var request = new UpdaterRequest(install, archive, hash, parent.Id, parent.StartTime.ToUniversalTime().Ticks, false);
        var requestFile = Path.Combine(fixture.Root, "rollback-request.json"); await File.WriteAllTextAsync(requestFile, JsonSerializer.Serialize(request));
        var start = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "updater", "ModernTubeDownloader.Updater.exe")) { UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("--request"); start.ArgumentList.Add(requestFile);
        using var updater = Process.Start(start)!;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20)); await updater.WaitForExitAsync(timeout.Token);
            Assert.True(parent.HasExited); Assert.Equal(1, updater.ExitCode);
            Assert.Equal("old executable", await File.ReadAllTextAsync(executable)); Assert.False(File.Exists(Path.Combine(install, "added.dll")));
            if (obstruction != "directory") Assert.Equal("original blocked DLL", await File.ReadAllTextAsync(blocked));
            if (obstruction == "readonly") Assert.True((File.GetAttributes(blocked) & FileAttributes.ReadOnly) != 0);
            Assert.Equal("private settings", await File.ReadAllTextAsync(Path.Combine(install, "settings.json")));
            foreach (var pair in before) Assert.Equal(pair.Value, SHA256.HashData(await File.ReadAllBytesAsync(pair.Key)));
            using var result = JsonDocument.Parse(await File.ReadAllTextAsync(requestFile + ".result.json")); Assert.False(result.RootElement.GetProperty("success").GetBoolean());
            using var journal = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(install, ".mtd-update-backup", "journal.json")));
            Assert.True(journal.RootElement.GetProperty("Completed").GetBoolean());
        }
        finally
        {
            if (!parent.HasExited) { parent.Kill(); await parent.WaitForExitAsync(); }
            if (!updater.HasExited) { updater.Kill(); await updater.WaitForExitAsync(); }
            if (obstruction == "readonly") File.SetAttributes(blocked, FileAttributes.Normal);
        }
    }

    private static async Task<string> ReadStagedExecutable(string path)
    {
        using var archive = System.IO.Compression.ZipFile.OpenRead(path);
        using var reader = new StreamReader(archive.GetEntry("ModernTubeDownloader.exe")!.Open()); return await reader.ReadToEndAsync();
    }

    [Fact]
    public async Task RollbackRestoresOtherFilesWhenOneChangedFileCannotBeRestoredYet()
    {
        using var fixture = new Fixture(); var installation = Path.Combine(fixture.Root, "installation"); Directory.CreateDirectory(installation);
        var executable = Path.Combine(installation, "ModernTubeDownloader.exe"); var locked = Path.Combine(installation, "existing.dll");
        await File.WriteAllTextAsync(executable, "old exe"); await File.WriteAllTextAsync(locked, "old DLL");
        var archive = fixture.Zip(("ModernTubeDownloader.exe", "new exe"), ("existing.dll", "new DLL"), ("last.dll", "last"));
        var extracted = Path.Combine(fixture.Root, "extracted"); var files = await UpdatePackageSecurity.ExtractAsync(archive, extracted);
        FileStream? held = null;
        try
        {
            Assert.Throws<AggregateException>(() => UpdateTransaction.Install(installation, extracted, files, index =>
            {
                if (index != 2) return;
                held = new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None);
                throw new IOException("Synthetic failure while a replaced file is locked by another process.");
            }));
            Assert.Equal("old exe", await File.ReadAllTextAsync(executable));
            using var journal = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(installation, ".mtd-update-backup", "journal.json")));
            Assert.False(journal.RootElement.GetProperty("Completed").GetBoolean());
        }
        finally { held?.Dispose(); }
        UpdateTransaction.Install(installation, extracted, files);
        Assert.Equal("new exe", await File.ReadAllTextAsync(executable)); Assert.Equal("new DLL", await File.ReadAllTextAsync(locked));
        Assert.Equal("old DLL", await File.ReadAllTextAsync(Path.Combine(installation, ".mtd-update-backup", "files", "existing.dll")));
    }
}
