using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Models;
using ModernTubeDownloader.Services;

namespace ModernTubeDownloader.Tests;

public sealed partial class WorkflowIntegrationTests
{
    [Fact]
    public async Task UnreadableHistoryBlocksPendingAndActiveVodAndLiveUntilRepairedRuntimeReopens()
    {
        var paths = AppPaths.Create(Path.Combine(root, "history-dependent-admission"));
        await SaveCustomToolSettingsAsync(paths);
        var settings = new SettingsService(paths, new NullAppLogger());
        await settings.LoadAsync();
        settings.Current.QueuePaused = false;
        settings.Current.AutoResumeAfterRestart = true;
        settings.Current.AutoResumeLiveAfterRestart = true;
        settings.Current.MaxSimultaneousDownloads = 2;
        settings.Current.MaxSimultaneousLiveRecordings = 2;
        settings.Current.ResumeLiveOnFailure = false;
        settings.Current.TemporaryDirectory = Path.Combine(paths.RootDirectory, "temp");
        settings.Current.FinalOutputDirectory = Path.Combine(paths.RootDirectory, "output");
        await settings.SaveAsync();
        var vod = new[]
        {
            new DownloadQueueItem { SourceUrl = "https://example.test/watch?id=history-pending&hold=1", VideoId = "history-pending", Status = DownloadStatus.Queued, QualityPresetId = "720" },
            new DownloadQueueItem { SourceUrl = "https://example.test/watch?id=history-active&hold=1", VideoId = "history-active", Status = DownloadStatus.DownloadingVideo, QualityPresetId = "720" }
        };
        var live = new[]
        {
            new LiveSession { SourceUrl = "https://example.test/live/active?behavior=wait&id=history-pending", State = LiveSessionState.Pending, QualityPresetId = "720" },
            new LiveSession { SourceUrl = "https://example.test/live/active?behavior=wait&id=history-active", State = LiveSessionState.Recording, QualityPresetId = "720" }
        };
        await new QueuePersistenceService(paths, new NullAppLogger()).SaveAsync(vod);
        await new LiveSessionPersistenceService(paths, new NullAppLogger()).SaveAsync(live);
        await File.WriteAllTextAsync(paths.HistoryFile, "null");
        var queueBytes = await File.ReadAllBytesAsync(paths.QueueFile);
        var liveBytes = await File.ReadAllBytesAsync(paths.LiveSessionsFile);
        var historyBytes = await File.ReadAllBytesAsync(paths.HistoryFile);
        for (var restart = 0; restart < 2; restart++)
        {
            using var services = AppServices.Create(paths, deferWorkStart: true);
            try
            {
                await services.Tools.Initialization;
                Assert.True(services.History.LoadFailed);
                Assert.False(services.Queue.LoadFailed); Assert.False(services.Live.LoadFailed);
                Assert.True(services.Queue.IsReadOnly); Assert.True(services.Live.IsReadOnly);
                Assert.True(services.Queue.Storage.State.IsBlocked);
                Assert.Equal(vod.Select(item => (item.Id, item.Status)), services.Queue.Snapshot().Select(item => (item.Id, item.Status)));
                Assert.Equal(live.Select(item => (item.Id, item.State)), services.Live.Snapshot().Select(item => (item.Id, item.State)));
                Assert.False(await services.Queue.ResumeAsync());
                services.Queue.SetPaused(false);
                Assert.True(services.Queue.IsPaused);
                Assert.False(services.Queue.Remove(vod[0].Id));
                Assert.Equal(0, services.Queue.RemoveAllPending());
                Assert.False(services.Queue.Move(vod[0].Id, 1));
                Assert.Throws<IOException>(() => services.Queue.Add(new()));
                Assert.Throws<IOException>(() => services.Queue.AddRangeSkippingDuplicates([new()]));
                Assert.Throws<IOException>(() => services.Live.Add("https://example.test/live/new", null, "720", LiveStartPolicy.FromNow));
                Assert.False(services.Live.Cancel(live[0].Id));
                Assert.False(services.Live.Resume(live[1].Id));
                await Assert.ThrowsAsync<IOException>(() => services.Queue.SaveAsync());
                await Assert.ThrowsAsync<IOException>(() => services.Live.SaveAsync());
                await services.StartWorkAsync();
                // Direct scheduler calls must not bypass the shared runtime guard.
                services.Processor.Start(); services.LiveScheduler.Start();
                Assert.Null(services.Queue.TryTakeNext());
                Assert.Null(services.Live.TakePending());
                Assert.Empty(services.Processor.ActiveItemIds); Assert.Empty(services.LiveScheduler.ActiveIds);
            }
            finally { await services.ShutdownAsync(); }
            Assert.Equal(queueBytes, await File.ReadAllBytesAsync(paths.QueueFile));
            Assert.Equal(liveBytes, await File.ReadAllBytesAsync(paths.LiveSessionsFile));
            Assert.Equal(historyBytes, await File.ReadAllBytesAsync(paths.HistoryFile));
            Assert.False(Directory.Exists(settings.Current.TemporaryDirectory) &&
                Directory.EnumerateFiles(settings.Current.TemporaryDirectory, ".test-started.json", SearchOption.AllDirectories).Any());
        }

        // Explicit synthetic repair and a new runtime restore admission without
        // replacing canonical IDs; actual owned FakeTool processes prove it.
        await File.WriteAllTextAsync(paths.HistoryFile, "[]");
        using var reopened = AppServices.Create(paths, deferWorkStart: true);
        var witnesses = new List<System.Diagnostics.Process>();
        try
        {
            await reopened.Tools.Initialization;
            Assert.False(reopened.History.LoadFailed);
            Assert.False(reopened.Queue.IsReadOnly); Assert.False(reopened.Live.IsReadOnly);
            Assert.True(await reopened.Queue.ResumeAsync());
            await reopened.StartWorkAsync();
            string[] markers = [];
            await WaitUntilAsync(() =>
            {
                if (!Directory.Exists(settings.Current.TemporaryDirectory)) return false;
                markers = Directory.GetFiles(settings.Current.TemporaryDirectory, ".test-started.json", SearchOption.AllDirectories);
                return markers.Length == 4 && markers.All(path =>
                {
                    try { return int.TryParse(File.ReadAllText(path), out var pid) && pid > 0; }
                    catch (IOException) { return false; }
                });
            }, TimeSpan.FromSeconds(10));
            foreach (var marker in markers)
            {
                var process = System.Diagnostics.Process.GetProcessById(int.Parse(await File.ReadAllTextAsync(marker)));
                witnesses.Add(process); Assert.False(process.HasExited); Assert.Contains("FakeTool", process.ProcessName);
            }
            Assert.Equal(4, witnesses.Select(process => process.Id).Distinct().Count());
            Assert.Equal(vod.Select(item => item.Id).Order(), reopened.Processor.ActiveItemIds.Order());
            Assert.Equal(live.Select(item => item.Id).Order(), reopened.LiveScheduler.ActiveIds.Order());
        }
        finally
        {
            try
            {
                await reopened.ShutdownAsync();
                Assert.All(witnesses, process => Assert.True(process.HasExited, $"Owned child {process.Id} survived shutdown."));
            }
            finally { foreach (var witness in witnesses) witness.Dispose(); }
        }
        Assert.Equal(vod.Select(item => item.Id).Order(), reopened.Queue.Snapshot().Select(item => item.Id).Order());
        Assert.Equal(live.Select(item => item.Id).Order(), reopened.Live.Snapshot().Select(item => item.Id).Order());
    }

    [Fact]
    public async Task FailedLegacyLiveMigrationOpensDiagnosticRuntimeWithoutMutatingEitherStore()
    {
        var paths = AppPaths.Create(Path.Combine(root, "legacy-corrupt-live"));
        await SaveCustomToolSettingsAsync(paths);
        var legacy = new DownloadQueueItem
        {
            SourceUrl = "https://example.test/live/active", VideoId = "live", Status = DownloadStatus.RecordingLive,
            LiveSession = new LiveSession { SourceUrl = "https://example.test/live/active" }
        };
        await new QueuePersistenceService(paths, new NullAppLogger()).SaveAsync([new() { VideoId = "vod" }, legacy]);
        await File.WriteAllTextAsync(paths.LiveSessionsFile, "{retained-invalid-live");
        var queueBefore = await File.ReadAllBytesAsync(paths.QueueFile);
        var liveBefore = await File.ReadAllBytesAsync(paths.LiveSessionsFile);
        using var services = AppServices.Create(paths, deferWorkStart: true);
        Assert.True(services.Queue.LoadFailed); Assert.True(services.Live.LoadFailed);
        await services.StartWorkAsync();
        Assert.Empty(services.Processor.ActiveItemIds); Assert.Empty(services.LiveScheduler.ActiveIds);
        await services.ShutdownAsync();
        Assert.Equal(queueBefore, await File.ReadAllBytesAsync(paths.QueueFile));
        Assert.Equal(liveBefore, await File.ReadAllBytesAsync(paths.LiveSessionsFile));
    }

    [Theory]
    [InlineData("queue.json")]
    [InlineData("live-sessions.json")]
    public async Task MalformedRuntimeStoreSurvivesTwoCompleteApplicationServiceLifetimes(string fileName)
    {
        var paths = AppPaths.Create(Path.Combine(root, "corrupt-runtime-" + fileName));
        await SaveCustomToolSettingsAsync(paths);
        var file = Path.Combine(paths.RootDirectory, fileName);
        var bytes = System.Text.Encoding.UTF8.GetBytes("[{\"id\":\"retained forensic marker — 日本語\",\"unfinished\":");
        await File.WriteAllBytesAsync(file, bytes);
        for (var restart = 0; restart < 2; restart++)
        {
            using var services = AppServices.Create(paths, deferWorkStart: true);
            Assert.True(services.Queue.Storage.State.IsBlocked);
            Assert.True(fileName == "queue.json" ? services.Queue.LoadFailed : services.Live.LoadFailed);
            await services.StartWorkAsync();
            Assert.Empty(services.Processor.ActiveItemIds);
            Assert.Empty(services.LiveScheduler.ActiveIds);
            await services.ShutdownAsync();
            Assert.Equal(bytes, await File.ReadAllBytesAsync(file));
        }
    }
}
