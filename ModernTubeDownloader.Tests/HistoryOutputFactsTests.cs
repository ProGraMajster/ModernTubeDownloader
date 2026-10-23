using System.Text.Json;
using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Models;
using ModernTubeDownloader.Services;

namespace ModernTubeDownloader.Tests;

public sealed class HistoryOutputFactsTests
{
    [Fact]
    public async Task LegacyRecordsKeepUnknownOutputFactsAndNewFactsSurviveReload()
    {
        var root = Path.Combine(Path.GetTempPath(), "mtd-history-facts-" + Guid.NewGuid().ToString("N"));
        try
        {
            var paths = AppPaths.Create(root); paths.EnsureCreated();
            var id = Guid.NewGuid();
            // An actual old shape: neither new optional field exists in the document.
            await File.WriteAllTextAsync(paths.HistoryFile, $$"""
                [{"QueueItemId":"{{id}}","Title":"Old","SourceUrl":"https://example.test/v","VideoId":"v",
                  "Channel":"Channel","Quality":"1080p","FinalPath":"missing.mp4","CompletedAt":"2026-01-01T00:00:00Z","Status":"Completed"}]
                """);
            var history = new HistoryService(paths, new NullAppLogger()); await history.LoadAsync();
            var legacy = Assert.Single(history.Snapshot());
            Assert.Null(legacy.DurationSeconds); Assert.Null(legacy.FileSizeBytes);
            var output = Path.Combine(root, "output.mp4"); await File.WriteAllBytesAsync(output, new byte[1234]);
            await history.AddAsync(legacy with { QueueItemId = Guid.NewGuid(), Title = "New", FinalPath = output,
                DurationSeconds = 12.5, FileSizeBytes = HistoryOutputFacts.TryFileSize(output, new NullAppLogger()) });
            File.Delete(output); // Persistence does not stat a missing/moved file on each refresh.
            var reloaded = new HistoryService(paths, new NullAppLogger()); await reloaded.LoadAsync();
            var entry = Assert.Single(reloaded.Snapshot(), item => item.Title == "New");
            Assert.Equal(12.5, entry.DurationSeconds); Assert.Equal(1234, entry.FileSizeBytes);
            Assert.Null(HistoryOutputFacts.TryFileSize(output, new NullAppLogger()));
            Assert.Null(reloaded.Snapshot().Single(item => item.Title == "Old").DurationSeconds);
            await reloaded.AddAsync(entry with { DurationSeconds = -1, FileSizeBytes = -1 });
            Assert.Null(reloaded.Snapshot().Single(item => item.Title == "New").DurationSeconds);
            Assert.Null(reloaded.Snapshot().Single(item => item.Title == "New").FileSizeBytes);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
