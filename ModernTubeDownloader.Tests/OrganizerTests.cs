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

namespace ModernTubeDownloader.Tests;

public sealed class OrganizerTests(ITestOutputHelper output)
{
    private sealed class Fixture : IAsyncDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "mtd-organizer-" + Guid.NewGuid().ToString("N"));
        public string Source => Path.Combine(Root, "downloads");
        public string Destination => Path.Combine(Root, "organized", "List Łódź");
        public AppPaths Paths { get; }
        public HistoryService History { get; }
        public DownloadQueueService Queue { get; }
        public SettingsService Settings { get; }
        private readonly List<DownloadOrganizerService> services = [];
        public Fixture()
        {
            Paths = AppPaths.Create(Path.Combine(Root, "profile")); Paths.EnsureCreated(); Directory.CreateDirectory(Source);
            Settings = new(Paths, new NullAppLogger()); Settings.Current.TemporaryDirectory = Path.Combine(Root, "working");
            Settings.Current.FinalOutputDirectory = Source; Settings.Current.QueuePaused = true;
            History = new(Paths, new NullAppLogger()); Queue = new(new QueuePersistenceService(Paths, new NullAppLogger()), new NullAppLogger(), Settings);
        }
        public DownloadOrganizerService Service(IOrganizerFaultObserver? fault = null)
        { var service = new DownloadOrganizerService(Paths, History, Queue, Settings, new NullAppLogger(), fault); services.Add(service); return service; }
        public static string Url(int number = 1) => "https://example.test/watch/" + number;
        public static string Metadata(int number = 1) => "{\n  \"id\": \"" + number + "\", \"webpage_url\": \"" + Url(number) + "\", \"title\": \"Łódź 日本語\", \"unknown_field\": [1,2,3]\n}\n";
        public async Task<DownloadHistoryEntry> SeedAsync(int number = 1, bool sidecar = true, bool central = false, string? fileName = null, bool queue = true, bool live = false)
        {
            var media = Path.Combine(Source, fileName ?? "recording " + number + ".mp4"); await File.WriteAllBytesAsync(media, Encoding.UTF8.GetBytes("synthetic media " + number));
            if (sidecar) await File.WriteAllTextAsync(Path.ChangeExtension(media, ".json"), Metadata(number), new UTF8Encoding(false));
            if (central) await File.WriteAllTextAsync(Path.Combine(Paths.MetadataDirectory, number + ".json"), Metadata(number), new UTF8Encoding(false));
            var entry = new DownloadHistoryEntry(Guid.NewGuid(), "Łódź " + number, Url(number), number.ToString(), "Channel", "720p", media, DateTimeOffset.UtcNow, "Completed",
                QualityPresetId: "720p", PlaylistId: "original-playlist", PlaylistIndex: number, RequestedRange: MediaTimeRange.Full, WasLiveRecording: live, FileSizeBytes: new FileInfo(media).Length);
            await History.AddAsync(entry);
            if (queue) { Queue.Add(new() { Id = entry.QueueItemId, JobSessionId = Guid.NewGuid(), SourceUrl = entry.SourceUrl, VideoId = entry.VideoId, Title = entry.Title, Status = DownloadStatus.Completed, FinalFile = media, QualityPresetId = "720p", RequestedRange = MediaTimeRange.Full }); await Queue.SaveAsync(); }
            return entry;
        }
        public Task<OrganizerPlan> PlanAsync(DownloadOrganizerService service, int number = 1, bool requireJson = true) =>
            service.BuildPlanAsync([new("list.txt", Destination, Entries: [new() { Url = Url(number) }])], Source, true, requireJson);
        public async ValueTask DisposeAsync()
        {
            foreach (var service in services) await service.StopAsync();
            await Queue.SaveAsync(); await Settings.SaveAsync();
            Directory.Delete(Root, true);
        }
    }
    private sealed class Fault(string target, int occurrence = 1, bool copy = false) : IOrganizerFaultObserver
    {
        private int seen;
        public bool ForceVerifiedCopy => copy;
        public void Checkpoint(string step, int completedFiles)
        { if (step == target && ++seen == occurrence) throw new IOException("Injected durable-boundary failure: " + step); }
    }

    [Fact]
    public async Task ExactIdentityPreservesRawJsonCompanionsHistoryIdsQueueIdsAndUndo()
    {
        await using var f = new Fixture(); var original = await f.SeedAsync();
        var subtitle = Path.ChangeExtension(original.FinalPath, ".pl.srt"); var thumbnail = Path.ChangeExtension(original.FinalPath, ".jpg");
        await File.WriteAllTextAsync(subtitle, "1\n00:00:00,000 --> 00:00:01,000\nŁódź"); await File.WriteAllBytesAsync(thumbnail, [1, 2, 3]);
        await File.WriteAllTextAsync(Path.Combine(f.Source, "recording 1 other.srt"), "unrelated");
        var service = f.Service(); var plan = await f.PlanAsync(service); var match = Assert.Single(plan.Matches);
        Assert.Equal(OrganizerMatchStatus.Found, match.Status); Assert.Equal(4, match.Files.Count);
        var originals = match.Files.ToDictionary(file => file.Source, file => File.ReadAllBytes(file.Source));
        Assert.All(match.Files, file => Assert.False(File.Exists(file.Destination)));
        var jobId = f.Queue.Snapshot().Single().JobSessionId;
        var journal = await service.MoveSelectedAsync(plan, [match.Id]); Assert.Equal(OrganizerJournalState.Completed, journal.State);
        Assert.All(match.Files, file => { Assert.False(File.Exists(file.Source)); Assert.Equal(originals[file.Source], File.ReadAllBytes(file.Destination)); });
        Assert.Equal(original with { FinalPath = match.Files[0].Destination }, f.History.Snapshot().Single());
        Assert.Equal(original.QueueItemId, f.Queue.Snapshot().Single().Id); Assert.Equal(jobId, f.Queue.Snapshot().Single().JobSessionId);
        Assert.Equal(match.Files[0].Destination, f.Queue.Snapshot().Single().FinalFile);
        Assert.True(File.Exists(Path.Combine(f.Source, "recording 1 other.srt")));
        await service.UndoAsync(journal.Id); Assert.Equal(original, f.History.Snapshot().Single());
        Assert.All(match.Files, file => { Assert.Equal(originals[file.Source], File.ReadAllBytes(file.Source)); Assert.False(File.Exists(file.Destination)); });
        Assert.False(await service.HasPendingRecoveryAsync());
    }

    [Fact]
    public async Task SharedCentralMetadataIsCopiedAndNeverRemovedByMoveOrUndo()
    {
        await using var f = new Fixture(); await f.SeedAsync(sidecar: false, central: true);
        var service = f.Service(); var plan = await f.PlanAsync(service); var match = Assert.Single(plan.Matches);
        var metadata = Assert.Single(match.Files.Where(file => file.CopyOnly)); var bytes = await File.ReadAllBytesAsync(metadata.Source);
        var operation = await service.MoveSelectedAsync(plan, [match.Id]);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(metadata.Source)); Assert.Equal(bytes, await File.ReadAllBytesAsync(metadata.Destination));
        await service.UndoAsync(operation.Id); Assert.Equal(bytes, await File.ReadAllBytesAsync(metadata.Source)); Assert.False(File.Exists(metadata.Destination));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("corrupt")]
    [InlineData("wrong-id")]
    [InlineData("wrong-provider")]
    public async Task MissingOrUntrustedJsonNeverEnablesImplicitMove(string kind)
    {
        await using var f = new Fixture(); var entry = await f.SeedAsync(sidecar: false);
        var sidecar = Path.ChangeExtension(entry.FinalPath, ".json");
        if (kind != "missing") await File.WriteAllTextAsync(sidecar, kind switch
        {
            "corrupt" => "{broken", "wrong-id" => Fixture.Metadata().Replace("\"id\": \"1\"", "\"id\": \"2\""),
            _ => Fixture.Metadata().Replace("example.test", "other.test")
        });
        var service = f.Service(); var plan = await f.PlanAsync(service); var match = Assert.Single(plan.Matches);
        Assert.Equal(OrganizerMatchStatus.Attention, match.Status);
        await Assert.ThrowsAsync<IOException>(() => service.MoveSelectedAsync(plan, [match.Id])); Assert.True(File.Exists(entry.FinalPath));
        if (kind == "missing") Assert.Equal(OrganizerMatchStatus.Found, (await f.PlanAsync(service, requireJson: false)).Matches.Single().Status);
    }

    [Fact]
    public async Task MultipleFilesAndListsNeedExplicitResolutionAndUnknownVariantsStayBlocked()
    {
        await using var f = new Fixture(); var first = await f.SeedAsync();
        var second = Path.Combine(f.Source, "another encode.mp4"); File.Copy(first.FinalPath, second); File.Copy(Path.ChangeExtension(first.FinalPath, ".json"), Path.ChangeExtension(second, ".json"));
        var service = f.Service(); var plan = await f.PlanAsync(service); var ambiguous = Assert.Single(plan.Matches);
        Assert.Equal(OrganizerMatchStatus.Ambiguous, ambiguous.Status); Assert.Equal(2, ambiguous.Candidates.Length);
        await service.ResolveCandidateAsync(plan, ambiguous.Id, first.FinalPath); Assert.Equal(OrganizerMatchStatus.Found, ambiguous.Status);
        await Assert.ThrowsAsync<IOException>(() => service.ResolveCandidateAsync(plan, ambiguous.Id, Path.Combine(f.Source, "not-a-candidate.mp4")));
        var variants = await service.BuildPlanAsync([new("range", f.Destination, Entries: [new() { Url = Fixture.Url(), Range = new(MediaRangeMode.Custom, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3)) }])], f.Source, true);
        Assert.Equal(OrganizerMatchStatus.Attention, variants.Matches.Single().Status);
        var twoLists = await service.BuildPlanAsync([new("one", f.Destination, Entries: [new() { Url = Fixture.Url() }]), new("two", Path.Combine(f.Root, "second"), Entries: [new() { Url = Fixture.Url() }])], f.Source, true);
        await service.ResolveCandidateAsync(twoLists, twoLists.Matches[0].Id, first.FinalPath);
        await service.ResolveCandidateAsync(twoLists, twoLists.Matches[1].Id, first.FinalPath);
        Assert.Equal(OrganizerMatchStatus.Conflict, twoLists.Matches[0].Status); Assert.Equal(OrganizerMatchStatus.Found, twoLists.Matches[1].Status);
    }

    [Fact]
    public async Task CompletedLiveAndUnfinishedQueueItemsAreExplicitlyBlocked()
    {
        await using var f = new Fixture(); var entry = await f.SeedAsync(live: true);
        var service = f.Service(); var plan = await f.PlanAsync(service); Assert.Equal(OrganizerMatchStatus.Attention, plan.Matches.Single().Status);
        var ordinary = await f.SeedAsync(2); f.Queue.Update(ordinary.QueueItemId, item => item.Status = DownloadStatus.DownloadingVideo, persist: false);
        Assert.Equal(OrganizerMatchStatus.Attention, (await f.PlanAsync(service, 2)).Matches.Single().Status);
        Assert.True(File.Exists(entry.FinalPath)); Assert.True(File.Exists(ordinary.FinalPath));
    }

    [Fact]
    public async Task DuplicateWithinOneListIsReportedWithoutBlockingItsFirstVerifiedEntry()
    {
        await using var f = new Fixture(); await f.SeedAsync(); var service = f.Service();
        var plan = await service.BuildPlanAsync([new("same-list", f.Destination, Entries: [new() { Url = Fixture.Url() }, new() { Url = Fixture.Url() }])], f.Source, true);
        Assert.Equal(OrganizerMatchStatus.Found, plan.Matches[0].Status); Assert.Equal(OrganizerMatchStatus.Duplicate, plan.Matches[1].Status);
        Assert.Empty(plan.Matches[1].Files);
    }

    [Fact]
    public async Task UnreadableCanonicalHistoryIsNeverReplacedByOrganizerOrShutdownSave()
    {
        await using var f = new Fixture(); var entry = await f.SeedAsync(); await File.WriteAllTextAsync(f.Paths.HistoryFile, "{broken-history");
        await f.History.LoadAsync(); Assert.True(f.History.LoadFailed);
        var service = f.Service(); var plan = await f.PlanAsync(service);
        await Assert.ThrowsAsync<IOException>(() => service.MoveSelectedAsync(plan, [plan.Matches.Single().Id]));
        await Assert.ThrowsAsync<IOException>(() => f.History.SaveAsync()); Assert.Equal("{broken-history", await File.ReadAllTextAsync(f.Paths.HistoryFile));
        Assert.True(File.Exists(entry.FinalPath)); Assert.Empty(await service.JournalsAsync());
    }

    [Fact]
    public async Task JunctionIntroducedAfterDryRunCannotRedirectDestination()
    {
        await using var f = new Fixture(); var entry = await f.SeedAsync(); var service = f.Service(); var plan = await f.PlanAsync(service);
        var outside = Path.Combine(f.Root, "outside-reviewed-target"); Directory.CreateDirectory(outside); Directory.CreateDirectory(Path.GetDirectoryName(f.Destination)!);
        var start = new ProcessStartInfo("cmd.exe") { Arguments = $"/c mklink /J \"{f.Destination}\" \"{outside}\"", CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        using var process = Process.Start(start)!; await process.WaitForExitAsync(); Assert.True(process.ExitCode == 0, await process.StandardError.ReadToEndAsync());
        Assert.True((File.GetAttributes(f.Destination) & FileAttributes.ReparsePoint) != 0);
        await Assert.ThrowsAsync<IOException>(() => service.MoveSelectedAsync(plan, [plan.Matches.Single().Id]));
        Assert.Empty(Directory.GetFiles(outside)); Assert.True(File.Exists(entry.FinalPath));
        Directory.Delete(f.Destination); // Remove only this fixture's junction; retain its target until fixture cleanup.
    }

    [Theory]
    [InlineData("AfterFile")]
    [InlineData("BeforeReferences")]
    [InlineData("AfterHistory")]
    public async Task DurableCrashWindowsRecoverIdempotentlyWithoutLosingMetadata(string checkpoint)
    {
        await using var f = new Fixture(); var entry = await f.SeedAsync(); var service = f.Service(new Fault(checkpoint));
        var plan = await f.PlanAsync(service); var match = plan.Matches.Single(); var expectedJson = File.ReadAllBytes(match.Files[1].Source);
        await Assert.ThrowsAsync<IOException>(() => service.MoveSelectedAsync(plan, [match.Id]));
        var journal = Assert.Single(await service.JournalsAsync()); Assert.Equal(OrganizerJournalState.RecoveryRequired, journal.State);
        Assert.True(await service.HasPendingRecoveryAsync());
        var fresh = f.Service(); var recovered = await fresh.RecoverAsync(journal.Id); Assert.Equal(OrganizerJournalState.Completed, recovered.State);
        Assert.Equal(expectedJson, File.ReadAllBytes(match.Files[1].Destination)); Assert.False(File.Exists(match.Files[1].Source));
        Assert.Equal(entry.QueueItemId, f.History.Snapshot().Single().QueueItemId); Assert.Equal(match.Files[0].Destination, f.History.Snapshot().Single().FinalPath);
        Assert.Equal(match.Files[0].Destination, f.Queue.Snapshot().Single().FinalFile);
        await fresh.RecoverAsync(journal.Id); Assert.Equal(2, Directory.GetFiles(f.Destination).Length);
    }

    [Fact]
    public async Task LockedJsonAndNewCollisionFailBeforeAnyMove()
    {
        await using var f = new Fixture(); var entry = await f.SeedAsync(); var service = f.Service();
        var plan = await f.PlanAsync(service); var match = plan.Matches.Single();
        using (var locked = new FileStream(match.Files[1].Source, FileMode.Open, FileAccess.Read, FileShare.None))
            await Assert.ThrowsAnyAsync<IOException>(() => service.MoveSelectedAsync(plan, [match.Id]));
        Assert.True(File.Exists(entry.FinalPath)); Assert.Empty(await service.JournalsAsync());
        Directory.CreateDirectory(f.Destination); await File.WriteAllTextAsync(match.Files[1].Destination, "occupant");
        await Assert.ThrowsAsync<IOException>(() => service.MoveSelectedAsync(plan, [match.Id]));
        Assert.Equal("occupant", await File.ReadAllTextAsync(match.Files[1].Destination)); Assert.True(File.Exists(entry.FinalPath));
    }

    [Fact]
    public async Task AtomicHistoryWriteFailureRetainsInMemoryPathsAndRecoverableJournal()
    {
        await using var f = new Fixture(); var entry = await f.SeedAsync(); var service = f.Service(); var plan = await f.PlanAsync(service); var match = plan.Matches.Single();
        var historyBytes = File.ReadAllBytes(f.Paths.HistoryFile);
        using (var locked = new FileStream(f.Paths.HistoryFile, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            // Windows rejects replacement of this no-delete-share handle with ERROR_ACCESS_DENIED.
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.MoveSelectedAsync(plan, [match.Id]));
            Assert.Equal(entry, f.History.Snapshot().Single()); Assert.Equal(historyBytes, File.ReadAllBytes(f.Paths.HistoryFile));
        }
        var journal = Assert.Single(await service.JournalsAsync()); await service.RecoverAsync(journal.Id);
        Assert.Equal(match.Files[0].Destination, f.History.Snapshot().Single().FinalPath);
    }

    [Theory]
    [InlineData("AfterCopyChunk")]
    [InlineData("AfterCopyPublished")]
    [InlineData("BeforeCopy")]
    public async Task VerifiedCopyFaultsKeepSourceAndRestartWithoutOverwriting(string checkpoint)
    {
        await using var f = new Fixture(); var entry = await f.SeedAsync(); await File.WriteAllBytesAsync(entry.FinalPath, new byte[2 * 1024 * 1024 + 37]);
        var service = f.Service(new Fault(checkpoint, copy: true)); var plan = await f.PlanAsync(service); var match = plan.Matches.Single();
        await Assert.ThrowsAsync<IOException>(() => service.MoveSelectedAsync(plan, [match.Id])); Assert.True(File.Exists(entry.FinalPath));
        var journal = Assert.Single(await service.JournalsAsync());
        var recovery = f.Service(new Fault("unused", copy: true)); await recovery.RecoverAsync(journal.Id);
        Assert.False(File.Exists(entry.FinalPath)); Assert.Equal(match.Files[0].Sha256, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(match.Files[0].Destination))));
        if (checkpoint == "AfterCopyChunk") Assert.Single(Directory.GetFiles(f.Destination, "*.organizing"));
    }

    [Fact]
    public async Task ChangedUndoIsRejectedBeforeMutationAndInterruptedUndoCanRecover()
    {
        await using var f = new Fixture(); await f.SeedAsync(); var service = f.Service(); var plan = await f.PlanAsync(service); var match = plan.Matches.Single();
        var journal = await service.MoveSelectedAsync(plan, [match.Id]); var media = File.ReadAllBytes(match.Files[0].Destination);
        await File.WriteAllTextAsync(match.Files[0].Destination, "changed");
        await Assert.ThrowsAsync<IOException>(() => service.UndoAsync(journal.Id)); Assert.True(File.Exists(match.Files[1].Destination)); Assert.False(File.Exists(match.Files[1].Source));
        await File.WriteAllBytesAsync(match.Files[0].Destination, media);
        var failing = f.Service(new Fault("AfterUndoFile")); await Assert.ThrowsAsync<IOException>(() => failing.UndoAsync(journal.Id));
        await service.RecoverAsync(journal.Id); Assert.Equal(OrganizerJournalState.Undone, (await service.JournalsAsync()).Single().State);
        Assert.Equal(media, File.ReadAllBytes(match.Files[0].Source)); Assert.True(File.Exists(match.Files[1].Source));
    }

    [Fact]
    public async Task RecoveryGuardChecksJournalsBeyondRecentHundredAndCorruptFilesFailClosed()
    {
        await using var f = new Fixture(); var service = f.Service(); Directory.CreateDirectory(service.JournalDirectory);
        var old = new OrganizerJournal { CreatedAt = DateTimeOffset.UtcNow.AddDays(-2), State = OrganizerJournalState.RecoveryRequired };
        var oldPath = Path.Combine(service.JournalDirectory, old.Id.ToString("N") + ".json"); await File.WriteAllTextAsync(oldPath, JsonSerializer.Serialize(old, JsonDefaults.Options)); File.SetLastWriteTimeUtc(oldPath, DateTime.UtcNow.AddDays(-2));
        for (var i = 0; i < 101; i++) { var journal = new OrganizerJournal { State = OrganizerJournalState.Completed }; await File.WriteAllTextAsync(Path.Combine(service.JournalDirectory, journal.Id.ToString("N") + ".json"), JsonSerializer.Serialize(journal, JsonDefaults.Options)); }
        var recent = await service.JournalsAsync(); Assert.Equal(100, recent.Count); Assert.Equal(old.Id, recent[0].Id); Assert.True(await service.HasPendingRecoveryAsync());
        old.State = OrganizerJournalState.Completed; await File.WriteAllTextAsync(oldPath, JsonSerializer.Serialize(old, JsonDefaults.Options)); Assert.False(await service.HasPendingRecoveryAsync());
        await File.WriteAllTextAsync(oldPath, "{broken"); Assert.True(await service.HasPendingRecoveryAsync());
    }

    [Fact]
    public async Task PauseCancellationAndShutdownDoNotLeaveUntrackedTransfers()
    {
        await using var f = new Fixture(); await f.SeedAsync(); var service = f.Service(); service.SetPaused(true);
        using var cancellation = new CancellationTokenSource(); var task = f.PlanAsync(service); Assert.True(service.IsBusy);
        await service.StopAsync(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task); Assert.False(service.IsBusy);
        Assert.Empty(await service.JournalsAsync());
    }

    [Theory]
    [InlineData("..\\escape")]
    [InlineData("CON")]
    [InlineData("aux.json")]
    [InlineData("name.")]
    [InlineData("name ")]
    public void UnsafeDestinationSegmentsCannotEscapeOrAliasWindowsDevices(string name)
    {
        var root = Path.Combine(Path.GetTempPath(), "mtd-organizer-safety");
        Assert.Throws<IOException>(() => OrganizerPathSafety.DirectoryPath(Path.Combine(root, name)));
        var suggestion = DownloadOrganizerService.SuggestDestination(root, name); Assert.True(OrganizerPathSafety.IsWithin(suggestion, root));
    }

    [Theory]
    [InlineData(2750)]
    [InlineData(10000)]
    public async Task LargeIndexedPlanHashesRealDistinctFilesOnceAndPreservesCanonicalRows(int count)
    {
        await using var f = new Fixture(); var records = new List<DownloadHistoryEntry>(count); var rows = new List<BatchImportEntry>(count);
        for (var i = 0; i < count; i++)
        {
            var media = Path.Combine(f.Source, "clip-" + i + ".mp4"); File.WriteAllBytes(media, BitConverter.GetBytes(i)); File.WriteAllText(Path.ChangeExtension(media, ".json"), Fixture.Metadata(i));
            records.Add(new(Guid.NewGuid(), "Synthetic " + i, Fixture.Url(i), i.ToString(), "Synthetic", "720p", media, DateTimeOffset.UnixEpoch.AddSeconds(i), "Completed", QualityPresetId: "720p"));
            rows.Add(new() { Url = Fixture.Url(i) });
        }
        await AtomicJsonFile.WriteAsync(f.Paths.HistoryFile, records); await f.History.LoadAsync(); var before = await File.ReadAllBytesAsync(f.Paths.HistoryFile);
        var memoryBefore = GC.GetTotalMemory(false); var timer = Stopwatch.StartNew();
        var plan = await f.Service().BuildPlanAsync([new("large", f.Destination, Entries: rows)], f.Source, true); timer.Stop();
        output.WriteLine($"Organizer real-file fixture: rows={count}, scanned={plan.ScannedFiles}, verifiedFiles={plan.Matches.Sum(match => match.Files.Count)}, planMs={timer.Elapsed.TotalMilliseconds:F1}, managedDelta={GC.GetTotalMemory(false) - memoryBefore}, controlsPerPage={DownloadOrganizerService.PageSize}");
        Assert.Equal(count, plan.Matches.Count); Assert.All(plan.Matches, match => Assert.Equal(OrganizerMatchStatus.Found, match.Status)); Assert.Equal(count * 2, plan.ScannedFiles);
        Assert.Equal(before, await File.ReadAllBytesAsync(f.Paths.HistoryFile)); Assert.False(Directory.Exists(f.Destination));
        Assert.Equal(records.Select(entry => entry.QueueItemId).Order(), f.History.Snapshot().Select(entry => entry.QueueItemId).Order());
    }
}
