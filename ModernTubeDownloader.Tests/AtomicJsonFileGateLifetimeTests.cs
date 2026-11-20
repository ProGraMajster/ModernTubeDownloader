using System.Text.Json;
using System.Text.Json.Serialization;
using ModernTubeDownloader.Infrastructure;

namespace ModernTubeDownloader.Tests;

public sealed class AtomicJsonFileGateLifetimeTests
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "mtd-json-gate-lifetime-" + Guid.NewGuid().ToString("N"));
    private static readonly TimeSpan TestBound = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task CanceledWaiterDoesNotReleaseHolderOrRetireRemainingWaiters()
    {
        var registry = new AtomicJsonFile.PathGateRegistry();
        var path = Path.Combine(root, "snapshot.json");
        using var holder = await registry.AcquireAsync(path);
        using var cancellation = new CancellationTokenSource();
        var canceledWaiter = registry.AcquireAsync(path, cancellation.Token);
        var survivingWaiter = registry.AcquireAsync(path);
        Assert.False(canceledWaiter.IsCompleted);
        Assert.False(survivingWaiter.IsCompleted);
        Assert.Equal(1, registry.RegisteredPathCount);

        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceledWaiter.WaitAsync(TestBound));
        Assert.False(survivingWaiter.IsCompleted);
        Assert.Equal(1, registry.RegisteredPathCount);
        holder.Dispose();
        using var survivor = await survivingWaiter.WaitAsync(TestBound);
        Assert.Equal(Path.GetFullPath(path), survivor.Path);
        Assert.Equal(1, registry.RegisteredPathCount);
        survivor.Dispose();
        survivor.Dispose(); // Disposal is idempotent and cannot release a new holder.
        Assert.Equal(0, registry.RegisteredPathCount);

        using var reacquired = await registry.AcquireAsync(path).WaitAsync(TestBound);
        var blockedAgain = registry.AcquireAsync(path);
        Assert.False(blockedAgain.IsCompleted);
        holder.Dispose();
        survivor.Dispose();
        Assert.False(blockedAgain.IsCompleted);
        reacquired.Dispose();
        using var next = await blockedAgain.WaitAsync(TestBound);
        next.Dispose();
        Assert.Equal(0, registry.RegisteredPathCount);
    }

    [Fact]
    public async Task InvalidPathAndAlreadyCanceledRequestNeverRegisterAnEntry()
    {
        var registry = new AtomicJsonFile.PathGateRegistry();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => registry.AcquireAsync(Path.Combine(root, "unused.json"), cancellation.Token));
        await Assert.ThrowsAnyAsync<ArgumentException>(() => registry.AcquireAsync(" "));
        await Assert.ThrowsAnyAsync<ArgumentException>(() => registry.AcquireAsync("invalid\0path"));
        Assert.Equal(0, registry.RegisteredPathCount);
    }

    [Fact]
    public async Task DistinctPathsAndCancellationReleaseRacesRetireAllOwnedEntries()
    {
        // This registry belongs only to this test. Other fixtures may legitimately
        // own paths in AtomicJsonFile's production registry at the same time.
        var registry = new AtomicJsonFile.PathGateRegistry();
        var leases = await Task.WhenAll(Enumerable.Range(0, 256).Select(index =>
            registry.AcquireAsync(Path.Combine(root, $"distinct-{index}.json")))).WaitAsync(TestBound);
        Assert.Equal(leases.Length, registry.RegisteredPathCount);
        foreach (var lease in leases) lease.Dispose();
        Assert.Equal(0, registry.RegisteredPathCount);

        for (var iteration = 0; iteration < 64; iteration++)
        {
            var path = Path.Combine(root, $"race-{iteration}.json");
            using var holder = await registry.AcquireAsync(path);
            using var cancellation = new CancellationTokenSource();
            var waiter = registry.AcquireAsync(path, cancellation.Token);
            Assert.False(waiter.IsCompleted);
            await Task.WhenAll(Task.Run(holder.Dispose), Task.Run(() => cancellation.Cancel())).WaitAsync(TestBound);
            try
            {
                using var winner = await waiter.WaitAsync(TestBound);
            }
            catch (OperationCanceledException) { Assert.True(cancellation.IsCancellationRequested); }
            Assert.Equal(0, registry.RegisteredPathCount);
            using var reacquired = await registry.AcquireAsync(path).WaitAsync(TestBound);
            Assert.Equal(1, registry.RegisteredPathCount);
        }
        Assert.Equal(0, registry.RegisteredPathCount);
    }

    [Theory]
    [InlineData("read")]
    [InlineData("serialize")]
    [InlineData("typed-publication")]
    [InlineData("raw-publication")]
    public async Task FailedIoOperationReleasesPathForSubsequentReadAndWrite(string failure)
    {
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "recoverable.json");
        if (failure == "read")
        {
            await File.WriteAllTextAsync(path, "{ broken JSON");
            await Assert.ThrowsAsync<JsonException>(() => AtomicJsonFile.ReadAsync<string>(path).WaitAsync(TestBound));
        }
        else if (failure == "serialize")
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => AtomicJsonFile.WriteAsync(path, new Unserializable()).WaitAsync(TestBound));
        }
        else
        {
            Directory.CreateDirectory(path);
            var publicationFailure = await Record.ExceptionAsync(() => failure == "typed-publication"
                ? AtomicJsonFile.WriteAsync(path, "replacement").WaitAsync(TestBound)
                : AtomicJsonFile.WriteRawJsonAsync(path, "\"replacement\"").WaitAsync(TestBound));
            Assert.True(publicationFailure is IOException or UnauthorizedAccessException,
                $"Expected an OS publication error for a directory destination, got {publicationFailure}");
            // Preserve the obstruction as fixture evidence while making the file
            // path available again; no user files or profile data are involved.
            Directory.Move(path, path + ".obstruction");
        }

        await AtomicJsonFile.WriteAsync(path, "recovered").WaitAsync(TestBound);
        Assert.Equal("recovered", await AtomicJsonFile.ReadAsync<string>(path).WaitAsync(TestBound));
        await AtomicJsonFile.WriteRawJsonAsync(path, "\"raw recovered\"").WaitAsync(TestBound);
        Assert.Equal("raw recovered", await AtomicJsonFile.ReadAsync<string>(path).WaitAsync(TestBound));
        Assert.Empty(Directory.EnumerateFiles(root, "*.tmp", SearchOption.AllDirectories));
    }

    [JsonConverter(typeof(UnserializableConverter))]
    public sealed record Unserializable;

    public sealed class UnserializableConverter : JsonConverter<Unserializable>
    {
        public override Unserializable Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            throw new InvalidOperationException("The failure fixture does not deserialize.");
        public override void Write(Utf8JsonWriter writer, Unserializable value, JsonSerializerOptions options) =>
            throw new InvalidOperationException("Controlled serialization failure.");
    }
}
