using ModernTubeDownloader.Batch;
using ModernTubeDownloader.Models;

namespace ModernTubeDownloader.Tests;

public sealed partial class BatchImportTests
{
    [Fact]
    public async Task ClipboardImportUsesCanonicalValidationDedupHandoffAndDurableIdentities()
    {
        await using var fixture = new Fixture(); using var service = fixture.Service();
        const string input = "# Links from clipboard\r\nhttps://example.test/a\n\nhttps://example.test/b\r\nhttps://example.test/a\nnot-a-url";
        var session = await service.ImportTextAsync(input, "360", PreferredVideoContainer.Mp4);
        Assert.Equal("clipboard.txt", session.FileName); Assert.Equal(4, session.ImportedCount);
        var entries = session.Snapshot();
        Assert.Equal(2, entries.Count(e => e.Status == BatchEntryStatus.Pending));
        Assert.Single(entries, e => e.Status == BatchEntryStatus.Duplicate);
        Assert.Single(entries, e => e.Status == BatchEntryStatus.Failed);
        await service.StartAsync(session);
        Assert.Equal(2, await service.AddReadyAsync(session));
        Assert.Equal(0, await service.AddReadyAsync(session));
        var restored = await fixture.Store.LoadAsync(session.Id);
        Assert.Equal(entries.Select(e => e.Id), restored.Snapshot().Select(e => e.Id));
        Assert.Equal(2, fixture.Queue.Snapshot().Count);
        Assert.All(restored.Snapshot().Where(e => e.QueueItemId is not null), e =>
            Assert.Contains(fixture.Queue.Snapshot(), job => job.Id == e.QueueItemId));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task EmptyOrOversizedClipboardNeverPublishesAnImport(bool oversized)
    {
        await using var fixture = new Fixture(); using var service = fixture.Service();
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => service.ImportTextAsync(oversized
            ? new string('x', BatchImportParser.MaximumClipboardBytes + 1) : " \r\n "));
        Assert.Equal(oversized ? "Batch.Error.ClipboardSize" : "Batch.Error.ClipboardEmpty", error.Message);
        Assert.Empty(await service.RecentAsync()); Assert.Empty(fixture.Queue.Snapshot());
    }
}
