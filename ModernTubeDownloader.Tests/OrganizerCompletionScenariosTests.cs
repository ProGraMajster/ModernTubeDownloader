using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ModernTubeDownloader.Batch;
using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Models;
using ModernTubeDownloader.Organizer;
using ModernTubeDownloader.Services;
using Xunit.Abstractions;
using Fixture = ModernTubeDownloader.Tests.OrganizerTests.Fixture;

namespace ModernTubeDownloader.Tests;

public sealed class OrganizerCompletionScenariosTests(ITestOutputHelper output)
{
    private sealed class Failure(string target, bool copy = false, int? hresult = null) : IOrganizerFaultObserver
    {
        private bool fired;
        public bool ForceVerifiedCopy => copy;
        public void Checkpoint(string step, int completedFiles)
        {
            if (fired || step != target) return;
            fired = true;
            throw new IOException("Synthetic organizer boundary: " + step, hresult ?? unchecked((int)0x80004005));
        }
    }

    [Theory]
    [InlineData("provider")]
    [InlineData("id")]
    [InlineData("range")]
    public async Task ConflictingCanonicalReferencesCannotAuthorizeAFileMove(string conflict)
    {
        await using var f = new Fixture(); var original = await f.SeedAsync();
        var incompatible = original with { QueueItemId = Guid.NewGuid() };
        incompatible = conflict switch
        {
            "provider" => incompatible with { SourceUrl = "https://other.test/watch/1" },
            "id" => incompatible with { VideoId = "another-media-id" },
            _ => incompatible with { RequestedRange = new(MediaRangeMode.Custom, TimeSpan.Zero, TimeSpan.FromSeconds(4)) }
        };
        await f.History.AddAsync(incompatible);
        var before = await File.ReadAllBytesAsync(f.Paths.HistoryFile);
        var service = f.Service(); var match = Assert.Single((await f.PlanAsync(service)).Matches);
        Assert.Equal(OrganizerMatchStatus.Attention, match.Status);
        Assert.True(File.Exists(original.FinalPath)); Assert.False(Directory.Exists(f.Destination));
        Assert.Equal(before, await File.ReadAllBytesAsync(f.Paths.HistoryFile)); Assert.Empty(await service.JournalsAsync());
    }

    [Fact]
    public async Task SidecarOnlyJsonPairMustAgreeOnIdEvenWhenProviderUrlMatches()
    {
        await using var f = new Fixture(); var media = Path.Combine(f.Source, "same title.mp4");
        await File.WriteAllBytesAsync(media, [1, 2, 3]);
        await File.WriteAllTextAsync(Path.ChangeExtension(media, ".json"), Fixture.Metadata());
        await File.WriteAllTextAsync(Path.ChangeExtension(media, ".info.json"), Fixture.Metadata().Replace("\"id\": \"1\"", "\"id\": \"foreign-id\""));
        var service = f.Service(); var plan = await f.PlanAsync(service);
        Assert.Equal(OrganizerMatchStatus.Attention, plan.Matches.Single().Status);
        await Assert.ThrowsAsync<IOException>(() => service.MoveSelectedAsync(plan, [plan.Matches.Single().Id]));
        Assert.True(File.Exists(media)); Assert.Equal(3, Directory.GetFiles(f.Source).Length);
    }

    [Fact]
    public async Task LegacyJournalWithoutStoredVideoIdRecoversEvidenceFromItsHashedJson()
    {
        await using var f = new Fixture(); var original = await f.SeedAsync(); var service = f.Service();
        var plan = await f.PlanAsync(service); var match = Assert.Single(plan.Matches); match.VerifiedVideoId = null;
        var legacy = new OrganizerJournal { ScanRoot = f.Source, Matches = [match] };
        await AtomicJsonFile.WriteAsync(Path.Combine(service.JournalDirectory, legacy.Id.ToString("N") + ".json"), legacy);
        await f.History.AddAsync(original with { VideoId = "changed" }); f.Queue.Update(original.QueueItemId, item => item.VideoId = "changed", persist: false); await f.Queue.SaveAsync();
        await Assert.ThrowsAsync<IOException>(() => service.RecoverAsync(legacy.Id));
        Assert.All(match.Files, file => Assert.True(File.Exists(file.Source))); Assert.False(Directory.Exists(f.Destination));
        await f.History.AddAsync(original); f.Queue.Update(original.QueueItemId, item => item.VideoId = original.VideoId, persist: false); await f.Queue.SaveAsync();
        await service.RecoverAsync(legacy.Id); await service.UndoAsync(legacy.Id);
        Assert.Equal(original, Assert.Single(f.History.Snapshot())); Assert.False(await service.HasPendingRecoveryAsync());
    }

    [Theory]
    [InlineData("id")]
    [InlineData("provider")]
    [InlineData("json")]
    public async Task ChangedEvidenceAfterPlanStopsTheWholeSelectionBeforeMutation(string changed)
    {
        await using var f = new Fixture(); var entry = await f.SeedAsync(); var service = f.Service();
        var plan = await f.PlanAsync(service); var match = Assert.Single(plan.Matches);
        Assert.Equal(OrganizerMatchStatus.Found, match.Status); Assert.Equal(entry.VideoId, match.VerifiedVideoId);
        if (changed == "json") await File.AppendAllTextAsync(match.Files[1].Source, " ");
        else await f.History.AddAsync(changed == "id" ? entry with { VideoId = "changed" } : entry with { SourceUrl = "https://other.test/watch/1" });
        await Assert.ThrowsAsync<IOException>(() => service.MoveSelectedAsync(plan, [match.Id]));
        Assert.All(match.Files, file => Assert.True(File.Exists(file.Source)));
        Assert.False(Directory.Exists(f.Destination)); Assert.Empty(await service.JournalsAsync());
    }

    [Fact]
    public async Task VacantDestinationReferencedByAnotherRecordingIsNotReused()
    {
        await using var f = new Fixture(); await f.SeedAsync(); var unrelated = await f.SeedAsync(2);
        var service = f.Service(); var plan = await f.PlanAsync(service); var match = Assert.Single(plan.Matches);
        await f.History.AddAsync(unrelated with { FinalPath = match.Files[0].Destination });
        await Assert.ThrowsAsync<IOException>(() => service.MoveSelectedAsync(plan, [match.Id]));
        Assert.True(File.Exists(match.MediaPath)); Assert.True(File.Exists(unrelated.FinalPath));
        Assert.False(Directory.Exists(f.Destination)); Assert.Empty(await service.JournalsAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnfinishedJobWithoutFinalFileBlocksTheSameIdentityBeforeAndAfterPlanning(bool admittedAfterPlan)
    {
        await using var f = new Fixture(); await f.SeedAsync(); var service = f.Service();
        var plan = admittedAfterPlan ? await f.PlanAsync(service) : null;
        f.Queue.Add(new() { Id = Guid.NewGuid(), SourceUrl = Fixture.Url(), VideoId = "1", Status = DownloadStatus.Finalizing, FinalFile = null });
        await f.Queue.SaveAsync();
        if (plan is null)
        {
            plan = await f.PlanAsync(service);
            Assert.Equal(OrganizerMatchStatus.Attention, plan.Matches.Single().Status);
            Assert.Equal("Organizer.Error.Active", plan.Matches.Single().ReasonKey);
        }
        await Assert.ThrowsAsync<IOException>(() => service.MoveSelectedAsync(plan, [plan.Matches.Single().Id]));
        Assert.False(Directory.Exists(f.Destination)); Assert.Empty(await service.JournalsAsync());
    }

    [Fact]
    public async Task IdenticalBareIdsOnDifferentProvidersRemainIndependentAndSharedBasenamesRemainAmbiguous()
    {
        await using var f = new Fixture(); var first = await f.SeedAsync(); var second = await f.SeedAsync(2);
        var other = second with { SourceUrl = "https://other.test/watch/1", VideoId = "1" };
        await f.History.AddAsync(other); f.Queue.Update(second.QueueItemId, job => { job.SourceUrl = other.SourceUrl; job.VideoId = other.VideoId; }, persist: false); await f.Queue.SaveAsync();
        await File.WriteAllTextAsync(Path.ChangeExtension(other.FinalPath, ".json"), Fixture.Metadata().Replace("example.test", "other.test"));
        var service = f.Service();
        var plan = await service.BuildPlanAsync([new("providers", f.Destination, Entries: [new() { Url = first.SourceUrl }, new() { Url = other.SourceUrl }])], f.Source, true);
        Assert.Equal(2, plan.Matches.Count); Assert.All(plan.Matches, match => Assert.Equal(OrganizerMatchStatus.Found, match.Status));
        Assert.NotEqual(plan.Matches[0].Identity, plan.Matches[1].Identity);
        var operation = await service.MoveSelectedAsync(plan, plan.Matches.Select(match => match.Id).ToArray()); await service.UndoAsync(operation.Id);
        Assert.Equal(new[] { first.QueueItemId, second.QueueItemId }.Order(), f.History.Snapshot().Select(entry => entry.QueueItemId).Order());
        File.Copy(first.FinalPath, Path.ChangeExtension(first.FinalPath, ".mkv"));
        var sharedPlan = await f.PlanAsync(service); var ambiguous = Assert.Single(sharedPlan.Matches); Assert.Equal(OrganizerMatchStatus.Ambiguous, ambiguous.Status);
        await service.ResolveCandidateAsync(sharedPlan, ambiguous.Id, first.FinalPath);
        Assert.Equal(OrganizerMatchStatus.Ambiguous, ambiguous.Status); Assert.Equal("Organizer.Reason.SharedBasename", ambiguous.ReasonKey);
        Assert.True(File.Exists(first.FinalPath)); Assert.True(File.Exists(Path.ChangeExtension(first.FinalPath, ".mkv")));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task RecoveryAndUndoRefuseKnownUnavailableCanonicalStoresBeforeMoreFileMutations(bool undo, bool queueBlocked)
    {
        await using var f = new Fixture(); await f.SeedAsync();
        var service = f.Service(undo ? null : new Failure("AfterFile")); var plan = await f.PlanAsync(service);
        if (undo) await service.MoveSelectedAsync(plan, [plan.Matches.Single().Id]);
        else await Assert.ThrowsAsync<IOException>(() => service.MoveSelectedAsync(plan, [plan.Matches.Single().Id]));
        var journal = Assert.Single(await service.JournalsAsync());
        var files = plan.Matches.Single().Files.SelectMany(file => new[] { file.Source, file.Destination }).ToArray();
        var before = ExistingHashes(files);
        if (queueBlocked) f.Queue.Storage.Report(new IOException("Synthetic unavailable queue store"), persistence: true);
        else { await File.WriteAllTextAsync(f.Paths.HistoryFile, "{unreadable"); await f.History.LoadAsync(); Assert.True(f.History.LoadFailed); }
        try
        {
            await Assert.ThrowsAsync<IOException>(() => undo ? service.UndoAsync(journal.Id) : service.RecoverAsync(journal.Id));
            Assert.Equal(before, ExistingHashes(files));
            Assert.Equal(journal.State, (await service.JournalsAsync()).Single().State);
            if (!queueBlocked) Assert.Equal("{unreadable", await File.ReadAllTextAsync(f.Paths.HistoryFile));
        }
        finally { if (queueBlocked) Assert.True(f.Queue.Storage.Recover(f.Queue.Storage.State.FailureVersion)); }
    }

    [Fact]
    public async Task PendingForwardOperationMustBeRecoveredBeforeUndoingAnOlderOperation()
    {
        await using var f = new Fixture(); await f.SeedAsync(); var service = f.Service();
        var first = await f.PlanAsync(service); var completed = await service.MoveSelectedAsync(first, [first.Matches.Single().Id]);
        await f.SeedAsync(2); var second = await f.PlanAsync(service, 2); var failing = f.Service(new Failure("AfterFile"));
        await Assert.ThrowsAsync<IOException>(() => failing.MoveSelectedAsync(second, [second.Matches.Single().Id]));
        var paths = first.Matches.Single().Files.Select(file => file.Destination).ToArray(); var before = ExistingHashes(paths);
        var error = await Assert.ThrowsAsync<IOException>(() => service.UndoAsync(completed.Id));
        Assert.Equal("Organizer.Error.RecoveryRequired", error.Message); Assert.Equal(before, ExistingHashes(paths));
        var pending = Assert.Single(await service.JournalsAsync(), value => value.State == OrganizerJournalState.RecoveryRequired);
        await service.RecoverAsync(pending.Id); await service.UndoAsync(completed.Id);
        Assert.All(first.Matches.Single().Files, file => Assert.True(File.Exists(file.Source)));
    }

    [Theory]
    [InlineData("AfterRenamePublished", false)]
    [InlineData("AfterQueue", false)]
    [InlineData("AfterRenamePublished", true)]
    [InlineData("BeforeUndoReferences", true)]
    [InlineData("AfterUndoHistory", true)]
    [InlineData("AfterUndoQueue", true)]
    public async Task FreshServicesRecoverDurableForwardAndUndoWindowsIdempotently(string checkpoint, bool duringUndo)
    {
        await using var f = new Fixture(); var original = await f.SeedAsync(); var healthy = f.Service();
        var plan = await f.PlanAsync(healthy); var match = Assert.Single(plan.Matches);
        var expected = ExistingHashes(match.Files.Select(file => file.Source));
        var failing = f.Service(new Failure(checkpoint)); Guid journalId;
        if (duringUndo)
        {
            journalId = (await healthy.MoveSelectedAsync(plan, [match.Id])).Id;
            await Assert.ThrowsAsync<IOException>(() => failing.UndoAsync(journalId));
        }
        else
        {
            await Assert.ThrowsAsync<IOException>(() => failing.MoveSelectedAsync(plan, [match.Id]));
            journalId = Assert.Single(await healthy.JournalsAsync()).Id;
        }
        await healthy.StopAsync(); await failing.StopAsync();
        var history = new HistoryService(f.Paths, new NullAppLogger()); await history.LoadAsync();
        var queue = new DownloadQueueService(new QueuePersistenceService(f.Paths, new NullAppLogger()), new NullAppLogger(), f.Settings); await queue.LoadAsync();
        using var restarted = new DownloadOrganizerService(f.Paths, history, queue, f.Settings, new NullAppLogger());
        try
        {
            var recovered = await restarted.RecoverAsync(journalId);
            Assert.Equal(duringUndo ? OrganizerJournalState.Undone : OrganizerJournalState.Completed, recovered.State);
            await restarted.RecoverAsync(journalId);
            var expectedPath = duringUndo ? original.FinalPath : match.Files[0].Destination;
            Assert.Equal(expectedPath, Assert.Single(history.Snapshot()).FinalPath);
            Assert.Equal(expectedPath, Assert.Single(queue.Snapshot()).FinalFile);
            Assert.Equal(original.QueueItemId, queue.Snapshot().Single().Id); Assert.Equal(DownloadStatus.Completed, queue.Snapshot().Single().Status);
            if (!duringUndo) await restarted.UndoAsync(journalId);
            await restarted.UndoAsync(journalId);
            Assert.Equal(expected, ExistingHashes(expected.Keys));
            Assert.All(match.Files, file => Assert.False(File.Exists(file.Destination)));
            Assert.False(await restarted.HasPendingRecoveryAsync());
            await history.LoadAsync(); await queue.LoadAsync();
            Assert.Equal(original, Assert.Single(history.Snapshot())); Assert.Equal(original.QueueItemId, Assert.Single(queue.Snapshot()).Id);
            output.WriteLine($"OR-C07 restart boundary={checkpoint}, undo={duringUndo}, originalHashes={expected.Count}, journal={recovered.State}; no job re-admitted.");
        }
        finally { await restarted.StopAsync(); }
    }

    [Fact]
    public async Task ReadOnlyJsonCopyFailureKeepsBothVerifiedCopiesAndRecoversAfterUnlock()
    {
        await using var f = new Fixture(); await f.SeedAsync(); var service = f.Service(new Failure("unused", copy: true));
        var plan = await f.PlanAsync(service); var match = Assert.Single(plan.Matches); var json = match.Files[1];
        File.SetAttributes(json.Source, File.GetAttributes(json.Source) | FileAttributes.ReadOnly);
        try
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.MoveSelectedAsync(plan, [match.Id]));
            Assert.Equal(json.Sha256, Hash(json.Source)); Assert.Equal(json.Sha256, Hash(json.Destination));
            Assert.Equal(match.MediaPath, Assert.Single(f.History.Snapshot()).FinalPath);
        }
        finally { File.SetAttributes(json.Source, FileAttributes.Normal); }
        var operation = Assert.Single(await service.JournalsAsync()); await service.RecoverAsync(operation.Id); await service.UndoAsync(operation.Id);
        Assert.All(match.Files, file => Assert.Equal(file.Sha256, Hash(file.Source)));
    }

    [Fact]
    public async Task InjectedDiskFullDuringCopyPreservesSourceAndEvidenceForRestart()
    {
        await using var f = new Fixture(); var entry = await f.SeedAsync(); await File.WriteAllBytesAsync(entry.FinalPath, new byte[1024 * 1024 + 11]);
        var service = f.Service(new Failure("AfterCopyChunk", copy: true, hresult: unchecked((int)0x80070070)));
        var plan = await f.PlanAsync(service); var match = Assert.Single(plan.Matches);
        var error = await Assert.ThrowsAsync<IOException>(() => service.MoveSelectedAsync(plan, [match.Id]));
        Assert.Equal(StorageFailureKind.DiskFull, StorageFailureClassifier.Classify(error)); Assert.Equal(match.Files[0].Sha256, Hash(entry.FinalPath));
        Assert.Single(Directory.GetFiles(f.Destination, "*.organizing"));
        var recovery = f.Service(new Failure("unused", copy: true)); var journal = Assert.Single(await recovery.JournalsAsync());
        await recovery.RecoverAsync(journal.Id); Assert.Equal(match.Files[0].Sha256, Hash(match.Files[0].Destination));
        Assert.Single(Directory.GetFiles(f.Destination, "*.organizing"));
    }

    [Fact]
    public async Task Mixed2526CompletedAnd91QueuedListsPreserveAllIdentitiesAcrossMoveRestartAndUndo()
    {
        await using var f = new Fixture(); const int completed = 2526, pending = 91;
        var history = new List<DownloadHistoryEntry>(completed); var jobs = new List<DownloadQueueItem>(completed + pending);
        string[] formats = [".mp4", ".mkv", ".webm", ".mp3", ".m4a", ".flac"];
        for (var i = 0; i < completed; i++)
        {
            var path = Path.Combine(f.Source, $"nagranie-{i:D4} Łódź_日本語" + formats[i % formats.Length]);
            var url = $"https://{(i % 2 == 0 ? "example" : "other")}.test/watch/{i}"; var id = i.ToString();
            var quality = i % formats.Length >= 3 ? "audio" : "720";
            await File.WriteAllBytesAsync(path, Encoding.UTF8.GetBytes($"controlled synthetic {i} {formats[i % formats.Length]}"));
            var json = JsonSerializer.Serialize(new { id, webpage_url = url, title = "Wspólny tytuł 日本語 🎵", retained = new[] { i, 13, 17 } });
            var jsonPath = i % 3 == 1 ? Path.Combine(f.Paths.MetadataDirectory, id + ".json") : Path.ChangeExtension(path, i % 3 == 0 ? ".info.json" : ".json");
            await File.WriteAllTextAsync(jsonPath, json, new UTF8Encoding(false));
            if (i < 6)
                foreach (var extension in new[] { ".pl.srt", ".jpg", ".description", ".chapters" })
                    await File.WriteAllTextAsync(Path.ChangeExtension(path, extension), $"Synthetic companion {i}: {extension} 日本語");
            var queueId = Guid.NewGuid();
            history.Add(new(queueId, "Wspólny tytuł 日本語 🎵", url, id, "Synthetic", quality, path, DateTimeOffset.UnixEpoch.AddSeconds(i), "Completed", QualityPresetId: quality, RequestedRange: MediaTimeRange.Full));
            jobs.Add(new() { Id = queueId, JobSessionId = Guid.NewGuid(), SourceUrl = url, VideoId = id, Title = "Wspólny tytuł", Status = DownloadStatus.Completed, FinalFile = path, QualityPresetId = quality, OutputDirectory = f.Source });
        }
        for (var i = 0; i < pending; i++) jobs.Add(new() { Id = Guid.NewGuid(), JobSessionId = Guid.NewGuid(), SourceUrl = $"https://pending.test/{i}", VideoId = "pending-" + i, Status = DownloadStatus.Queued, OutputDirectory = f.Source });
        await AtomicJsonFile.WriteAsync(f.Paths.HistoryFile, history); await f.History.LoadAsync();
        await new QueuePersistenceService(f.Paths, new NullAppLogger()).SaveAsync(jobs); await f.Queue.LoadAsync();
        var txt = Path.Combine(f.Root, "lista.txt"); var csv = Path.Combine(f.Root, "lista.csv"); var jsonList = Path.Combine(f.Root, "lista.json");
        await File.WriteAllLinesAsync(txt, history.Where((_, i) => i % 3 == 0).Select(entry => entry.SourceUrl));
        await File.WriteAllLinesAsync(csv, new[] { "url" }.Concat(history.Where((_, i) => i % 3 == 1).Select(entry => entry.SourceUrl)));
        await File.WriteAllTextAsync(jsonList, JsonSerializer.Serialize(history.Where((_, i) => i % 3 == 2).Select(entry => entry.SourceUrl)));
        var lists = new[] { new OrganizerSourceList("TXT", Path.Combine(f.Root, "sorted", "TXT"), txt), new OrganizerSourceList("CSV", Path.Combine(f.Root, "sorted", "CSV"), csv), new OrganizerSourceList("JSON", Path.Combine(f.Root, "sorted", "JSON"), jsonList) };
        var service = f.Service(); var timer = Stopwatch.StartNew(); var memory = GC.GetTotalMemory(false);
        var plan = await service.BuildPlanAsync(lists, f.Source, true); timer.Stop(); var managedPlanDelta = GC.GetTotalMemory(false) - memory;
        Assert.Equal(completed, plan.Matches.Count); Assert.All(plan.Matches, match => Assert.Equal(OrganizerMatchStatus.Found, match.Status));
        var selection = plan.Matches.GroupBy(match => match.ListName).SelectMany(group => group.Take(2)).ToArray();
        var selectedFiles = selection.SelectMany(match => match.Files).ToArray(); var originals = ExistingHashes(selectedFiles.Select(file => file.Source));
        var journal = await service.MoveSelectedAsync(plan, selection.Select(match => match.Id).ToArray());
        Assert.Equal(completed, f.History.Snapshot().Count); Assert.Equal(completed + pending, f.Queue.Snapshot().Count);
        Assert.Equal(jobs.Select(job => (job.Id, job.JobSessionId, job.Status)).OrderBy(job => job.Id), f.Queue.Snapshot().Select(job => (job.Id, job.JobSessionId, job.Status)).OrderBy(job => job.Id));
        Assert.All(selectedFiles, file => Assert.Equal(file.Sha256, Hash(file.Destination)));
        Assert.All(selectedFiles.Where(file => file.CopyOnly), file => Assert.Equal(file.Sha256, Hash(file.Source)));
        await service.StopAsync();
        var freshHistory = new HistoryService(f.Paths, new NullAppLogger()); await freshHistory.LoadAsync();
        var freshQueue = new DownloadQueueService(new QueuePersistenceService(f.Paths, new NullAppLogger()), new NullAppLogger(), f.Settings); await freshQueue.LoadAsync();
        using var restarted = new DownloadOrganizerService(f.Paths, freshHistory, freshQueue, f.Settings, new NullAppLogger());
        try
        {
            Assert.Equal(completed, freshQueue.Snapshot().Count(job => job.Status == DownloadStatus.Completed));
            Assert.Equal(pending, freshQueue.Snapshot().Count(job => job.Status == DownloadStatus.Queued));
            await restarted.RecoverAsync(journal.Id); await restarted.UndoAsync(journal.Id); await restarted.UndoAsync(journal.Id);
            Assert.Equal(originals, ExistingHashes(originals.Keys));
            Assert.Equal(history.OrderBy(entry => entry.QueueItemId), freshHistory.Snapshot().OrderBy(entry => entry.QueueItemId));
            Assert.Equal(jobs.Select(job => (job.Id, job.JobSessionId, job.Status, job.FinalFile, job.OutputDirectory)).OrderBy(job => job.Id), freshQueue.Snapshot().Select(job => (job.Id, job.JobSessionId, job.Status, job.FinalFile, job.OutputDirectory)).OrderBy(job => job.Id));
            Assert.False(await restarted.HasPendingRecoveryAsync());
            output.WriteLine($"OR-C10 completed={completed}, queued={pending}, lists=3(TXT/CSV/JSON), formats={formats.Length}, scannedFiles={plan.ScannedFiles}, plannedFiles={plan.Matches.Sum(match => match.Files.Count)}, selected={selection.Length}, planMs={timer.Elapsed.TotalMilliseconds:F1}, managedPlanDelta={managedPlanDelta}; physical cross-volume NOT QUALIFIED.");
        }
        finally { await restarted.StopAsync(); }
    }

    private static string Hash(string file) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file)));
    private static SortedDictionary<string, string> ExistingHashes(IEnumerable<string> files) => new(files.Distinct(StringComparer.OrdinalIgnoreCase)
        .Where(File.Exists).ToDictionary(file => file, Hash), StringComparer.OrdinalIgnoreCase);
}
