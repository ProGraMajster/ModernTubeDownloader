using System.Text.Json;
using ModernTubeDownloader.Infrastructure;

namespace ModernTubeDownloader.Tests;

public sealed class AtomicJsonFileTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        "ModernTubeDownloader.AtomicJson.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ConcurrentWritesToSameDestinationRemainAtomic()
    {
        var path = Path.Combine(root, "metadata", "same-video.json");
        var payloads = Enumerable.Range(0, 24)
            .Select(index => JsonSerializer.Serialize(new { index, content = new string((char)('a' + index), 64 * 1024) }))
            .ToArray();

        await Task.WhenAll(payloads.Select(payload => AtomicJsonFile.WriteRawJsonAsync(path, payload)));

        var stored = await File.ReadAllTextAsync(path);
        Assert.Contains(stored, payloads);
        using var document = JsonDocument.Parse(stored);
        Assert.InRange(document.RootElement.GetProperty("index").GetInt32(), 0, payloads.Length - 1);
        Assert.Empty(Directory.EnumerateFiles(Path.GetDirectoryName(path)!, "*.tmp"));
    }

    [Fact]
    public async Task FailedWriteDoesNotPoisonLaterWrites()
    {
        await Assert.ThrowsAnyAsync<Exception>(() => AtomicJsonFile.WriteRawJsonAsync("\0", "{}"));

        var path = Path.Combine(root, "after-failure.json");
        await AtomicJsonFile.WriteRawJsonAsync(path, "{\"ok\":true}").WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal("{\"ok\":true}", await File.ReadAllTextAsync(path));
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
            Directory.Delete(root, recursive: true);
    }
}
