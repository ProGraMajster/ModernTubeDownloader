using System.Text.Json;
using System.Text;

namespace ModernTubeDownloader.Infrastructure;

internal static class AtomicJsonFile
{
    private static readonly SemaphoreSlim IoGate = new(1, 1);

    public static async Task<T?> ReadAsync<T>(string path, CancellationToken cancellationToken = default)
    {
        // Windows replacement can fail while a reader holds the destination,
        // even with delete sharing. Use the existing publication gate for the
        // complete read lifetime, closing its handle before a writer proceeds.
        await IoGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
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
        finally
        {
            IoGate.Release();
        }
    }

    public static async Task WriteAsync<T>(string path, T value, CancellationToken cancellationToken = default)
    {
        await IoGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
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
        finally
        {
            IoGate.Release();
        }
    }

    public static async Task WriteRawJsonAsync(string path, string json, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(json);

        await IoGate.WaitAsync(cancellationToken).ConfigureAwait(false);
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
            IoGate.Release();
        }
    }
}
