using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Models;

namespace ModernTubeDownloader.Services;

public sealed class QueuePersistenceService(AppPaths paths, IAppLogger logger)
{
    private readonly SemaphoreSlim gate = new(1, 1);

    public async Task<IReadOnlyList<DownloadQueueItem>> LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await AtomicJsonFile.ReadAsync<List<DownloadQueueItem>>(paths.QueueFile, cancellationToken).ConfigureAwait(false) ?? [];
        }
        catch (Exception ex)
        {
            logger.Error("Could not read the saved download queue.", ex);
            return [];
        }
    }

    public async Task SaveAsync(IReadOnlyList<DownloadQueueItem> items, CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await AtomicJsonFile.WriteAsync(paths.QueueFile, items, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }
}
