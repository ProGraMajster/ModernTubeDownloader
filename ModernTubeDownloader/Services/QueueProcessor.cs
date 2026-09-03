using ModernTubeDownloader.Infrastructure;
using ModernTubeDownloader.Models;

namespace ModernTubeDownloader.Services;

public sealed class QueueProcessor : IDisposable
{
    private readonly DownloadQueueService queue;
    private readonly DownloadJobExecutor executor;
    private readonly IAppLogger logger;
    private readonly SemaphoreSlim signal = new(0, int.MaxValue);
    private readonly CancellationTokenSource shutdown = new();
    private readonly object activeSync = new();
    private CancellationTokenSource? activeCancellation;
    private Guid? activeItemId;
    private Task? loopTask;

    public QueueProcessor(DownloadQueueService queue, DownloadJobExecutor executor, IAppLogger logger)
    {
        this.queue = queue;
        this.executor = executor;
        this.logger = logger;
        queue.Changed += QueueChanged;
    }

    public Guid? ActiveItemId
    {
        get { lock (activeSync) return activeItemId; }
    }

    public void Start()
    {
        if (loopTask is not null)
            return;
        loopTask = Task.Run(ProcessLoopAsync);
        Wake();
    }

    public bool CancelActive(Guid id)
    {
        lock (activeSync)
        {
            if (activeItemId != id || activeCancellation is null)
                return false;
            activeCancellation.Cancel();
            return true;
        }
    }

    public async Task StopAsync()
    {
        shutdown.Cancel();
        lock (activeSync)
            activeCancellation?.Cancel();
        Wake();
        if (loopTask is not null)
        {
            try { await loopTask.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }
    }

    private async Task ProcessLoopAsync()
    {
        while (!shutdown.IsCancellationRequested)
        {
            var item = queue.TryTakeNext();
            if (item is null)
            {
                await signal.WaitAsync(shutdown.Token).ConfigureAwait(false);
                continue;
            }

            using var active = CancellationTokenSource.CreateLinkedTokenSource(shutdown.Token);
            lock (activeSync)
            {
                activeCancellation = active;
                activeItemId = item.Id;
            }

            try
            {
                logger.Info($"Queue processing started: {item.Id}.");
                await executor.ExecuteAsync(item, (update, persist) => queue.Update(item.Id, update, persist), active.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                queue.Update(item.Id, queueItem =>
                {
                    queueItem.Status = DownloadStatus.Cancelled;
                    queueItem.StatusMessage = "Cancelled";
                    queueItem.ErrorMessage = null;
                    queueItem.SpeedBytesPerSecond = null;
                    queueItem.Eta = null;
                    queueItem.CompletedAt = DateTimeOffset.Now;
                }, true);
                logger.Info($"Queue processing cancelled: {item.Id}.");
            }
            catch (Exception ex)
            {
                queue.Update(item.Id, queueItem =>
                {
                    queueItem.Status = DownloadStatus.Failed;
                    queueItem.StatusMessage = "Failed";
                    queueItem.ErrorMessage = ex.Message;
                    queueItem.SpeedBytesPerSecond = null;
                    queueItem.Eta = null;
                    queueItem.CompletedAt = DateTimeOffset.Now;
                }, true);
                logger.Error($"Queue processing failed: {item.Id}.", ex);
            }
            finally
            {
                lock (activeSync)
                {
                    activeCancellation = null;
                    activeItemId = null;
                }
                await queue.SaveAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }
    }

    private void QueueChanged(object? sender, QueueChangedEventArgs e)
    {
        if (e.Kind is QueueChangeKind.Collection or QueueChangeKind.Pause or QueueChangeKind.Order ||
            e.Kind == QueueChangeKind.Item && e.ItemId is { } id && queue.Find(id)?.Status == DownloadStatus.Queued)
            Wake();
    }

    private void Wake()
    {
        try { signal.Release(); } catch (SemaphoreFullException) { }
    }

    public void Dispose()
    {
        queue.Changed -= QueueChanged;
        shutdown.Dispose();
        signal.Dispose();
        lock (activeSync)
            activeCancellation?.Dispose();
    }
}
