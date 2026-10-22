using ModernTubeDownloader.Infrastructure;

namespace ModernTubeDownloader.Services;

public interface IQueueStorageProbe
{
    Task CheckWritableAsync(IEnumerable<string> directories, CancellationToken token);
    long? AvailableBytes(string directory);
}

/// <summary>Small, operation-bound probes. Never fills a disk or polls continuously.</summary>
public sealed class QueueStorageProbe : IQueueStorageProbe
{
    public async Task CheckWritableAsync(IEnumerable<string> directories, CancellationToken token)
    {
        foreach (var directory in directories.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            token.ThrowIfCancellationRequested();
            Directory.CreateDirectory(directory);
            var probe = Path.Combine(directory, ".mtd-write-probe-" + Guid.NewGuid().ToString("N"));
            await using var stream = new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                4096, FileOptions.Asynchronous | FileOptions.DeleteOnClose | FileOptions.WriteThrough);
            await stream.WriteAsync(new byte[] { 0 }, token).ConfigureAwait(false);
            await stream.FlushAsync(token).ConfigureAwait(false);
        }
    }

    public long? AvailableBytes(string directory)
    {
        try { return new DriveInfo(Path.GetPathRoot(Path.GetFullPath(directory))!).AvailableFreeSpace; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { return null; }
    }
}

public sealed record QueueStorageState(StorageFailureKind Kind, bool PersistenceDegraded, long FailureVersion,
    string? Directory = null)
{
    public bool IsBlocked => Kind != StorageFailureKind.None || PersistenceDegraded;
    public string MessageKey => Kind switch
    {
        StorageFailureKind.DiskFull => "Storage.DiskFull",
        StorageFailureKind.InsufficientSpace => "Storage.LowSpace",
        StorageFailureKind.DirectoryUnavailable => "Storage.DirectoryUnavailable",
        _ => "Storage.PersistenceDegraded"
    };
}

/// <summary>One process-local safety latch shared by VOD admission, persistence and media phases.</summary>
/// <remarks>Does not cancel running processes, own downloads, delete partials or automatically resume.</remarks>
public sealed class QueueStorageSafety(IQueueStorageProbe? probe = null, Func<IEnumerable<string>>? directories = null)
{
    private readonly object sync = new();
    private readonly IQueueStorageProbe probe = probe ?? new QueueStorageProbe();
    private QueueStorageState state = new(StorageFailureKind.None, false, 0);
    public event EventHandler? Changed;
    public QueueStorageState State { get { lock (sync) return state; } }

    public bool Report(Exception error, bool persistence = false, string? directory = null)
    {
        var kind = StorageFailureClassifier.Classify(error);
        if (!persistence && kind == StorageFailureKind.None) return false;
        bool changed;
        lock (sync)
        {
            var next = state with
            {
                Kind = state.Kind == StorageFailureKind.DiskFull ? state.Kind : kind == StorageFailureKind.None ? state.Kind : kind,
                PersistenceDegraded = state.PersistenceDegraded || persistence,
                FailureVersion = state.FailureVersion + 1,
                Directory = directory ?? state.Directory
            };
            changed = next.Kind != state.Kind || next.PersistenceDegraded != state.PersistenceDegraded;
            state = next;
        }
        if (changed) Changed?.Invoke(this, EventArgs.Empty);
        return changed;
    }

    public void ThrowIfBlocked()
    {
        if (State.IsBlocked) throw new StoragePausedException();
    }

    public Task CheckWritableAsync(CancellationToken token) => probe.CheckWritableAsync(directories?.Invoke() ?? [], token);

    public void Preflight(string directory, long? estimatedAllocation)
    {
        ThrowIfBlocked();
        // Unknown sizes and unavailable volume information are advisory only.
        // Runtime ENOSPC/HRESULT detection remains authoritative.
        if (estimatedAllocation is > 0 and < long.MaxValue - 67_108_864 && probe.AvailableBytes(directory) is { } available &&
            available < estimatedAllocation.Value + 67_108_864)
        {
            var error = new StorageSpaceException();
            Report(error, directory: directory);
            throw error;
        }
    }

    internal bool Recover(long failureVersion)
    {
        lock (sync)
        {
            // An active operation failing during the probe/checkpoint must not be
            // erased by a concurrent Resume completion.
            if (state.FailureVersion != failureVersion) return false;
            state = new(StorageFailureKind.None, false, failureVersion);
        }
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }
}
