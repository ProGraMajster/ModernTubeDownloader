using System.Text.Json;
using ModernTubeDownloader.Batch;
using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Models;
using ModernTubeDownloader.Services;

namespace ModernTubeDownloader.Tests;

public sealed partial class BatchImportTests
{
    [Fact]
    [Trait("Scenario", "BATCH-C01")]
    public async Task BatchCompletionWithoutViewSurvivesImmediateQueueRemovalAndRepeatedImport()
    {
        await using var f = new Fixture(); using var service = f.Service();
        var source = await f.File(".txt", "https://example.test/one");
        var session = await service.ImportAsync(source); await service.RunAutomaticAsync(session);
        var item = Assert.Single(f.Queue.Snapshot()); var destination = item.OutputDirectory;
        // No UI timer, RefreshQueue or startup guard between completion and removal.
        f.Queue.Update(item.Id, current => current.Status = DownloadStatus.Completed, true);
        Assert.True(f.Queue.Remove(item.Id));
        var saved = await WaitForBatchManifestAsync(f, session.Id, BatchWorkflowPhase.Completed);
        Assert.Equal(item.Id, Assert.Single(saved.Snapshot()).QueueItemId);
        Assert.Equal(BatchEntryStatus.Completed, Assert.Single(saved.Snapshot()).Status);
        var repeated = await service.ImportAsync(source); Assert.Same(session, repeated);
        await service.RunAutomaticAsync(repeated);
        Assert.Empty(f.Queue.Snapshot()); Assert.Equal(destination, repeated.OutputDirectory);
        Assert.Equal(item.Id, Assert.Single(repeated.Snapshot()).QueueItemId);
        Assert.False(await service.HasUnfinishedSessionsAsync());
    }

    [Fact]
    [Trait("Scenario", "BATCH-C02")]
    public async Task BatchCompletionStartupRepairsStaleManifestAndContinuesObservingWithoutView()
    {
        await using var f = new Fixture(); using var previous = f.Service();
        var session = await previous.ImportAsync(await f.File(".txt", "https://example.test/one\nhttps://example.test/two"));
        await previous.RunAutomaticAsync(session); var ids = f.Queue.Snapshot().Select(value => value.Id).ToArray();
        await previous.StopAsync();
        f.Queue.Update(ids[0], item => item.Status = DownloadStatus.Completed, true);
        using var restarted = f.Service(); await restarted.InitializeAsync();
        Assert.True(await restarted.HasUnfinishedSessionsAsync());
        f.Queue.Update(ids[1], item => item.Status = DownloadStatus.Completed, true);
        var saved = await WaitForBatchManifestAsync(f, session.Id, BatchWorkflowPhase.Completed);
        Assert.Equal(ids.Order(), saved.Snapshot().Select(value => value.QueueItemId!.Value).Order());
        Assert.All(saved.Snapshot(), entry => Assert.Equal(BatchEntryStatus.Completed, entry.Status));
        Assert.False(await restarted.HasUnfinishedSessionsAsync()); Assert.True(f.Queue.IsPaused);
    }

    [Fact]
    [Trait("Scenario", "BATCH-C03")]
    public async Task BatchCompletionExplicitRetryPreservesIdentitiesFoldersSelectionAndGlobalPause()
    {
        await using var f = new Fixture(); using var service = f.Service();
        var session = await service.ImportAsync(await f.File(".txt", "https://example.test/one\nhttps://example.test/two"));
        await service.RunAutomaticAsync(session);
        var jobs = f.Queue.Snapshot().ToArray(); var originalFolder = jobs[0].OutputDirectory;
        foreach (var job in jobs) f.Queue.Update(job.Id, item => item.Status = DownloadStatus.Failed, true);
        var unrelated = new DownloadQueueItem { SourceUrl = "https://example.test/unrelated", VideoId = "unrelated" };
        f.Queue.Add(unrelated); f.Settings.Current.FinalOutputDirectory = Path.Combine(f.Root, "changed-global");
        Assert.Equal(1, await service.RetryFailedAsync(session, new HashSet<Guid> { jobs[0].Id }));
        Assert.Equal(DownloadStatus.Queued, jobs[0].Status); Assert.Equal(DownloadStatus.Failed, jobs[1].Status);
        Assert.Equal(originalFolder, jobs[0].OutputDirectory); Assert.True(f.Queue.IsPaused);
        Assert.Equal(DownloadStatus.Queued, unrelated.Status); Assert.Equal(3, f.Queue.Snapshot().Count);
        Assert.Equal(0, await service.RetryFailedAsync(session, new HashSet<Guid> { jobs[0].Id }));
        var restored = await f.Store.LoadAsync(session.Id);
        Assert.Equal(jobs.Select(value => value.Id).Order(), restored.Snapshot().Select(value => value.QueueItemId!.Value).Order());
    }

    [Fact]
    [Trait("Scenario", "BATCH-C04")]
    public async Task BatchCompletionConcurrentMultiListAutomaticRequestsShareOnePreparationAndCanCancel()
    {
        await using var f = new Fixture(); var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var service = f.Service(async (url, token) =>
        { entered.TrySetResult(); await Task.Delay(Timeout.Infinite, token); return new(url, Metadata(url), null, null); });
        var sessions = await service.ImportFilesAsync([await f.File(".txt", "https://example.test/one"), await f.File(".json", "[\"https://example.test/two\"]")]);
        var first = service.RunAutomaticAsync(sessions); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Same(first, service.RunAutomaticAsync(sessions));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.StartAsync(sessions[1]));
        await service.CancelAsync(); await first;
        Assert.Empty(f.Queue.Snapshot()); Assert.True(f.Queue.IsPaused);
        Assert.Equal(BatchWorkflowPhase.Cancelled, sessions[0].WorkflowPhase);
        Assert.Equal(BatchEntryStatus.Pending, Assert.Single(sessions[1].Snapshot()).Status);
        Assert.False(service.IsOperating);
    }

    [Fact]
    [Trait("Scenario", "BATCH-C05")]
    public async Task BatchCompletionMultiFileSelectionValidatesEverythingBeforeFirstImport()
    {
        await using var f = new Fixture(); using var service = f.Service();
        var txt = await f.File(".txt", "https://example.test/one");
        var unsupported = await f.File(".exe", "https://example.test/two");
        await Assert.ThrowsAsync<InvalidDataException>(() => service.ImportFilesAsync([txt, unsupported]));
        await Assert.ThrowsAsync<InvalidDataException>(() => service.ImportFilesAsync([]));
        await Assert.ThrowsAsync<InvalidDataException>(() => service.ImportFilesAsync(Enumerable.Repeat(txt, 33).ToArray()));
        await Assert.ThrowsAsync<InvalidDataException>(() => service.ImportFilesAsync([txt, Path.Combine(f.Root, "missing.txt")]));
        Assert.Empty(await service.RecentAsync()); Assert.Empty(f.Queue.Snapshot());
        var json = await f.File(".json", "[\"https://example.test/two\"]");
        var csv = await f.File(".csv", "url\nhttps://example.test/three");
        var sessions = await service.ImportFilesAsync([txt, json, csv, txt]);
        Assert.Equal(3, sessions.Count); Assert.Equal(3, sessions.Select(value => value.OutputDirectory).Distinct().Count());
        Assert.Equal(sessions.Select(value => value.Id), (await service.ImportFilesAsync([txt, json, csv])).Select(value => value.Id));
        await service.RunAutomaticAsync(sessions);
        Assert.Equal(3, f.Queue.Snapshot().Count); Assert.True(f.Queue.IsPaused);
        Assert.Equal(sessions.Select(value => value.OutputDirectory).Order(), f.Queue.Snapshot().Select(value => value.OutputDirectory).Order());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Scenario", "BATCH-C06")]
    public async Task BatchCompletionJournalRecoveryRetainsOriginalBytesAndAcceptsFurtherDurableUpdates(bool completeWithoutNewline)
    {
        await using var f = new Fixture(); using var service = f.Service();
        var session = await service.ImportAsync(await f.File(".txt", "https://example.test/one"));
        var row = Assert.Single(session.Snapshot()); row.Status = BatchEntryStatus.Ready; row.Revision = 1;
        await f.Store.SaveEntryAsync(session, row);
        var journal = Path.Combine(f.Root, "Batch", session.Id.ToString("N"), "updates.jsonl");
        var committed = await File.ReadAllTextAsync(journal);
        if (completeWithoutNewline) await File.WriteAllTextAsync(journal, committed.TrimEnd('\r', '\n'));
        else await File.AppendAllTextAsync(journal, "{\"id\":\"torn");
        var original = await File.ReadAllBytesAsync(journal);
        var recovered = await f.Store.LoadAsync(session.Id);
        Assert.Equal(BatchEntryStatus.Ready, Assert.Single(recovered.Snapshot()).Status);
        var next = Assert.Single(recovered.Snapshot()); next.Status = BatchEntryStatus.Completed; next.Revision++;
        await f.Store.SaveEntryAsync(recovered, next);
        var reopened = await f.Store.LoadAsync(session.Id);
        Assert.Equal(row.Id, Assert.Single(reopened.Snapshot()).Id);
        Assert.Equal(BatchEntryStatus.Completed, Assert.Single(reopened.Snapshot()).Status);
        var backups = Directory.GetFiles(Path.GetDirectoryName(journal)!, "updates.jsonl.torn-*");
        if (completeWithoutNewline) Assert.Empty(backups);
        else Assert.Equal(original, await File.ReadAllBytesAsync(Assert.Single(backups)));
    }

    [Fact]
    [Trait("Scenario", "BATCH-C07")]
    public async Task BatchCompletionCorruptionInsideJournalFailsClosedAndRetainsEveryByte()
    {
        await using var f = new Fixture(); using var service = f.Service();
        var session = await service.ImportAsync(await f.File(".txt", "https://example.test/one"));
        var journal = Path.Combine(f.Root, "Batch", session.Id.ToString("N"), "updates.jsonl");
        await File.WriteAllTextAsync(journal, "{broken\n" + JsonSerializer.Serialize(Assert.Single(session.Snapshot()), JsonDefaults.Options));
        var original = await File.ReadAllBytesAsync(journal);
        await Assert.ThrowsAsync<JsonException>(() => f.Store.LoadAsync(session.Id));
        Assert.Equal(original, await File.ReadAllBytesAsync(journal));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(journal)!, "updates.jsonl.torn-*"));
    }

    private static async Task<BatchImportSession> WaitForBatchManifestAsync(Fixture fixture, Guid id, BatchWorkflowPhase phase)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (true)
        {
            var saved = await fixture.Store.LoadAsync(id, timeout.Token);
            if (saved.WorkflowPhase == phase) return saved;
            await Task.Delay(20, timeout.Token);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Scenario", "BATCH-C08")]
    public async Task BatchCompletionAppendAfterTornWriteRepairsBeforeNewRecordWithoutRestart(bool terminatedTail)
    {
        await using var f = new Fixture(); using var service = f.Service();
        var session = await service.ImportAsync(await f.File(".txt", "https://example.test/one"));
        var row = Assert.Single(session.Snapshot()); row.Status = BatchEntryStatus.Ready; row.Revision = 1;
        await f.Store.SaveEntryAsync(session, row);
        var journal = Path.Combine(f.Root, "Batch", session.Id.ToString("N"), "updates.jsonl");
        await File.AppendAllTextAsync(journal, "{torn" + (terminatedTail ? "\n" : ""));
        var damaged = await File.ReadAllBytesAsync(journal);
        row.Status = BatchEntryStatus.Completed; row.Revision++;
        await f.Store.SaveEntryAsync(session, row); // Deliberately no LoadAsync first.
        var saved = await f.Store.LoadAsync(session.Id);
        Assert.Equal(BatchEntryStatus.Completed, Assert.Single(saved.Snapshot()).Status);
        Assert.Equal(row.Revision, Assert.Single(saved.Snapshot()).Revision);
        Assert.Equal(damaged, await File.ReadAllBytesAsync(Assert.Single(Directory.GetFiles(Path.GetDirectoryName(journal)!, "updates.jsonl.torn-*"))));
    }

    [Fact]
    [Trait("Scenario", "BATCH-C09")]
    public async Task BatchCompletionHistoryProvesCompletionAfterCrashBetweenQueueRemovalAndBatchJournal()
    {
        await using var f = new Fixture(); using var previous = f.Service();
        var source = await f.File(".txt", "https://example.test/one");
        var session = await previous.ImportAsync(source); await previous.RunAutomaticAsync(session);
        var job = Assert.Single(f.Queue.Snapshot()); await previous.StopAsync();
        var history = new HistoryService(f.Paths, new NullAppLogger());
        await history.AddAsync(new(job.Id, job.Title, job.SourceUrl, job.VideoId, "channel", "best",
            Path.Combine(f.Root, "retained.mp4"), DateTimeOffset.UtcNow, "Completed"));
        f.Queue.Update(job.Id, item => item.Status = DownloadStatus.Completed, true); Assert.True(f.Queue.Remove(job.Id));
        await f.Queue.SaveAsync();
        using var restarted = new BatchImportService(f.Store,
            (url, _) => throw new InvalidOperationException("A completed job must not be analyzed again."),
            f.Queue, f.Settings, new NullAppLogger(), () => history.Snapshot().Where(entry => entry.Status == "Completed").Select(entry => entry.QueueItemId).ToHashSet());
        try
        {
            await restarted.InitializeAsync();
            var restored = await restarted.ImportAsync(source); await restarted.RunAutomaticAsync(restored);
            Assert.Empty(f.Queue.Snapshot()); Assert.Equal(job.Id, Assert.Single(restored.Snapshot()).QueueItemId);
            Assert.Equal(BatchEntryStatus.Completed, Assert.Single(restored.Snapshot()).Status);
            Assert.False(await restarted.HasUnfinishedSessionsAsync()); Assert.Single(history.Snapshot());
        }
        finally { await restarted.StopAsync(); }
    }

    [Fact]
    [Trait("Scenario", "BATCH-C10")]
    public async Task BatchCompletionFailedJournalCommitRemainsRetryableWithoutPublishingTerminalManifest()
    {
        await using var f = new Fixture(); using var service = f.Service();
        var session = await service.ImportAsync(await f.File(".txt", "https://example.test/one"));
        await service.RunAutomaticAsync(session); var job = Assert.Single(f.Queue.Snapshot());
        var journal = Path.Combine(f.Root, "Batch", session.Id.ToString("N"), "updates.jsonl");
        await using (var blocked = new FileStream(journal, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            f.Queue.Update(job.Id, item => item.Status = DownloadStatus.Completed, true);
            await Assert.ThrowsAsync<IOException>(() => service.RefreshQueueAsync(session));
            Assert.Equal(BatchEntryStatus.AddedToQueue, Assert.Single(session.Snapshot()).Status);
            Assert.Equal(BatchWorkflowPhase.WaitingForQueue, session.WorkflowPhase);
        }
        await service.RefreshQueueAsync(session);
        var restored = await f.Store.LoadAsync(session.Id);
        Assert.Equal(BatchWorkflowPhase.Completed, restored.WorkflowPhase);
        Assert.Equal(BatchEntryStatus.Completed, Assert.Single(restored.Snapshot()).Status);
        Assert.Equal(job.Id, Assert.Single(restored.Snapshot()).QueueItemId);
    }

    [Fact]
    [Trait("Scenario", "BATCH-C11")]
    public async Task BatchCompletionFailedManifestCommitIsRetriedEvenWhenInMemoryPhaseAlreadyMatches()
    {
        await using var f = new Fixture(); using var service = f.Service();
        var session = await service.ImportAsync(await f.File(".txt", "https://example.test/one"));
        await service.RunAutomaticAsync(session); var job = Assert.Single(f.Queue.Snapshot());
        var blocked = Path.Combine(f.Root, "Batch", session.Id.ToString("N"), "session.json.tmp");
        Directory.CreateDirectory(blocked);
        try
        {
            f.Queue.Update(job.Id, item => item.Status = DownloadStatus.Completed, true);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.RefreshQueueAsync(session));
            Assert.Equal(BatchWorkflowPhase.Completed, session.WorkflowPhase);
            Assert.Equal(BatchWorkflowPhase.WaitingForQueue, (await f.Store.LoadAsync(session.Id)).WorkflowPhase);
        }
        finally { Directory.Delete(blocked); }
        await service.RefreshQueueAsync(session);
        Assert.Equal(BatchWorkflowPhase.Completed, (await f.Store.LoadAsync(session.Id)).WorkflowPhase);
    }

    [Fact]
    [Trait("Scenario", "BATCH-C12")]
    public async Task BatchCompletionStartupRejectsNullRowContractsButKeepsHealthySessionsObservable()
    {
        await using var f = new Fixture(); using var previous = f.Service();
        var broken = await previous.ImportAsync(await f.File(".txt", "https://example.test/broken"));
        var healthy = await previous.ImportAsync(await f.File(".txt", "https://example.test/healthy"));
        await previous.RunAutomaticAsync(healthy); var job = Assert.Single(f.Queue.Snapshot()); await previous.StopAsync();
        var path = Path.Combine(f.Root, "Batch", broken.Id.ToString("N"), "entries.jsonl");
        var json = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(Assert.Single(broken.Snapshot())))!;
        json["Url"] = null;
        // The default serializer above writes property names as declared; the store is case-insensitive.
        await File.WriteAllTextAsync(path, json.ToJsonString()); var bytes = await File.ReadAllBytesAsync(path);
        using var restarted = f.Service(); await restarted.InitializeAsync();
        Assert.True(await restarted.HasUnfinishedSessionsAsync());
        f.Queue.Update(job.Id, item => item.Status = DownloadStatus.Completed, true);
        Assert.Equal(BatchEntryStatus.Completed, Assert.Single((await WaitForBatchManifestAsync(f, healthy.Id, BatchWorkflowPhase.Completed)).Snapshot()).Status);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(path)); Assert.True(await restarted.HasUnfinishedSessionsAsync());
    }

    [Fact]
    [Trait("Scenario", "BATCH-C13")]
    public async Task BatchCompletionCancelledInitialImportKeepsPartialRowsAndDoesNotBlockStartupForever()
    {
        await using var f = new Fixture(); using var service = f.Service(); using var cancel = new CancellationTokenSource();
        var source = await f.File(".txt", string.Join('\n', Enumerable.Range(0, 2000).Select(value => "https://example.test/" + value)));
        var progress = new InlineProgress(value => { if (value.Completed >= 1000) cancel.Cancel(); });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ImportAsync(source, token: cancel.Token, progress: progress));
        Assert.Empty(f.Queue.Snapshot()); Assert.Empty(await service.RecentAsync());
        var folder = Assert.Single(Directory.GetDirectories(Path.Combine(f.Root, "Batch")));
        var partial = Path.Combine(folder, "entries.tmp"); Assert.True(new FileInfo(partial).Length > 0);
        var bytes = await File.ReadAllBytesAsync(partial);
        using var marker = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(folder, "import-terminal.json")));
        Assert.Equal(Guid.ParseExact(Path.GetFileName(folder), "N"), marker.RootElement.GetProperty("sessionId").GetGuid());
        Assert.Equal(1, marker.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("Cancelled", marker.RootElement.GetProperty("outcome").GetString());
        Assert.False(marker.RootElement.GetProperty("queueAdmission").GetBoolean());
        using var restarted = f.Service(); await restarted.InitializeAsync();
        Assert.False(await restarted.HasUnfinishedSessionsAsync()); Assert.Equal(bytes, await File.ReadAllBytesAsync(partial));
        var fresh = await restarted.ImportAsync(source); Assert.Equal(2000, fresh.ImportedCount); Assert.Empty(f.Queue.Snapshot());
        Assert.NotEqual(Guid.ParseExact(Path.GetFileName(folder), "N"), fresh.Id);
    }

    [Fact]
    [Trait("Scenario", "BATCH-C14")]
    public async Task BatchCompletionRejectedInitialImportIsTerminalButUnknownAndForgedMarkersStayBlocked()
    {
        await using var f = new Fixture(); using var service = f.Service();
        var invalid = await f.File(".json", "[\"https://example.test/one\",{");
        await Assert.ThrowsAsync<BatchJsonFormatException>(() => service.ImportAsync(invalid));
        Assert.Empty(f.Queue.Snapshot()); Assert.Empty(await service.RecentAsync());
        var folder = Assert.Single(Directory.GetDirectories(Path.Combine(f.Root, "Batch")));
        Assert.True(File.Exists(Path.Combine(folder, "entries.tmp")));
        var markerPath = Path.Combine(folder, "import-terminal.json");
        var original = await File.ReadAllTextAsync(markerPath);
        using var marker = JsonDocument.Parse(original);
        Assert.Equal("Rejected", marker.RootElement.GetProperty("outcome").GetString());
        Assert.False(await service.HasUnfinishedSessionsAsync());
        var tampered = System.Text.Json.Nodes.JsonNode.Parse(original)!; tampered["sessionId"] = Guid.NewGuid();
        await File.WriteAllTextAsync(markerPath, tampered.ToJsonString()); Assert.True(await service.HasUnfinishedSessionsAsync());
        tampered = System.Text.Json.Nodes.JsonNode.Parse(original)!; tampered["schemaVersion"] = 2;
        await File.WriteAllTextAsync(markerPath, tampered.ToJsonString()); Assert.True(await service.HasUnfinishedSessionsAsync());
        tampered = System.Text.Json.Nodes.JsonNode.Parse(original)!; tampered["queueAdmission"] = true;
        await File.WriteAllTextAsync(markerPath, tampered.ToJsonString()); Assert.True(await service.HasUnfinishedSessionsAsync());
        tampered = System.Text.Json.Nodes.JsonNode.Parse(original)!; tampered.AsObject().Remove("queueAdmission");
        await File.WriteAllTextAsync(markerPath, tampered.ToJsonString()); Assert.True(await service.HasUnfinishedSessionsAsync());
        await File.WriteAllTextAsync(markerPath, new string(' ', 4097)); Assert.True(await service.HasUnfinishedSessionsAsync());
        await File.WriteAllTextAsync(markerPath, original); Assert.False(await service.HasUnfinishedSessionsAsync());
        Directory.CreateDirectory(Path.Combine(f.Root, "Batch", Guid.NewGuid().ToString("N")));
        Assert.True(await service.HasUnfinishedSessionsAsync()); Assert.Empty(f.Queue.Snapshot());
    }

    [Fact]
    [Trait("Scenario", "BATCH-C15")]
    public async Task BatchCompletionAbortMarkerWriteFailureKeepsOriginalPartialImportBlocked()
    {
        await using var f = new Fixture(); using var service = f.Service();
        var session = new BatchImportSession { FileName = "rejected.txt" };
        var folder = Path.Combine(f.Root, "Batch", session.Id.ToString("N"));
        Directory.CreateDirectory(Path.Combine(folder, "import-terminal.json.tmp"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Store.CreateAsync(session, RejectedRows(), CancellationToken.None));
        Assert.False(File.Exists(Path.Combine(folder, "import-terminal.json")));
        Assert.True(File.Exists(Path.Combine(folder, "entries.tmp"))); Assert.Empty(f.Queue.Snapshot());
        Assert.True(await service.HasUnfinishedSessionsAsync());
        static async IAsyncEnumerable<BatchImportEntry> RejectedRows()
        {
            yield return new() { Ordinal = 1, Url = "https://example.test/one" };
            await Task.CompletedTask;
            throw new InvalidDataException("Batch.Error.Format");
        }
    }
}
