using System.IO.Compression;
using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Services;
using ModernTubeDownloader.Updates;

namespace ModernTubeDownloader.Tests;

public sealed class ApplicationUpdateTests
{
    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "mtd-update-test-" + Guid.NewGuid().ToString("N"));
        public Fixture() => Directory.CreateDirectory(Root);
        public string Zip(params (string Name, string Contents)[] files)
        {
            var path = Path.Combine(Root, Guid.NewGuid().ToString("N") + ".zip");
            using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
            foreach (var (name, contents) in files)
            { using var writer = new StreamWriter(archive.CreateEntry(name).Open()); writer.Write(contents); }
            return path;
        }
        public void Dispose() => Directory.Delete(Root, true);
    }
    private sealed class Source(ApplicationRelease? release) : IApplicationReleaseSource
    { public Task<ApplicationRelease?> GetLatestAsync(CancellationToken token) => Task.FromResult(release); }
    private sealed class TimeoutThenReadySource : IApplicationReleaseSource
    {
        private int calls;
        public async Task<ApplicationRelease?> GetLatestAsync(CancellationToken token)
        {
            if (Interlocked.Increment(ref calls) == 1) await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return Release();
        }
    }
    private sealed class DownloadHandler(byte[] zip, string checksum) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = request.RequestUri!.AbsolutePath.EndsWith(".sha256")
                ? new StringContent(checksum) : new ByteArrayContent(zip) });
    }
    private static ApplicationRelease Release(string version = "1.2.0")
    {
        var file = $"ModernTubeDownloader-{version}-win-x64.zip";
        var prefix = GitHubApplicationReleaseSource.Repository;
        return new(version, "Original release notes", new(prefix + "/releases/tag/v" + version),
            new(prefix + "/releases/download/v" + version + "/" + file), new(prefix + "/releases/download/v" + version + "/" + file + ".sha256"));
    }

    [Theory]
    [InlineData("../outside.dll")]
    [InlineData("native-host/../../outside.dll")]
    [InlineData("C:/outside.dll")]
    [InlineData("/outside.dll")]
    [InlineData("native-host\\outside.dll")]
    [InlineData("native-host/file.dll:stream")]
    [InlineData("Auth/profile.json")]
    [InlineData("settings.json")]
    [InlineData("native-host/cookies.txt")]
    [InlineData("updater/CON.dll")]
    [InlineData("updater/file.dll.")]
    [InlineData("video.mp4")]
    public async Task UnsafeArchiveFailsBeforeWritingFiles(string name)
    {
        using var fixture = new Fixture();
        var zip = fixture.Zip(("ModernTubeDownloader.exe", "new"), (name, "bad"));
        var extracted = Path.Combine(fixture.Root, "extracted");
        await Assert.ThrowsAsync<InvalidDataException>(() => UpdatePackageSecurity.ExtractAsync(zip, extracted));
        Assert.False(Directory.Exists(extracted));
    }

    [Fact]
    public async Task DuplicateCaseInsensitiveArchivePathsAreRejected()
    {
        using var fixture = new Fixture();
        var zip = fixture.Zip(("ModernTubeDownloader.exe", "new"), ("ModernTubeDownloader.EXE", "bad"));
        await Assert.ThrowsAsync<InvalidDataException>(() => UpdatePackageSecurity.ExtractAsync(zip, Path.Combine(fixture.Root, "extract")));
    }

    [Fact]
    public async Task ChecksumMismatchAndInvalidZipAreRejected()
    {
        using var fixture = new Fixture();
        var zip = fixture.Zip(("ModernTubeDownloader.exe", "new"));
        var hash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(zip)));
        await UpdatePackageSecurity.VerifyAsync(zip, hash);
        await Assert.ThrowsAsync<InvalidDataException>(() => UpdatePackageSecurity.VerifyAsync(zip, new string('0', 64)));
        await Assert.ThrowsAsync<InvalidDataException>(() => UpdatePackageSecurity.VerifyAsync(zip, "not-a-hash"));
        var invalid = Path.Combine(fixture.Root, "invalid.zip"); await File.WriteAllTextAsync(invalid, "not a zip");
        await Assert.ThrowsAsync<InvalidDataException>(() => UpdatePackageSecurity.ExtractAsync(invalid, Path.Combine(fixture.Root, "invalid")));
    }

    [Fact]
    public async Task TransactionInstallsOnlyPackageFilesAndRollsBackAfterMidCopyFailure()
    {
        using var fixture = new Fixture();
        var install = Path.Combine(fixture.Root, "install"); Directory.CreateDirectory(install);
        await File.WriteAllTextAsync(Path.Combine(install, "ModernTubeDownloader.exe"), "old exe");
        await File.WriteAllTextAsync(Path.Combine(install, "existing.dll"), "old dll");
        await File.WriteAllTextAsync(Path.Combine(install, "settings.json"), "private user setting");
        var data = Path.Combine(fixture.Root, "AppData"); Directory.CreateDirectory(data);
        await File.WriteAllTextAsync(Path.Combine(data, "queue.json"), "user queue");
        var zip = fixture.Zip(("ModernTubeDownloader.exe", "new exe"), ("added.dll", "new"), ("existing.dll", "new dll"), ("LICENSE", "MIT"));
        var extract = Path.Combine(fixture.Root, "extract");
        var files = await UpdatePackageSecurity.ExtractAsync(zip, extract);
        Assert.Throws<IOException>(() => UpdateTransaction.Install(install, extract, files, index => { if (index == 2) throw new IOException("Synthetic copy failure"); }));
        Assert.Equal("old exe", await File.ReadAllTextAsync(Path.Combine(install, "ModernTubeDownloader.exe")));
        Assert.Equal("old dll", await File.ReadAllTextAsync(Path.Combine(install, "existing.dll")));
        Assert.False(File.Exists(Path.Combine(install, "added.dll")));
        UpdateTransaction.Install(install, extract, files);
        Assert.Equal("new exe", await File.ReadAllTextAsync(Path.Combine(install, "ModernTubeDownloader.exe")));
        Assert.Equal("private user setting", await File.ReadAllTextAsync(Path.Combine(install, "settings.json")));
        Assert.Equal("user queue", await File.ReadAllTextAsync(Path.Combine(data, "queue.json")));
        Assert.Single(Directory.EnumerateDirectories(install, ".mtd-update-backup"));
        Assert.Equal("old exe", await File.ReadAllTextAsync(Path.Combine(install, ".mtd-update-backup", "files", "ModernTubeDownloader.exe")));
    }

    [Fact]
    public async Task RealUpdaterProcessWaitsForParentThenInstallsWithoutStartingApplication()
    {
        using var fixture = new Fixture();
        var install = Path.Combine(fixture.Root, "installation"); Directory.CreateDirectory(install);
        var executable = Path.Combine(install, "ModernTubeDownloader.exe"); await File.WriteAllTextAsync(executable, "old");
        var zip = fixture.Zip(("ModernTubeDownloader.exe", "new fixture (not executable)"), ("native-host/helper.dll", "new native host"));
        var hash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(zip)));
        var parentStart = new ProcessStartInfo(CoreTests.FakeToolPath()) { UseShellExecute = false, CreateNoWindow = true };
        parentStart.ArgumentList.Add("--sleep-short");
        using var parent = Process.Start(parentStart)!;
        var request = new UpdaterRequest(install, zip, hash, parent.Id, parent.StartTime.ToUniversalTime().Ticks, false);
        var requestFile = Path.Combine(fixture.Root, "request.json"); await File.WriteAllTextAsync(requestFile, JsonSerializer.Serialize(request));
        var updaterStart = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "updater", "ModernTubeDownloader.Updater.exe")) { UseShellExecute = false, CreateNoWindow = true };
        updaterStart.ArgumentList.Add("--request"); updaterStart.ArgumentList.Add(requestFile);
        using var updater = Process.Start(updaterStart)!;
        try
        {
            await Task.Delay(500);
            Assert.False(parent.HasExited); Assert.False(updater.HasExited);
            Assert.Equal("old", await File.ReadAllTextAsync(executable));
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            await updater.WaitForExitAsync(timeout.Token);
            Assert.True(parent.HasExited); Assert.Equal(0, updater.ExitCode);
            Assert.Equal("new fixture (not executable)", await File.ReadAllTextAsync(executable));
            using var result = JsonDocument.Parse(await File.ReadAllTextAsync(requestFile + ".result.json"));
            Assert.True(result.RootElement.GetProperty("success").GetBoolean());
        }
        finally
        {
            if (!parent.HasExited) { parent.Kill(); await parent.WaitForExitAsync(); }
            if (!updater.HasExited) { updater.Kill(); await updater.WaitForExitAsync(); }
        }
    }

    [Theory]
    [InlineData("1.2.0", "1.1.0", true)]
    [InlineData("1.2.0", "1.2.0-candidate.1", true)]
    [InlineData("1.1.0", "1.2.0-candidate.1", false)]
    [InlineData("1.2.0", "1.2.0", false)]
    [InlineData("1.3.0-beta.1", "1.2.0", false)]
    [InlineData("garbage", "1.1.0", false)]
    public void VersionComparisonPreventsDowngrade(string latest, string current, bool expected)
        => Assert.Equal(expected, ApplicationUpdateService.IsNewer(latest, current));

    [Fact]
    public async Task InstalledStableReleaseHasNoUpdateOrInstallationIntent()
    {
        using var fixture = new Fixture(); using var http = new HttpClient();
        var paths = AppPaths.Create(Path.Combine(fixture.Root, "data"));
        using var service = new ApplicationUpdateService(paths, new SettingsService(paths, new NullAppLogger()),
            http, new NullAppLogger(), new Source(Release()), "1.2.0");
        await service.CheckAsync();
        Assert.Equal("Updates.Current", service.StatusKey);
        Assert.Null(service.State.Available);
        Assert.False(service.Ready);
        Assert.False(service.State.InstallOnExit);
    }

    [Fact]
    public void ReleaseSelectionRequiresExactRepositoryTagAssetAndStableChannel()
    {
        JsonDocument Metadata(bool pre, string repo, string assetName)
            => JsonDocument.Parse(JsonSerializer.Serialize(new { draft = false, prerelease = pre, tag_name = "v1.2.0", html_url = repo + "/releases/tag/v1.2.0", body = "Original notes",
                assets = new[] { new { name = assetName, browser_download_url = repo + "/releases/download/v1.2.0/" + assetName },
                    new { name = assetName + ".sha256", browser_download_url = repo + "/releases/download/v1.2.0/" + assetName + ".sha256" } } }));
        using var valid = Metadata(false, GitHubApplicationReleaseSource.Repository, "ModernTubeDownloader-1.2.0-win-x64.zip");
        Assert.Equal("1.2.0", GitHubApplicationReleaseSource.Parse(valid.RootElement).Version);
        using var wrongRepo = Metadata(false, "https://github.com/other/ModernTubeDownloader", "ModernTubeDownloader-1.2.0-win-x64.zip");
        Assert.Throws<InvalidDataException>(() => GitHubApplicationReleaseSource.Parse(wrongRepo.RootElement));
        using var prerelease = Metadata(true, GitHubApplicationReleaseSource.Repository, "ModernTubeDownloader-1.2.0-win-x64.zip");
        Assert.Throws<InvalidDataException>(() => GitHubApplicationReleaseSource.Parse(prerelease.RootElement));
        using var wrongAsset = Metadata(false, GitHubApplicationReleaseSource.Repository, "other.zip");
        Assert.Throws<InvalidDataException>(() => GitHubApplicationReleaseSource.Parse(wrongAsset.RootElement));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DownloadStagingChecksHashPersistsStateAndDoesNotTouchInstallation(bool mismatch)
    {
        using var fixture = new Fixture();
        var zip = fixture.Zip(("ModernTubeDownloader.exe", "fixture exe"));
        var bytes = await File.ReadAllBytesAsync(zip);
        var hash = mismatch ? new string('0', 64) : Convert.ToHexString(SHA256.HashData(bytes));
        using var http = new HttpClient(new DownloadHandler(bytes, hash + "  ModernTubeDownloader-1.2.0-win-x64.zip"));
        var paths = AppPaths.Create(Path.Combine(fixture.Root, "data"));
        var settings = new SettingsService(paths, new NullAppLogger()); await settings.LoadAsync();
        using var service = new ApplicationUpdateService(paths, settings, http, new NullAppLogger(), new Source(Release()), "1.1.0");
        await service.CheckAsync(); await service.DownloadAsync();
        Assert.Equal(!mismatch, service.Ready);
        Assert.Equal(mismatch ? "Updates.Failed" : "Updates.Ready", service.StatusKey);
        Assert.False(Directory.EnumerateFiles(paths.RootDirectory, "*.exe", SearchOption.TopDirectoryOnly).Any());
        if (!mismatch)
        {
            await service.SetInstallOnExitAsync(true); await service.DeferAsync(true);
            using var restored = new ApplicationUpdateService(paths, settings, http, new NullAppLogger(), new Source(null), "1.1.0");
            await restored.InitializeAsync();
            Assert.True(restored.Ready); Assert.True(restored.State.InstallOnExit); Assert.Equal("1.2.0", restored.State.SkippedVersion);
            using var alreadyUpdated = new ApplicationUpdateService(paths, settings, http, new NullAppLogger(), new Source(null), "1.2.0");
            await alreadyUpdated.InitializeAsync(); Assert.False(alreadyUpdated.Ready); Assert.False(alreadyUpdated.State.InstallOnExit);
        }
    }

    [Fact]
    public async Task CheckDeadlineIsReportedAndDoesNotPreventAnotherCheck()
    {
        using var fixture = new Fixture(); using var http = new HttpClient();
        var paths = AppPaths.Create(Path.Combine(fixture.Root, "data"));
        var settings = new SettingsService(paths, new NullAppLogger());
        using var service = new ApplicationUpdateService(paths, settings, http, new NullAppLogger(),
            new TimeoutThenReadySource(), "1.1.0", TimeSpan.FromMilliseconds(100));
        await service.CheckAsync(); Assert.Equal("Updates.Failed", service.StatusKey); Assert.Null(service.State.LastCheck);
        await service.CheckAsync(); Assert.Equal("Updates.Available", service.StatusKey); Assert.Equal("1.2.0", service.State.Available?.Version);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.CheckAsync(cancelled.Token));
    }

    [Fact]
    public async Task ANewReleaseCannotReuseAnOlderStagedArchive()
    {
        using var fixture = new Fixture(); using var http = new HttpClient();
        var paths = AppPaths.Create(Path.Combine(fixture.Root, "data"));
        using var service = new ApplicationUpdateService(paths, new SettingsService(paths, new NullAppLogger()), http,
            new NullAppLogger(), new Source(Release("1.3.0")), "1.1.0");
        service.State.Available = Release(); service.State.StagedArchive = fixture.Zip(("ModernTubeDownloader.exe", "old"));
        service.State.StagedHash = new string('0', 64); service.State.InstallOnExit = true;
        Assert.True(service.Ready); await service.CheckAsync();
        Assert.False(service.Ready); Assert.False(service.State.InstallOnExit); Assert.Equal("1.3.0", service.State.Available?.Version);
    }
}
