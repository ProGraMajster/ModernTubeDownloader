using System.Diagnostics;
using ModernTubeDownloader.Batch;
using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Models;
using ModernTubeDownloader.Services;

namespace ModernTubeDownloader.Tests;

public sealed partial class WorkflowIntegrationTests
{
    [Fact]
    [Trait("Scenario", "SHUTDOWN-C01")]
    public async Task ShutdownDrainsRealVodAndLiveProcessesAfterBatchJournalCommitFails()
    {
        var paths = AppPaths.Create(Path.Combine(root, "shutdown-journal-failure"));
        await SaveCustomToolSettingsAsync(paths);
        var settings = new SettingsService(paths, new NullAppLogger());
        await settings.LoadAsync();
        settings.Current.QueuePaused = true;
        settings.Current.MaxSimultaneousDownloads = 2;
        settings.Current.MaxSimultaneousLiveRecordings = 1;
        settings.Current.RetryFailedDownloads = false;
        settings.Current.ResumeLiveOnFailure = false;
        settings.Current.TemporaryDirectory = Path.Combine(paths.RootDirectory, "temp");
        settings.Current.FinalOutputDirectory = Path.Combine(paths.RootDirectory, "output");
        await settings.SaveAsync();
        var source = Path.Combine(paths.RootDirectory, "completed-list.txt");
        await File.WriteAllTextAsync(source, "https://example.test/watch?id=shutdown-batch&hold=1");
        var sourceBytes = await File.ReadAllBytesAsync(source);
        using var services = AppServices.Create(paths, deferWorkStart: true);
        var witnesses = new List<Process>();
        FileStream? blockedJournal = null;
        try
        {
            await services.Tools.Initialization;
            var session = await services.Batch.ImportAsync(source, "720", PreferredVideoContainer.Mp4);
            await services.Batch.RunAutomaticAsync(session);
            var batchJob = Assert.Single(services.Queue.Snapshot());
            var metadata = await services.Metadata.AnalyzeAsync("https://example.test/watch?id=shutdown-vod&hold=1");
            var quality = QualityPreset.Find("720");
            var heldVod = DownloadQueueItem.FromMetadata(metadata, quality, FormatSelector.Select(metadata, quality));
            services.Queue.Add(heldVod);
            var liveMetadata = await services.Metadata.AnalyzeAsync("https://example.test/live/active?behavior=wait&id=shutdown-live");
            var heldLive = services.Live.Add(liveMetadata.WebpageUrl, liveMetadata, "720", LiveStartPolicy.FromNow);
            var canonicalIds = new[] { batchJob.Id, heldVod.Id }.Order().ToArray();
            Assert.True(await services.Queue.ResumeAsync());
            await services.StartWorkAsync();

            string[] markers = [];
            await WaitUntilAsync(() =>
            {
                if (!Directory.Exists(settings.Current.TemporaryDirectory)) return false;
                markers = Directory.GetFiles(settings.Current.TemporaryDirectory, ".test-started.json", SearchOption.AllDirectories);
                return markers.Length == 3 && markers.All(marker => TryReadPid(marker, out _)) &&
                    services.Processor.ActiveItemIds.Count == 2 && services.LiveScheduler.ActiveIds.Contains(heldLive.Id);
            }, TimeSpan.FromSeconds(10));
            foreach (var marker in markers)
            {
                Assert.True(TryReadPid(marker, out var pid));
                var process = Process.GetProcessById(pid);
                witnesses.Add(process);
                Assert.False(process.HasExited);
                Assert.Contains("FakeTool", process.ProcessName);
            }
            Assert.Equal(3, witnesses.Select(process => process.Id).Distinct().Count());

            // Reach an actual running download before denying the Batch journal's
            // next write. All three child processes are real isolated FakeTool jobs.
            await services.Batch.RefreshQueueAsync(session);
            var priorStatus = Assert.Single(session.Snapshot()).Status;
            Assert.NotEqual(BatchEntryStatus.Completed, priorStatus);
            var journal = Path.Combine(paths.RootDirectory, "Batch", session.Id.ToString("N"), "updates.jsonl");
            blockedJournal = new FileStream(journal, FileMode.Open, FileAccess.Read, FileShare.Read);
            var journalBytes = await File.ReadAllBytesAsync(journal);
            var batchMarker = Assert.Single(markers, marker => marker.Contains(batchJob.JobSessionId.ToString("N"), StringComparison.OrdinalIgnoreCase));
            await File.WriteAllTextAsync(Path.Combine(Path.GetDirectoryName(batchMarker)!, ".test-finish.json"), "true");
            var completed = await WaitForTerminalStateAsync(services.Queue, batchJob.Id, TimeSpan.FromSeconds(25));
            Assert.Equal(DownloadStatus.Completed, completed.Status);
            await WaitUntilAsync(() => !services.Processor.ActiveItemIds.Contains(batchJob.Id), TimeSpan.FromSeconds(5));
            await Assert.ThrowsAsync<IOException>(() => services.Batch.RefreshQueueAsync(session));
            Assert.Equal(priorStatus, Assert.Single(session.Snapshot()).Status);
            Assert.Equal(journalBytes, await File.ReadAllBytesAsync(journal));
            Assert.Equal(heldVod.Id, Assert.Single(services.Processor.ActiveItemIds));
            Assert.Equal(heldLive.Id, Assert.Single(services.LiveScheduler.ActiveIds));
            Assert.Equal(2, witnesses.Count(process => !process.HasExited));
            var completedPath = Assert.IsType<string>(completed.FinalFile);
            var completedBytes = await File.ReadAllBytesAsync(completedPath);
            Assert.NotEmpty(completedBytes);
            Assert.Contains(services.History.Snapshot(), entry => entry.QueueItemId == batchJob.Id && entry.FinalPath == completedPath);

            // Hold the OS-level write denial until Shutdown returns: success here
            // would hide the storage failure, but an early throw would strand owners.
            var failure = await Assert.ThrowsAsync<AggregateException>(() =>
                services.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.Contains(failure.Flatten().InnerExceptions, error => error is IOException);
            Assert.Empty(services.Processor.ActiveItemIds);
            Assert.Empty(services.LiveScheduler.ActiveIds);
            Assert.All(witnesses, process => Assert.True(process.HasExited, $"Owned child {process.Id} survived shutdown."));
            Assert.Equal(DownloadStatus.Interrupted, services.Queue.Find(heldVod.Id)!.Status);
            Assert.Equal(canonicalIds, services.Queue.Snapshot().Select(item => item.Id).Order());
            Assert.Equal(canonicalIds, (await new QueuePersistenceService(paths, new NullAppLogger()).LoadAsync()).Select(item => item.Id).Order());
            Assert.Equal(heldLive.Id, Assert.Single(await new LiveSessionPersistenceService(paths, new NullAppLogger()).LoadAsync()).Id);
            Assert.Equal(journalBytes, await File.ReadAllBytesAsync(journal));
            Assert.Equal(sourceBytes, await File.ReadAllBytesAsync(source));
            Assert.Equal(completedBytes, await File.ReadAllBytesAsync(completedPath));
            output.WriteLine($"SHUTDOWN-C01: Batch={batchJob.Id}; VOD={heldVod.Id}; LIVE={heldLive.Id}; " +
                $"ownedPids={string.Join(',', witnesses.Select(process => process.Id))}; exited=3; retainedJournalBytes={journalBytes.Length}; " +
                $"retainedCompletedBytes={completedBytes.Length}; aggregateErrors={failure.Flatten().InnerExceptions.Count}");
        }
        finally
        {
            blockedJournal?.Dispose();
            // On the unfixed product, the assertions must fail while the fixture
            // still drains its own real children. This is cleanup, never a retry.
            await Cleanup("Updates", services.Updates.StopAsync);
            await Cleanup("Batch", services.Batch.StopAsync);
            await Cleanup("Organizer", services.Organizer.StopAsync);
            await Cleanup("VOD", services.Processor.StopAsync);
            await Cleanup("LIVE", services.LiveScheduler.StopAsync);
            await Cleanup("Remote", services.WebRemote.StopAsync);
            await Cleanup("Tools", services.Tools.StopAsync);
            foreach (var process in witnesses) process.Dispose();
        }

        async Task Cleanup(string owner, Func<Task> stop)
        {
            try { await stop().WaitAsync(TimeSpan.FromSeconds(10)); }
            catch (Exception error) { output.WriteLine($"SHUTDOWN-C01 cleanup {owner}: {error.GetType().Name}: {error.Message}"); }
        }
        static bool TryReadPid(string marker, out int pid)
        {
            pid = 0;
            try { return int.TryParse(File.ReadAllText(marker), out pid) && pid > 0; }
            catch (IOException) { return false; }
        }
    }
}
