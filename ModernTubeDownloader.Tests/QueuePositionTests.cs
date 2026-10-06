using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Models;
using ModernTubeDownloader.Services;
using ModernTubeDownloader.Views;

namespace ModernTubeDownloader.Tests;

public sealed class QueuePositionTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "ModernTubeDownloader.QueuePosition.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task PositionsFollowAddMoveRemoveAndRetry()
    {
        var paths = AppPaths.Create(root);
        paths.EnsureCreated();
        var logger = new NullAppLogger();
        var queue = new DownloadQueueService(new QueuePersistenceService(paths, logger), logger);
        var first = Item("first");
        var second = Item("second");
        var third = Item("third");

        queue.Add(first);
        AssertPositions(queue, (first, "1"));
        queue.Add(second);
        queue.Add(third);
        AssertPositions(queue, (first, "1"), (second, "2"), (third, "3"));

        Assert.True(queue.MoveBy(third.Id, -1));
        AssertPositions(queue, (first, "1"), (third, "2"), (second, "3"));
        Assert.True(queue.MoveBy(third.Id, 1));
        AssertPositions(queue, (first, "1"), (second, "2"), (third, "3"));
        Assert.True(queue.Move(third.Id, 0));
        AssertPositions(queue, (third, "1"), (first, "2"), (second, "3"));
        Assert.True(queue.Move(third.Id, int.MaxValue));
        AssertPositions(queue, (first, "1"), (second, "2"), (third, "3"));

        Assert.True(queue.Remove(second.Id));
        AssertPositions(queue, (first, "1"), (third, "2"));
        var fourth = Item("fourth");
        queue.Add(fourth);
        AssertPositions(queue, (first, "1"), (third, "2"), (fourth, "3"));

        queue.Update(third.Id, item => item.Status = DownloadStatus.Failed);
        Assert.True(queue.Retry(third.Id));
        AssertPositions(queue, (first, "1"), (third, "2"), (fourth, "3"));
        await queue.SaveAsync();
    }

    [Fact]
    public async Task PositionsAreDerivedAgainAfterQueueReload()
    {
        var paths = AppPaths.Create(root);
        paths.EnsureCreated();
        var logger = new NullAppLogger();
        var persistence = new QueuePersistenceService(paths, logger);
        var first = Item("first");
        var second = Item("second");
        var third = Item("third");
        await persistence.SaveAsync([third, first, second]);

        var queue = new DownloadQueueService(persistence, logger);
        await queue.LoadAsync();

        AssertPositions(queue, (third, "1"), (first, "2"), (second, "3"));
    }

    [Fact]
    public void DisabledPositionsProduceNoBadgesOrReservedSlots()
    {
        var items = new[] { Item("first"), Item("second"), Item("third") };

        Assert.Empty(QueuePositionPresenter.Build(items, showNumbers: false));
        Assert.Equal("1", QueuePositionPresenter.Build(items, showNumbers: true)[items[0].Id]);
    }

    private static DownloadQueueItem Item(string id) => new() { VideoId = id, Title = id };

    private static void AssertPositions(DownloadQueueService queue, params (DownloadQueueItem Item, string Position)[] expected)
    {
        var items = queue.Snapshot();
        Assert.Equal(expected.Select(value => value.Item.Id), items.Select(item => item.Id));
        var labels = QueuePositionPresenter.Build(items, showNumbers: true);
        Assert.Equal(expected.Select(value => value.Position), items.Select(item => labels[item.Id]));
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
            Directory.Delete(root, recursive: true);
    }
}
