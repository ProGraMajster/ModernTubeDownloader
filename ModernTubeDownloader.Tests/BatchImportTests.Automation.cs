using System.Text.Json;
using ModernTubeDownloader.Batch;
using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Models;
using ModernTubeDownloader.Services;

namespace ModernTubeDownloader.Tests;

public sealed partial class BatchImportTests
{
    [Fact]
    public async Task AutomaticImportKeepsGlobalPauseAndReusesDurableSessionAndJobIdentitiesAfterRestart()
    {
        await using var f = new Fixture(); using var service = f.Service();
        f.Settings.Current.FinalOutputDirectory = Path.Combine(f.Root, "downloads");
        var prior = new DownloadQueueItem { VideoId = "prior", SourceUrl = "https://example.test/prior" };
        f.Queue.Add(prior);
        var source = await f.File(".txt", "https://example.test/one\nhttps://example.test/one\nhttps://example.test/two\ninvalid");
        var session = await service.ImportAsync(source);
        var first = service.RunAutomaticAsync(session); var second = service.RunAutomaticAsync(session);
        Assert.Same(first, second); await first;
        Assert.True(f.Queue.IsPaused); Assert.Equal(DownloadStatus.Queued, prior.Status);
        Assert.Equal(3, f.Queue.Snapshot().Count);
        Assert.Equal(BatchWorkflowPhase.WaitingForQueue, service.CurrentWorkflowPhase(session));
        Assert.Equal(1, service.Page(session, BatchFilter.Duplicates, "", 0).Total);
        Assert.Equal(1, service.Page(session, BatchFilter.Errors, "", 0).Total);
        var linked = session.Snapshot().Where(entry => entry.QueueItemId is not null).ToArray();
        Assert.All(linked, entry => Assert.Equal(entry.Id, entry.QueueItemId));
        Assert.All(f.Queue.Snapshot().Where(item => item.Id != prior.Id), item => Assert.Equal(session.OutputDirectory, item.OutputDirectory));
        var ids = f.Queue.Snapshot().Select(item => item.Id).ToArray();
        await f.Queue.CheckpointBeforeStartAsync(CancellationToken.None);
        using var restarted = f.Service();
        var reopened = await restarted.ImportAsync(source);
        Assert.Equal(session.Id, reopened.Id); Assert.False(restarted.IsAnalyzing);
        await restarted.RunAutomaticAsync(reopened);
        Assert.Equal(ids, f.Queue.Snapshot().Select(item => item.Id));
        var persisted = await new QueuePersistenceService(f.Paths, new NullAppLogger()).LoadAsync();
        Assert.Null(persisted.Single(item => item.Id == prior.Id).OutputDirectory);
        Assert.All(persisted.Where(item => item.Id != prior.Id), item => Assert.Equal(session.OutputDirectory, item.OutputDirectory));
    }

    [Fact]
    public async Task AutomaticImportRetainsDifferentQualityContainerAndRangeVariants()
    {
        await using var f = new Fixture(); using var service = f.Service();
        var session = await service.ImportAsync(await f.File(".json", """
            [{"url":"https://example.test/one","quality":"360"},
             {"url":"https://example.test/one","quality":"720"},
             {"url":"https://example.test/one","quality":"360","container":"mkv"},
             {"url":"https://example.test/one","quality":"360","start":2,"end":10},
             {"url":"https://example.test/one","quality":"360"}]
            """));
        Assert.Equal(1, service.Page(session, BatchFilter.Duplicates, "", 0).Total);
        await service.RunAutomaticAsync(session);
        Assert.Equal(4, f.Queue.Snapshot().Count);
        Assert.Equal(2, f.Queue.Snapshot().Select(item => item.QualityPresetId).Distinct().Count());
        Assert.Single(f.Queue.Snapshot().Where(item => item.RequestedRange.Mode == MediaRangeMode.Custom));
    }

    [Fact]
    public async Task AutomaticImportUsesSeparateListFoldersAndRequiresExplicitSameNameResolution()
    {
        await using var f = new Fixture(); using var service = f.Service();
        f.Settings.Current.FinalOutputDirectory = Path.Combine(f.Root, "downloads");
        var firstDirectory = Path.Combine(f.Root, "first"); var secondDirectory = Path.Combine(f.Root, "second");
        Directory.CreateDirectory(firstDirectory); Directory.CreateDirectory(secondDirectory);
        var first = Path.Combine(firstDirectory, "My.list.txt"); var second = Path.Combine(secondDirectory, "My.list.txt");
        await File.WriteAllTextAsync(first, "https://example.test/one"); await File.WriteAllTextAsync(second, "https://example.test/two");
        var one = await service.ImportAsync(first); var two = await service.ImportAsync(second);
        Assert.Equal(Path.Combine(f.Settings.Current.FinalOutputDirectory, "My.list"), one.OutputDirectory);
        Assert.NotEqual(one.Id, two.Id); Assert.False(two.DestinationConfirmed);
        await Assert.ThrowsAsync<InvalidDataException>(() => service.RunAutomaticAsync(two));
        Assert.Empty(f.Queue.Snapshot());
        await service.SetDestinationAsync(two, f.Settings.Current.FinalOutputDirectory, "Second list", true);
        await service.RunAutomaticAsync(new[] { one, two });
        Assert.Equal(2, f.Queue.Snapshot().Select(item => item.OutputDirectory).Distinct().Count());
        var destinations = f.Queue.Snapshot().Select(item => item.OutputDirectory).ToArray();
        f.Settings.Current.FinalOutputDirectory = Path.Combine(f.Root, "changed-global");
        await service.RunAutomaticAsync(one);
        Assert.Equal(destinations, f.Queue.Snapshot().Select(item => item.OutputDirectory));
        await Assert.ThrowsAsync<InvalidDataException>(() => service.SetDestinationAsync(one, f.Root, "changed", true));
    }

    [Fact]
    public async Task AutomaticImportCancellationPersistsAndRequiresExplicitRestart()
    {
        await using var f = new Fixture(); var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var service = f.Service(async (url, token) => { entered.TrySetResult(); await Task.Delay(Timeout.Infinite, token); return new(url, Metadata(url), null, null); });
        var session = await service.ImportAsync(await f.File(".txt", "https://example.test/one\nhttps://example.test/two"));
        var operation = service.RunAutomaticAsync(session); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await service.SetPausedAsync(session, true); Assert.Equal(BatchWorkflowPhase.Paused, service.CurrentWorkflowPhase(session));
        await service.CancelAsync(); await operation;
        Assert.Empty(f.Queue.Snapshot()); Assert.Equal(BatchWorkflowPhase.Cancelled, session.WorkflowPhase);
        using var restarted = f.Service(); var restored = await restarted.OpenAsync(session.Id);
        Assert.False(restarted.IsOperating); Assert.True(restored.Paused);
        Assert.Equal(BatchWorkflowPhase.Cancelled, restored.WorkflowPhase);
        Assert.True(await restarted.HasUnfinishedSessionsAsync());
        await restarted.RunAutomaticAsync(restored); Assert.Equal(2, f.Queue.Snapshot().Count);
    }

    [Fact]
    public async Task AutomaticImportRecoversAnAdmissionLinkLostBeforeBatchJournalIncludingCompletedJobs()
    {
        await using var f = new Fixture(); using var service = f.Service();
        var session = await service.ImportAsync(await f.File(".txt", "https://example.test/one"));
        var entry = Assert.Single(session.Snapshot());
        f.Queue.Add(new() { Id = entry.Id, SourceUrl = entry.Url, VideoId = "one", Status = DownloadStatus.Completed, FinalFile = Path.Combine(f.Root, "existing.mp4") });
        await f.Queue.CheckpointBeforeStartAsync(CancellationToken.None);
        await service.RunAutomaticAsync(session);
        Assert.Single(f.Queue.Snapshot());
        Assert.Equal(entry.Id, Assert.Single(session.Snapshot()).QueueItemId);
        Assert.Equal(BatchEntryStatus.Completed, Assert.Single(session.Snapshot()).Status);
        Assert.Equal(BatchWorkflowPhase.Completed, session.WorkflowPhase);
        Assert.False(await service.HasUnfinishedSessionsAsync());
        await service.RunAutomaticAsync(session);
        Assert.Single(f.Queue.Snapshot()); Assert.Equal(entry.Id, Assert.Single(session.Snapshot()).QueueItemId);
    }

    [Fact]
    public async Task AutomaticImportChangedFileIsSeparateAndDestinationPersistenceFailureRestoresAcceptedFolder()
    {
        await using var f = new Fixture(); using var service = f.Service();
        f.Settings.Current.FinalOutputDirectory = Path.Combine(f.Root, "downloads");
        var source = await f.File(".txt", "https://example.test/one");
        var first = await service.ImportAsync(source);
        await File.WriteAllTextAsync(source, "https://example.test/two");
        var second = await service.ImportAsync(source);
        Assert.NotEqual(first.Id, second.Id); Assert.False(second.DestinationConfirmed);
        var accepted = first.OutputDirectory;
        Directory.CreateDirectory(Path.Combine(f.Root, "Batch", first.Id.ToString("N"), "session.json.tmp"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.SetDestinationAsync(first, f.Root, "changed-folder", true));
        Assert.Equal(accepted, first.OutputDirectory);
        Assert.Equal(accepted, (await f.Store.LoadAsync(first.Id)).OutputDirectory);
    }

    [Fact]
    public async Task AutomaticImportDoesNotAnalyzeInvalidRowsOrTreatAnEmptyListAsCompleted()
    {
        await using var f = new Fixture(); var checks = 0;
        using var service = f.Service((url, _) => { checks++; return Task.FromResult(new SourceCheckResult(url, Metadata(url), null, null)); });
        var session = await service.ImportAsync(await f.File(".json", """[{"url":"https://example.test/one","quality":"bad"},"invalid"]"""));
        await service.RunAutomaticAsync(session); Assert.Equal(0, checks); Assert.Empty(f.Queue.Snapshot());
        Assert.Equal(BatchWorkflowPhase.Attention, session.WorkflowPhase);
        var empty = await service.ImportAsync(await f.File(".txt", "# empty\n"));
        await service.RunAutomaticAsync(empty); Assert.Equal(BatchWorkflowPhase.Prepared, empty.WorkflowPhase);
    }

    [Fact]
    public async Task AutomaticImportStartupGuardExaminesOlderSessionsBeyondRecentThirtyAndIncompleteImports()
    {
        await using var f = new Fixture(); using var service = f.Service();
        var pending = await service.ImportAsync(await f.File(".txt", "https://example.test/pending"));
        for (var index = 0; index < 31; index++)
        {
            var terminal = await service.ImportAsync(await f.File(".txt", "# complete " + index));
            terminal.WorkflowPhase = BatchWorkflowPhase.Completed; await f.Store.SaveSessionAsync(terminal);
        }
        Assert.DoesNotContain(await service.RecentAsync(), value => value.Id == pending.Id);
        Assert.True(await service.HasUnfinishedSessionsAsync());
        pending.WorkflowPhase = BatchWorkflowPhase.Completed; await f.Store.SaveSessionAsync(pending);
        Assert.False(await service.HasUnfinishedSessionsAsync());
        Directory.CreateDirectory(Path.Combine(f.Root, "Batch", Guid.NewGuid().ToString("N")));
        Assert.True(await service.HasUnfinishedSessionsAsync());
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("CON")]
    [InlineData("LPT1.txt")]
    [InlineData("folder.")]
    [InlineData("folder ")]
    [InlineData("bad:name")]
    public void AutomaticImportDestinationRejectsUnsafeExplicitPaths(string suffix)
    {
        var root = Path.GetTempPath();
        Assert.Throws<InvalidDataException>(() => DownloadOutputPolicy.NormalizeDirectory(Path.Combine(root, suffix)));
        Assert.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar,
            DownloadOutputPolicy.ForList(root, suffix), StringComparison.OrdinalIgnoreCase);
    }
}
