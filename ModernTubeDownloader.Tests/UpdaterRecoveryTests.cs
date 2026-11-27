using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Services;
using ModernTubeDownloader.Updates;

namespace ModernTubeDownloader.Tests;

public sealed partial class ApplicationUpdateTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SourceSharingViolationsAreRetriedOnlyWithinTheFixedBudget(bool release)
    {
        using var fixture = new Fixture();
        var target = Path.Combine(fixture.Root, "journal.json");
        var replacement = target + ".tmp";
        await File.WriteAllTextAsync(target, "old");
        await File.WriteAllTextAsync(replacement, "new");
        using var held = new FileStream(replacement, FileMode.Open, FileAccess.Read, FileShare.Read);
        List<int> attempts = [];
        void OnRetry(int attempt, int code)
        {
            Assert.Equal(unchecked((int)0x80070020), code);
            attempts.Add(attempt);
            if (release) held.Dispose();
        }

        if (release) UpdateFilePublication.Replace(replacement, target, OnRetry);
        else Assert.Throws<IOException>(() => UpdateFilePublication.Replace(replacement, target, OnRetry));

        Assert.Equal(release ? [1] : new[] { 1, 2, 3 }, attempts);
        Assert.Equal(release ? "new" : "old", await File.ReadAllTextAsync(target));
    }

    [Fact]
    public async Task TargetAccessDenialPreservesTheFirstErrorAndVerifiedFiles()
    {
        using var fixture = new Fixture();
        var installation = Directory.CreateDirectory(Path.Combine(fixture.Root, "installation")).FullName;
        var exe = Path.Combine(installation, "ModernTubeDownloader.exe");
        await File.WriteAllTextAsync(exe, "original");
        var extracted = Path.Combine(fixture.Root, "extracted");
        var files = await UpdatePackageSecurity.ExtractAsync(
            fixture.Zip(("ModernTubeDownloader.exe", "updated"), ("added.dll", "addition")), extracted);
        var path = JournalPath(installation);
        try
        {
            var result = Assert.Throws<UpdateTransactionException>(() => UpdateTransaction.Install(installation, extracted, files,
                beforeCopy: null, observe: null, beforeCommit: journal => File.SetAttributes(journal, FileAttributes.ReadOnly)));
            Assert.Equal(UpdateOperation.ReplaceJournal, result.Failure.Operation);
            Assert.Equal(0x80070005u, unchecked((uint)result.Failure.Error.HResult));
            Assert.Equal(UpdateOperation.ReplaceJournal, Assert.Single(result.RecoveryFailures).Operation);
            Assert.Equal(UpdateRecoveryStatus.Unknown, result.Recovery);
            Assert.Equal("original", await File.ReadAllTextAsync(exe));
            Assert.False(File.Exists(Path.Combine(installation, "added.dll")));
            Assert.False(ReadJournal(installation).Completed);
        }
        finally
        {
            if (File.Exists(path)) File.SetAttributes(path, FileAttributes.Normal);
        }

        UpdateTransaction.Install(installation + Path.DirectorySeparatorChar, extracted, files);
        Assert.True(ReadJournal(installation).Completed);
        Assert.Equal("updated", await File.ReadAllTextAsync(exe));
    }

    [Fact]
    public async Task ModifiedBackupCannotBePublishedAsRestored()
    {
        using var fixture = new Fixture();
        var installation = Directory.CreateDirectory(Path.Combine(fixture.Root, "installation")).FullName;
        var exe = Path.Combine(installation, "ModernTubeDownloader.exe");
        await File.WriteAllTextAsync(exe, "original");
        var extracted = Path.Combine(fixture.Root, "extracted");
        var files = await UpdatePackageSecurity.ExtractAsync(
            fixture.Zip(("ModernTubeDownloader.exe", "updated"), ("added.dll", "addition")), extracted);
        var backup = Path.Combine(installation, ".mtd-update-backup", "files", "ModernTubeDownloader.exe");
        var result = Assert.Throws<UpdateTransactionException>(() => UpdateTransaction.Install(installation, extracted, files, index =>
        {
            if (index != 1) return;
            File.WriteAllText(backup, "tampered");
            throw new IOException("Synthetic failure");
        }));
        Assert.Equal(UpdateRecoveryStatus.Partial, result.Recovery);
        Assert.IsType<InvalidDataException>(Assert.Single(result.RecoveryFailures).Error);
        Assert.Equal("updated", await File.ReadAllTextAsync(exe));
        Assert.False(ReadJournal(installation).Completed);
        Assert.Equal(Sha("original"), ReadJournal(installation).Files[0].OriginalSha256);
        Assert.Equal("tampered", await File.ReadAllTextAsync(backup));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RecoveryOnlyChangesPathsAuthorizedByVerifiableJournal(bool verifiable)
    {
        using var fixture = new Fixture();
        var installation = Directory.CreateDirectory(Path.Combine(fixture.Root, "installation")).FullName;
        var backup = Path.Combine(installation, ".mtd-update-backup");
        Directory.CreateDirectory(Path.Combine(backup, "files"));
        Directory.CreateDirectory(Path.Combine(backup, "started"));
        var exe = Path.Combine(installation, "ModernTubeDownloader.exe");
        var added = Path.Combine(installation, "added.dll");
        await File.WriteAllTextAsync(exe, "partially updated");
        await File.WriteAllTextAsync(added, "old update addition");
        await File.WriteAllTextAsync(Path.Combine(backup, "files", "ModernTubeDownloader.exe"), "original");
        foreach (var index in new[] { "0", "1" }) await File.WriteAllTextAsync(Path.Combine(backup, "started", index), "1");
        var journal = new UpdateJournal(installation,
            [new("ModernTubeDownloader.exe", true, verifiable ? Sha("original") : null),
             new("added.dll", false, AddedSha256: verifiable ? Sha("old update addition") : null)], false, verifiable);
        await File.WriteAllTextAsync(JournalPath(installation), JsonSerializer.Serialize(journal));
        var extracted = Path.Combine(fixture.Root, "extracted");
        var files = await UpdatePackageSecurity.ExtractAsync(fixture.Zip(("ModernTubeDownloader.exe", "updated")), extracted);
        if (verifiable)
        {
            UpdateTransaction.Install(installation, extracted, files, _ =>
            {
                Assert.Equal("original", File.ReadAllText(exe));
                Assert.False(File.Exists(added));
            });
            Assert.Equal("updated", await File.ReadAllTextAsync(exe));
        }
        else
        {
            var failure = Assert.Throws<UpdateTransactionException>(() => UpdateTransaction.Install(installation, extracted, files));
            Assert.Equal(UpdateRecoveryStatus.Unknown, failure.Recovery);
            Assert.Equal(UpdateOperation.UnverifiedLegacyJournal, Assert.Single(failure.RecoveryFailures).Operation);
            Assert.Equal("partially updated", await File.ReadAllTextAsync(exe));
            Assert.Equal("old update addition", await File.ReadAllTextAsync(added));
            Assert.False(ReadJournal(installation).Completed);
        }
    }

    [Fact]
    public async Task AConcurrentUpdaterCannotWriteToTheSameInstallation()
    {
        using var fixture = new Fixture();
        var installation = Directory.CreateDirectory(Path.Combine(fixture.Root, "installation")).FullName;
        await File.WriteAllTextAsync(Path.Combine(installation, "ModernTubeDownloader.exe"), "original");
        var extracted = Path.Combine(fixture.Root, "extracted");
        var files = await UpdatePackageSecurity.ExtractAsync(fixture.Zip(("ModernTubeDownloader.exe", "updated")), extracted);
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var owner = Task.Run(() => UpdateTransaction.Install(installation, extracted, files, _ =>
        {
            entered.Set();
            Assert.True(release.Wait(TimeSpan.FromSeconds(15)));
        }));
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(15)));
            var failure = Assert.Throws<UpdateTransactionException>(() =>
                UpdateTransaction.Install(installation + Path.DirectorySeparatorChar, extracted, files));
            Assert.Equal(UpdateOperation.LockInstallation, failure.Failure.Operation);
            Assert.Equal("original", await File.ReadAllTextAsync(Path.Combine(installation, "ModernTubeDownloader.exe")));
        }
        finally
        {
            release.Set();
            await owner;
        }
        Assert.True(ReadJournal(installation).Completed);
    }

    [Theory]
    [InlineData("{\"success\":false,\"recovery\":\"partial\"}", "Updates.RollbackPartial")]
    [InlineData("{\"success\":false,\"recovery\":\"restored\"}", "Updates.RollbackRestored")]
    [InlineData("{\"success\":true,\"restartError\":{}}", "Updates.InstalledRestartUnconfirmed")]
    [InlineData("{\"success\":true,\"restartPending\":true}", "Updates.InstalledRestartUnconfirmed")]
    public async Task InstallerOutcomeSurvivesMissingStagedZipAndLaterCheck(string result, string status)
    {
        using var fixture = new Fixture();
        using var http = new HttpClient();
        var paths = AppPaths.Create(Path.Combine(fixture.Root, "data"));
        var root = Directory.CreateDirectory(Path.Combine(paths.RootDirectory, "Updates")).FullName;
        var runner = Directory.CreateDirectory(Path.Combine(root, "runner-" + Guid.NewGuid().ToString("N"))).FullName;
        var request = Path.Combine(runner, "request.json");
        await File.WriteAllTextAsync(request + ".result.json", result);
        await AtomicJsonFile.WriteAsync(Path.Combine(root, "state.json"), new ApplicationUpdateState
        {
            LastInstallerRequest = request, LastStartupResultKey = "Updates.StartupInstalling",
            Available = Release(), StagedHash = new string('1', 64),
            StagedArchive = Path.Combine(root, "ModernTubeDownloader-1.2.0-win-x64.zip")
        });
        var settings = new SettingsService(paths, new NullAppLogger());
        using var service = new ApplicationUpdateService(paths, settings, http, new NullAppLogger(),
            new Source(null), "1.1.0");
        await service.InitializeAsync();
        Assert.False(service.StateUnavailable);
        Assert.Null(service.State.StagedArchive);
        Assert.Null(service.State.LastInstallerRequest);
        Assert.Equal(status, service.StatusKey);
        Assert.Equal(status, service.State.LastStartupResultKey);
        await service.CheckAsync();
        Assert.Equal(status, service.StatusKey);
        using var restarted = new ApplicationUpdateService(paths, settings, http, new NullAppLogger(), new Source(null), "1.1.0");
        await restarted.InitializeAsync();
        Assert.Equal(status, restarted.StatusKey);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RealUpdaterCommitsEvenWhenRestartFails(bool exitsImmediately)
    {
        using var fixture = new Fixture();
        var installation = Directory.CreateDirectory(Path.Combine(fixture.Root, "installation")).FullName;
        var executable = Path.Combine(installation, "ModernTubeDownloader.exe");
        await File.WriteAllTextAsync(executable, "original");
        string archive;
        if (exitsImmediately)
        {
            // A valid apphost without its managed entry assembly launches and exits immediately.
            archive = Path.Combine(fixture.Root, "apphost.zip");
            using var zip = System.IO.Compression.ZipFile.Open(archive, System.IO.Compression.ZipArchiveMode.Create);
            System.IO.Compression.ZipFileExtensions.CreateEntryFromFile(zip, CoreTests.FakeToolPath(), "ModernTubeDownloader.exe");
        }
        else archive = fixture.Zip(("ModernTubeDownloader.exe", "synthetic non-executable payload"));
        var id = Guid.NewGuid();
        var requestFile = Path.Combine(fixture.Root, "request.json");
        var request = new UpdaterRequest(installation, archive, Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(archive))),
            int.MaxValue, 0, true, OperationId: id);
        await File.WriteAllTextAsync(requestFile, JsonSerializer.Serialize(request));

        var start = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "updater", "ModernTubeDownloader.Updater.exe"))
            { UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("--request");
        start.ArgumentList.Add(requestFile);
        using var updater = Process.Start(start)!;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            await updater.WaitForExitAsync(timeout.Token);
            Assert.Equal(3, updater.ExitCode);
            Assert.True(ReadJournal(installation).Completed);
            if (exitsImmediately) Assert.Equal(await File.ReadAllBytesAsync(CoreTests.FakeToolPath()), await File.ReadAllBytesAsync(executable));
            else Assert.Equal("synthetic non-executable payload", await File.ReadAllTextAsync(executable));
            var json = await File.ReadAllTextAsync(requestFile + ".result.json");
            using var result = JsonDocument.Parse(json);
            Assert.True(result.RootElement.GetProperty("success").GetBoolean());
            Assert.Equal(id, result.RootElement.GetProperty("operationId").GetGuid());
            Assert.Equal("restart", result.RootElement.GetProperty("restartError").GetProperty("operation").GetString());
            Assert.Equal(exitsImmediately ? JsonValueKind.Number : JsonValueKind.Null, result.RootElement.GetProperty("restartExitCode").ValueKind);
            Assert.DoesNotContain(installation, json);
            var diagnostics = await File.ReadAllLinesAsync(requestFile + ".diagnostics.jsonl");
            Assert.Contains(diagnostics, line => line.Contains("Updater.Commit.Completed") && line.Contains(id.ToString()));
            Assert.Contains(diagnostics, line => line.Contains("Updater.Restart.Failed") && line.Contains(id.ToString()));
        }
        finally
        {
            if (!updater.HasExited) { updater.Kill(); await updater.WaitForExitAsync(); }
        }
    }

    private static string JournalPath(string installation) => Path.Combine(installation, ".mtd-update-backup", "journal.json");

    private static UpdateJournal ReadJournal(string installation) =>
        JsonSerializer.Deserialize<UpdateJournal>(File.ReadAllText(JournalPath(installation)))!;

    private static string Sha(string value) => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value)));
}
