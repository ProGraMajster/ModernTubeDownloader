using System.IO.Compression;
using System.Text.Json;
using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Organizer;
using ModernTubeDownloader.Services;
using ModernTubeDownloader.Settings;
using ModernTubeDownloader.Theming;
using ModernTubeDownloader.Views;
using ModernFormsNext;
using ModernFormsNext.Testing;
using Path = System.IO.Path;

namespace ModernTubeDownloader.Tests;

public sealed class DiagnosticLoggingTests
{
    [Fact]
    public async Task LevelsChangeImmediatelyAndReloadFromProfile()
    {
        await using var fixture = new OrganizerTests.Fixture();
        var root = fixture.Paths.LogDirectory;
        using (var logger = new FileAppLogger(root))
        {
            var settings = new SettingsService(fixture.Paths, logger);
            await settings.LoadAsync();
            Assert.False(settings.Current.Diagnostics.Advanced);
            logger.Event(Message(DiagnosticLevel.Debug, 10));
            settings.Current.Diagnostics.Advanced = true;
            settings.Current.Diagnostics.IncludeTrace = true;
            await settings.SaveAsync();
            logger.SetDetail(settings.Current.Diagnostics.Advanced, settings.Current.Diagnostics.IncludeTrace);
            logger.Event(Message(DiagnosticLevel.Trace, 20));
            logger.SetDetail(false, false);
            logger.Event(Message(DiagnosticLevel.Debug, 30));
            logger.Event(Message(DiagnosticLevel.Error, 40));
        }
        var state = new SettingsService(fixture.Paths, new NullAppLogger());
        await state.LoadAsync();
        Assert.True(state.Current.Diagnostics.Advanced && state.Current.Diagnostics.IncludeTrace);
        Assert.Equal(new long?[] { 20, 40 }, Read(root).Select(x => x.Counts?.Items).Order().ToArray());
    }

    [Fact]
    public async Task ConcurrentEventsStayWholeWithConsistentSessionAndOperationIds()
    {
        await using var fixture = new OrganizerTests.Fixture();
        var operation = Guid.NewGuid();
        using (var logger = new FileAppLogger(fixture.Paths.LogDirectory))
            Parallel.For(0, 128, i => logger.Event(new(DiagnosticModule.Queue, DiagnosticStage.Run, DiagnosticOutcome.Progress,
                OperationId: operation, Counts: new(Items: i))));
        var rows = Read(fixture.Paths.LogDirectory);
        Assert.Equal(128, rows.Length);
        Assert.Equal(Enumerable.Range(0, 128).Select(i => (long?)i), rows.Select(x => x.Counts?.Items).Order());
        Assert.All(rows, x => Assert.Equal(operation, x.OperationId));
        Assert.Single(rows.Select(x => x.ApplicationSessionId).Distinct());
    }

    [Fact]
    public async Task SegmentsRotateAndExpiredLogsArePrunedWhileUnrelatedFilesRemain()
    {
        await using var fixture = new OrganizerTests.Fixture();
        var root = fixture.Paths.LogDirectory;
        var old = Path.Combine(root, "ModernTubeDownloader-20001001.log");
        await File.WriteAllTextAsync(old, "old");
        File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddDays(-15));
        var unrelated = Path.Combine(root, "custom.log");
        await File.WriteAllTextAsync(unrelated, "keep");
        var limits = new DiagnosticLogLimits(4096, 12288);
        using (var logger = new FileAppLogger(root, limits, TimeProvider.System))
            for (var i = 0; i < 90; i++) logger.Event(Message(DiagnosticLevel.Info, i));
        var logs = DiagnosticLogFiles.List(root);
        Assert.InRange(logs.Length, 2, 4);
        Assert.All(logs, file => Assert.InRange(file.Length, 1, limits.FileBytes));
        Assert.True(logs.Sum(file => file.Length) <= limits.TotalBytes);
        Assert.False(File.Exists(old));
        Assert.Equal("keep", await File.ReadAllTextAsync(unrelated));
    }

    [Fact]
    public async Task BackpressureShedsLowPriorityAndKeepsWarning()
    {
        await using var fixture = new OrganizerTests.Fixture();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using (var logger = new FileAppLogger(fixture.Paths.LogDirectory, new(NormalCapacity: 2, PriorityCapacity: 4),
            TimeProvider.System, file =>
            {
                entered.Set();
                if (!release.Wait(TimeSpan.FromSeconds(15))) throw new IOException("fixture timeout");
                return File.Create(file);
            }))
        {
            logger.SetDetail(true, true);
            logger.Event(Message(DiagnosticLevel.Trace, 1));
            try
            {
                Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));
                for (var i = 0; i < 100; i++) logger.Event(Message(DiagnosticLevel.Trace, i + 2));
                logger.Event(Message(DiagnosticLevel.Warn, 321));
            }
            finally { release.Set(); }
        }
        var records = Read(fixture.Paths.LogDirectory);
        Assert.Contains(records, row => row.LogLevel == DiagnosticLevel.Warn && row.Counts?.Items == 321);
        Assert.Contains(records, row => row.Module == DiagnosticModule.Logging && row.Counts?.Dropped >= 90);
    }

    [Theory]
    [InlineData(unchecked((int)0x80070070))]
    [InlineData(unchecked((int)0x80070005))]
    [InlineData(unchecked((int)0x80070020))]
    public async Task WriterSurvivesDiskAccessAndSharingFailures(int hresult)
    {
        await using var fixture = new OrganizerTests.Fixture();
        var attempts = 0;
        using (var logger = new FileAppLogger(fixture.Paths.LogDirectory, new(), TimeProvider.System, path =>
        {
            if (Interlocked.Increment(ref attempts) == 1) throw new IOException("Private text", hresult);
            return File.Create(path);
        }))
        {
            logger.Event(Message(DiagnosticLevel.Error, 22));
            await WaitAsync(() => logger.LastError == hresult);
            logger.Event(Message(DiagnosticLevel.Warn, 23));
            await WaitAsync(() => Volatile.Read(ref attempts) >= 2 && logger.LastError == 0);
        }
        Assert.Contains(Read(fixture.Paths.LogDirectory), row => row.Counts?.Items == 22);
    }

    [Fact]
    public async Task OrganizerPlanReportsTerminalOutcomeForLargeAndInvalidInputs()
    {
        await using var fixture = new OrganizerTests.Fixture();
        using var logger = new FileAppLogger(fixture.Paths.LogDirectory);
        logger.SetDetail(true, true);
        using var organizer = new DownloadOrganizerService(fixture.Paths, fixture.History, fixture.Queue, fixture.Settings, logger);
        var entries = Enumerable.Range(0, 3500).Select(i => new ModernTubeDownloader.Batch.BatchImportEntry { Url = OrganizerTests.Fixture.Url(i) }).ToArray();
        var list = new OrganizerSourceList("Private media list", fixture.Destination, Entries: entries);
        var good = Guid.NewGuid();
        var bad = Guid.NewGuid();
        var plan = await organizer.BuildPlanAsync([list], fixture.Source, true, false, good, default);
        Assert.Equal(3500, plan.Matches.Count);
        await Assert.ThrowsAsync<OrganizerPathException>(() =>
            organizer.BuildPlanAsync([list], Path.Combine(fixture.Root, "Private path"), true, false, bad, default));
        logger.Dispose();
        var rows = Read(fixture.Paths.LogDirectory);
        Assert.Contains(rows, row => row.OperationId == good && row.Stage == DiagnosticStage.Plan && row.Outcome == DiagnosticOutcome.Completed);
        Assert.Contains(rows, row => row.OperationId == bad && row.Stage == DiagnosticStage.Plan && row.Outcome == DiagnosticOutcome.Failed);
        Assert.Contains(rows, row => row.OperationId == good && row.Counts?.Items == 3500);
        Assert.DoesNotContain(Directory.EnumerateFiles(fixture.Paths.LogDirectory).SelectMany(File.ReadLines),
            line => line.Contains("Private", StringComparison.Ordinal) || line.Contains("example.test", StringComparison.Ordinal));
    }

    private static DiagnosticEvent Message(DiagnosticLevel level, int i) =>
        new(DiagnosticModule.Application, DiagnosticStage.Run, DiagnosticOutcome.Progress, level, Counts: new(Items: i));

    private static DiagnosticRecord[] Read(string root) => DiagnosticLogFiles.List(root)
        .SelectMany(file => File.ReadLines(file.FullName)).Select(line => JsonSerializer.Deserialize<DiagnosticRecord>(line, DiagnosticRecord.Json)!).ToArray();

    private static async Task WaitAsync(Func<bool> check)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        while (!check()) await Task.Delay(20, timeout.Token);
    }
}

public sealed partial class ExperiencePresentationTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task DiagnosticsCategoryIsSharedAndPersistsChanges(bool modern)
    {
        using var fixture = new Fixture();
        using var services = await fixture.Services(AppThemeMode.Dark);
        using (var host = ModernFormsTestHost.Create())
        {
            services.Appearance.Initialize();
            using var view = new SettingsView(services);
            AppTypography.SetDashboardPresentation(view, modern);
            view.SetDashboard(modern);
            host.Show(view, 1320, 1000);
            Ui(host, () => view.ShowCategory("Diagnostics"));
            var toggle = Find<CheckBox>(view, "DiagnosticsAdvanced");
            var detail = Find<ComboBox>(view, "DiagnosticsLevel");
            Assert.True(toggle.Visible);
            Assert.False(detail.Enabled);
            Ui(host, () => toggle.Checked = true);
            Pump(host, () => detail.Enabled && services.Logger.IsEnabled(DiagnosticLevel.Debug));
            Ui(host, () => detail.SelectedIndex = 1);
            Pump(host, () => detail.Enabled && services.Logger.IsEnabled(DiagnosticLevel.Trace));
            var reopen = new SettingsService(services.Paths, new NullAppLogger());
            var read = reopen.LoadAsync();
            Pump(host, () => read.IsCompleted);
            await read;
            Assert.True(reopen.Current.Diagnostics.Advanced && reopen.Current.Diagnostics.IncludeTrace);
            host.Dispatcher.ThrowUnhandledExceptions();
        }
        await services.ShutdownAsync();
    }

    [Fact]
    public async Task DiagnosticsExportWritesOnlyValidatedSnapshotsAndStructuredRecords()
    {
        using var fixture = new Fixture();
        using var services = await fixture.Services(AppThemeMode.Dark);
        var root = services.Paths.LogDirectory;
        const string secret = "fixture-token-do-not-export";
        var legacy = Path.Combine(root, "ModernTubeDownloader-20260101.log");
        await File.WriteAllTextAsync(legacy, secret);
        var sessionId = Guid.NewGuid();
        var forged = DiagnosticRecord.Create(new(DiagnosticModule.Updater, DiagnosticStage.Run, DiagnosticOutcome.Failed,
            OperationId: sessionId), sessionId, FileAppLogger.ApplicationVersion) with { CallSite = secret, EventName = secret, CancellationReason = secret };
        var path = Path.Combine(root, $"MTD-events-v1-{DateTime.UtcNow:yyyyMMdd}-{sessionId:N}-000001.log");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(forged, DiagnosticRecord.Json) + "\n" + secret);
        var updater = Path.Combine(services.Paths.RootDirectory, "Updates", "runner-" + sessionId.ToString("N"));
        Directory.CreateDirectory(updater);
        await File.WriteAllTextAsync(Path.Combine(updater, "request.json.diagnostics.jsonl"), JsonSerializer.Serialize(forged, DiagnosticRecord.Json) + "\n");
        await File.WriteAllTextAsync(Path.Combine(updater, "request.json"), secret);
        var dest = Path.Combine(services.Paths.RootDirectory, "diagnostics-test.zip");
        var counts = await services.Diagnostics.ExportAsync(dest, true, default);
        Assert.True(counts.Included >= 2);
        Assert.True(counts.Excluded >= 1);
        using (var zip = ZipFile.OpenRead(dest))
        {
            Assert.Contains(zip.Entries, x => x.FullName.StartsWith("updater/", StringComparison.Ordinal));
            Assert.Contains(zip.Entries, x => x.Name == "settings-redacted.json");
            foreach (var entry in zip.Entries)
            {
                using var reader = new StreamReader(entry.Open());
                Assert.DoesNotContain(secret, await reader.ReadToEndAsync());
                Assert.DoesNotContain("CookieFilePath", entry.Name, StringComparison.OrdinalIgnoreCase);
            }
        }
        var cancelled = Path.Combine(services.Paths.RootDirectory, "cancelled.zip");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => services.Diagnostics.ExportAsync(cancelled, true, new(true)));
        Assert.False(File.Exists(cancelled));
        Assert.Empty(Directory.EnumerateFiles(services.Paths.RootDirectory, ".mtd-diagnostics-*.tmp"));
        await services.ShutdownAsync();
    }
}
