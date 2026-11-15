using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using ModernTubeDownloader.Infrastructure;

namespace ModernTubeDownloader.Tests;

public sealed class AtomicJsonFileSharingTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "mtd-atomic-read-sharing-" + Guid.NewGuid().ToString("N"));
    private static readonly ConcurrentDictionary<Guid, ReadGate> readers = new();
    // Deadlock bounds for a deliberately held reader; not performance assertions.
    private static readonly TimeSpan TestBound = TimeSpan.FromSeconds(5);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AtomicPublicationWaitsForHeldReadThenPublishesCompleteNewVersion(bool rawJson)
    {
        var path = Path.Combine(root, "session.json");
        var key = Guid.NewGuid();
        var oldSnapshot = new HeldSnapshot(key, 1, new string('a', 64 * 1024));
        var newSnapshot = new HeldSnapshot(key, 2, new string('b', 64 * 1024));
        await AtomicJsonFile.WriteAsync(path, oldSnapshot);
        using var gate = new ReadGate();
        Assert.True(readers.TryAdd(key, gate));
        // The converter deliberately holds ReadAsync inside its await-using stream
        // lifetime. No timing race or repeated publication loop is needed.
        var oldRead = Task.Run(() => AtomicJsonFile.ReadAsync<HeldSnapshot>(path));
        Task? publication = null;
        try
        {
            await gate.Entered.Task.WaitAsync(TestBound);
            Assert.False(oldRead.IsCompleted);
            Assert.Throws<IOException>(() =>
            {
                using var inPlace = new FileStream(path, FileMode.Open, FileAccess.Write,
                    FileShare.ReadWrite | FileShare.Delete);
            });

            using var cancellation = new CancellationTokenSource();
            var canceledRead = AtomicJsonFile.ReadAsync<HeldSnapshot>(path, cancellation.Token);
            Assert.False(canceledRead.IsCompleted);
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceledRead.WaitAsync(TestBound));

            publication = rawJson
                ? AtomicJsonFile.WriteRawJsonAsync(path, JsonSerializer.Serialize(newSnapshot))
                : AtomicJsonFile.WriteAsync(path, newSnapshot);

            // Both writers create their staging file before their first I/O
            // await. No staging file proves publication is queued at the shared
            // gate, including after a waiting reader cancels. No delay is used.
            Assert.False(publication.IsCompleted);
            Assert.Empty(Directory.EnumerateFiles(root, "*.tmp"));
            Assert.False(oldRead.IsCompleted);
        }
        finally
        {
            gate.Release.Set();
            try
            {
                await Task.WhenAll(oldRead, publication ?? Task.CompletedTask).WaitAsync(TestBound);
            }
            finally { readers.TryRemove(key, out _); }
        }
        Assert.Equal(oldSnapshot, await oldRead);
        Assert.Equal(newSnapshot, await AtomicJsonFile.ReadAsync<HeldSnapshot>(path).WaitAsync(TestBound));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingStoreReturnsDefaultAndReleasesIoGate(bool missingDirectory)
    {
        if (!missingDirectory) Directory.CreateDirectory(root);
        var path = Path.Combine(root, "missing.json");
        Assert.Null(await AtomicJsonFile.ReadAsync<string>(path).WaitAsync(TestBound));
        await AtomicJsonFile.WriteAsync(path, "saved").WaitAsync(TestBound);
        Assert.Equal("saved", await AtomicJsonFile.ReadAsync<string>(path).WaitAsync(TestBound));
    }

    [Fact]
    public async Task InvalidJsonPropagatesAndReleasesIoGateWithoutChangingOriginal()
    {
        var path = Path.Combine(root, "invalid.json");
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(path, "{invalid");
        var before = await File.ReadAllBytesAsync(path);
        await Assert.ThrowsAsync<JsonException>(() => AtomicJsonFile.ReadAsync<JsonElement>(path).WaitAsync(TestBound));
        Assert.Equal(before, await File.ReadAllBytesAsync(path));
        var independentPath = Path.Combine(root, "valid.json");
        await AtomicJsonFile.WriteAsync(independentPath, "saved").WaitAsync(TestBound);
        Assert.Equal("saved", await AtomicJsonFile.ReadAsync<string>(independentPath).WaitAsync(TestBound));
        Assert.Equal(before, await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public async Task ExclusiveWriterStillBlocksReadAndOriginalBytesRemainIntact()
    {
        var path = Path.Combine(root, "exclusive.json");
        await AtomicJsonFile.WriteRawJsonAsync(path, "{\"value\":\"original\"}");
        var before = await File.ReadAllBytesAsync(path);
        using (var exclusive = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            await Assert.ThrowsAsync<IOException>(() => AtomicJsonFile.ReadAsync<JsonElement>(path));
        Assert.Equal(before, await File.ReadAllBytesAsync(path));
        Assert.Equal("original", (await AtomicJsonFile.ReadAsync<JsonElement>(path)).GetProperty("value").GetString());
    }

    [JsonConverter(typeof(HeldSnapshotConverter))]
    public sealed record HeldSnapshot(Guid Key, int Revision, string Payload);

    public sealed class HeldSnapshotConverter : JsonConverter<HeldSnapshot>
    {
        public override HeldSnapshot Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var document = JsonDocument.ParseValue(ref reader);
            var value = document.RootElement;
            var snapshot = new HeldSnapshot(value.GetProperty("key").GetGuid(), value.GetProperty("revision").GetInt32(),
                value.GetProperty("payload").GetString()!);
            if (snapshot.Revision == 1 && readers.TryGetValue(snapshot.Key, out var gate))
            {
                gate.Entered.TrySetResult();
                if (!gate.Release.Wait(TimeSpan.FromSeconds(10)))
                    throw new TimeoutException("The test did not release its deliberately held JSON reader.");
            }
            return snapshot;
        }

        public override void Write(Utf8JsonWriter writer, HeldSnapshot value, JsonSerializerOptions options)
        {
            writer.WriteStartObject(); writer.WriteString("key", value.Key); writer.WriteNumber("revision", value.Revision);
            writer.WriteString("payload", value.Payload); writer.WriteEndObject();
        }
    }

    private sealed class ReadGate : IDisposable
    {
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal ManualResetEventSlim Release { get; } = new(false);
        public void Dispose() => Release.Dispose();
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}
