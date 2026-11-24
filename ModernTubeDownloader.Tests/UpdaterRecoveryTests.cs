using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using ModernTubeDownloader.Updates;

namespace ModernTubeDownloader.Tests;

public sealed partial class ApplicationUpdateTests
{
    [Fact]
    public async Task JournalSharingViolationRollsBackAfterTheReplacementHandleCloses()
    {
        using var fixture = new Fixture();
        var installation = Directory.CreateDirectory(Path.Combine(fixture.Root, "installation")).FullName;
        var executable = Path.Combine(installation, "ModernTubeDownloader.exe");
        await File.WriteAllTextAsync(executable, "old executable");
        var extracted = Path.Combine(fixture.Root, "extracted");
        var files = await UpdatePackageSecurity.ExtractAsync(
            fixture.Zip(("ModernTubeDownloader.exe", "new executable"), ("added.dll", "added"), ("last.dll", "last")), extracted);
        var saves = 0;
        var copied = new List<int>();
        var failure = Assert.Throws<UpdateTransactionException>(() => UpdateTransaction.Install(installation, extracted, files, copied.Add,
            journal => ++saves == 3 ? new FileStream(journal, FileMode.Open, FileAccess.Read, FileShare.Read) : null));

        Assert.Equal([0, 1], copied);
        Assert.Equal("replace-journal", failure.Failure.Operation);
        Assert.Equal("last.dll", failure.Failure.RelativePath);
        Assert.Contains(unchecked((uint)failure.Failure.Error.HResult), new uint[] { 0x80070020, 0x80070005 });
        Assert.Equal(UpdateRecoveryStatus.Restored, failure.Recovery);
        Assert.Empty(failure.RecoveryFailures);
        Assert.Equal("old executable", await File.ReadAllTextAsync(executable));
        Assert.False(File.Exists(Path.Combine(installation, "added.dll")));
        Assert.True(ReadTestJournal(installation).Completed);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PersistentJournalDenialPreservesBothErrorsAndAllowsExplicitRetry(bool failFirstInJournal)
    {
        using var fixture = new Fixture();
        var installation = Directory.CreateDirectory(Path.Combine(fixture.Root, "installation")).FullName;
        var executable = Path.Combine(installation, "ModernTubeDownloader.exe");
        var last = Path.Combine(installation, "last.dll");
        await File.WriteAllTextAsync(executable, "old executable");
        await File.WriteAllTextAsync(last, "old library");
        var extracted = Path.Combine(fixture.Root, "extracted");
        var files = await UpdatePackageSecurity.ExtractAsync(
            fixture.Zip(("ModernTubeDownloader.exe", "new executable"), ("added.dll", "added"), ("last.dll", "new library")), extracted);
        var path = TestJournalPath(installation);
        var firstFailure = new IOException("Installation failed after preparing the added file.");
        try
        {
            var failure = Assert.Throws<UpdateTransactionException>(() => UpdateTransaction.Install(installation, extracted, files, index =>
            {
                if (index != 1) return;
                File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.ReadOnly);
                if (!failFirstInJournal) throw firstFailure;
            }));
            Assert.Equal(UpdateRecoveryStatus.Unknown, failure.Recovery);
            if (failFirstInJournal)
            {
                Assert.Equal("replace-journal", failure.Failure.Operation);
                Assert.IsType<UnauthorizedAccessException>(failure.InnerException);
            }
            else
            {
                Assert.Equal("install-file", failure.Failure.Operation);
                Assert.Same(firstFailure, failure.InnerException);
            }
            var recoveryFailure = Assert.Single(failure.RecoveryFailures);
            Assert.Equal("replace-journal", recoveryFailure.Operation);
            Assert.Equal(0x80070005u, unchecked((uint)recoveryFailure.Error.HResult));
            Assert.Equal("old executable", await File.ReadAllTextAsync(executable));
            Assert.Equal("old library", await File.ReadAllTextAsync(last));
            Assert.False(File.Exists(Path.Combine(installation, "added.dll")));
            var durable = ReadTestJournal(installation);
            Assert.False(durable.Completed);
            Assert.Equal(new[] { "ModernTubeDownloader.exe", "added.dll" }, durable.Files.Select(file => file.RelativePath));
        }
        finally { File.SetAttributes(path, File.GetAttributes(path) & ~FileAttributes.ReadOnly); }

        UpdateTransaction.Install(installation, extracted, files);
        Assert.Equal("new executable", await File.ReadAllTextAsync(executable));
        Assert.Equal("new library", await File.ReadAllTextAsync(last));
        Assert.Equal("added", await File.ReadAllTextAsync(Path.Combine(installation, "added.dll")));
        Assert.True(ReadTestJournal(installation).Completed);
    }

    [Fact]
    public async Task RetryResumesAnInterruptedRollbackBeforeCopyingAnyNewPayload()
    {
        using var fixture = new Fixture();
        var installation = Directory.CreateDirectory(Path.Combine(fixture.Root, "installation")).FullName;
        var backup = Directory.CreateDirectory(Path.Combine(installation, ".mtd-update-backup", "files")).FullName;
        var executable = Path.Combine(installation, "ModernTubeDownloader.exe");
        var alreadyRestored = Path.Combine(installation, "restored.dll");
        var added = Path.Combine(installation, "added.dll");
        await File.WriteAllTextAsync(Path.Combine(backup, "ModernTubeDownloader.exe"), "old executable");
        await File.WriteAllTextAsync(Path.Combine(backup, "restored.dll"), "old library");
        await File.WriteAllTextAsync(alreadyRestored, "old library");
        await File.WriteAllTextAsync(added, "partial installation");
        await File.WriteAllTextAsync(TestJournalPath(installation),
            JsonSerializer.Serialize(new UpdateJournal(installation,
                [new("ModernTubeDownloader.exe", true), new("added.dll", false), new("restored.dll", true)], false)));
        var extracted = Path.Combine(fixture.Root, "extracted");
        var files = await UpdatePackageSecurity.ExtractAsync(fixture.Zip(("ModernTubeDownloader.exe", "new executable")), extracted);

        var callbacks = 0;
        UpdateTransaction.Install(installation, extracted, files, _ =>
        {
            callbacks++;
            Assert.Equal("old executable", File.ReadAllText(executable));
            Assert.Equal("old library", File.ReadAllText(alreadyRestored));
            Assert.False(File.Exists(added));
        });
        Assert.Equal(1, callbacks);
        Assert.Equal("new executable", await File.ReadAllTextAsync(executable));
        Assert.True(ReadTestJournal(installation).Completed);
    }

    [Theory]
    [InlineData("partial")]
    [InlineData("unknown")]
    public async Task RealUpdaterReportsUnfinishedRecoveryEvenWithRestartRequested(string expectedRecovery)
    {
        using var fixture = new Fixture();
        var installation = Directory.CreateDirectory(Path.Combine(fixture.Root, "installation")).FullName;
        var backup = Directory.CreateDirectory(Path.Combine(installation, ".mtd-update-backup", "files")).FullName;
        var executable = Path.Combine(installation, "ModernTubeDownloader.exe");
        var blocked = Path.Combine(installation, "blocked.dll");
        await File.WriteAllTextAsync(executable, "original executable");
        await File.WriteAllTextAsync(blocked, "unrecovered library");
        await File.WriteAllTextAsync(Path.Combine(backup, "blocked.dll"), "original library");
        var added = Path.Combine(installation, "added.dll");
        await File.WriteAllTextAsync(added, "added");
        var journalPath = TestJournalPath(installation);
        var journalText = expectedRecovery == "partial"
            ? JsonSerializer.Serialize(new UpdateJournal(installation, [new("blocked.dll", true), new("added.dll", false)], false))
            : "{ invalid";
        await File.WriteAllTextAsync(journalPath, journalText);
        using var held = new FileStream(blocked, FileMode.Open, FileAccess.Read, FileShare.None);
        var archive = fixture.Zip(("ModernTubeDownloader.exe", "new executable"));
        var hash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(archive)));
        using var parent = Process.Start(new ProcessStartInfo(CoreTests.FakeToolPath(), "--sleep-short") { UseShellExecute = false, CreateNoWindow = true })!;
        var requestFile = Path.Combine(fixture.Root, "request.json");
        var request = new UpdaterRequest(installation, archive, hash, parent.Id, parent.StartTime.ToUniversalTime().Ticks, true);
        await File.WriteAllTextAsync(requestFile, JsonSerializer.Serialize(request));
        var start = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "updater", "ModernTubeDownloader.Updater.exe"))
            { UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("--request");
        start.ArgumentList.Add(requestFile);
        using var process = Process.Start(start)!;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            await process.WaitForExitAsync(timeout.Token);
            Assert.Equal(1, process.ExitCode);
            using var result = JsonDocument.Parse(await File.ReadAllTextAsync(requestFile + ".result.json"));
            Assert.False(result.RootElement.GetProperty("success").GetBoolean());
            Assert.Equal(expectedRecovery, result.RootElement.GetProperty("recovery").GetString());
            var first = result.RootElement.GetProperty("failure").GetProperty("operation").GetString();
            if (expectedRecovery == "partial")
            {
                Assert.Equal("recover-previous-transaction", first);
                var rollbackFailure = Assert.Single(result.RootElement.GetProperty("recoveryFailures").EnumerateArray());
                Assert.Equal("blocked.dll", rollbackFailure.GetProperty("relativePath").GetString());
                Assert.False(File.Exists(added));
            }
            else
            {
                Assert.Equal("read-journal", first);
                Assert.True(File.Exists(added));
            }
            Assert.Equal(journalText, await File.ReadAllTextAsync(journalPath));
            Assert.Equal("original executable", await File.ReadAllTextAsync(executable));
        }
        finally
        {
            if (!process.HasExited) { process.Kill(); await process.WaitForExitAsync(); }
            if (!parent.HasExited) { parent.Kill(); await parent.WaitForExitAsync(); }
        }
    }

    private static string TestJournalPath(string installation) => Path.Combine(installation, ".mtd-update-backup", "journal.json");

    private static UpdateJournal ReadTestJournal(string installation) =>
        JsonSerializer.Deserialize<UpdateJournal>(File.ReadAllText(TestJournalPath(installation)))!;
}
