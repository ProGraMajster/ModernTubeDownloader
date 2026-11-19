using System.Text.Json;
using System.Text.Json.Nodes;
using ModernTubeDownloader.Batch;
using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Models;
using ModernTubeDownloader.Organizer;
using ModernTubeDownloader.Services;

namespace ModernTubeDownloader.Tests;

public sealed class MigrationExtensionDataTests
{
    private const string Unknown = "{\"label\":\"Łódź 日本語\",\"ids\":[7,11,13],\"nested\":{\"keep\":true},\"nullable\":null}";
    private static JsonNode Evidence() => JsonNode.Parse(Unknown)!;
    private static Dictionary<string, JsonElement> Fields() => new() { ["futureData"] = JsonSerializer.Deserialize<JsonElement>(Unknown) };
    private static void AssertEvidence(JsonNode? node) => Assert.True(JsonNode.DeepEquals(Evidence(), node));

    [Fact]
    public async Task OrganizerMoveUndoAndReopenPreserveUnknownQueueHistoryAndNestedFields()
    {
        await using var f = new OrganizerTests.Fixture();
        var original = await f.SeedAsync();
        var sidecar = await File.ReadAllBytesAsync(Path.ChangeExtension(original.FinalPath, ".json"));
        var queue = JsonNode.Parse(await File.ReadAllTextAsync(f.Paths.QueueFile))!.AsArray();
        var history = JsonNode.Parse(await File.ReadAllTextAsync(f.Paths.HistoryFile))!.AsArray();
        queue[0]!["futureData"] = Evidence(); history[0]!["futureData"] = Evidence();
        queue[0]!["requestedRange"]!["futureData"] = Evidence(); history[0]!["requestedRange"]!["futureData"] = Evidence();
        queue[0]!["subtitleOptions"]!["futureData"] = Evidence(); queue[0]!["sponsorBlockOptions"]!["futureData"] = Evidence();
        queue[0]!["selectedFormats"] = JsonSerializer.SerializeToNode(new FormatSelection("v1", "a1", "mp4", "m4a", false, true), JsonDefaults.Options);
        queue[0]!["selectedFormats"]!["futureData"] = Evidence();
        await File.WriteAllTextAsync(f.Paths.QueueFile, queue.ToJsonString());
        await File.WriteAllTextAsync(f.Paths.HistoryFile, history.ToJsonString());
        await f.Queue.LoadAsync(); await f.History.LoadAsync();
        Assert.False(f.Queue.LoadFailed); Assert.False(f.History.LoadFailed);
        var jobId = Assert.Single(f.Queue.Snapshot()).JobSessionId;
        var service = f.Service(); var plan = await f.PlanAsync(service); var match = Assert.Single(plan.Matches);
        Assert.Equal(OrganizerMatchStatus.Found, match.Status);
        var operation = await service.MoveSelectedAsync(plan, [match.Id]);
        await VerifyStores(match.Files[0].Destination);
        await service.UndoAsync(operation.Id);
        await VerifyStores(original.FinalPath);
        Assert.Equal(sidecar, await File.ReadAllBytesAsync(Path.ChangeExtension(original.FinalPath, ".json")));

        async Task VerifyStores(string expectedPath)
        {
            var freshHistory = new HistoryService(f.Paths, new NullAppLogger()); await freshHistory.LoadAsync();
            var freshQueue = new DownloadQueueService(new QueuePersistenceService(f.Paths, new NullAppLogger()), new NullAppLogger(), f.Settings);
            await freshQueue.LoadAsync();
            Assert.Equal(original.QueueItemId, Assert.Single(freshHistory.Snapshot()).QueueItemId);
            Assert.Equal(expectedPath, Assert.Single(freshHistory.Snapshot()).FinalPath);
            Assert.Equal((original.QueueItemId, jobId, expectedPath), (Assert.Single(freshQueue.Snapshot()).Id, freshQueue.Snapshot()[0].JobSessionId, freshQueue.Snapshot()[0].FinalFile));
            var q = JsonNode.Parse(await File.ReadAllTextAsync(f.Paths.QueueFile))![0]!;
            var h = JsonNode.Parse(await File.ReadAllTextAsync(f.Paths.HistoryFile))![0]!;
            AssertEvidence(q["futureData"]); AssertEvidence(h["futureData"]);
            foreach (var name in new[] { "requestedRange", "subtitleOptions", "sponsorBlockOptions", "selectedFormats" }) AssertEvidence(q[name]!["futureData"]);
            AssertEvidence(h["requestedRange"]!["futureData"]);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LegacyLiveMigrationAndRestartPreserveWrapperAndSessionUnknownData(bool destinationAlreadyExists)
    {
        await using var f = new OrganizerTests.Fixture();
        var liveId = Guid.NewGuid();
        var legacy = new DownloadQueueItem
        {
            SourceUrl = "https://example.test/live", VideoId = "live1", Status = DownloadStatus.WaitingForLive,
            AdditionalData = Fields(), LiveSession = new() { Id = liveId, AdditionalData = Fields() },
            RequestedRange = MediaTimeRange.Full with { AdditionalData = Fields() },
            SubtitleOptions = new() { AdditionalData = Fields() }, SponsorBlockOptions = new() { AdditionalData = Fields() },
            SelectedFormats = new("v1", "a1", "mp4", "m4a", false, true) { AdditionalData = Fields() }
        };
        var vod = new DownloadQueueItem { SourceUrl = "https://example.test/vod", VideoId = "vod1", AdditionalData = Fields() };
        var persistence = new QueuePersistenceService(f.Paths, new NullAppLogger());
        await persistence.SaveAsync([legacy, vod]);
        var livePersistence = new LiveSessionPersistenceService(f.Paths, new NullAppLogger());
        if (destinationAlreadyExists)
            await livePersistence.SaveAsync([new() { Id = liveId, SourceUrl = legacy.SourceUrl, State = LiveSessionState.Completed }]);
        await livePersistence.MigrateLegacyQueueAsync(f.Settings);
        await livePersistence.MigrateLegacyQueueAsync(f.Settings);
        var live = new LiveSessionService(new(f.Paths, new NullAppLogger()), f.Settings, new NullAppLogger());
        await live.LoadAsync(); await live.SaveAsync();
        var saved = Assert.Single(live.Snapshot()); Assert.Equal(liveId, saved.Id);
        Assert.Equal(destinationAlreadyExists ? LiveSessionState.Completed : LiveSessionState.WaitingForLive, saved.State);
        var rows = JsonNode.Parse(await File.ReadAllTextAsync(f.Paths.LiveSessionsFile))!.AsArray();
        AssertEvidence(rows[0]!["futureData"]); AssertEvidence(rows[0]!["legacyQueueAdditionalData"]!["futureData"]);
        foreach (var key in new[] { "requestedRange", "subtitleOptions", "sponsorBlockOptions", "selectedFormats" })
        {
            var archived = rows[0]!["legacyQueueAdditionalData"]![key]!.AsObject();
            Assert.Single(archived); AssertEvidence(archived["futureData"]);
        }
        var kept = Assert.Single(await new QueuePersistenceService(f.Paths, new NullAppLogger()).LoadAsync());
        Assert.Equal(vod.Id, kept.Id); Assert.Equal(vod.JobSessionId, kept.JobSessionId);
        AssertEvidence(JsonSerializer.SerializeToNode(kept, JsonDefaults.Options)!["futureData"]);
        var snapshot = Assert.Single(live.Snapshot()); snapshot.AdditionalData!.Clear(); snapshot.LegacyQueueAdditionalData!.Clear();
        Assert.Single(Assert.Single(live.Snapshot()).AdditionalData!); Assert.Equal(5, Assert.Single(live.Snapshot()).LegacyQueueAdditionalData!.Count);
    }

    [Fact]
    public async Task ConflictingLegacyLiveExtensionsRetainBothOriginalStores()
    {
        await using var f = new OrganizerTests.Fixture(); var id = Guid.NewGuid();
        await new QueuePersistenceService(f.Paths, new NullAppLogger()).SaveAsync([new()
        {
            SourceUrl = "https://example.test/live", LiveSession = new() { Id = id, AdditionalData = Fields() }
        }]);
        var store = new LiveSessionPersistenceService(f.Paths, new NullAppLogger());
        var different = Fields(); different["futureData"] = JsonSerializer.SerializeToElement("conflicting original");
        await store.SaveAsync([new() { Id = id, SourceUrl = "https://example.test/live", AdditionalData = different }]);
        var queueBefore = await File.ReadAllBytesAsync(f.Paths.QueueFile); var liveBefore = await File.ReadAllBytesAsync(f.Paths.LiveSessionsFile);
        await Assert.ThrowsAsync<InvalidDataException>(() => store.MigrateLegacyQueueAsync(f.Settings));
        Assert.Equal(queueBefore, await File.ReadAllBytesAsync(f.Paths.QueueFile)); Assert.Equal(liveBefore, await File.ReadAllBytesAsync(f.Paths.LiveSessionsFile));
    }

    [Fact]
    public async Task ConflictingNestedLegacyLiveExtensionsRetainBothOriginalStores()
    {
        await using var f = new OrganizerTests.Fixture(); var id = Guid.NewGuid();
        await new QueuePersistenceService(f.Paths, new NullAppLogger()).SaveAsync([new()
        {
            SourceUrl = "https://example.test/live", LiveSession = new() { Id = id },
            SubtitleOptions = new() { AdditionalData = Fields() }
        }]);
        var store = new LiveSessionPersistenceService(f.Paths, new NullAppLogger());
        await store.SaveAsync([new() { Id = id, SourceUrl = "https://example.test/live",
            LegacyQueueAdditionalData = new() { ["subtitleOptions"] = JsonSerializer.SerializeToElement(new { futureData = "other original" }) } }]);
        var queueBefore = await File.ReadAllBytesAsync(f.Paths.QueueFile); var liveBefore = await File.ReadAllBytesAsync(f.Paths.LiveSessionsFile);
        await Assert.ThrowsAsync<InvalidDataException>(() => store.MigrateLegacyQueueAsync(f.Settings));
        Assert.Equal(queueBefore, await File.ReadAllBytesAsync(f.Paths.QueueFile)); Assert.Equal(liveBefore, await File.ReadAllBytesAsync(f.Paths.LiveSessionsFile));
    }

    [Fact]
    public async Task BatchCommittedUpdatesAndMetadataSnapshotsRetainUnknownFields()
    {
        await using var f = new OrganizerTests.Fixture(); var root = Path.Combine(f.Paths.RootDirectory, "Batch");
        var store = new BatchSessionStore(root); var session = new BatchImportSession { AdditionalData = Fields() };
        var row = new BatchImportEntry { Ordinal = 1, Url = "https://example.test/one", Identity = "https://example.test/one", AdditionalData = Fields(), Range = MediaTimeRange.Full with { AdditionalData = Fields() } };
        await store.CreateAsync(session, Rows(), CancellationToken.None);
        var loaded = await store.LoadAsync(session.Id); var changed = Assert.Single(loaded.Snapshot());
        changed.Status = BatchEntryStatus.Completed; changed.QueueItemId = Guid.NewGuid(); changed.Revision++;
        await store.SaveEntryAsync(loaded, changed); loaded.WorkflowPhase = BatchWorkflowPhase.Completed; await store.SaveSessionAsync(loaded);
        var reopened = await new BatchSessionStore(root).LoadAsync(session.Id); var final = Assert.Single(reopened.Snapshot());
        Assert.Equal(row.Id, final.Id); Assert.Equal(changed.QueueItemId, final.QueueItemId); Assert.Equal(BatchEntryStatus.Completed, final.Status);
        AssertEvidence(JsonSerializer.SerializeToNode(reopened.MetadataSnapshot(), JsonDefaults.Options)!["futureData"]);
        AssertEvidence(JsonSerializer.SerializeToNode(final, JsonDefaults.Options)!["futureData"]);
        AssertEvidence(JsonSerializer.SerializeToNode(final.Range, JsonDefaults.Options)!["futureData"]);
        final.AdditionalData!.Clear(); final.Range.AdditionalData!.Clear();
        var unaffected = Assert.Single(reopened.Snapshot()); Assert.Single(unaffected.AdditionalData!); Assert.Single(unaffected.Range.AdditionalData!);
        var metadata = reopened.MetadataSnapshot(); metadata.AdditionalData!.Clear(); Assert.Single(reopened.AdditionalData!);
        async IAsyncEnumerable<BatchImportEntry> Rows() { yield return row; await Task.CompletedTask; }
    }

    [Fact]
    public void UnknownRangeAndFormatDataDoesNotAlterKnownEqualityOrLeakIntoSnapshots()
    {
        var range = new MediaTimeRange(MediaRangeMode.Custom, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5)) { AdditionalData = Fields() };
        var plain = new MediaTimeRange(MediaRangeMode.Custom, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5));
        Assert.Equal(plain, range); Assert.Equal(plain.GetHashCode(), range.GetHashCode());
        Assert.Single(new HashSet<MediaTimeRange> { plain, range });
        var selected = new FormatSelection("v1", "a1", "mp4", "m4a", false, true) { AdditionalData = Fields() };
        Assert.Equal(new FormatSelection("v1", "a1", "mp4", "m4a", false, true), selected);
        var item = new DownloadQueueItem { AdditionalData = Fields(), RequestedRange = range, SelectedFormats = selected,
            SubtitleOptions = new() { AdditionalData = Fields() }, SponsorBlockOptions = new() { AdditionalData = Fields() } };
        var copy = item.PersistenceCopy();
        copy.AdditionalData!.Clear(); copy.RequestedRange.AdditionalData!.Clear(); copy.SelectedFormats!.AdditionalData!.Clear();
        copy.SubtitleOptions.AdditionalData!.Clear(); copy.SponsorBlockOptions.AdditionalData!.Clear();
        Assert.Single(item.AdditionalData!); Assert.Single(item.RequestedRange.AdditionalData!); Assert.Single(item.SelectedFormats!.AdditionalData!);
        Assert.Single(item.SubtitleOptions.AdditionalData!); Assert.Single(item.SponsorBlockOptions.AdditionalData!);
    }

    [Fact]
    public async Task NestedSettingsAndUpdaterStateUnknownFieldsSurviveNormalPolicyActions()
    {
        await using var f = new OrganizerTests.Fixture();
        var settings = new JsonObject { ["updates"] = new JsonObject { ["automaticChecks"] = false, ["mode"] = 0, ["policyVersion"] = 1 } };
        foreach (var key in new[] { "startup", "notifications", "subtitleDefaults", "sponsorBlockDefaults" }) settings[key] = new JsonObject { ["futureData"] = Evidence() };
        await File.WriteAllTextAsync(f.Paths.SettingsFile, settings.ToJsonString());
        await f.Settings.LoadAsync(); await f.Settings.SaveAsync();
        var savedSettings = JsonNode.Parse(await File.ReadAllTextAsync(f.Paths.SettingsFile))!;
        foreach (var key in new[] { "startup", "notifications", "subtitleDefaults", "sponsorBlockDefaults" }) AssertEvidence(savedSettings[key]!["futureData"]);
        var stateFile = Path.Combine(f.Paths.RootDirectory, "Updates", "state.json"); Directory.CreateDirectory(Path.GetDirectoryName(stateFile)!);
        await File.WriteAllTextAsync(stateFile, new JsonObject { ["futureData"] = Evidence(), ["skippedVersion"] = "1.2.1" }.ToJsonString());
        using var http = new HttpClient(); using var update = new ApplicationUpdateService(f.Paths, f.Settings, http, new NullAppLogger(), new NoRelease(), "1.3.0-candidate.1");
        await update.InitializeAsync(); await update.CheckAsync();
        AssertEvidence(JsonNode.Parse(await File.ReadAllTextAsync(stateFile))!["futureData"]);
        Assert.Equal("1.2.1", update.State.SkippedVersion); Assert.False(update.ShouldInstallOnExit); Assert.False(f.Settings.Current.Updates.InstallAtStartup);
    }

    private sealed class NoRelease : IApplicationReleaseSource
    {
        public Task<ApplicationRelease?> GetLatestAsync(CancellationToken token) => Task.FromResult<ApplicationRelease?>(null);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(1)]
    public async Task HistoryRepublicationPreservesUnknownFieldsWithoutMixingLiveParts(int? part)
    {
        await using var f = new OrganizerTests.Fixture(); var id = Guid.NewGuid();
        var original = new DownloadHistoryEntry(id, "Old", "https://example.test/history", "1", "Synthetic", "720", "old.mp4",
            DateTimeOffset.UnixEpoch, "Partial", RequestedRange: MediaTimeRange.Full with { AdditionalData = Fields() },
            LivePartIndex: part, WasLiveRecording: part.HasValue) { AdditionalData = Fields() };
        await f.History.AddAsync(original);
        var unrelated = original with { QueueItemId = part.HasValue ? id : Guid.NewGuid(), LivePartIndex = part.HasValue ? 2 : null,
            FinalPath = "unrelated.mp4", AdditionalData = new() { ["otherEvidence"] = JsonSerializer.SerializeToElement("retain other part") } };
        await f.History.AddAsync(unrelated);
        var replacement = new DownloadHistoryEntry(id, "Completed title", original.SourceUrl, "1", "Synthetic", "720", "new.mp4",
            DateTimeOffset.UtcNow, "Completed", RequestedRange: MediaTimeRange.Full, LivePartIndex: part, WasLiveRecording: part.HasValue)
        { AdditionalData = new() { ["newEvidence"] = JsonSerializer.SerializeToElement(23) } };
        await f.History.AddAsync(replacement);
        replacement.AdditionalData!.Clear(); // Caller mutation must not change committed state.
        var reopened = new HistoryService(f.Paths, new NullAppLogger()); await reopened.LoadAsync();
        Assert.False(reopened.LoadFailed); Assert.Equal(2, reopened.Snapshot().Count);
        var result = Assert.Single(reopened.Snapshot(), entry => entry.QueueItemId == id && entry.LivePartIndex == part);
        Assert.Equal("Completed title", result.Title); Assert.Equal("new.mp4", result.FinalPath); Assert.Equal("Completed", result.Status);
        AssertEvidence(JsonSerializer.SerializeToNode(result, JsonDefaults.Options)!["futureData"]);
        Assert.Equal(23, result.AdditionalData!["newEvidence"].GetInt32());
        AssertEvidence(JsonSerializer.SerializeToNode(result.RequestedRange, JsonDefaults.Options)!["futureData"]);
        Assert.DoesNotContain("otherEvidence", result.AdditionalData.Keys);
        var other = Assert.Single(reopened.Snapshot(), entry => entry.FinalPath == "unrelated.mp4");
        Assert.Single(other.AdditionalData!); Assert.Equal("retain other part", other.AdditionalData!["otherEvidence"].GetString());

        // Changing the known range does not transplant evidence for an old clip.
        await reopened.AddAsync(replacement with { RequestedRange = new(MediaRangeMode.Custom, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)) });
        Assert.Null(Assert.Single(reopened.Snapshot(), entry => entry.QueueItemId == id && entry.LivePartIndex == part).RequestedRange!.AdditionalData);
    }
}
