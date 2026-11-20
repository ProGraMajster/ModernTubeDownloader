using System.Text.Json;
using System.Text;

namespace ModernTubeDownloader.Infrastructure;

internal static class AtomicJsonFile
{
    private static readonly PathGateRegistry IoGates = new();

    public static async Task<T?> ReadAsync<T>(string path, CancellationToken cancellationToken = default)
    {
        // Windows replacement can fail while a reader holds the destination,
        // even with delete sharing. Use the file's publication gate for the
        // complete read lifetime, closing its handle before a writer proceeds.
        using var operation = await IoGates.AcquireAsync(path, cancellationToken).ConfigureAwait(false);
        path = operation.Path;
        // File.Exists hides access errors and directories behind `false`.
        // Only a genuinely absent path is a new store; consumers must see
        // other I/O failures so their recovery latches protect the profile.
        FileStream stream;
        try { stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true); }
        catch (FileNotFoundException) { return default; }
        catch (DirectoryNotFoundException) { return default; }
        await using (stream)
            return await JsonSerializer.DeserializeAsync<T>(stream, JsonDefaults.Options, cancellationToken).ConfigureAwait(false);
    }

    public static async Task WriteAsync<T>(string path, T value, CancellationToken cancellationToken = default)
    {
        using var operation = await IoGates.AcquireAsync(path, cancellationToken).ConfigureAwait(false);
        path = operation.Path;
        var directory = Path.GetDirectoryName(path) ?? throw new InvalidOperationException("The file has no directory.");
        Directory.CreateDirectory(directory);
        var temporaryPath = path + ".tmp";

        await using (var stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
        {
            await JsonSerializer.SerializeAsync(stream, value, JsonDefaults.Options, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        File.Move(temporaryPath, path, true);
    }

    public static async Task WriteRawJsonAsync(string path, string json, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(json);

        using var operation = await IoGates.AcquireAsync(path, cancellationToken).ConfigureAwait(false);
        path = operation.Path;
        string? temporaryPath = null;
        try
        {
            var directory = Path.GetDirectoryName(path) ?? throw new InvalidOperationException("The file has no directory.");
            Directory.CreateDirectory(directory);
            temporaryPath = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");

            await using (var stream = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             81920,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            await using (var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
            {
                await writer.WriteAsync(json.AsMemory(), cancellationToken).ConfigureAwait(false);
                await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, path, true);
        }
        finally
        {
            if (temporaryPath is not null && File.Exists(temporaryPath))
            {
                try { File.Delete(temporaryPath); } catch { }
            }
        }
    }

    // Canonical path identity, not physical file identity: GetFullPath resolves
    // relative/dot/separator aliases but does not resolve junctions or hard links.
    // The same captured absolute path must also be used for I/O, even if another
    // thread changes the process working directory while an operation waits.
    internal sealed class PathGateRegistry
    {
        private readonly object sync = new();
        private readonly Dictionary<string, Entry> entries = new(
            OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

        internal int RegisteredPathCount
        {
            get { lock (sync) return entries.Count; }
        }

        internal async Task<Lease> AcquireAsync(string path, CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            var absolutePath = System.IO.Path.GetFullPath(path);
            cancellationToken.ThrowIfCancellationRequested();
            Entry entry;
            lock (sync)
            {
                if (!entries.TryGetValue(absolutePath, out entry!))
                    entries.Add(absolutePath, entry = new Entry());
                // Count both holders and queued waiters before releasing sync.
                // A holder cannot retire a semaphore that still has a waiter.
                entry.References++;
            }

            try
            {
                await entry.Semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
                return new Lease(this, absolutePath, entry);
            }
            catch
            {
                Unregister(absolutePath, entry);
                throw;
            }
        }

        private void Unregister(string path, Entry entry)
        {
            lock (sync)
            {
                if (--entry.References != 0) return;
                entries.Remove(path);
                entry.Semaphore.Dispose();
            }
        }

        internal sealed class Entry
        {
            internal SemaphoreSlim Semaphore { get; } = new(1, 1);
            internal int References;
        }

        internal sealed class Lease(PathGateRegistry owner, string path, Entry acquiredEntry) : IDisposable
        {
            private Entry? entry = acquiredEntry;
            internal string Path { get; } = path;

            public void Dispose()
            {
                var releasedEntry = Interlocked.Exchange(ref entry, null);
                if (releasedEntry is null) return;
                releasedEntry.Semaphore.Release();
                owner.Unregister(Path, releasedEntry);
            }
        }
    }
}
