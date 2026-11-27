using System.Globalization;
using ModernTubeDownloader.Batch;
using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Models;
using ModernTubeDownloader.Organizer;
using ModernTubeDownloader.Services;

namespace ModernTubeDownloader.Tests;

public sealed partial class WorkflowIntegrationTests
{
    [Fact]
    [Trait("Scenario", "MIXED-C01")]
    public async Task BatchCompletionMixedVodLiveOrganizerHistoryAndRestartUseOneDurableRuntime()
    {
        var paths = AppPaths.Create(Path.Combine(root, "mixed-completion")); await SaveCustomToolSettingsAsync(paths);
        var settings = new SettingsService(paths, new NullAppLogger()); await settings.LoadAsync();
        settings.Current.QueuePaused = true;
        settings.Current.MaxSimultaneousDownloads = 2; settings.Current.MaxSimultaneousLiveRecordings = 1;
        settings.Current.FinalOutputDirectory = Path.Combine(root, "mixed-completion-media");
        settings.Current.TemporaryDirectory = Path.Combine(paths.RootDirectory, "temp");
        settings.Current.StoreMetadataJson = settings.Current.SaveMetadataJsonSidecar = true;
        await settings.SaveAsync(); Directory.CreateDirectory(settings.Current.FinalOutputDirectory);
        var first = Path.Combine(paths.RootDirectory, "List A.txt"); var second = Path.Combine(paths.RootDirectory, "List B.json");
        await File.WriteAllTextAsync(first, "https://example.test/watch?id=mixed-one&hold=1");
        await File.WriteAllTextAsync(second, "[\"https://example.test/watch?id=mixed-two&hold=1\"]");
        Guid[] jobIds, sessionIds; Guid liveId; string[] outputPaths;
        using (var services = AppServices.Create(paths))
        {
            try
            {
                await services.Tools.Initialization;
                var sessions = await services.Batch.ImportFilesAsync([first, second], "720", PreferredVideoContainer.Mp4);
                await services.Batch.RunAutomaticAsync(sessions);
                sessionIds = sessions.Select(value => value.Id).ToArray(); jobIds = services.Queue.Snapshot().Select(value => value.Id).ToArray();
                Assert.Equal(2, jobIds.Length); Assert.True(services.Queue.IsPaused);
                var metadata = await services.Metadata.AnalyzeAsync("https://example.test/live/active?behavior=wait&id=mixed-live");
                var live = services.Live.Add(metadata.WebpageUrl, metadata, "720", LiveStartPolicy.FromNow); liveId = live.Id;
                Assert.True(await services.Queue.ResumeAsync());
                await WaitUntilAsync(() => services.Processor.ActiveItemIds.Count == 2 && services.LiveScheduler.ActiveIds.Contains(live.Id) &&
                    Directory.Exists(settings.Current.TemporaryDirectory) &&
                    Directory.GetFiles(settings.Current.TemporaryDirectory, ".test-started.json", SearchOption.AllDirectories).Length == 3,
                    TimeSpan.FromSeconds(10));
                var markers = Directory.GetFiles(settings.Current.TemporaryDirectory, ".test-started.json", SearchOption.AllDirectories);
                var pids = await Task.WhenAll(markers.Select(async marker => int.Parse(await File.ReadAllTextAsync(marker))));
                Assert.Equal(3, pids.Distinct().Count());
                foreach (var pid in pids)
                {
                    using var process = System.Diagnostics.Process.GetProcessById(pid);
                    Assert.False(process.HasExited); Assert.Contains("FakeTool", process.ProcessName);
                }
                Assert.Equal(jobIds.Order(), services.Processor.ActiveItemIds.Order());
                Assert.Empty(services.History.Snapshot());
                var destination = Path.Combine(root, "mixed-completion-organized");
                var lists = new[] { new OrganizerSourceList("List A", destination, Entries: sessions[0].Snapshot()) };
                var activePlan = await services.Organizer.BuildPlanAsync(lists, settings.Current.FinalOutputDirectory, true);
                Assert.DoesNotContain(activePlan.Matches, entry => entry.Status == OrganizerMatchStatus.Found);
                Assert.Equal(2, services.Processor.ActiveItemIds.Count); Assert.Contains(liveId, services.LiveScheduler.ActiveIds);
                foreach (var marker in markers.Where(value => !value.Contains("LiveSessions", StringComparison.Ordinal)))
                    await File.WriteAllTextAsync(Path.Combine(Path.GetDirectoryName(marker)!, ".test-finish.json"), "true");
                foreach (var id in jobIds) Assert.Equal(DownloadStatus.Completed,
                    (await WaitForTerminalStateAsync(services.Queue, id, TimeSpan.FromSeconds(25))).Status);
                Assert.Contains(liveId, services.LiveScheduler.ActiveIds);
                // Assert durable Batch state without opening a view or calling RefreshQueue.
                using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
                {
                    var store = new BatchSessionStore(Path.Combine(paths.RootDirectory, "Batch"));
                    foreach (var id in sessionIds)
                        while ((await store.RecentAsync(timeout.Token)).Single(session => session.Id == id).WorkflowPhase != BatchWorkflowPhase.Completed)
                            await Task.Delay(25, timeout.Token);
                }
                var completedPlan = await services.Organizer.BuildPlanAsync(lists, settings.Current.FinalOutputDirectory, true);
                Assert.Equal(OrganizerMatchStatus.Found, Assert.Single(completedPlan.Matches).Status);
                Assert.False(Directory.Exists(destination)); // Dry run never moves files.
                outputPaths = services.Queue.Snapshot().Select(value => value.FinalFile!).ToArray();
                Assert.All(outputPaths, path => Assert.True(File.Exists(path)));
                Assert.Equal(2, outputPaths.Select(Path.GetDirectoryName).Distinct().Count());
                Assert.True(services.Live.StopAndSave(liveId));
                Assert.Equal(LiveSessionState.Completed, (await WaitForLiveTerminalAsync(services.Live, liveId, TimeSpan.FromSeconds(20))).State);
                var history = services.History.Snapshot(); Assert.Equal(3, history.Count);
                Assert.Single(history, value => value.WasLiveRecording);
                var filtered = HistoryPage.Filter(history, "List A", CultureInfo.InvariantCulture, CancellationToken.None);
                Assert.Equal(jobIds[0], Assert.Single(filtered).QueueItemId);
                Assert.Equal(1, HistoryPage.Count(filtered.Length)); Assert.Equal(0, HistoryPage.Clamp(99, filtered.Length));
                Assert.False(await services.Batch.HasUnfinishedSessionsAsync());
            }
            finally { await services.ShutdownAsync(); }
        }
        using var restarted = AppServices.Create(paths);
        try
        {
            Assert.Equal(jobIds.Order(), restarted.Queue.Snapshot().Select(value => value.Id).Order());
            Assert.Equal(outputPaths.Order(), restarted.Queue.Snapshot().Select(value => value.FinalFile).Order());
            Assert.Equal(LiveSessionState.Completed, restarted.Live.Find(liveId)!.State);
            Assert.Empty(restarted.Processor.ActiveItemIds); Assert.Empty(restarted.LiveScheduler.ActiveIds);
            var sessions = await restarted.Batch.ImportFilesAsync([first, second], "720", PreferredVideoContainer.Mp4);
            Assert.Equal(sessionIds, sessions.Select(value => value.Id)); await restarted.Batch.RunAutomaticAsync(sessions);
            Assert.Equal(jobIds.Order(), restarted.Queue.Snapshot().Select(value => value.Id).Order());
            Assert.Equal(3, restarted.History.Snapshot().Count); Assert.False(await restarted.Batch.HasUnfinishedSessionsAsync());
        }
        finally { await restarted.ShutdownAsync(); }
    }
}
