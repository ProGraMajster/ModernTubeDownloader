using ModernTubeDownloader.Batch;
using ModernTubeDownloader.Models;

namespace ModernTubeDownloader.Tests;

public sealed partial class BatchImportTests
{
    [Fact]
    public async Task FailedInitialAnalysisJournalRestoresPendingAndExplicitRetryUsesSameIdentity()
    {
        await using var fixture = new Fixture();
        var checks = 0;
        using var service = fixture.Service((url, _) =>
        { Interlocked.Increment(ref checks); return Task.FromResult(new SourceCheckResult(url, Metadata(url), null, null)); });
        var completed = 0;
        service.AnalysisCompleted += (_, _) => Interlocked.Increment(ref completed);
        var session = await service.ImportTextAsync("https://example.test/watch/analysis-journal-failure");
        var original = Assert.Single(session.Snapshot());
        var notifications = new System.Collections.Concurrent.ConcurrentQueue<BatchSessionChanged>();
        service.SessionChanged += (_, change) => notifications.Enqueue(change);
        var journal = Path.Combine(fixture.Root, "Batch", session.Id.ToString("N"), "updates.jsonl");
        using (var locked = new FileStream(journal, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read))
            await Assert.ThrowsAsync<IOException>(() => service.StartAsync(session));
        var failed = Assert.Single(session.Snapshot());
        Assert.Equal(original.Id, failed.Id); Assert.Equal(original.Revision, failed.Revision);
        Assert.Equal(BatchEntryStatus.Pending, failed.Status); Assert.False(failed.HasMetadata);
        Assert.Equal(0, checks); Assert.Equal(0, completed); Assert.False(service.IsAnalyzing); Assert.False(service.IsOperating);
        Assert.Equal(1, session.PresentationRevision);
        Assert.NotEmpty(notifications);
        Assert.All(notifications, change => Assert.Equal(BatchPresentationChange.State, change.Change));
        await service.StartAsync(session);
        var ready = Assert.Single(session.Snapshot());
        Assert.Equal(1, checks); Assert.Equal(1, completed); Assert.Equal(original.Id, ready.Id); Assert.True(ready.HasMetadata);
        Assert.Equal(BatchEntryStatus.Ready, ready.Status);
        var persisted = Assert.Single((await fixture.Store.LoadAsync(session.Id)).Snapshot());
        Assert.Equal(ready.Id, persisted.Id); Assert.Equal(ready.Revision, persisted.Revision); Assert.Equal(ready.Status, persisted.Status);
    }

    [Fact]
    public async Task CommittedBatchMutationNotifiesOnceAndPassiveReconciliationDoesNotNotify()
    {
        await using var fixture = new Fixture(); using var service = fixture.Service();
        var session = await service.ImportTextAsync("https://example.test/watch/notify");
        var notifications = new List<BatchSessionChanged>();
        service.SessionChanged += (_, change) => notifications.Add(change);
        await service.EditAsync(session, session.Snapshot().Select(row => row.Id).ToHashSet(), quality: "720");
        var change = Assert.Single(notifications); Assert.Equal(session.Id, change.SessionId);
        Assert.True(change.Change.HasFlag(BatchPresentationChange.Rows));
        Assert.Equal("720", (await fixture.Store.LoadAsync(session.Id)).Snapshot()[0].Quality);
        var revision = session.PresentationRevision;
        await service.RefreshQueueAsync(session); await service.RefreshQueueAsync(session);
        Assert.Single(notifications); Assert.Equal(revision, session.PresentationRevision);
    }

    [Fact]
    public async Task FailedJournalWriteCannotPublishAnEntryChangeNotification()
    {
        await using var fixture = new Fixture(); using var service = fixture.Service();
        var session = await service.ImportTextAsync("https://example.test/watch/notify-failure");
        var original = Assert.Single(session.Snapshot());
        var ids = new HashSet<Guid> { original.Id };
        var notifications = 0; service.SessionChanged += (_, _) => Interlocked.Increment(ref notifications);
        var journal = Path.Combine(fixture.Root, "Batch", session.Id.ToString("N"), "updates.jsonl");
        using (var locked = new FileStream(journal, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read))
        {
            await Assert.ThrowsAsync<IOException>(() => service.EditAsync(session, ids, quality: "720"));
            AssertOriginalInMemory();
            await Assert.ThrowsAsync<IOException>(() => service.EditAsync(session, ids, remove: true));
            AssertOriginalInMemory();
        }
        Assert.Equal(0, notifications); Assert.Equal(0, session.PresentationRevision);
        Assert.Equal("best", (await fixture.Store.LoadAsync(session.Id)).Snapshot()[0].Quality);
        await service.EditAsync(session, ids, quality: "720");
        Assert.Equal(1, notifications); Assert.Equal(1, session.PresentationRevision);
        var retried = Assert.Single(session.Snapshot());
        var persisted = Assert.Single((await fixture.Store.LoadAsync(session.Id)).Snapshot());
        Assert.Equal(original.Id, retried.Id); Assert.Equal("720", retried.Quality);
        Assert.Equal(original.Revision + 1, retried.Revision); Assert.False(retried.Removed);
        Assert.Equal(retried.Id, persisted.Id); Assert.Equal(retried.Quality, persisted.Quality); Assert.Equal(retried.Revision, persisted.Revision);

        void AssertOriginalInMemory()
        {
            var current = Assert.Single(session.Snapshot());
            Assert.Equal(original.Id, current.Id); Assert.Equal(original.Ordinal, current.Ordinal);
            Assert.Equal(original.Quality, current.Quality); Assert.Equal(original.Container, current.Container);
            Assert.Equal(original.Range, current.Range); Assert.Equal(original.Status, current.Status);
            Assert.Equal(original.Revision, current.Revision); Assert.False(current.Removed);
            Assert.Equal(original.EstimatedSizeBytes, current.EstimatedSizeBytes);
        }
    }

    [Fact]
    public async Task SelectedQueueProgressCopiesOnlyRequestedCanonicalIdsAndIsDetached()
    {
        await using var fixture = new Fixture();
        var first = new DownloadQueueItem { Id = Guid.NewGuid(), Status = DownloadStatus.Queued };
        var second = new DownloadQueueItem { Id = Guid.NewGuid(), Status = DownloadStatus.Completed };
        fixture.Queue.Add(first); fixture.Queue.Add(second);
        var snapshot = Assert.Single(fixture.Queue.ProgressSnapshot(new HashSet<Guid> { second.Id, Guid.NewGuid() }));
        Assert.Equal(second.Id, snapshot.Id); Assert.Equal(DownloadStatus.Completed, snapshot.Status);
        fixture.Queue.Update(second.Id, item => item.Status = DownloadStatus.Failed);
        Assert.Equal(DownloadStatus.Completed, snapshot.Status);
        Assert.Empty(fixture.Queue.ProgressSnapshot(new HashSet<Guid>()));
        Assert.Equal(2, fixture.Queue.Snapshot().Count);
    }
}
